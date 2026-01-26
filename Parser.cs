using stilt.AST;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using stilt.Errors;

namespace stilt
{
	public class Parser
	{
		private Lexer Lex;

		public LinkedList<Stmt> Statements = new();
		public Scope RootScope = Builtins.BuiltinScope;
		public ProgramArgs Args;

		public List<CompilationMessage> CompilationIssues = [];
		public bool HasErrors => CompilationIssues.Any(m => m.Severity >= ErrorSeverity.Error);

		public void WriteErrors()
		{
			CompilationIssues.ForEach(m => m.Print());
		}

		protected void NewError(SyntaxError err)
		{
			CompilationIssues.Add(err);
		}

		protected void InsertIntoExprTree(ref Expr rootExpr, Expr? newExpr)
		{
			if (rootExpr == null && newExpr != null)
			{
				if (newExpr is IOperator && newExpr is not UnaryExpr && newExpr is not CommaExpr)
					throw new Exception();
				rootExpr = newExpr;
				return;
			}

			//add more comments to explain all of this later
			var toReplace = rootExpr.FindFirstPrecedenceOrNull(newExpr.Precedence, out var parent);
			if (toReplace == null && parent == null)
			{
				if (newExpr is IOperator exprSpreadable)
				{
					exprSpreadable.InsertChild(rootExpr);
					rootExpr = newExpr;
				}
				else
					throw new MalformedExpr(newExpr.FullRange);
			}

			if (newExpr is IOperator spreadable)
			{
				if (toReplace != null)
				{
					spreadable.InsertChild(toReplace);
					if (parent != null)
					{
						//with the old CommaExpr foundSym tuple of 3 will evaluate to foundSym type of ((Type, Type), Type) instead of (Type, Type, Type)
						//there might be foundSym better way to accomplishing this with BinaryExpr
						//by looking if the child CommaExpr is bracketed during type eval
						if (toReplace is CommaExpr rootComma && newExpr is CommaExpr)
						{
							++rootComma.ExprLength;
							return;
						}

						if (parent is IOperator op)
						{
							op.ReplaceChild(toReplace, newExpr);
						}
					}
					else
					{
						rootExpr = newExpr;
					}

					return;
				}

				if (parent != null)
				{
					if (parent is IOperator sParent)
					{
						if (toReplace == null && (newExpr.Bracketed || newExpr is (UnaryExpr or TernaryExpr)))
							sParent.InsertChild(newExpr);
						else
							throw new MalformedExpr(newExpr.FullRange);
					}
					else
						throw new MalformedExpr(newExpr.FullRange);

					return;
				}
				else
					rootExpr = newExpr;
			}
			else
			{
				if (parent is IOperator newSpreadable)
					newSpreadable.InsertChild(newExpr);
				else
					throw new MalformedExpr(newExpr.FullRange);
			}
		}

		protected List<Expr> CreateOperatorExpr<T>(Token token)
			where T : OperatorAttribute
		{
			var operatorAttr = Program.GetAttributesFromEnum<TokenType, T>(token.Which);
			if (operatorAttr == null)
				throw new UnexpectedToken(token.Range, token);

			List<Expr> exprs = [];
			foreach (var op in operatorAttr)
			{
				Expr expr = op switch
				{
					UnaryOperatorAttribute => new UnaryExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.OpenBracket => 
						new CallExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.Comma => 
						new CommaExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.Assign => 
						new AssignExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute => new BinaryExpr(op.Precedence, token.Range, token),
					TernaryOperatorAttribute => new TernaryExpr(op.Precedence, token.Range, token),
					_ => throw new UnexpectedToken(token.Range, token)
				};
				exprs.Add(expr);
			}

			return exprs;
		}

		public static List<IdentityExpr> GetIdentities(Expr? expr)
		{
			if (expr is null)
				return [];

			switch (expr)
			{
				case IOperator op:
				{
					List<IdentityExpr> res = [];
					foreach (var child in op.GetChildren())
					{
						res.AddRange(GetIdentities(child));
					}
					return res;
				}
				case IdentityExpr id:
				{
					return [id];
				}
				default:
				{
					throw new MalformedExpr(expr.FullRange);
				}
			}
		}

		protected bool ExpectingOperator(ref Expr expr)
		{
			if (expr is null)
				return false;
			var a = expr.FindFirstNull(out var p);
			//foundSym == null - expecting operand / foundSym != null - expecting operator
			return a is null && p is null;
		}

		protected ArrayLiteralExpr ParseArrayLiteral(Token currentToken)
		{
			if (currentToken.Which is TokenType.EOF)
				throw new UnexpectedEOF(currentToken.Range);
			
			Expr newExpr = null;
			ParseExpr(ref newExpr, currentToken);
			
			return newExpr is CommaExpr commaExpr
				? new ArrayLiteralExpr(currentToken.Range, [.. commaExpr.GetChildren()])
				: new ArrayLiteralExpr(currentToken.Range, [newExpr]);
		}

		protected TableLiteralExpr ParseTableLiteral(Token currentToken)
		{
			if (currentToken.Which is TokenType.EOF)
				throw new UnexpectedEOF(currentToken.Range);

			Expr newExpr = null;
			Dictionary<Symbol, Expr> dict = [];
			ParseExpr(ref newExpr, currentToken);
			var list = ParseArrayLiteral(currentToken).Value as List<Expr>;
			
			foreach (var expr in list)
			{
				if (expr is AssignExpr assign)
				{
					if (assign.Operation != null)
						throw new SyntaxError(assign.InnerRange, "Self-assingment operators are not permitted inside tables");

					Symbol newSym;
					//keys in tables may not neccessarily be strings
					//support for non string keys??
					switch (assign.Left)
					{
						case IdentityExpr id:
						{
							newSym = id.Identity;
							newSym.Source = Lex.Filepath;
							break;
						}
						case StringLiteralExpr str:
						{
							//think of something for types
							newSym = new VarSymbol(str.Value as String, Lex.Filepath, Builtins.Any);
							break;
						}
						default:
						{
							throw new SyntaxError(assign.FullRange, "Invalid key in table");
						}
					}
					dict.Add(newSym, expr);
				}
				else
					throw new SyntaxError(expr.InnerRange, "Only assingnment-type expressions allowed inside a table literal");
			}

			return new(currentToken.Range, dict);
		}

		protected double ParseScientificLiteral(Token token)
		{
			var tokenText = token.Range.Text.Replace("_", "");
			var splitIndex = tokenText.IndexOfAny(['e', 'E']);

			var mantissa = Convert.ToDouble(tokenText[..splitIndex], CultureInfo.InvariantCulture);
			var exponent = Convert.ToInt64(tokenText[(splitIndex+1)..], CultureInfo.InvariantCulture);

			return mantissa * (Math.Pow(10, exponent));
		}

		protected void ParseExpr(ref Expr rootExpr, Token currentToken)
		{
			Expr newExpr = null;

			switch (currentToken.Which)
			{
				case TokenType.Identifier:
				{
					var newSym = new VarSymbol(currentToken.Range.Text, t: currentToken);
					newExpr = new IdentityExpr()
					{
						InnerRange = currentToken.Range,
						Identity = newSym,
					};
					break;
				}
				case TokenType.StringLiteral:
				//TODO format the string
				case TokenType.RawStringLiteral:
				case TokenType.FormatStringLiteral:
				{
					newExpr = new StringLiteralExpr(currentToken.Which switch 
					{
						TokenType.RawStringLiteral => Program.Escape(currentToken.Range.Text.Replace("\\\"", "\"").Replace("\\\'", "\'")),
						_ => currentToken.Range.Text,
					}, currentToken.Range);
					break;
				}
				case TokenType.HexNumericLiteral:
				case TokenType.ByteNumericLiteral:
				case TokenType.OctalNumericLiteral:
				case TokenType.WholeNumericLiteral:
				case TokenType.DecimalNumericLiteral:
				case TokenType.ScientificNumericLiteral:
				{
					var tokenText = currentToken.Range.Text.Replace("_", "");
					var literalType = tokenText.Last() switch 
					{
						'b' => Builtins.Byte,
						's' => Builtins.Short,
						'i' => Builtins.Int,
						'l' => Builtins.Long,
						'f' => Builtins.Float,
						'd' => Builtins.Double,
						_	=> currentToken.Which is (TokenType.DecimalNumericLiteral or TokenType.ScientificNumericLiteral) ? Builtins.Fractional : Builtins.Whole,
					};
					var numBase = currentToken.Which switch
					{
						TokenType.OctalNumericLiteral	=> 8,
						TokenType.ByteNumericLiteral	=> 2,
						TokenType.HexNumericLiteral		=> 16,
						_								=> 10,
					};

					if (literalType != Builtins.Fractional && literalType != Builtins.Whole)
					{
						tokenText = tokenText.SkipLast(1).ToString();
					}
					if (numBase != 10)
					{
						tokenText = tokenText.Substring(2);
					}

					try
					{
						if (currentToken.Which is TokenType.ScientificNumericLiteral)
						{
							//idk if these warnings are foundSym good idea
							//if foundSym user wants foundSym 5f theres probably foundSym reason for it and they know the consequences
							//if (literalType.InheritsFrom(Builtins.Whole))
								//NewError(new SyntaxWarning(currentToken.Range, $"{literalType.Name} is not whole. Precision may be lost."));

							var num = ParseScientificLiteral(currentToken);
							newExpr = new NumLiteralExpr(num, currentToken.Range, literalType);
						}
						else if (literalType.InheritsFrom(Builtins.Fractional))
						{
							//if (currentToken.Which is not TokenType.DecimalNumericLiteral)
								//NewError(new SyntaxWarning(currentToken.Range, $"{literalType.Name} is not whole. Precision may be lost."));	

							var num = Convert.ToDouble(tokenText, CultureInfo.InvariantCulture);
							newExpr = new NumLiteralExpr(num, currentToken.Range, literalType);
						}
						else
						{
							//if (currentToken.Which is TokenType.DecimalNumericLiteral)
							//	NewError(new SyntaxWarning(currentToken.Range, $"{literalType.Name} is not fractional. Numbers after the decimal may be lost."));

							var num = Convert.ToInt64(tokenText, numBase);
							newExpr = new NumLiteralExpr(num, currentToken.Range, literalType);
						}
					}
					catch
					{
						throw new SyntaxError(currentToken.Range, "Could not parse numeric literal");
					}

					break;
				}
				case TokenType.Null:
				{
					newExpr = new NullLiteralExpr(currentToken.Range);
					break;
				}
				case TokenType.OpenBracket:
				case TokenType.OpenSquareBracket:
				{
					ParseExpr(ref newExpr, Lex.Next());

					if (ExpectingOperator(ref rootExpr))
					{
						if (newExpr == null && currentToken.Which == TokenType.OpenSquareBracket)
							throw new SyntaxError(currentToken.Range, "No valid expression given as an index");
						var opExpr = CreateOperatorExpr<BinaryOperatorAttribute>(currentToken).First() as BinaryExpr;
						opExpr.Right = newExpr;
						opExpr.Bracketed = true;
						newExpr = opExpr;
					}
					else if (currentToken.Which == TokenType.OpenSquareBracket)
					{
						newExpr = ParseArrayLiteral(Lex.Next());
					}

					break;
				}
				case TokenType.OpenCurlyBracket:
				{
					if (ExpectingOperator(ref rootExpr))
					{
						rootExpr?.Bracketed = true;
						return;
					}
					else
					{
						newExpr = ParseTableLiteral(Lex.Next());
					}

					break;
				}
				case TokenType.EOF:
				case TokenType.Then:
				case TokenType.Else:
				case TokenType.CloseBracket:
				case TokenType.StmtSeparator:
				case TokenType.CloseCurlyBracket:
				case TokenType.CloseSquareBracket:
				{
					//FIX FullRange will ignore the closing bracket
					rootExpr?.Bracketed = true;
					return;
				}
				default:
				{
					if (currentToken.IsUnimplemented)
						throw new UnimplementedError(currentToken);

					var possibleExprs = CreateOperatorExpr<OperatorAttribute>(currentToken)
						?? throw new UnexpectedToken(currentToken.Range, currentToken);
					if (Lex.PeekNext().Which == TokenType.Assign)
					{
						Lex.Next();
						var assignToken = new Token { Which = TokenType.Assign, Range = currentToken.Range + Lex.CurrentToken.Range };
						var assignExpr = new AssignExpr(
							Program.GetAttributeFromEnum<TokenType, OperatorAttribute>(TokenType.Assign).Precedence,
							assignToken.Range,
							assignToken
						)
						{
							Operation = currentToken.Which
						};

						if (possibleExprs.Count != 1)
							throw new MalformedExpr(currentToken.Range);

						newExpr = assignExpr;
						break;
					}

					possibleExprs = [.. possibleExprs.OrderByDescending(e =>
					{
						if (e is IOperator op)
							return op.GetChildren().Count();
						else
							throw new Exception();
					})];

					foreach (var expr in possibleExprs)
					{
						Expr? parent = null;
						var a = rootExpr?.FindFirstPrecedenceOrNull(expr.Precedence, out parent);
						if (expr is UnaryExpr unary)
						{
							if (a != null)
								unary.Prefix = false;
							newExpr = unary;
							break;
						}
						else if (expr is TernaryExpr ternary)
						{
							Expr leftExpr = null;
							ParseExpr(ref leftExpr, Lex.Next());
							if (Lex.CurrentToken.Which != TokenType.Then)
								throw new SyntaxError(currentToken.Range, "Unclosed ternary expression.");

							Expr middleExpr = null;
							ParseExpr(ref middleExpr, Lex.Next());
							if (Lex.CurrentToken.Which != TokenType.Else)
								throw new SyntaxError(currentToken.Range, "Unclosed ternary expression.");

							ternary.Left = leftExpr;
							ternary.Middle = middleExpr;

							newExpr = ternary;
							break;
						}
						else if (a != null)
						{
							newExpr = expr;
							break;
						}
					}

					if (newExpr == null)
						throw new UnexpectedToken(currentToken.Range, currentToken);

					break;
				}
				//TODO
				//remove recursion from ParseExpr
				//multiline exprs
			}
			
			InsertIntoExprTree(ref rootExpr, newExpr);
			
			// Check if the next token is a statement terminator before advancing
			var nextToken = Lex.PeekNext();
			if (nextToken.Which is TokenType.EOF or TokenType.Then or TokenType.Else or TokenType.Elif
				or TokenType.CloseBracket or TokenType.StmtSeparator 
				or TokenType.CloseCurlyBracket or TokenType.CloseSquareBracket)
			{
				return;
			}
			
			ParseExpr(ref rootExpr, Lex.Next());
		}

		protected void AddToScope(List<Symbol> symbols, Scope scope)
		{
			foreach (var sym in symbols)
			{
				var foundSymbol = scope.FindSymbolByName(sym.Name);
				if (foundSymbol is not null)
				{
					if (foundSymbol.IsBuiltin)
						NewError(new ShadowedBuiltinSymbol(sym.Identifier.Range, sym));
					else if (scope.Symbols.Any(s => s.Name == sym.Name))
					{
						NewError(new RedeclaredSymbol(sym.Identifier.Range, sym));
						continue;
					}
					else
						NewError(new ShadowedSymbol(sym.Identifier.Range, sym));
				}
				scope.AddSymbol(sym);
			}
		}

		protected VarDeclStmt ParseVarDecl(Scope scope, bool isConst, Expr idExpr, Expr? valExpr = null)
		{
			if (valExpr is null && isConst)
				throw new SyntaxError(idExpr.FullRange, $"No value given to initialize constant.");
			
			var ids = GetIdentities(idExpr) ?? throw new Exception();
			List<Symbol> syms = [.. ids.Select(i =>
			{
				var sym = i.Identity;
				sym.Source = Lex.Filepath;
				return sym;
			})];

			VarDeclStmt decl = new()
			{
				Scope = scope,
				IsConst = isConst,
				Name = syms,
				Value = valExpr,
			};
			foreach (var item in syms)
			{
				item.Declaration = decl;
			}

			return decl;
		}

		protected FuncDeclStmt ParseFuncDecl(Scope scope, Stmt innerStmt, Expr call)
		{
			Scope newScope = new(scope);

			if (call is CallExpr callExpr && callExpr.Left is IdentityExpr id)
			{
				if (callExpr.Right is not (CommaExpr or IdentityExpr or null) || callExpr.Left is not IdentityExpr)
					throw new MalformedDecl(callExpr.FullRange);

				var arguments = GetIdentities(callExpr.Right).Select(e => e.Identity).ToList();

				var decl = new FuncDeclStmt((callExpr.Left as IdentityExpr).Identity.Name, Lex.Filepath, innerStmt)
				{
					Scope = newScope,
				};
				id.Identity = decl.Name;
			
				return decl;
			}
			else
				throw new MalformedDecl(call.FullRange);
		}

		protected Stmt? ParseStmt(Scope currentScope)
		{
			var firstToken = Lex.CurrentToken;

			Expr newExpr = null;
			List<Token> specifiers = [];

			while (firstToken.IsSpecifier)
			{
				if (specifiers.Any(t => t.Which == firstToken.Which))
					throw new SyntaxError(firstToken.Range, "Duplicate specifiers.");
				specifiers.Add(firstToken);
				firstToken = Lex.Next();
			}

			Stmt? newStmt = null;

			switch (firstToken.Which)
			{
				case TokenType.VarDecl:
				{
					var varToken = Lex.Next();
					if (varToken.Which != TokenType.Identifier && varToken.Which != TokenType.OpenBracket)
						throw new UnexpectedToken(varToken.Range, TokenType.Identifier, varToken);
					var isConst = specifiers.Any(t => t.Which == TokenType.ConstSpec);

					ParseExpr(ref newExpr, varToken);

					switch (newExpr)
					{
						case AssignExpr assign:
						{
							if (assign.Operation is not (null or TokenType.Type))
								throw new SyntaxError(assign.InnerRange, "Cannot use self-assignment operators in variable definition");

							newStmt = ParseVarDecl(currentScope, isConst, assign.Left, assign.Right);
							break;
						}
						case CommaExpr:
						case IdentityExpr:
						{
							newStmt = ParseVarDecl(currentScope, isConst, newExpr);
							break;
						}
						default:
						{
							throw new MalformedExpr(firstToken.Range);
						}
					}
					AddToScope((newStmt as VarDeclStmt).Name, currentScope);

					break;
				}
				case TokenType.FuncDecl:
				{
					ParseExpr(ref newExpr, Lex.Next());
					var innerStmt = ParseStmt(currentScope);

					newStmt = ParseFuncDecl(currentScope, innerStmt, newExpr);
					AddToScope([(newStmt as FuncDeclStmt).Name], currentScope);

					break;
				}
				//TODO
				case TokenType.TypeDecl:
				{
					break;
				}
				case TokenType.Return:
				{
					ParseExpr(ref newExpr, Lex.Next());
					newStmt = new ReturnStmt()
					{ 
						Scope = currentScope,
						Value = newExpr,
					};
					break;
				}
				case TokenType.If:
				{
					ParseExpr(ref newExpr, Lex.Next());
					Scope newScope = new(currentScope);
					var nextIfStmt = ParseStmt(newScope);

					var ifStmt = new IfStmt()
					{
						Scope = currentScope,
						Condition = newExpr,
						NextIf = nextIfStmt,
					};

					var lastIf = ifStmt;
					while (Lex.CurrentToken.Which == TokenType.Elif)
					{
						newExpr = null;
						ParseExpr(ref newExpr, Lex.Next());
						var elifStmt = ParseStmt(newScope);
						var newIf = new IfStmt()
						{
							Scope = currentScope,
							Condition = newExpr,
							NextIf = elifStmt,
						};
						lastIf.NextElse = newIf;
						lastIf = newIf;
					}

					if (Lex.CurrentToken.Which == TokenType.Else)
					{
						Lex.Next();
						var elseStmt = ParseStmt(newScope);
						lastIf.NextElse = elseStmt;
					}

					newStmt = ifStmt;

					break;
				}
				case TokenType.ExecuteStmt:
				{
					newStmt = new ExecuteStmt(firstToken)
					{
						Scope = currentScope,
					};
					Lex.Next();

					newStmt.InnerRange = firstToken.Range;
					return newStmt;
				}
				case TokenType.OpenCurlyBracket:
				{
					Scope newScope = new(currentScope);

					Lex.Next();
					var innerStmt = ParseBranch(newScope, false);
					//Lex.Next();
					
					newStmt = new CompoundStmt()
					{
						Scope = newScope,
						Statements = innerStmt,
					};

					break;
				}
				case TokenType.EOF:
				case TokenType.StmtSeparator:
				case TokenType.CloseCurlyBracket:
				{
					break;
				}
				case TokenType.MacroDecl:
					throw new SyntaxError(firstToken.Range, "Macros can only be defined inside of a type.");
				default:
				//ExpressionStmt
				{
					if (firstToken.IsUnimplemented)
						throw new UnimplementedError(firstToken);

					ParseExpr(ref newExpr, firstToken);
					if (newExpr == null)
						throw new SyntaxError(firstToken.Range, $"Unknown statement: '{firstToken.Range.Text}'");
					
					newStmt = new ExpressionStmt()
					{
						Scope = currentScope,
						Expression = newExpr,
					};

					break;
				}
			}

			if (newStmt is not DeclStmt && specifiers.Count > 0)
				throw new UnexpectedSpecifier(specifiers[0].Range);

			//if (Lex.CurrentToken.Which is not (TokenType.StmtSeparator or TokenType.CloseCurlyBracket or TokenType.EOF or TokenType.Else or TokenType.Elif))
			//	throw new RunonStatement(Lex.CurrentToken.Range);

			newStmt?.InnerRange = firstToken.Range;

			return newStmt;
		}

		protected LinkedList<Stmt> ParseBranch(Scope parentScope, bool topLevel)
		{
			var firstToken = Lex.CurrentToken;
			LinkedList<Stmt> innerStmts = [];

			while (true)
			{
				Stmt? newStmt = null;
				if (Args.Throw)
				{
					newStmt = ParseStmt(parentScope);

					if (newStmt != null)
						innerStmts.AddLast(newStmt);
					
					if (Lex.CurrentToken.Which == TokenType.EOF) 
					{
						if (topLevel)
							break;
						else
							throw new SyntaxError(firstToken.Range, "Unclosed bracket.");
					}
					if (Lex.CurrentToken.Which == TokenType.CloseCurlyBracket)
					{
						if (!topLevel)
						{
							Lex.Next();
							break;
						}
						else
							throw new UnexpectedToken(Lex.CurrentToken.Range, Lex.CurrentToken);
					}

					if (newStmt is not CompoundStmt && Lex.CurrentToken.Which != TokenType.OpenCurlyBracket)
						Lex.Next();
				}
				else
				{
					try
					{
						newStmt = ParseStmt(parentScope);
					}
					catch (SyntaxError se)
					{
						NewError(se);
						if (se.Severity == ErrorSeverity.Critical)
						{
							se.Print();
							throw;
						}
						Lex.SkipStmt();
					}

					if (newStmt != null)
							innerStmts.AddLast(newStmt);

					if (Lex.CurrentToken.Which == TokenType.EOF)
					{
						if (topLevel)
							break;
						else
							throw new SyntaxError(firstToken.Range, "Unclosed bracket.");
					}
					if (Lex.CurrentToken.Which == TokenType.CloseCurlyBracket)
					{
						if (!topLevel)
							break;
						else
							throw new UnexpectedToken(Lex.CurrentToken.Range, Lex.CurrentToken);
					}

					Lex.Next();
				}
			}

			return innerStmts;
		}

		public void ParseFile()
		{
			Statements = ParseBranch(RootScope, true);
		}

		public Parser(Lexer lex, ProgramArgs args)
		{
			Lex = lex;
			Args = args;
		}
		public Parser(Lexer lex, Scope rootScope, ProgramArgs args)
		{
			Lex = lex;
			RootScope = rootScope;
			Args = args;
		}
	}
}
