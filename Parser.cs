using stilt.AST;
using System.ComponentModel.DataAnnotations;

namespace stilt
{
	public class Parser
	{
		[Required]
		public Lexer Lex;
		public LinkedList<Stmt> Statements = new();
		//public Stmt? RootStmt;
		//public Stmt? LastStmt;

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
			[Required] public Symbol Got;
			[Required] public FileRange Position;

			public UndefinedSymbolException(Symbol symbol, FileRange pos)
				: base($"Unknown symbol: '{symbol.Name}' found\n\t@ {pos.ToLineAndColumnF()}")
			{
				Got = symbol;
				Position = pos;
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

		public void InsertIntoExprTree(ref Expr rootExpr, Expr? newExpr)
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

		public void ParseExpr(ref Expr rootExpr, Token? currentToken, Scope scope)
		{
			if (currentToken == null) return;
			Expr newExpr = null;

			switch (currentToken.Which)
			{
				case TokenType.Identifier:
				{
					var newSym = scope.FindSymbolByName<VarSymbol>(currentToken.Text);
					if (newSym == null)
						throw new UndefinedSymbolException(newSym, currentToken.Range);

					newExpr = new IdentityExpr()
					{
						Identity = newSym
					};

					if (Lex.PeekNext()?.Which == TokenType.Colon)
					{
						Lex.Next();
						var typeName = Lex.Next();
						if (typeName == null || typeName.Which != TokenType.Identifier)
							throw new UnexpectedTokenException(typeName, typeName?.Range);
						var typeSym = scope.FindSymbolByName<TypeSymbol>(typeName.Text) 
						?? throw new UndefinedSymbolException(new TypeSymbol(typeName.Text, Lex.Filepath), typeName.Range);

						if (newExpr is IdentityExpr i) i.Type = typeSym;
						else throw new Exception();
					}
					
					break;
				}
				case TokenType.FormatStringLiteral:
				//for format strings turn into String.Format(string) in the future
				case TokenType.StringLiteral:
				case TokenType.NumericLiteral:
				{
					newExpr = new LiteralExpr()
					{
						Value = currentToken.Text
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
					ParseExpr(ref newExpr, Lex.Next(), scope);
					break;
				}
				case TokenType.StmtSeparator:
				case TokenType.CloseBracket:
				case TokenType.CloseSquareBracket:
				case TokenType.OpenCurlyBracket:
				case TokenType.CloseCurlyBracket:
				{
					rootExpr.Bracketed = true;
					return;
				}
				//case TokenType.Colon:
				//{
				//	break;
				//}
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
			//Program.Dump(rootExpr);
			ParseExpr(ref rootExpr, Lex.Next(), scope);
		}

		public void ParseStmt()
		{
			var firstToken = Lex.CurrentToken;
			Expr newExpr = null;
			Stmt newStmt = null;
			Scope newScope = new(Statements.Last?.Value.Scope);

			switch (firstToken?.Which)
			{
				case TokenType.ConstDecl:
				case TokenType.VarDecl:
				{
					var varToken = Lex.Next();
					if (varToken.Which != TokenType.Identifier)
						throw new UnexpectedTokenException(TokenType.Identifier, varToken, varToken.Range);

					var newSymbol = new VarSymbol(varToken.Text, Lex.Filepath);
					if (Statements.Last?.Value.Scope.IsInScope(newSymbol) ?? false)
						throw new RedeclaredSymbolException(newSymbol, varToken.Range);
					newScope.AddSymbol(newSymbol);

					ParseExpr(ref newExpr, varToken, newScope);
					if (newExpr == null && firstToken.Which == TokenType.ConstDecl)
						throw new SyntaxErrorException("No value given to initialize constant '{0}'",
							varToken.Range, varToken.Text);

					newStmt = new VarDeclStmt()
					{
						Name = newSymbol,
						IsConst = firstToken.Which == TokenType.ConstDecl,
						Scope = newScope,
						Value = newExpr
					};

					break;
				}
				case TokenType.FuncDecl:
				{
					break;
				}
				case TokenType.If:
				{
					

					break;
				}
				default:
				{
					if (firstToken == null)
						throw new Exception();
					ParseExpr(ref newExpr, firstToken, newScope);
					if (newExpr == null)
						throw new SyntaxErrorException("Unknown statement '{0}'", firstToken.Range, firstToken.Text);
					ExpressionStmt stmt = new()
					{
						Expression = newExpr,
						Scope = newScope,
					};

					break;
				}
			}

			if (newStmt != null) Statements.AddLast(newStmt);
		}

		public Parser(Lexer lex)
		{
			Lex = lex;
		}
	}
}
