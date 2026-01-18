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

		protected void ParseExpr(ref Expr rootExpr, Token? currentToken, Scope scope)
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

					//if (Lex.PeekNext()?.Which == TokenType.Type)
					//{
					//	Lex.Next();
					//	var typeName = Lex.Next();
					//	if (typeName == null || typeName.Which != TokenType.Identifier)
					//		throw new UnexpectedTokenException(typeName, typeName?.Range);
					//	var typeSym = scope.FindSymbolByName<TypeSymbol>(typeName.Text) 
					//	?? throw new UndefinedSymbolException(new TypeSymbol(typeName.Text, Lex.Filepath), typeName.Range);

					//	if (newExpr is IdentityExpr i) i.Type = typeSym;
					//	else throw new Exception();
					//}
					
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
				//case TokenType.Type:
				//{
				//	var typeToken = Lex.Next()
				//	?? throw new Exception();
				//	if (typeToken.Which != TokenType.Identifier)
				//		throw new UnexpectedTokenException(TokenType.Identifier, typeToken, typeToken.Range);

				//	var typeSymbol = scope.FindSymbolByName<TypeSymbol>(typeToken.Text)
				//	?? throw new UndefinedSymbolException(typeToken.Text, typeToken.Range);
				//	rootExpr.Type = typeSymbol;

				//	if (rootExpr is IdentityExpr sym && sym.Identity == TypeSymbol.Any)
				//	{
				//		switch (sym.Identity)
				//		{
				//			case VarSymbol var:
				//			{
				//				if (var.Type == TypeSymbol.Any)
				//					var.Type = typeSymbol;
				//				else
				//					//
				//					throw new Exception();
				//				break;
				//			}
				//		}
				//	}

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

		protected List<VarSymbol> ParsePattern(Scope scope, Token? firstToken)
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
						nextToken = Lex.Next()
						?? throw new Exception();

						if (nextToken.Which != TokenType.Identifier)
							throw new UnexpectedTokenException(TokenType.Identifier, nextToken, nextToken.Range);
						TypeSymbol? newType;
						if ((newType = scope.FindSymbolByName<TypeSymbol>(nextToken.Text)) == null)
							throw new UndefinedSymbolException(nextToken.Text, nextToken.Range);

						res.Append(new VarSymbol(varToken.Text, Lex.Filepath, newType));
						//scope.AddSymbol(new VarSymbol(varToken.Text, Lex.Filepath, newType));
						break;
					}
					case TokenType.Comma:
					case TokenType.CloseBracket:
					case TokenType.Assign:
					{
						res.Append(new VarSymbol(varToken.Text, Lex.Filepath));
						//scope.AddSymbol(new VarSymbol(varToken.Text, Lex.Filepath));
						if (nextToken.Which == TokenType.Comma) continue;
						break;
					}
					default:
						throw new UnexpectedTokenException(TokenType.CloseBracket, nextToken, nextToken.Range);
				}
			}

			if (firstToken != null) Lex.Goto(firstToken);
			return res;
		}

		protected List<VarSymbol> ParseVarPattern(Scope scope, Token? firstToken)
		{
			return ParsePattern(scope, firstToken);
		}

		protected List<TypeSymbol> ParseTypePattern(Scope scope, Token? firstToken)
		{
			return [.. ParsePattern(scope, firstToken).Select(v => v.Type)];
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
					
					var pattern = ParseVarPattern(newScope, varToken);
					pattern.ForEach(v =>
					{
						if (!newScope.IsInScope(v))
							newScope.AddSymbol(v);
						else
							throw new RedeclaredSymbolException(v, varToken.Range);
					});

					ParseExpr(ref newExpr, varToken, newScope);
					if (newExpr == null && firstToken.Which == TokenType.ConstDecl)
						throw new SyntaxErrorException("No value given to initialize constant '{0}'",
							varToken.Range, varToken.Text);

					break;
				}
				case TokenType.FuncDecl:
				{
					var funcName = Lex.Next();
					if (funcName.Which != TokenType.Identifier)
						throw new UnexpectedTokenException(TokenType.Identifier, funcName, funcName.Range);

					FuncSymbol funcSymbol = new(funcName.Text, Lex.Filepath);
					newScope.AddSymbol(funcSymbol);
					var arguments = ParseVarPattern(newScope, null);

					if (!ParseStmt(ref newStmt))
						throw new Exception();

					newStmt = new FuncDeclStmt()
					{
						Scope = newScope,
						Name = funcSymbol,
						Arguments = arguments,
						Value = newStmt
					};
					funcSymbol.Declaration = newStmt as FuncDeclStmt;

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
