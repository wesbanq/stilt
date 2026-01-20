using stilt.AST;
using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Text.RegularExpressions;

namespace stilt
{
	public class Parser
	{
		public Lexer Lex;
		public LinkedList<Stmt> Statements = new();
		public Scope RootScope = new();
		public ProgramArgs Args;

		public List<CompilationMessage> CompilationIssues = [];
		public bool HasErrors => CompilationIssues.Any(m => m.Severity >= ErrorSeverity.Error);

		public class SyntaxError : CompilationMessage
		{
			public SyntaxError(FileRange range)
				: base("Syntax error", range, ErrorSeverity.Error)
			{ }
			public SyntaxError(FileRange range, string msg)
				: base(msg, range, ErrorSeverity.Error)
			{ }
			public SyntaxError(FileRange range, string msg, ErrorSeverity severity)
				: base(msg, range, severity)
			{ }
		}

		public class RedeclaredSymbolError : SyntaxError
		{	
			public RedeclaredSymbolError(FileRange range, Symbol symbol)
				: base(range, $"Multiple definitions for symbol: '{symbol.Name}'")
			{ }
		}

		public class UnimplementedError : SyntaxError
		{
			public UnimplementedError(Token token)
				: base(token.Range, $"'{token.Which}' is not implemented yet.")
			{ }
		}

		public class UndefinedSymbolError : SyntaxError
		{
			public string Got;

			public UndefinedSymbolError(FileRange pos, Symbol symbol)
				: base(pos, $"Use of undefined symbol: '{symbol.Name}'")
			{
				Got = symbol.Name;
			}
			public UndefinedSymbolError(FileRange pos, string symbolName)
				: base(pos, $"Use of undefined symbol: '{symbolName}'")
			{
				Got = symbolName;
			}
		}

		public class MalformedExprError : SyntaxError
		{
			public MalformedExprError(FileRange start)
				: base(start, "Malformed expression.")
			{ }
		}

		public class RunonStatement : SyntaxError
		{
			public RunonStatement(FileRange range) 
				: base(range, "Statement is expected to end here, but it keeps going.")
			{ }
		}

		public class UnexpectedToken : SyntaxError
		{
			public TokenType Expected;
			public Token Got;

			public UnexpectedToken(FileRange pos, TokenType expected, Token? got)
				: base(pos, $"Unexpected token: '{got.Which}'.\nExpected: '{expected}'.")
			{
				Expected = expected;
				Got = got;
			}

			public UnexpectedToken(FileRange? pos, Token got)
				: base(pos, $"Unexpected token: '{Program.Escape(got.Which.ToString())}'.")
			{ }
		}

		public class UnexpectedEOF : SyntaxError
		{
			public UnexpectedEOF(FileRange range)
				: base(range, "File unexpectedly ended.")
			{ }
		}

		public class UnexpectedSpecifier : SyntaxError
		{
			public UnexpectedSpecifier(FileRange pos)
				: base(pos, $"Unexpected specifier '{pos.Text}'.")
			{ }
		}

		public class SyntaxWarning : SyntaxError
		{
			public SyntaxWarning(FileRange pos, string msg)
				: base(pos, msg)
			{
				Severity = ErrorSeverity.Warning;
			}
		}

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
				if (newExpr is IOperator && newExpr is not UnaryExpr)
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
					throw new MalformedExprError(newExpr.FullRange);
			}

			if (newExpr is IOperator spreadable)
			{
				if (toReplace != null)
				{
					spreadable.InsertChild(toReplace);
					if (parent != null)
					{
						//with the old CommaExpr a tuple of 3 will evaluate to a type of ((Type, Type), Type) instead of (Type, Type, Type)
						//there might be a better way to accomplishing this with BinaryExpr
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
						if (toReplace == null && newExpr.Bracketed)
							sParent.InsertChild(newExpr);
						else
							throw new MalformedExprError(newExpr.FullRange);
					}
					else
						throw new MalformedExprError(newExpr.FullRange);

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
					throw new MalformedExprError(newExpr.FullRange);
			}
		}

		protected List<Expr> CreateOperatorExpr(Token token)
		{
			var operatorAttr = Compiler.GetAttributesFromEnum<TokenType, OperatorAttribute>(token.Which);
			if (operatorAttr == null)
				throw new UnexpectedToken(token.Range, token);

			List<Expr> exprs = [];
			foreach (var op in operatorAttr)
			{
				exprs.Add(Activator.CreateInstance(op.AssociatedExpr, op.Precedence, token.Range) as Expr);
			}

			return exprs
			//change error
			?? throw new Exception();
		}

		protected List<IdentityExpr> GetIdentities(Expr expr)
		{
			switch (expr)
			{
				case CommaExpr op:
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
					throw new MalformedExprError(expr.FullRange);
				}
			}
		}

		protected bool ExpectingOperator(ref Expr expr)
		{
			//a == null - expecting operand / a != null - expecting operator
			return expr?.FindFirstPrecedenceOrNull(expr.Precedence, out var _) != null;
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

			var mantissa = Convert.ToDouble(tokenText[..splitIndex]);
			var exponent = Convert.ToInt64(tokenText[(splitIndex+1)..]);

			return mantissa * (Math.Pow(10, exponent));
		}

		protected void ParseExpr(ref Expr rootExpr, Token currentToken)
		{
			Expr newExpr = null;

			switch (currentToken.Which)
			{
				case TokenType.Identifier:
				{
					var newSym = new VarSymbol(currentToken.Range.Text);
					newExpr = new IdentityExpr()
					{
						InnerRange = currentToken.Range,
						Identity = newSym,
					};
					break;
				}
				case TokenType.StringLiteral:
				//TODO format the string
				case TokenType.FormatStringLiteral:
				{
					newExpr = new StringLiteralExpr(currentToken.Range.Text, currentToken.Range);
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
					var literalType = tokenText[0] switch 
					{
						'b' => Builtins.Byte,
						's' => Builtins.Short,
						'i' => Builtins.Int,
						'l' => Builtins.Long,
						'f' => Builtins.Float,
						'd' => Builtins.Double,
						//if it isnt a decimal number it can only be a whole number
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
						tokenText = tokenText.Substring(1);
					}
					if (numBase != 10)
					{
						tokenText = tokenText.Substring(2);
					}

					try
					{
						if (currentToken.Which is TokenType.ScientificNumericLiteral)
						{
							//if (literalType.InheritsFrom(Builtins.Whole))
								//NewError(new SyntaxWarning(currentToken.Range, $"{literalType.Name} is not whole. Precision may be lost."));

							var num = ParseScientificLiteral(currentToken);
							newExpr = new NumLiteralExpr(num, currentToken.Range, literalType);
						}
						else if (literalType.InheritsFrom(Builtins.Fractional))
						{
							//if (currentToken.Which is not TokenType.DecimalNumericLiteral)
								//NewError(new SyntaxWarning(currentToken.Range, $"{literalType.Name} is not whole. Precision may be lost."));	

							var num = Convert.ToDouble(tokenText);
							newExpr = new NumLiteralExpr(num, currentToken.Range, literalType);
						}
						else
						{
							if (currentToken.Which is TokenType.DecimalNumericLiteral)
								NewError(new SyntaxWarning(currentToken.Range, $"{literalType.Name} is not fractional. Precision may be lost."));

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
					//empty brackets are equal to null
					ParseExpr(ref newExpr, Lex.Next());

					if (ExpectingOperator(ref rootExpr))
					{
						if (newExpr == null && currentToken.Which == TokenType.OpenSquareBracket)
							throw new SyntaxError(currentToken.Range, "No valid expression given as an index");
						var opExpr = CreateOperatorExpr(currentToken).First() as BinaryExpr;
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
						rootExpr?.Bracketed = true;
					else
					{
						newExpr = ParseTableLiteral(Lex.Next());
					}

					break;
				}
				case TokenType.EOF:
				case TokenType.CloseBracket:
				case TokenType.StmtSeparator:
				case TokenType.CloseCurlyBracket:
				case TokenType.CloseSquareBracket:
				{
					//FIX FullRange will ignore the closing bracket
					rootExpr?.Bracketed = true;
					return;
				}
				case TokenType.Type:
				{
					//TODO finish this part
					var typeToken = Lex.Next()
						?? throw new UnexpectedToken(currentToken.Range, TokenType.Identifier, null);
					if (typeToken.Which != TokenType.Identifier && typeToken.Which != TokenType.OpenBracket)
						throw new UnexpectedToken(typeToken.Range, TokenType.Identifier, typeToken);

					var typeSymbol = new TypeSymbol(typeToken.Range.Text);
					rootExpr.Type = typeSymbol;

					if (rootExpr is IdentityExpr sym)
					{
						switch (sym.Identity)
						{
							case VarSymbol var:
							{
								if (var.Type == null || var.Type == Builtins.Any)
									var.Type = typeSymbol;
								else
									//change error
									throw new Exception();
								break;
							}
						}
					}

					break;
				}
				default:
				{
					if (currentToken.IsUnimplemented)
						throw new UnimplementedError(currentToken);
					var possibleExprs = CreateOperatorExpr(currentToken)
						?? throw new UnexpectedToken(currentToken.Range, currentToken);

					if (Lex.PeekNext().Which == TokenType.Assign)
					{
						Lex.Next();
						possibleExprs = possibleExprs.Where(e => e is BinaryExpr).Select(e => 
							new AssignExpr(Compiler.GetAttributeFromEnum<TokenType, OperatorAttribute>(TokenType.Assign).Precedence, 
								currentToken.Range + Lex.CurrentToken.Range)
							{
								Operation = currentToken.Which
							} as Expr
						).ToList();

						if (possibleExprs.Count != 1)
							throw new MalformedExprError(currentToken.Range);

						newExpr = possibleExprs.First();
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
						if (a == null && expr is UnaryExpr)
						{
							newExpr = expr;
							break;
						}
						else
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
				//new parsing alg
				//remove recursion from ParseExpr
				//multiline exprs
			}
			
			InsertIntoExprTree(ref rootExpr, newExpr);
			ParseExpr(ref rootExpr, Lex.Next());
		}

		protected List<Symbol> AddTempSymToScope(Expr begin, Scope scope)
		{
			var left = GetIdentities(begin);
			return [.. left.Select(i =>
			{
				if (i.Identity.IsTemp)
				{
					i.Identity.Source = Lex.Filepath;
					scope.AddSymbol(i.Identity);
					return i.Identity;
				}
				else throw new Exception();
			})];
		}

		protected void ParseStmt(ref Stmt newStmt)
		{
			var firstToken = Lex.CurrentToken;

			Expr newExpr = null;
			Scope newScope = new(Statements.Last?.Value.Scope ?? RootScope);
			List<Symbol> newSymbols = [];
			List<Token> specifiers = [];

			while (firstToken.IsSpecifier == true)
			{
				specifiers.Add(firstToken);
				firstToken = Lex.Next();
			}

			switch (firstToken.Which)
			{
				case TokenType.VarDecl:
				{
					var varToken = Lex.Next();
					if (varToken.Which != TokenType.Identifier && varToken.Which != TokenType.OpenBracket)
						throw new UnexpectedToken(varToken.Range, TokenType.Identifier, varToken);

					ParseExpr(ref newExpr, varToken);

					if (newExpr is AssignExpr assign)
					{
						//unnecessary?
						if (assign.Left == null || assign.Right == null)
							throw new MalformedExprError(firstToken.Range);

						if (assign.Operation != null)
							throw new SyntaxError(assign.InnerRange, "Cannot use self-assignment operators in variable definition");

						newSymbols = AddTempSymToScope(assign.Left, newScope);
					}
					else
					{
						if (specifiers.Any(t => t.Which == TokenType.ConstSpec))
							throw new SyntaxError(varToken.Range, $"No value given to initialize constant '{varToken.Range.Text}'");

						else if (newExpr is CommaExpr || newExpr is IdentityExpr)
						{
							newSymbols = AddTempSymToScope(newExpr, newScope);
						}
						else
							throw new MalformedExprError(firstToken.Range);
					}

					newStmt = new VarDeclStmt()
					{
						Scope = newScope,
						Name = newSymbols,
						IsConst = specifiers.Any(t => t.Which == TokenType.ConstSpec),
						Value = newExpr,
					};

					break;
				}
				case TokenType.FuncDecl:
				//TODO emit warining when defining macro outside type
				case TokenType.MacroDecl:
				{
					ParseExpr(ref newExpr, Lex.Next());

					if (newExpr is CallExpr funcCall && funcCall.Left is IdentityExpr id)
					{
						ParseStmt(ref newStmt);
						//if (!ParseStmt(ref newStmt))
							//throw new UnexpectedToken(firstToken.Range, null);

						var arguments = GetIdentities(funcCall.Right).Select(e => 
						{
							if (e.Identity is VarSymbol v)
								return v;
							else
								throw new MalformedExprError(firstToken.Range);
						}).ToList();

						newStmt = new FuncDeclStmt(id.Identity.Name, Lex.Filepath, newStmt)
						{
							Scope = newScope,
						};
						newScope.AddSymbol((newStmt as FuncDeclStmt).Name);
						id.Identity = (newStmt as FuncDeclStmt).Name;
					}
					else
						throw new MalformedExprError(firstToken.Range);

					break;
				}
				case TokenType.If:
				case TokenType.Elif:
				{
					ParseExpr(ref newExpr, Lex.Next());
					ParseStmt(ref newStmt);

					newStmt = new IfStmt()
					{
						Scope = newScope,
						Condition = newExpr,
						NextIf = newStmt,
					};

					if (firstToken.Which == TokenType.Elif)
					{
						if (Statements.Last?.Value is IfStmt ifStmt)
						{
							while (ifStmt?.NextIf is IfStmt)
							{
								ifStmt = ifStmt.NextIf as IfStmt;
							}
							ifStmt.NextElse = new IfStmt()
							{
								Scope = newScope,
								Condition = newExpr,
								NextIf = newStmt
							};
							return;
						}
						else
							throw new UnexpectedToken(firstToken.Range, firstToken);
					}

					break;
				}
				case TokenType.Else:
				{
					if (Statements.Last?.Value is IfStmt ifStmt)
					{
						while (ifStmt?.NextIf is IfStmt)
						{
							ifStmt = ifStmt.NextIf as IfStmt;
						}
						ParseStmt(ref newStmt);
						ifStmt.NextElse = newStmt;
						return;
					}
					else
						throw new UnexpectedToken(firstToken.Range, firstToken);
				}
				case TokenType.ExecuteStmt:
				{
					newStmt = new ExecuteStmt(firstToken)
					{
						Scope = newScope,
					};
					Lex.Next();

					return;
				}
				case TokenType.OpenCurlyBracket:
				{
					Lex.Next();
					var innerStmt = ParseBranch();
					Lex.Next();

					if (Lex.CurrentToken.Which is TokenType.EOF)
						throw new SyntaxError(firstToken.Range, "This bracket is missing a closing counterpart.", ErrorSeverity.Critical);

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
					if (newExpr == null)
						throw new SyntaxError(firstToken.Range, $"Unknown statement: '{firstToken.Range.Text}'");
					
					newStmt = new ExpressionStmt()
					{
						Scope = newScope,
						Expression = newExpr,
					};

					break;
				}
			}

			if (newStmt is not DeclStmt && specifiers.Count > 0)
				throw new UnexpectedSpecifier(specifiers[0].Range);

			if (Lex.CurrentToken.Which is not (TokenType.StmtSeparator or TokenType.CloseCurlyBracket or TokenType.EOF))
				throw new RunonStatement(Lex.CurrentToken.Range);

			return;
		}

		protected LinkedList<Stmt> ParseBranch(bool topLevel = false)
		{
			var firstToken = Lex.CurrentToken;
			LinkedList<Stmt> innerStmts = [];

			while (true)
			{
				Stmt newStmt = null;
				if (Args.Throw)
				{
					ParseStmt(ref newStmt);

					if (newStmt != null)
						innerStmts.AddLast(newStmt);
					
					if (Lex.CurrentToken.Which is TokenType.EOF) 
					{
						if (topLevel)
							break;
						else
							throw new SyntaxError(firstToken.Range, "Unclosed bracket.");
					}
					if (Lex.CurrentToken.Which is TokenType.CloseCurlyBracket)
					{
						if (!topLevel)
							break;
						else
							throw new UnexpectedToken(Lex.CurrentToken.Range, Lex.CurrentToken);
					}

					Lex.Next();
					//if (Lex.CurrentToken.Which is TokenType.StmtSeparator)
					//	//two StmtSeparator tokens in a row should be impossible
					//	throw new Exception();
				}
				else
				{
					try
					{
						ParseStmt(ref newStmt);
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

					if (Lex.CurrentToken.Which is TokenType.EOF)
					{
						if (topLevel)
							break;
						else
							throw new SyntaxError(firstToken.Range, "Unclosed bracket.");
					}
					if (Lex.CurrentToken.Which is TokenType.CloseCurlyBracket)
					{
						if (!topLevel)
							break;
						else
							throw new UnexpectedToken(Lex.CurrentToken.Range, Lex.CurrentToken);
					}

					Lex.Next();
					//if (Lex.CurrentToken.Which is TokenType.StmtSeparator)
					//	//two StmtSeparator tokens in a row should be impossible
					//	throw new Exception();
				}
			}

			return innerStmts;
		}

		public void ParseFile()
		{
			Statements = ParseBranch(true);
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
