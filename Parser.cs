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

		public class UnexpectedEOF : SyntaxError
		{
			public UnexpectedEOF(Lexer lex)
				: base(lex.Text.EOF, "File unexpectedly ended")
			{ }
		}

		public void WriteErrors()
		{
			CompilationIssues.ForEach(m => m.Print());
		}

		protected void NewError(SyntaxError err)
		{
			CompilationIssues.Add(err);
			//if (err.Severity >= ErrorSeverity.Error)
			//	throw err;
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

			//needs another rework but im lazy
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
					//change error
					throw new Exception();
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
							throw new Exception();
					}
					else
						//change error
						throw new Exception();

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
				exprs.Add(Activator.CreateInstance(op.AssociatedExpr, op.Precedence, token.Range) as Expr);
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
					throw new MalformedExprError(expr.FullRange);
				}
			}
			return res;
		}

		protected bool ExpectingOperator(ref Expr expr)
		{
			//a == null - expecting operand / a != null - expecting operator
			return expr?.FindFirstPrecedenceOrNull(expr.Precedence, out var _) != null;
		}

		protected ArrayLiteralExpr ParseArrayLiteral(Token? currentToken)
		{
			if (currentToken is null)
				throw new UnexpectedEOF(Lex);
			
			Expr newExpr = null;
			ParseExpr(ref newExpr, currentToken);
			
			return newExpr is CommaExpr commaExpr
				? new ArrayLiteralExpr(currentToken.Range, [.. commaExpr.GetChildren()])
				: new ArrayLiteralExpr(currentToken.Range, [newExpr]);
		}

		protected TableLiteralExpr ParseTableLiteral(Token? currentToken)
		{
			if (currentToken is null)
				throw new UnexpectedEOF(Lex);

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
					//separate dictionary type for non string keys?????????????
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
						//SERGH9isehjgfz9rhg8jzr9ehjeifnh
						InnerRange = currentToken.Range,
						Identity = newSym,
					};
					break;
				}
				case TokenType.StringLiteral:
				//TODO		   VVVVVVVVVVVVVVVVVVV
				case TokenType.FormatStringLiteral:
				{
					newExpr = new StringLiteralExpr(currentToken.Range.Text, currentToken.Range);
					break;
				}
				case TokenType.NumericLiteral:
				{
					newExpr = new NumLiteralExpr(int.Parse(currentToken.Range.Text), currentToken.Range);
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
				case TokenType.CloseBracket:
				case TokenType.StmtSeparator:
				case TokenType.CloseSquareBracket:
				{
					//FIX FullRange will ignore the closing bracket
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
					if (currentToken.IsUnimplemented)
						throw new UnimplementedError(currentToken);
					var possibleExprs = CreateOperatorExpr(currentToken)
						?? throw new UnexpectedToken(currentToken.Range, currentToken);

					if (Lex.PeekNext()?.Which == TokenType.Assign)
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

						if (assign.Operation != null)
							throw new SyntaxError(assign.InnerRange, "Cannot use self-assignment operators in variable definition");

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
						Value = newExpr,
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
						Statements = innerStmt,
					};

					break;
				}
				case null:
				case TokenType.CloseCurlyBracket:
				{
					return false;
				}
				case TokenType.StmtSeparator:
				{
					return true;
				}
				default:
				//ExprStmt
				{
					if (firstToken.IsUnimplemented)
						throw new UnimplementedError(firstToken);

					ParseExpr(ref newExpr, firstToken);
					if (newExpr == null)
						throw new SyntaxError(firstToken.Range, $"Unknown statement: '{firstToken.Range.Text}'");
					
					newStmt = new ExprStmt()
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
				try
				{
					if (!ParseStmt(ref newStmt))
						break;
					if (Lex.CurrentToken == null)
						break;
					else if (Lex.CurrentToken?.Which == TokenType.StmtSeparator)
						Lex.Next();
					else
						throw new ArgumentException("sdgfsdgrsgfr");
				}
				catch (SyntaxError se)
				{
					NewError(se);
					if ((Args.Throw && se.Severity >= ErrorSeverity.Error) || se.Severity == ErrorSeverity.Critical)
					{
						se.Print();
						throw;
					}
					Lex.SkipStmt();
				}
				finally
				{
					if (newStmt != null)
						innerStmts.AddLast(newStmt);
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
