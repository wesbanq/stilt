using stilt.AST;
using System;
using System.ComponentModel.DataAnnotations;
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
				: base(start, "Malformed expression")
			{ }
		}

		public class UnexpectedToken : SyntaxError
		{
			public TokenType Expected;
			public Token Got;

			public UnexpectedToken(FileRange pos, TokenType expected, Token? got)
				: base(pos, $"Unexpected token: '{got.Which}'\nExpected: '{expected}'")
			{
				Expected = expected;
				Got = got;
			}

			public UnexpectedToken(FileRange? pos, Token? got)
				: base(pos, $"Unexpected token: {got?.Range.Text}")
			{
				if (got != null && pos != null)
				{
					Got = got;
				}
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
					{
						if (toReplace == null)
							sParent.InsertChild(newExpr);
						else
							sParent.ReplaceChild(toReplace, newExpr);
					}
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

		protected List<Expr> CreateOperatorExpr(Token token)
		{
			var operatorAttr = Compiler.GetAttributesFromEnum<TokenType, OperatorAttribute>(token.Which);
			if (operatorAttr == null)
				throw new UnexpectedToken(token.Range, token);

			List<Expr> exprs = [];
			foreach (var op in operatorAttr)
			{
				exprs.Add(Activator.CreateInstance(op.AssociatedExpr, op.Precedence) as Expr);
			}

			return exprs
			//change error
			?? throw new Exception();
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
					throw new Exception();
				}
			}
			return res;
		}

		//protected List<VarSymbol> ParsePattern(Scope scope, Token? firstToken, bool ignoreType = false)
		//{
		//	Token varToken;
		//	List<VarSymbol> res = [];

		//	while (true)
		//	{
		//		varToken = Lex.Next();
		//		if (varToken.Which == TokenType.OpenBracket) continue;
		//		var nextToken = Lex.Next();
		//		if (nextToken.Which == TokenType.OpenBracket) break;
		//		switch (nextToken.Which)
		//		{
		//			case TokenType.Type:
		//			{
		//				if (ignoreType)
		//					throw new UnexpectedToken(nextToken.Range, nextToken);

		//				nextToken = Lex.Next()
		//				?? throw new Exception();

		//				if (nextToken.Which != TokenType.Identifier)
		//					throw new UnexpectedToken(nextToken.Range, TokenType.Identifier, nextToken);

		//				var typeToken = Lex.Next();
		//				if (typeToken.Which != TokenType.Identifier)
		//					throw new UnexpectedToken(typeToken.Range, TokenType.Identifier, typeToken);

		//				res.Append(new VarSymbol(varToken.Text, new TypeSymbol(typeToken.Text)));
		//				break;
		//			}
		//			case TokenType.Comma:
		//			{
		//				res.Append(new VarSymbol(varToken.Text));
		//				continue;
		//			}
		//			case TokenType.CloseBracket:
		//			case TokenType.Assign:
		//			{
		//				res.Append(new VarSymbol(varToken.Text));
		//				break;
		//			}
		//			default:
		//				throw new UnexpectedToken(nextToken.Range, TokenType.CloseBracket, nextToken);
		//		}
		//	}

		//	if (firstToken != null) Lex.Goto(firstToken);
		//	return res;
		//}

		protected void ParseExpr(ref Expr rootExpr, Token? currentToken)
		{
			if (currentToken == null) return;
			Expr newExpr = null;

			switch (currentToken.Which)
			{
				case TokenType.Identifier:
				{
					var newSym = new VarSymbol(currentToken.Range.Text);
					newExpr = new IdentityExpr()
					{
						Identity = newSym
					};
					
					break;
				}
				//TODO
				case TokenType.FormatStringLiteral:
				//
				case TokenType.StringLiteral:
				{
					newExpr = new StringLiteralExpr(currentToken.Range.Text);

					break;
				}
				case TokenType.NumericLiteral:
				{
					newExpr = new NumLiteralExpr(int.Parse(currentToken.Range.Text));

					break;
				}
				case TokenType.Null:
				{
					newExpr = new NullLiteralExpr();

					break;
				}
				case TokenType.OpenSquareBracket:
				{
					//TODO
					newExpr = CreateOperatorExpr(currentToken).First();
					ParseExpr(ref newExpr, Lex.Next());
					break;
				}
				case TokenType.OpenBracket:
				{
					//empty brackets are equal to null
					ParseExpr(ref newExpr, Lex.Next());
					if (newExpr == null)
						newExpr = new NullLiteralExpr();

					var a = rootExpr?.FindFirstPrecedenceOrNull(newExpr.Precedence, out var _);
					//a == null - expecting operand / a != null - expecting operator
					if (a != null)
					{
						var callExpr = CreateOperatorExpr(currentToken).First() as CallExpr;
						callExpr.Right = newExpr;
						newExpr = callExpr;
					}

					break;
				}
				case TokenType.StmtSeparator:
				case TokenType.CloseBracket:
				case TokenType.CloseSquareBracket:
				case TokenType.OpenCurlyBracket:
				{
					rootExpr?.Bracketed = true;
					return;
				}
				case TokenType.Type:
				{
					//TODO
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
					if (Compiler.GetAttributeFromEnum<TokenType, UnimplementedAttribute>(currentToken.Which) != null)
						throw new UnimplementedError(currentToken);
					var possibleExprs = CreateOperatorExpr(currentToken)
						?? throw new UnexpectedToken(currentToken.Range, currentToken);

					if (Lex.PeekNext()?.Which == TokenType.Assign)
					{
						Lex.Next();
						possibleExprs = possibleExprs.Where(e => e is BinaryExpr).Select(e => 
							new AssignExpr(Compiler.GetAttributeFromEnum<TokenType, OperatorAttribute>(TokenType.Assign).Precedence)
							{
								Operation = e as BinaryExpr
							} as Expr
						).ToList();
						if (possibleExprs.Count != 1)
							throw new MalformedExprError(currentToken.Range);

						newExpr = possibleExprs.First();
						break;
					}

					possibleExprs = possibleExprs.OrderByDescending(e =>
					{
						if (e is IOperator op)
							return op.GetChildren().Count();
						else
							throw new Exception();
					}).ToList();

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
				//array/table lits
				//remove recursion
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

		protected bool ParseStmt(ref Stmt newStmt)
		{
			var firstToken = Lex.CurrentToken;
			Expr newExpr = null;
			Scope newScope = new(Statements.Last?.Value.Scope ?? RootScope);
			List<Symbol> newSymbols = [];

			switch (firstToken?.Which)
			{
				case TokenType.VarDecl:
				case TokenType.ConstDecl:
				{
					var varToken = Lex.Next()
					?? throw new UnexpectedToken(firstToken.Range, null);
					if (varToken.Which != TokenType.Identifier && varToken.Which != TokenType.OpenBracket)
						throw new UnexpectedToken(varToken.Range, TokenType.Identifier, varToken);

					ParseExpr(ref newExpr, varToken);

					if (newExpr is AssignExpr assign)
					{
						//unnecessary?
						if (assign.Left == null || assign.Right == null)
							throw new MalformedExprError(firstToken.Range);

						//ReplaceIdentities
						newSymbols = AddTempSymToScope(assign.Left, newScope);
					}
					else
					{
						if (firstToken.Which == TokenType.ConstDecl)
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
						IsConst = firstToken.Which == TokenType.ConstDecl,
						Value = newExpr
					};

					break;
				}
				case TokenType.FuncDecl:
				//TODO restict macros to only be in types
				case TokenType.MacroDecl:
				{
					ParseExpr(ref newExpr, Lex.Next());

					if (newExpr is CallExpr funcCall && funcCall.Left is IdentityExpr id)
					{
						if (!ParseStmt(ref newStmt))
							throw new UnexpectedToken(firstToken.Range, null);

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
							return true;
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
						return true;
					}
					else
						throw new UnexpectedToken(firstToken.Range, firstToken);
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
				case TokenType.StmtSeparator:
				case TokenType.CloseCurlyBracket:
				{
					return false;
				}
				default:
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

			return newStmt != null;
		}

		protected LinkedList<Stmt> ParseBranch()
		{
			LinkedList<Stmt> innerStmts = [];

			while (true)
			{
				Stmt newStmt = null;
				if (Args.Throw)
				{
					if (!ParseStmt(ref newStmt))
						break;
					if (Lex.CurrentToken?.Which == TokenType.StmtSeparator)
						Lex.Next();
					else
						throw new ArgumentException("sdgfsdgrsgfr");
					
					innerStmts.AddLast(newStmt);
				}
				else
				{
					try
					{
						if (!ParseStmt(ref newStmt))
							break;
						if (Lex.CurrentToken?.Which == TokenType.StmtSeparator)
							Lex.Next();
						else
							throw new ArgumentException("sdgfsdgrsgfr");
					}
					catch (SyntaxError se)
					{
						NewError(se);
						if (se.Severity == ErrorSeverity.Critical)
						{
							se.Print();
							break;
						}
						Lex.SkipStmt();
					}
					finally
					{
						if (newStmt != null)
							innerStmts.AddLast(newStmt);
					}
				}
			}

			return innerStmts;
		}

		public void ParseFile()
		{
			Statements = ParseBranch();
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
