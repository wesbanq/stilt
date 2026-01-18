using Newtonsoft.Json.Linq;
using stilt.AST;
using System;
using System.ComponentModel.DataAnnotations;

namespace stilt
{
	public class Parser
	{
		public Lexer Lex;
		public LinkedList<Stmt> Statements = new();
		public Scope RootScope = new();

		public List<CompilationMessage> CompilationErrors = [];
		public bool ErrorsEncoutered => CompilationErrors.Any(m => m.Severity >= ErrorSeverity.Error);
		public CompilationMessage? CritcalError => CompilationErrors.Find(m => m.Severity == ErrorSeverity.Critical);

		public class SyntaxError : CompilationMessage
		{
			public SyntaxError(FileRange range)
				: base("Syntax error", range, ErrorSeverity.Error)
			{ }
			public SyntaxError(FileRange range, string msg)
				: base(msg, range, ErrorSeverity.Error)
			{ }
			public SyntaxError(FileRange range, string msg, params string[] strings)
				: base(string.Format(msg, strings), range, ErrorSeverity.Error)
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
				: base(token.Range, $"{token.Which} is not implemented yet.")
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
				: base(start, $"Malformed expression")
			{ }
		}

		public class UnexpectedToken : SyntaxError
		{
			public TokenType Expected;
			public Token Got;

			public UnexpectedToken(FileRange pos, TokenType expected, Token got)
				: base(pos, $"Unexpected token: '{got.Text}'\nExpected: '{Token.GetRulesFromType(expected).First()}'")
			{
				Expected = expected;
				Got = got;
			}

			public UnexpectedToken(FileRange? pos, Token? got)
				: base(pos, $"Unexpected token: {got?.Text}")
			{
				if (got != null && pos != null)
				{
					Got = got;
				}
			}
		}

		protected void NewError(SyntaxError err)
		{
			CompilationErrors.Add(err);
			//oerr = err;
		}

		protected void InsertIntoExprTree(ref Expr rootExpr, Expr? newExpr)
		{
			if (rootExpr == null || newExpr == null)
			{
				if (newExpr != null)
					rootExpr = newExpr;
				return;
			}
			var toReplace = rootExpr.FindFirstPrecedenceOrNull(newExpr.Precedence, out var parent);
			if (toReplace == null && parent == null)
			{
				if (newExpr is IOperator exprSpreadable)
				{
					exprSpreadable.InsertChild(rootExpr);
					rootExpr = newExpr;
				}
				else
					//change error
					throw new Exception();
			}

			if (newExpr is IOperator spreadable)
			{
				if (toReplace != null)
					spreadable.InsertChild(toReplace);
				
				if (parent != null)
				{
					if (parent is IOperator sParent)
						sParent.ReplaceChild(toReplace, newExpr);
					else
						//change error
						throw new Exception();
				}
				else
					rootExpr = newExpr;
			}
			else
			{
				if (parent is IOperator newSpreadable)
					newSpreadable.InsertChild(newExpr);
				else
					//change error
					throw new ArgumentException();
			}
		}

		protected Expr? CreateOperatorExpr(Token token)
		{
			var operatorAttr = Compiler.GetAttributeFromEnum<TokenType, OperatorAttribute>(token.Which);
			if (operatorAttr == null)
			{
				NewError(new UnexpectedToken(token.Range, token));
				return null;
			}

			var newExpr = Activator.CreateInstance(operatorAttr.AssociatedExpr, operatorAttr.Precedence) as Expr;
			return newExpr ?? throw new Exception();
			//change error
		}

		protected void ParseExpr(ref Expr rootExpr, Token? currentToken)
		{
			if (currentToken == null) return;
			Expr newExpr = null;

			switch (currentToken.Which)
			{
				case TokenType.Identifier:
				{
					var newSym = new VarSymbol(currentToken.Text);
					newExpr = new IdentityExpr()
					{
						Identity = newSym
					};
					
					break;
				}
				//for format strings turn into smth like String.Format(string) in the future
				case TokenType.FormatStringLiteral:
				case TokenType.StringLiteral:
				{
					newExpr = new LiteralExpr()
					{
						Value = currentToken.Text
					};

					break;
				}
				case TokenType.NumericLiteral:
				{
					newExpr = new LiteralExpr()
					{
						Value = int.Parse(currentToken.Text)
					};

					break;
				}
				case TokenType.OpenSquareBracket:
				{
					newExpr = CreateOperatorExpr(currentToken)
					//change error
					?? throw new Exception();
					ParseExpr(ref newExpr, Lex.Next());
					break;
				}
				case TokenType.OpenBracket:
				{
					newExpr = CreateOperatorExpr(currentToken)
					//change error
					?? throw new Exception();
					var a = rootExpr.FindFirstPrecedenceOrNull(newExpr.Precedence, out var parent);
					if (a == null && parent != null)
						newExpr = null;

					ParseExpr(ref newExpr, Lex.Next());
					break;
				}
				case TokenType.StmtSeparator:
				case TokenType.CloseBracket:
				case TokenType.CloseSquareBracket:
				case TokenType.OpenCurlyBracket:
				{
					rootExpr.Bracketed = true;
					return;
				}
				case TokenType.Type:
				{
					//TODO
					var typeToken = Lex.Next()
					//change error
					?? throw new Exception();
					if (typeToken.Which != TokenType.Identifier && typeToken.Which != TokenType.OpenBracket)
					{
						NewError(new UnexpectedToken(typeToken.Range, TokenType.Identifier, typeToken));
						break;
					}

					var typeSymbol = new TypeSymbol(typeToken.Text);
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
					if (Compiler.GetAttributeFromEnum<TokenType, UnimplementedAttribute>(currentToken.Which) != null)
					{
						NewError(new UnimplementedError(currentToken));
						return;
					}

					newExpr = CreateOperatorExpr(currentToken)
					//change error
					?? throw new Exception();

					if (Lex.PeekNext()?.Which == TokenType.Assign
						&& newExpr is BinaryExpr binExpr)
					{
						Lex.Next();
						var precedence = Compiler.GetAttributeFromEnum<TokenType, OperatorAttribute>(TokenType.Assign).Precedence;
						newExpr = new AssignExpr(precedence)
						{
							Operation = binExpr
						};
					}
					//TODO unary/ternary, array/table lits
					break;
				}
			}

			InsertIntoExprTree(ref rootExpr, newExpr);
			ParseExpr(ref rootExpr, Lex.Next());
		}

		protected List<IdentityExpr> GetIdentities(Expr expr)
		{
			List<IdentityExpr> res = [];
			switch (expr) 
			{
				case IOperator op:
				{
					foreach (var child in op.GetChildren())
					{
						if (child != null)
							res.AddRange(GetIdentities(child));
					}
					break;
				}
				case IdentityExpr id:
				{
					res.Add(id);
					break;
				}
				default:
				{
					//change error
					throw new ArgumentException();
				}
			}
			return res;
		}

		protected bool ParseStmt(ref Stmt newStmt)
		{
			var firstToken = Lex.CurrentToken;
			Expr newExpr = null;
			Scope newScope = new(Statements.Last?.Value.Scope ?? RootScope);

			switch (firstToken?.Which)
			{
				case TokenType.ConstDecl:
				case TokenType.VarDecl:
				{
					var varToken = Lex.Next();
					if (varToken.Which != TokenType.Identifier || varToken.Which != TokenType.OpenBracket)
					{
						NewError(new UnexpectedToken(varToken.Range, TokenType.Identifier, varToken));
						return true;
					}

					ParseExpr(ref newExpr, varToken);

					if (newExpr == null && firstToken.Which == TokenType.ConstDecl)
					{
						NewError(new SyntaxError(varToken.Range,
							"No value given to initialize constant '{0}'", varToken.Text));
						return true;
					}

					if (newExpr is AssignExpr assign)
					{
						if (assign.Left == null || assign.Right == null)
						{
							NewError(new MalformedExprError(firstToken.Range));
							return true;
						}

						//ReplaceIdentities
						var left = GetIdentities(assign.Left);
						var right = GetIdentities(assign.Right);

						Dictionary<Symbol, Symbol> vars = [];
						foreach (var id in left)
						{
							if (id.Identity is VarSymbol var)
								vars.Add(id.Identity, new VarSymbol(var.Name, var.Source, var.Type));
							else
								throw new Exception();
						}

						foreach (var id in right)
						{
							id.Identity = vars[id.Identity];
						}
					}
					else
						throw new Exception();

					break;
				}
				case TokenType.FuncDecl:
				{
					ParseExpr(ref newExpr, Lex.Next());

					if (newExpr is CallExpr funcCall && funcCall.Left is IdentityExpr id)
					{
						if (!ParseStmt(ref newStmt))
							//change error
							throw new Exception();

						var arguments = GetIdentities(funcCall.Right).Select(e => 
						{
							if (e.Identity is VarSymbol v)
								return v;
							else
								//change error
								throw new Exception();
						}).ToList();

						newStmt = new FuncDeclStmt(id.Identity.Name, Lex.Filepath, newStmt)
						{
							Scope = newScope,
						};
						newScope.AddSymbol((newStmt as FuncDeclStmt).Name);
						id.Identity = (newStmt as FuncDeclStmt).Name;
					}
					else
					{
						NewError(new MalformedExprError(firstToken.Range));
						return true;
					}

					break;
				}
				case TokenType.Elif:
				case TokenType.If:
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
							return true;
						}
						else
						{
							NewError(new UnexpectedToken(firstToken.Range, firstToken));
							return true;
						}
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
						return true;
					}
					else
					{
						NewError(new UnexpectedToken(firstToken.Range, firstToken));
						return true;
					}
				}
				case TokenType.OpenCurlyBracket:
				{
					Lex.Next();
					var innerStmt = ParseBranch();
					newStmt = new CompoundStmt()
					{
						Scope = newScope,
						Statements = innerStmt
					};

					break;
				}
				case null:
				case TokenType.CloseCurlyBracket:
				{
					return false;
				}
				default:
				{
					if (Compiler.GetAttributeFromEnum<TokenType, UnimplementedAttribute>(firstToken.Which) != null)
					{
						NewError(new UnimplementedError(firstToken));
						return true;
					}
					ParseExpr(ref newExpr, firstToken);
					if (newExpr == null)
					{
						NewError(new SyntaxError(firstToken.Range, "Unknown statement '{0}'", firstToken.Text));
						return true;
					}

					newStmt = new ExpressionStmt()
					{
						Scope = newScope,
						Expression = newExpr,
					};

					break;
				}
			}

			return newStmt != null;
		}

		public LinkedList<Stmt> ParseBranch()
		{
			LinkedList<Stmt> innerStmts = [];

			Stmt newStmt = null;
			while (ParseStmt(ref newStmt))
			{
				innerStmts.AddLast(newStmt);
			}

			return innerStmts;
		}

		public void ParseFile()
		{
			Statements = ParseBranch();
		}

		public Parser(Lexer lex)
		{
			Lex = lex;
		}
		public Parser(Lexer lex, Scope rootScope)
		{
			Lex = lex;
			RootScope = rootScope;
		}
	}
}
