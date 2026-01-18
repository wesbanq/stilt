using stilt.AST;
using System.ComponentModel.DataAnnotations;

namespace stilt
{
	public class Parser
	{
		[Required]
		public Lexer Lex;
		public Stmt? RootStmt;
		public Stmt? LastStmt;

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

		public void InsertInto(ref Expr rootExpr, Expr? newExpr)
		{
			if (rootExpr == null || newExpr == null)
			{
				if (newExpr != null)
				{
					newExpr.Bracketed = true;
					rootExpr = newExpr;
				}
				return;
			}
			var toReplace = rootExpr.FindFirstPrecedenceOrNull(newExpr.Precedence, out var parent);
			if (toReplace == null && parent == null)
				throw new Exception();

			if (newExpr is ISpreadable spreadable)
			{
				if (toReplace != null)
				{
					spreadable.Shove(toReplace);
				}
				
				if (parent != null)
				{
					if (parent is ISpreadable sParent)
						sParent.Replace(toReplace, newExpr);
					else
						throw new Exception();
				}
				else
				{
					rootExpr.Bracketed = false;
					rootExpr = newExpr;
					rootExpr.Bracketed = true;
				}
			}
			else
			{
				if (parent is ISpreadable newSpreadable)
				{
					//Program.Dump(parent);
					newSpreadable.Shove(newExpr);
				}
				else
				{
					//Program.Dump(parent);
					throw new ArgumentException();
				}
			}
		}

		public void ParseExpr(ref Expr rootExpr, Token? firstToken)
		{
			//var firstToken = Lex.Next();
			if (firstToken == null) return;
			Expr? newExpr = null;

			switch (firstToken?.Which)
			{
			//bracketed
				case TokenType.Identifier:
				{
					VarSymbol newSym = new(firstToken.Text, Lex.Filepath);
					//if (!LastStmt?.Scope.IsInScope(newSym) ?? true)
					//	throw new UndefinedSymbolException(newSym, firstToken.Range);

					newExpr = new IdentitiyExpr()
					{
						Identity = newSym	
					};

					break;
				}
				case TokenType.FormatStringLiteral:
				//for format strings turn into String.Format(string) in the future
				case TokenType.StringLiteral:
				case TokenType.NumericLiteral:
				{
					newExpr = new LiteralExpr()
					{
						Value = firstToken.Text
					};

					break;
				}
				case TokenType.OpenSquareBracket:
				case TokenType.OpenBracket:
				{
					//if (firstToken.Which == TokenType.OpenSquareBracket)
					//{
					//	IndexExpr indexExpr = new();
					//}
					ParseExpr(ref newExpr, Lex.Next());
					break;
				}
				case TokenType.StmtSeparator:
				case TokenType.CloseBracket:
				case TokenType.CloseSquareBracket:
				{
					return;
				}
				default:
				{
					var operatorAttr = Compiler.GetAttributeFromEnum<TokenType, OperatorAttribute>(firstToken.Which);
					if (operatorAttr == null)
						throw new UnexpectedTokenException(firstToken, firstToken?.Range);
					
					newExpr = Activator.CreateInstance(operatorAttr.AssociatedExpr, operatorAttr.Precedence) as Expr;

					break;
				}
			}

			InsertInto(ref rootExpr, newExpr);
			Program.Dump(rootExpr);
			ParseExpr(ref rootExpr, Lex.Next());
		}

		public Stmt ParseStmt()
		{
			//var firstToken = Lex.Next();

			Expr newExp = null;
			ParseExpr(ref newExp, Lex.CurrentToken);
			ExpressionStmt newStmt = new(LastStmt)
			{
				Expression = newExp,
			};
			return newStmt;

			//switch (firstToken.Which)
			//{
			//	case TokenType.ConstDecl:
			//	case TokenType.VarDecl:
			//	{
			//		if (Lex.Next().Which != TokenType.Identifier)
			//			//throw new UnexpectedTokenException();
			//			throw new Exception();
			//		Expr newExpr = null;
			//		ParseExpr(ref newExpr);
			//		if (newExpr == null && firstToken.Which == TokenType.ConstDecl)
			//			throw new SyntaxErrorException("No value given to initialize constant '{0}'", 
			//				Lex.CurrentToken.Range, Lex.CurrentToken.Text);
			//		var declStmt = new VarDeclStmt()
			//		{
			//			Name = new VarSymbol(Lex.Next().Text, Lex.Filepath),
			//			IsConst = firstToken.Which == TokenType.ConstDecl,
			//			Value = newExpr,
			//			Scope = LastStmt?.Scope ?? new(),
			//			Prev = LastStmt
			//		};
			//		if (LastStmt != null)
			//		{
			//			if (LastStmt.Scope.IsInScope(declStmt.Name) == true)
			//				throw new RedeclaredSymbolException(declStmt.Name, firstToken.Range);
			//			LastStmt.Scope.Symbols.Add(declStmt.Name);
			//			LastStmt.Next = declStmt;
			//		}
			//		LastStmt = declStmt;
			//		break;
			//	}
			//	case TokenType.If:
			//	{
			//		break;
			//	}
			//	default:
			//	{
			//		Expr newExpr = null;
			//		ParseExpr(ref newExpr);
			//		if (newExpr == null)
			//			throw new SyntaxErrorException("Unknown statement '{0}'", Lex.CurrentToken.Range, Lex.CurrentToken.Text);
			//		ExpressionStmt stmt = new()
			//		{
			//			Expression = newExpr,
			//			Scope = LastStmt?.Scope ?? new(),
			//			Prev = LastStmt
			//		};
			//		if (LastStmt != null)
			//			LastStmt.Next = stmt;
			//		LastStmt = stmt;
			//		break;
			//	}
			//}
		}

		public Parser(Lexer lex)
		{
			Lex = lex;
		}
	}
}
