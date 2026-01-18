using stilt.AST;
using System.ComponentModel.DataAnnotations;

namespace stilt
{
	public class Parser
	{
		public Lexer Lex;
		public LinkedList<Stmt> Statements = new();
		public Scope RootScope = new();

		public class RedeclaredSymbolException : Exception
		{
			[Required] FileRange Range;
			
			public RedeclaredSymbolException(Symbol symbol, FileRange range)
				: base($"Multiple declarations for symbol '{symbol.Name}'\n\t in {range.Filename} @ {range.ToLineAndColumnF()}")
			{
				Range = range;
			}
		}

		public class SyntaxErrorException : Exception
		{
			[Required] public FileRange Position;

			public SyntaxErrorException(string message, FileRange pos, params string[] strings)
				: base(string.Format(message, strings) + $"\n\tIn file {pos.Filename}, " +
				$"at line: {pos.ToLineAndColumn().line}, col: {pos.ToLineAndColumn().column}")
			{
				Position = pos;
			}
			public SyntaxErrorException(string message, FileRange pos)
				: base(message + $"\n\tIn file {pos.Filename}, " +
				$"at line: {pos.ToLineAndColumn().line}, col: {pos.ToLineAndColumn().column}")
			{
				Position = pos;
			}
		}

		public class UndefinedSymbolException : Exception
		{
			public string Got;
			[Required] public FileRange Position;

			public UndefinedSymbolException(Symbol symbol, FileRange pos)
				: base($"Unknown symbol: '{symbol.Name}' found\n\tin file: {pos.Filename} @ {pos.ToLineAndColumnF()}")
			{
				Got = symbol.Name;
				Position = pos;
			}
			public UndefinedSymbolException(string symbolName, FileRange pos)
				: base($"Unknown symbol: '{symbolName}' found\n\tin file: {pos.Filename} @ {pos.ToLineAndColumnF()}")
			{
				Got = symbolName;
				Position = pos;
			}
		}

		public class MalformedExprException : Exception
		{
			public FileRange Start;

			public MalformedExprException(FileRange start)
				: base($"Malformed expression starting:\n\t@ {start.ToLineAndColumnF()}")
			{
				Start = start;
			}
		}

		public class UnexpectedTokenException : Exception
		{
			public TokenType Expected;
			public Token Got;
			public FileRange Position;

			public UnexpectedTokenException(TokenType expected, Token got, FileRange pos)
				: base($"Unexpected character at {pos.ToLineAndColumn()}\nExpected: '{Token.GetRulesFromType(expected).First()}'\nGot: '{got.Text}'")
			{
				Expected = expected;
				Got = got;
				Position = pos;
			}

			public UnexpectedTokenException(Token? got, FileRange? pos)
				: base($"Unexpected token: {got?.Text}\n\t@ {pos?.ToLineAndColumnF()}")
			{
				if (got != null && pos != null)
				{
					Got = got;
					Position = pos;
				}
			}
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
					throw new ArgumentException();
			}
		}

		protected Expr? CreateOperatorExpr(Token token)
		{
			var operatorAttr = Compiler.GetAttributeFromEnum<TokenType, OperatorAttribute>(token.Which);
			if (operatorAttr == null)
				throw new UnexpectedTokenException(token, token.Range);

			var newExpr = Activator.CreateInstance(operatorAttr.AssociatedExpr, operatorAttr.Precedence) as Expr;
			return newExpr ?? throw new Exception();
		}

		protected List<VarSymbol> ParsePattern(Scope scope, Token? firstToken, bool ignoreType = false)
		{
			Token varToken;
			List<VarSymbol> res = [];

			//while ((varToken = Lex.Next())?.Which == TokenType.Identifier)
			while (true)
			{
				varToken = Lex.Next();
				if (varToken.Which == TokenType.OpenBracket) continue;
				var nextToken = Lex.Next();
				if (nextToken.Which == TokenType.OpenBracket) break;
				switch (nextToken.Which)
				{
					case TokenType.Type:
					{
						if (ignoreType) 
							throw new UnexpectedTokenException(nextToken, nextToken.Range);

						nextToken = Lex.Next()
						?? throw new Exception();

						if (nextToken.Which != TokenType.Identifier)
							throw new UnexpectedTokenException(TokenType.Identifier, nextToken, nextToken.Range);

						var typeToken = Lex.Next();
						if (typeToken.Which != TokenType.Identifier)
							throw new UnexpectedTokenException(TokenType.Identifier, typeToken, typeToken.Range);

						res.Append(new VarSymbol(varToken.Text, new TypeSymbol(typeToken.Text)));
						break;
					}
					case TokenType.Comma:
					{
						res.Append(new VarSymbol(varToken.Text));
						continue;
					}
					case TokenType.CloseBracket:
					case TokenType.Assign:
					{
						res.Append(new VarSymbol(varToken.Text));
						break;
					}
					default:
						throw new UnexpectedTokenException(TokenType.CloseBracket, nextToken, nextToken.Range);
				}
			}

			if (firstToken != null) Lex.Goto(firstToken);
			return res;
		}

		//protected List<VarSymbol> ParseVarPattern(Scope scope, Token? firstToken)
		//{
		//	return ParsePattern(scope, firstToken);
		//}

		//protected List<TypeSymbol> ParseTypePattern(Scope scope, Token? firstToken)
		//{
		//	var a = ParsePattern(scope, firstToken).Select(static v => v.Type);
		//	if (a.Any(l => l.Count > 1))
		//		throw new Exception();

		//	return [.. a.Select(l => l)];
		//}

		protected void ParseExpr(ref Expr rootExpr, Token? currentToken, Scope scope)
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
				//for format strings turn into String.Format(string) in the future
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
					newExpr = CreateOperatorExpr(currentToken);
					ParseExpr(ref newExpr, Lex.Next(), scope);
					break;
				}
				case TokenType.OpenBracket:
				{
					newExpr = CreateOperatorExpr(currentToken);
					var a = rootExpr.FindFirstPrecedenceOrNull(newExpr.Precedence, out var parent);
					if (a == null && parent != null)
						newExpr = null;

					ParseExpr(ref newExpr, Lex.Next(), scope);
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
					var typeToken = Lex.Next()
						?? throw new Exception();
					if (typeToken.Which != TokenType.Identifier && typeToken.Which != TokenType.OpenBracket)
						throw new UnexpectedTokenException(TokenType.Identifier, typeToken, typeToken.Range);

					List<TypeSymbol> typeSymbol;
					if (typeToken.Which == TokenType.OpenBracket)
					{
						var a = ParsePattern(scope, typeToken, true).Select(t => t.SingletonType).ToList()
						?? throw new UnexpectedTokenException(typeToken, typeToken.Range);
						if (a.Any(t => t == null) || a == null)
							throw new Exception();

						typeSymbol = a;
					}
					else
					{
						typeSymbol = [new TypeSymbol(typeToken.Text)];
					}
					rootExpr.Type = typeSymbol;

					if (rootExpr is IdentityExpr sym)
					{
						switch (sym.Identity)
						{
							case VarSymbol var:
							{
								if (var.Type == null || var.Type.Count == 1 && var.Type[0] == TypeSymbol.Any)
									var.Type = typeSymbol;
								else
									throw new Exception();
								break;
							}
						}
					}

					break;
				}
				default:
				{
					newExpr = CreateOperatorExpr(currentToken);
					if (Lex.PeekNext()?.Which == TokenType.Assign
						&& newExpr is BinaryExpr)
					{
						Lex.Next();
						var precedence = Compiler.GetAttributeFromEnum<TokenType, OperatorAttribute>(TokenType.Assign).Precedence;
						newExpr = new AssignExpr(precedence)
						{
							Operation = newExpr as BinaryExpr
						};
					}
					//TODO unary & ternary
					//func calls, arrays, index
					break;
				}
			}

			InsertIntoExprTree(ref rootExpr, newExpr);
			ParseExpr(ref rootExpr, Lex.Next(), scope);
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
						throw new UnexpectedTokenException(TokenType.Identifier, varToken, varToken.Range);

					ParseExpr(ref newExpr, varToken, newScope);

					if (newExpr == null && firstToken.Which == TokenType.ConstDecl)
						throw new SyntaxErrorException("No value given to initialize constant '{0}'",
							varToken.Range, varToken.Text);

					if (newExpr is AssignExpr assign)
					{
						if (assign.Left == null || assign.Right == null)
							throw new MalformedExprException(firstToken.Range);

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
					ParseExpr(ref newExpr, Lex.Next(), newScope);

					if (newExpr is CallExpr funcCall && funcCall.Left is IdentityExpr id)
					{
						FuncSymbol funcSymbol = new(id.Identity.Name, Lex.Filepath);
						newScope.AddSymbol(funcSymbol);
						id.Identity = funcSymbol;

						if (!ParseStmt(ref newStmt))
							throw new Exception();

						var arguments = GetIdentities(funcCall.Right).Select(e => 
						{
							if (e.Identity is VarSymbol v)
								return v;
							else
								throw new MalformedExprException(firstToken.Range);
						}).ToList();

						newStmt = new FuncDeclStmt()
						{
							Scope = newScope,
							Name = funcSymbol,
							Value = newStmt
						};
						funcSymbol.Declaration = newStmt as FuncDeclStmt;
						funcSymbol.Arguments = arguments;
						funcSymbol.Return = funcCall.Type;
					}
					else
						throw new MalformedExprException(firstToken.Range);

					break;
				}
				case TokenType.If:
				{
					ParseExpr(ref newExpr, Lex.Next(), newScope);
					ParseStmt(ref newStmt);

					newStmt = new IfStmt()
					{
						Scope = newScope,
						Condition = newExpr,
						NextIf = newStmt,
					};

					break;
				}
				case TokenType.Else:
				{
					if (Statements.Last?.Value is IfStmt ifStmt)
					{
						ParseStmt(ref newStmt);
						ifStmt.NextElse = newStmt;
						return true;
					}
					else
						throw new UnexpectedTokenException(firstToken, firstToken.Range);
				}
				case TokenType.OpenCurlyBracket:
				{
					var innerStmt = new Parser(Lex, newScope);
					innerStmt.ParseBranch();
					newStmt = new CompoundStmt()
					{
						Scope = newScope,
						Statements = innerStmt.Statements
					};

					break;
				}
				case null:
				case TokenType.CloseCurlyBracket:
				{
					break;
				}
				default:
				{
					ParseExpr(ref newExpr, firstToken, newScope);
					if (newExpr == null)
						throw new SyntaxErrorException("Unknown statement '{0}'", firstToken.Range, firstToken.Text);
					
					newStmt = new ExpressionStmt()
					{
						Scope = newScope,
						Expression = newExpr,
					};

					break;
				}
			}

			if (newStmt != null)
			{
				Statements.AddLast(newStmt);
				return true;
			}
			else
				return false;
		}

		public void ParseBranch()
		{
			Stmt newStmt = null;
			while (ParseStmt(ref newStmt))
			{
				Statements.AddLast(newStmt);
			}
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
