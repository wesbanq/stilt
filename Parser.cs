#pragma warning disable CS8601
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

		protected void InsertIntoExprTree(ref Expr? rootExpr, Expr? newExpr)
		{
			if (rootExpr is null && newExpr is not null)
			{
				if (!newExpr.Bracketed && newExpr is not UnaryExpr && newExpr is not CommaExpr && newExpr is IOperator)
					throw new MalformedExpr(newExpr.FullRange ?? throw new InvalidOperationException("Expression has no FullRange"));
					// throw new Exception();
				rootExpr = newExpr;
				return;
			}

			//get rid of null derefence warnings
			if (newExpr is null || rootExpr is null)
				return;

			//add more comments to explain all of this later
			var toReplace = rootExpr.FindFirstPrecedenceOrNull(newExpr.Precedence, out var parent);
			if (toReplace is null && parent is null)
			{
				if (newExpr is IOperator exprSpreadable)
				{
					exprSpreadable.InsertChild(rootExpr);
					rootExpr = newExpr;
				}
				else
					throw new MalformedExpr(newExpr.FullRange ?? throw new InvalidOperationException("Expression has no FullRange"));
			}

			if (newExpr is IOperator spreadable)
			{
				if (toReplace is not null)
				{
					//with the old CommaExpr foundSym tuple of 3 will evaluate to foundSym type of ((Type, Type), Type) instead of (Type, Type, Type)
					//there might be foundSym better way to accomplishing this with BinaryExpr
					//by looking if the child CommaExpr is bracketed during type eval
					if (toReplace is CommaExpr rootComma && newExpr is CommaExpr)
					{
						++rootComma.ExprLength;
						return;
					}

					spreadable.InsertChild(toReplace);
					if (parent is not null)
					{
						if (parent is IOperator op)
						{
							op.ReplaceChild(toReplace, newExpr);
						}
						else
							throw new MalformedExpr(toReplace?.FullRange ?? newExpr?.FullRange ?? throw new InvalidOperationException("Expression has no FullRange"));
					}
					else
					{
						rootExpr = newExpr;
					}

					return;
				}

				if (parent is not null)
				{
					if (parent is IOperator sParent)
					{
						if (toReplace is null && (newExpr.Bracketed || newExpr is (UnaryExpr or TernaryExpr)))
							sParent.InsertChild(newExpr);
						else
						{
							var range = newExpr.FullRange ?? throw new InvalidOperationException("Expression has no FullRange");
							throw new MalformedExpr(range);
						}
					}
					else
					{
						var range = newExpr.FullRange ?? throw new InvalidOperationException("Expression has no FullRange");
						throw new MalformedExpr(range);
					}

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
					throw new MalformedExpr(newExpr.FullRange ?? throw new InvalidOperationException("Expression has no FullRange"));
			}
		}

		protected List<Expr> CreateOperatorExpr<T>(Token token)
			where T : OperatorAttribute
		{
			var operatorAttr = Program.GetAttributesFromEnum<TokenType, T>(token.Which);
			if (operatorAttr is null)
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
					BinaryOperatorAttribute when token.Which == TokenType.Access => 
						new AccessExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.NullAccess => 
						new NullAccessExpr(op.Precedence, token.Range, token),
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
				case CommaExpr comma:
				{
					return [.. comma.GetChildren().SelectMany(GetIdentities)];
				}
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
					throw new MalformedExpr(expr.FullRange ?? throw new InvalidOperationException("Expression has no FullRange"));
				}
			}
		}

		protected bool ExpectingOperator(ref Expr? expr)
		{
			if (expr is null)
				return false;
			var a = expr.FindFirstNull(out var p);
			//foundSym is null - expecting operand / foundSym is not null - expecting operator
			return a is null && p is null;
		}

		protected ArrayLiteralExpr ParseArrayLiteral(Token currentToken)
		{
			if (currentToken.Which is TokenType.EOF)
				throw new UnexpectedEOF(currentToken.Range);
			
			Expr? newExpr = null;
			ParseExpr(ref newExpr, currentToken);
			
			if (newExpr is null)
				throw new SyntaxError(currentToken.Range, "Empty table literal.");
			
			return newExpr is CommaExpr commaExpr
				? new ArrayLiteralExpr(currentToken.Range, [.. commaExpr.GetChildren()])
				: new ArrayLiteralExpr(currentToken.Range, [newExpr]);
		}

		protected TableLiteralExpr ParseTableLiteral(Token currentToken)
		{
			if (currentToken.Which is TokenType.EOF)
				throw new UnexpectedEOF(currentToken.Range);

			Expr? newExpr = null;
			Dictionary<Symbol, Expr> dict = [];
			ParseExpr(ref newExpr, currentToken);
			var list = ParseArrayLiteral(currentToken).Value as List<Expr>;
			
			if (list is null)
				throw new MalformedExpr(currentToken.Range);
			
			foreach (var expr in list)
			{
				if (expr is AssignExpr assign)
				{
					if (assign.Operation is not null)
						throw new SyntaxError(assign.InnerRange ?? assign.FullRange ?? throw new InvalidOperationException("Assign expression has no range"), "Self-assingment operators are not permitted inside tables");

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
							//think of something for other string literal types
							newSym = new VarSymbol((str.Value as String) ?? throw new MalformedExpr(str.FullRange ?? str.InnerRange ?? throw new InvalidOperationException("String literal has no range")), Lex.Filepath, Builtins.Any);
							break;
						}
						default:
						{
							throw new SyntaxError(assign.FullRange ?? throw new InvalidOperationException("Assign expression has no FullRange"), "Invalid key in table");
						}
					}
					dict.Add(newSym, expr);
				}
				else
					throw new SyntaxError(expr.InnerRange ?? expr.FullRange ?? throw new InvalidOperationException("Expression has no range"), "Only assingnment-type expressions allowed inside a table literal");
			}

			return new(currentToken.Range, dict);
		}

		protected double ParseScientificLiteral(Token token)
		{
			var tokenText = token.Range.Text.Replace("_", "");
			var splitIndex = tokenText.IndexOfAny(['e', 'E']);

			var mantissa = Convert.ToDouble(tokenText[..splitIndex], CultureInfo.InvariantCulture);
			var exponent = Convert.ToInt64(tokenText[(splitIndex+1)..], CultureInfo.InvariantCulture);

			return mantissa * Math.Pow(10, exponent);
		}

		protected void ParseExpr(ref Expr? rootExpr, Token currentToken)
		{
			Expr? newExpr = null;

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
						if (tokenText is null || tokenText.Length < 2)
							throw new SyntaxError(currentToken.Range, "Invalid numeric literal format.");
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
						throw new SyntaxError(currentToken.Range, "Could not parse numeric literal.");
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
						if (newExpr is null && currentToken.Which == TokenType.OpenSquareBracket)
							throw new SyntaxError(currentToken.Range, "No valid expression given as an index.");
						var opExpr = CreateOperatorExpr<BinaryOperatorAttribute>(currentToken).First() as BinaryExpr;
						if (opExpr is null)
							throw new MalformedExpr(currentToken.Range);
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
				case TokenType.In:
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
					if (Lex.NextIs(TokenType.Assign))
					{
						Lex.Next();
						Lex.SkipStmtSeparator();
						var assignToken = new Token { Which = TokenType.Assign, Range = currentToken.Range + Lex.CurrentToken.Range };
						var assignAttr = Program.GetAttributeFromEnum<TokenType, OperatorAttribute>(TokenType.Assign);
						if (assignAttr is null)
							throw new UnexpectedToken(currentToken.Range, currentToken);
						var assignExpr = new AssignExpr(
							assignAttr.Precedence,
							assignToken.Range,
							assignToken
						)
						{
							Operation = currentToken.Which,
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
							if (a is not null)
								unary.Prefix = false;
							newExpr = unary;
							break;
						}
						else if (expr is TernaryExpr ternary)
						{
							Expr? leftExpr = null;
							ParseExpr(ref leftExpr, Lex.Next());
							if (!Lex.CurrentIs(TokenType.Then))
								throw new SyntaxError(currentToken.Range, "Unclosed ternary expression.");

							Expr? middleExpr = null;
							ParseExpr(ref middleExpr, Lex.Next());
							if (!Lex.CurrentIs(TokenType.Else))
								throw new SyntaxError(currentToken.Range, "Unclosed ternary expression.");

							ternary.Left = leftExpr;
							ternary.Middle = middleExpr;

							newExpr = ternary;
							break;
						}
						else if (a is not null)
						{
							newExpr = expr;
							break;
						}
					}

					if (newExpr is null)
						throw new UnexpectedToken(currentToken.Range, currentToken);

					break;
				}
				//TODO
				//remove recursion from ParseExpr
				//multiline exprs
			}
			
			InsertIntoExprTree(ref rootExpr, newExpr);
			ParseExpr(ref rootExpr, Lex.Next());
		}

		protected void AddToScope(List<Symbol> symbols, Scope scope)
		{
			foreach (var sym in symbols)
			{
				var foundSymbol = scope.FindSymbolByName(sym.Name);
				if (foundSymbol is not null)
				{
					var range = sym.Identifier?.Range 
						?? throw new Exception();
					if (foundSymbol.IsBuiltin)
						NewError(new ShadowedBuiltinSymbol(range, sym));
					else if (scope.Symbols.Any(s => s.Name == sym.Name))
					{
						NewError(new RedeclaredSymbol(range, sym));
						continue;
					}
					else
						NewError(new ShadowedSymbol(range, sym));
				}
				scope.AddSymbol(sym);
			}
		}

		protected VarDeclStmt ParseVarDecl(Scope scope, bool isConst, Expr expr)
		{
			Expr? idExpr = null;
			Expr? valExpr = null;

			switch (expr)
			{
				case AssignExpr assign:
				{
					if (assign.Operation is not (null or TokenType.Type))
						throw new SyntaxError(assign.InnerRange ?? assign.FullRange ?? throw new InvalidOperationException("Assign expression has no range"), "Cannot use self-assignment operators in variable definition");

					if (assign.Left is null)
						throw new MalformedExpr(assign.FullRange ?? throw new InvalidOperationException("Assign expression has no FullRange"));
					
					idExpr = assign.Left;
					valExpr = assign.Right;
					break;
				}
				case CommaExpr:
				case IdentityExpr:
				{
					idExpr = expr;
					valExpr = null;
					break;
				}
				default:
				{
					throw new MalformedExpr(expr.FullRange ?? throw new InvalidOperationException("Expression has no FullRange"));
				}
			}

			if (valExpr is null && isConst)
				throw new SyntaxError(idExpr.FullRange ?? throw new InvalidOperationException("Expression has no FullRange"), $"No value given to initialize constant.");
			
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
					throw new MalformedDecl(callExpr.FullRange ?? throw new InvalidOperationException("Call expression has no FullRange"));

				var arguments = GetIdentities(callExpr.Right).Select(e => e.Identity).ToList();

				var leftId = callExpr.Left as IdentityExpr;
				if (leftId is null)
					throw new MalformedDecl(callExpr.FullRange ?? throw new InvalidOperationException("Call expression has no FullRange"));
				var decl = new FuncDeclStmt(leftId.Identity.Name, Lex.Filepath, innerStmt)
				{
					Scope = newScope,
				};
				id.Identity = decl.Name;
			
				return decl;
			}
			else
				throw new MalformedDecl(call.FullRange ?? throw new InvalidOperationException("Expression has no FullRange"));
		}

		protected Stmt? ParseLoopStmt(Scope currentScope, Token firstToken)
		{
			Scope newScope = new(currentScope);
			Stmt? newStmt = null;

			switch (firstToken.Which)
			{
				case TokenType.While:
				{
					Expr? conditionExpr = null;
					ParseExpr(ref conditionExpr, Lex.Next());
					if (conditionExpr is null)
						throw new MalformedExpr(firstToken.Range);
					
					Lex.SkipStmtSeparator();
					var bodyStmt = ParseStmt(newScope);
					if (bodyStmt is null)
						throw new SyntaxError(firstToken.Range, "Expected statement after while condition");
					
					newStmt = new PreconditionLoopStmt()
					{
						Scope = newScope,
						Condition = conditionExpr,
						Body = bodyStmt,
					};

					break;
				}
				case TokenType.For:
				{
					VarDeclStmt? loopVar = null;
					Expr? conditionExpr = null;
					Expr? iteratorExpr = null;
					
					var nextToken = Lex.Next();

					if (Lex.CurrentIs(TokenType.VarDecl))
					{
						loopVar = (ParseStmt(newScope) as VarDeclStmt)
							?? throw new UnexpectedToken(nextToken.Range, TokenType.VarDecl, nextToken);
					}
					
					nextToken = Lex.Expect(TokenType.StmtSeparator);
					ParseExpr(ref conditionExpr, nextToken);

					nextToken = Lex.Expect(TokenType.StmtSeparator);
					ParseExpr(ref iteratorExpr, nextToken);
					
					nextToken = Lex.Expect(TokenType.StmtSeparator);

					Lex.SkipStmtSeparator();
					var bodyStmt = ParseStmt(newScope);

					newStmt = new ForLoopStmt()
					{
						Scope = newScope,
						LoopVariable = loopVar,
						Condition = conditionExpr,
						Iterator = iteratorExpr,
						Body = bodyStmt,
					};

					break;
				}
				case TokenType.Foreach:
				{
					firstToken = Lex.ExpectNext(TokenType.VarDecl);
					firstToken = Lex.ExpectNext(TokenType.Identifier);
					
					Expr? loopVar = null;
					ParseExpr(ref loopVar, firstToken);
					if (loopVar is null)
						throw new SyntaxError(firstToken.Range, "Expected variable declaration.");

					var lopVar = ParseVarDecl(newScope, false, loopVar);
					firstToken = Lex.Expect(TokenType.In);
					
					Expr? iteratorExpr = null;
					ParseExpr(ref iteratorExpr, firstToken);
					if (iteratorExpr is null)
						throw new SyntaxError(firstToken.Range, "Expected expression after foreach iterator.");
					
					//firstToken = Lex.Expect(TokenType.StmtSeparator);
					//FIX newline b4 { will error 
					Lex.SkipStmtSeparator();
					var bodyStmt = ParseStmt(newScope)
						/*?? throw new SyntaxError(firstToken.Range, "Expected statement after foreach header")*/;
                    
					newStmt = new ForeachLoopStmt()
					{
						Scope = newScope,
						LoopVariable = lopVar,
						Iterator = iteratorExpr,
						Body = bodyStmt,
					};

					break;
				}
				case TokenType.Repeat:
				{
					Lex.GoPast(TokenType.Repeat);
					Lex.SkipStmtSeparator();
					var bodyStmt = ParseStmt(newScope);

					if (Lex.NextIs(TokenType.Until))
					{
						Lex.GoPast(TokenType.Until);
						Lex.SkipStmtSeparator();
						Expr? conditionExpr = null;
						ParseExpr(ref conditionExpr, Lex.CurrentToken);
						if (conditionExpr is null)
							throw new MalformedExpr(firstToken.Range);
						
						newStmt = new PostconditionLoopStmt()
						{
							Scope = newScope,
							Condition = conditionExpr,
							Body = bodyStmt,
						};
					}
					else
					{
						newStmt = new LoopStmt()
						{
							Scope = newScope,
							Body = bodyStmt,
						};
					}

					break;
				}
			}

			return newStmt;
		}

		protected Stmt? ParseStmt(Scope currentScope)
		{
			var firstToken = Lex.CurrentToken;

			Expr? newExpr = null;
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
					Lex.GoPast(TokenType.VarDecl);
					var isConst = specifiers.Any(t => t.Which == TokenType.ConstSpec);

					ParseExpr(ref newExpr, Lex.CurrentToken);

					if (newExpr is null)
						throw new MalformedExpr(firstToken.Range);
					
					newStmt = ParseVarDecl(currentScope, isConst, newExpr);
					
					if (newStmt is VarDeclStmt varDecl)
						AddToScope(varDecl.Name, currentScope);

					break;
				}
				case TokenType.FuncDecl:
				{
					ParseExpr(ref newExpr, Lex.GoPast(TokenType.FuncDecl));
					Lex.SkipStmtSeparator();
					var innerStmt = ParseStmt(currentScope);
					if (innerStmt is null)
						throw new SyntaxError(firstToken.Range, "Expected statement after function declaration.");
					if (newExpr is null)
						throw new MalformedExpr(firstToken.Range);

					newStmt = ParseFuncDecl(currentScope, innerStmt, newExpr);
					if (newStmt is FuncDeclStmt funcDecl)
						AddToScope([funcDecl.Name], currentScope);

					break;
				}
				//TODO
				case TokenType.TypeDecl:
				{
					break;
				}
				case TokenType.Return:
				{
					Expr? returnExpr = null;
					ParseExpr(ref returnExpr, Lex.GoPast(TokenType.Return));
					newStmt = new ReturnStmt()
					{ 
						Scope = currentScope,
						Value = returnExpr,
					};
					break;
				}
				case TokenType.Break:
				{
					newStmt = new BreakStmt()
					{
						Scope = currentScope,
					};
					break;
				}
				case TokenType.Continue:
				{
					newStmt = new ContinueStmt()
					{
						Scope = currentScope,
					};
					break;
				}
				case TokenType.For:
				case TokenType.While:
				case TokenType.Repeat:
				case TokenType.Foreach:
				{
					newStmt = ParseLoopStmt(currentScope, firstToken);
					break;
				}
				case TokenType.If:
				{
					Expr? conditionExpr = null;
					ParseExpr(ref conditionExpr, Lex.GoPast(TokenType.If));
					if (conditionExpr is null)
						throw new MalformedExpr(firstToken.Range);
					Scope newScope = new(currentScope);
					Lex.SkipStmtSeparator();
					var nextIfStmt = ParseStmt(newScope);
					if (nextIfStmt is null)
						throw new SyntaxError(firstToken.Range, "Expected statement after if condition");

					var ifStmt = new IfStmt()
					{
						Scope = currentScope,
						Condition = conditionExpr,
						NextIf = nextIfStmt,
					};

					var lastIf = ifStmt;
					while (Lex.NextIs(TokenType.Elif))
					{
						var elifToken = Lex.GoPast(TokenType.Elif);
						Expr? elifCondition = null;
						ParseExpr(ref elifCondition, elifToken);
						if (elifCondition is null)
							throw new MalformedExpr(firstToken.Range);
						Lex.SkipStmtSeparator();
						var elifStmt = ParseStmt(newScope);
						if (elifStmt is null)
							throw new SyntaxError(firstToken.Range, "Expected statement after elif condition");
						var newIf = new IfStmt()
						{
							Scope = currentScope,
							Condition = elifCondition,
							NextIf = elifStmt,
						};
						lastIf.NextElse = newIf;
						lastIf = newIf;
					}

					if (Lex.NextIs(TokenType.Else))
					{
						Lex.GoPast(TokenType.Else);
						lastIf.NextElse = ParseStmt(newScope);
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
					Lex.GoPast(TokenType.ExecuteStmt);

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
				default:
				//ExpressionStmt
				{
					if (firstToken.IsUnimplemented)
						throw new UnimplementedError(firstToken);

					ParseExpr(ref newExpr, firstToken);
					if (newExpr is null)
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

			if (newStmt is not null)
				newStmt.InnerRange = firstToken.Range;

			//CONTAINERSTMT
			//ParseGenericStmt()
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

					if (newStmt is not null)
						innerStmts.AddLast(newStmt);
					
					if (Lex.CurrentIs(TokenType.EOF)) 
					{
						if (topLevel)
							break;
						else
							throw new SyntaxError(firstToken.Range, "Unclosed bracket.");
					}
					if (Lex.CurrentIs(TokenType.CloseCurlyBracket) && newStmt is null)
					{
						if (!topLevel)
						{
							break;
						}
						else
							throw new UnexpectedToken(Lex.CurrentToken.Range, Lex.CurrentToken);
					}

					Lex.Next();
				}
				else
				{
					try
					{
						newStmt = ParseStmt(parentScope);

						if (newStmt is not null)
							innerStmts.AddLast(newStmt);
						
						if (Lex.CurrentIs(TokenType.EOF)) 
						{
							if (topLevel)
								break;
							else
								throw new SyntaxError(firstToken.Range, "Unclosed bracket.");
						}
						if (Lex.CurrentIs(TokenType.CloseCurlyBracket) && newStmt is null)
						{
							if (!topLevel)
							{
								break;
							}
							else
								throw new UnexpectedToken(Lex.CurrentToken.Range, Lex.CurrentToken);
						}

						Lex.Next();
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
				}
			}

			// Lex.Next();
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
