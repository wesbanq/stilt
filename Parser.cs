using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using stilt.AST;

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
				: base($"Redeclared '{symbol.Name}' in file {range.Filename}\n\t@ {range.ToLineAndColumnF()}")
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

		public Expr? ParseExpr(Expr? rootExpr = null)
		{
			var firstToken = Lex.CurrentToken;

			switch (firstToken?.Which)
			{
				case TokenType.Identifier:
				{
					VarSymbol newSym = new(firstToken.Text, Lex.Filepath);
					if (!LastStmt?.Scope.IsInScope(newSym) ?? true)
						throw new UndefinedSymbolException(newSym, firstToken.Range);

					IdentitiyExpr newExpr = new()
					{ 
						Identity = newSym
					};
					rootExpr ??= newExpr;

					return ParseExpr(rootExpr);
				}
				case TokenType.OpenBracket:
				{
					return ParseExpr(rootExpr?.FindFirstNull());
				}
				case TokenType.StmtSeparator:
				case TokenType.CloseBracket:
				{
					return rootExpr;
				}
				default:
				{
					throw new UnexpectedTokenException(firstToken, firstToken?.Range);
				}
			}


		}

		//public Expr? ParseExpr()
		//{
		//	Expr? rootExpr = null;

		//	for 
		//	(;
		//		Lex.Next().Which switch 
		//		{ 
		//			TokenType.StmtSeparator => false,
		//			TokenType.OpenCurlyBracket => false,
		//			TokenType.CloseCurlyBracket => false,
		//			_ => true
		//		}
		//	;)
		//	{
		//		rootExpr = ParseExpr(rootExpr);
		//	}

		//	return rootExpr;
		//}

		public void ParseStmt()
		{
			var firstToken = Lex.Next();

			switch (firstToken.Which)
			{
				case TokenType.ConstDecl:
				case TokenType.VarDecl:
				{
					if (Lex.Next().Which != TokenType.Identifier)
						//throw new UnexpectedTokenException();
						throw new Exception();
					var newExpr = ParseExpr();
					if (newExpr == null && firstToken.Which == TokenType.ConstDecl)
						throw new SyntaxErrorException("No value given to initialize constant '{0}'", 
							Lex.CurrentToken.Range, Lex.CurrentToken.Text);
					var declStmt = new VarDeclStmt()
					{
						Name = new VarSymbol(Lex.Next().Text, Lex.Filepath),
						IsConst = firstToken.Which == TokenType.ConstDecl,
						Value = newExpr,
						Scope = LastStmt?.Scope ?? new(),
						Prev = LastStmt
					};
					if (LastStmt != null)
					{
						if (LastStmt.Scope.IsInScope(declStmt.Name) == true)
							throw new RedeclaredSymbolException(declStmt.Name, firstToken.Range);
						LastStmt.Scope.Symbols.Add(declStmt.Name);
						LastStmt.Next = declStmt;
					}
					LastStmt = declStmt;
					break;
				}
				case TokenType.If:
				{
					break;
				}
				default:
				{
					var newExpr = ParseExpr();
					if (newExpr == null)
						throw new SyntaxErrorException("Unknown statement '{0}'", Lex.CurrentToken.Range, Lex.CurrentToken.Text);
					ExpressionStmt stmt = new()
					{
						Expression = newExpr,
						Scope = LastStmt?.Scope ?? new(),
						Prev = LastStmt
					};
					if (LastStmt != null)
						LastStmt.Next = stmt;
					LastStmt = stmt;
					break;
				}
			}
		}

		public void ParseBranch()
		{
			
		}
	}
}
