#pragma warning disable CS8601
using System.Diagnostics;
using System.Globalization;

namespace stilt
{
	public class Parser
	{
		public readonly ParserResult Result;
		
		private readonly Lexer Lex;
		private readonly ProgramArgs Args;
		private List<DecoratorObject> CurrentDecorators = [];
		private int _depth = 0;

		private void NewError(SyntaxError err)
		{
			Result.CompilationIssues.Add(err);
		}

		private ArrayLiteralExpr ParseArrayLiteral(Token currentToken)
		{
			if (currentToken.Which is TokenType.EOF)
				throw new UnexpectedEOF(currentToken.Range);
			
			Expr? newExpr = null;
			ParseExpr(ref newExpr, currentToken);
			
			if (newExpr is null)
				throw new SyntaxError(currentToken.Range, "Empty table literal.");
			
			return newExpr is CommaExpr commaExpr
				? new ArrayLiteralExpr(currentToken.Range, [.. commaExpr.GetChildren().OfType<Expr>()])
				: new ArrayLiteralExpr(currentToken.Range, [newExpr]);
		}

		private double ParseScientificLiteral(Token token)
		{
			var tokenText = token.Range.Text.Replace("_", "");
			var splitIndex = tokenText.IndexOfAny(['e', 'E']);

			var mantissa = Convert.ToDouble(tokenText[..splitIndex], CultureInfo.InvariantCulture);
			var exponent = Convert.ToInt64(tokenText[(splitIndex+1)..], CultureInfo.InvariantCulture);

			return mantissa * Math.Pow(10, exponent);
		}

		private void ParseExpr(ref Expr? rootExpr, Token currentToken)
		{
			Expr? newExpr = null;

			switch (currentToken.Which)
			{
				case TokenType.Identifier:
				{
					var newSym = new UnresolvedReference(currentToken.Range.Text, currentToken);
					newExpr = new IdentityExpr()
					{
						InnerRange = currentToken.Range,
						Identity = new SymbolReference(newSym),
					};
					break;
				}
				case TokenType.StringLiteral:
				{
					var text = currentToken.Range.Text;
					var firstQuote = text.IndexOfAny(['"', '\'']);
					var specifiers = text[..firstQuote].Split('r', 'f', 't', 'm');
					var raw = specifiers.Contains("r");
					var format = specifiers.Contains("f");
					var tagged = specifiers.Contains("t");
					var multi = specifiers.Contains("m");

					if (raw && (format || tagged || multi))
						throw new SyntaxError(currentToken.Range, "Raw string literals cannot have format, tagged, or multi specifiers.");

					var stringText = text[(firstQuote + 1)..^1];
					if (!multi)
						stringText = stringText.Replace("\n", "");
					if (!raw)
						stringText = Utils.Unescape(stringText);

					newExpr = new StringLiteralExpr(stringText, currentToken.Range, format, tagged, multi, raw);
					break;
				}
				case TokenType.True:
				case TokenType.False:
				{
					newExpr = new BoolLiteralExpr(currentToken.Which == TokenType.True, currentToken.Range);
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

					if (Expr.ExpectingOperator(rootExpr))
					{
						if (newExpr is null && currentToken.Which == TokenType.OpenSquareBracket)
							throw new SyntaxError(currentToken.Range, "No valid expression given as an index.");
						if (!currentToken.TryGetOperatorExprs<BinaryOperatorAttribute>(out var opExprs))
							throw new MalformedExpr(currentToken.Range);
						
						var opExpr = opExprs.First() as BinaryExpr;
						opExpr!.Right = newExpr;
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
					if (Expr.ExpectingOperator(rootExpr))
					{
						if (rootExpr is not null)
							rootExpr.Bracketed = true;
						return;
					}
					else
					{
						// newExpr = ParseTableLiteral(Lex.Next());
						throw new NotImplementedException();
					}
				}
				case TokenType.In:
				case TokenType.EOF:
				case TokenType.Then:
				case TokenType.Else:
				case TokenType.CloseBracket:
				case TokenType.StmtSeparator:
				case TokenType.StrictStmtSeparator:
				case TokenType.CloseCurlyBracket:
				case TokenType.CloseSquareBracket:
				{
					//FIX FullRange will ignore the closing bracket
					if (rootExpr is not null)
						rootExpr.Bracketed = true;
					return;
				}
				default:
				{
					if (currentToken.IsUnimplemented)
						throw new UnimplementedError(currentToken);

					if (!currentToken.TryGetOperatorExprs<OperatorAttribute>(out var possibleExprs))
						throw new UnexpectedToken(currentToken.Range, currentToken);
					if (Lex.NextIs(TokenType.Assign, TokenType.Type))
					{
						Lex.Next();
						var assignToken = new Token 
						{ 
							Which = TokenType.Assign, 
							Range = currentToken.Range + Lex.CurrentToken.Range 
						};
						var assignAttr = Utils.GetAttributeFromEnum<TokenType, OperatorAttribute>(TokenType.Assign);
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

						newExpr = assignExpr;
						break;
					}

					possibleExprs = [.. possibleExprs.OrderByDescending(e =>
					{
						if (e is IOperator op)
							return op.GetChildren().Count();
						else
							throw new UnreachableException();
					})];

					foreach (var expr in possibleExprs)
					{
                        var a = rootExpr?.FindFirstPrecedenceOrNull(expr.Precedence, out Expr? parent);

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
			}
			
			rootExpr = Expr.InsertIntoTree(rootExpr, newExpr);
			ParseExpr(ref rootExpr, Lex.Next());
		}

		/// <summary>
		/// Parses a type: '('type[','...]')' | identifier['('type[','...]')'].
		/// </summary>
		private UnresolvedReference ParseType()
		{
			if (Lex.CurrentIs(TokenType.OpenBracket))
			{
				Lex.GoPast(TokenType.OpenBracket);
				var innerArgs = new List<UnresolvedReference>();
				do
				{
					innerArgs.Add(ParseType());
				} while (Lex.CurrentIs(TokenType.Comma));
				Lex.ExpectThis(TokenType.CloseBracket);
				return new UnresolvedReference($"Tuple_{innerArgs.Count}", Lex.CurrentToken, typeArguments: innerArgs);
			}

			var typeNameToken = Lex.ExpectThis(TokenType.Identifier);

			var typeArgs = new List<UnresolvedReference>();
			if (Lex.CurrentIs(TokenType.OpenSquareBracket))
			{
				Lex.GoPast(TokenType.OpenSquareBracket);
				do
				{
					typeArgs.Add(ParseType());
				} while (Lex.CurrentIs(TokenType.Comma));
				Lex.ExpectThis(TokenType.CloseSquareBracket);
			}

			UnresolvedReference? qualifier = null;
			if (Lex.CurrentIs(TokenType.Access))
			{
				Lex.GoPast(TokenType.Access);
				qualifier = ParseType();
			}

			return new UnresolvedReference(typeNameToken.Range.Text, typeNameToken, qualifier, typeArgs);
		}

		private Stmt? ParseLoopStmt(Scope currentScope, Token firstToken)
		{
			Scope newScope = new(currentScope);
			Stmt? newStmt = null;

			switch (firstToken.Which)
			{
				case TokenType.While:
				{
					var w = ParseGenericStmt(
						newScope,
						ExpectedValue.Kw(TokenType.While),
						ExpectedValue.Expr,
						ExpectedValue.Opt(ExpectedValue.Kws(TokenType.StmtSeparator, TokenType.StrictStmtSeparator)),
						ExpectedValue.Stmt);
					newStmt = new PreconditionLoopStmt()
					{
						Scope = newScope,
						Condition = w.SingleExpr,
						Body = w.SingleStmt,
					};
					break;
				}
				case TokenType.For:
				{
					Lex.GoPast(TokenType.For);
					VarDeclStmt? loopVar = null;
					if (Lex.CurrentIs(TokenType.VarDecl))
					{
						loopVar = ParseStmt(newScope) as VarDeclStmt
							?? throw new SyntaxError(Lex.CurrentToken.Range, "Expected variable declaration in for-loop initializer.");
					}
					var tail = ParseGenericStmt(
						newScope,
						ExpectedValue.Kws(TokenType.StrictStmtSeparator, TokenType.StmtSeparator),
						ExpectedValue.Expr,
						ExpectedValue.Kws(TokenType.StrictStmtSeparator, TokenType.StmtSeparator),
						ExpectedValue.Expr,
						ExpectedValue.Kws(TokenType.StrictStmtSeparator, TokenType.StmtSeparator),
						ExpectedValue.Opt(ExpectedValue.Kws(TokenType.StmtSeparator, TokenType.StrictStmtSeparator)),
						ExpectedValue.Stmt);
					if (tail.Exprs.Count != 2 || tail.Stmts.Count != 1)
						throw new SyntaxError(firstToken.Range, "Invalid for-loop header.");
					newStmt = new ForLoopStmt()
					{
						Scope = newScope,
						LoopVariable = loopVar,
						Condition = tail.Exprs[0],
						Iterator = tail.Exprs[1],
						Body = tail.Stmts[0],
					};
					break;
				}
				case TokenType.Foreach:
				{
					var fe = ParseGenericStmt(
						newScope,
						ExpectedValue.Kw(TokenType.Foreach),
						ExpectedValue.Stmt, //FIX might cause issues
						ExpectedValue.Kw(TokenType.In),
						ExpectedValue.Expr,
						ExpectedValue.Opt(ExpectedValue.Kws(TokenType.StmtSeparator, TokenType.StrictStmtSeparator)),
						ExpectedValue.Stmt);
					if (fe.Exprs.Count != 1 || fe.Stmts.Count != 2)
						throw new SyntaxError(firstToken.Range, "Invalid foreach loop.");
					var loopVarDecl = fe.Stmts[0] as VarDeclStmt
						?? throw new SyntaxError(firstToken.Range, "Expected variable declaration after foreach.");
					newStmt = new ForeachLoopStmt()
					{
						Scope = newScope,
						LoopVariable = loopVarDecl,
						Iterator = fe.Exprs[0],
						Body = fe.Stmts[1],
					};
					break;
				}
				case TokenType.Repeat:
				{
					var r = ParseGenericStmt(
						newScope,
						ExpectedValue.Kw(TokenType.Repeat),
						ExpectedValue.Opt(ExpectedValue.Kws(TokenType.StmtSeparator, TokenType.StrictStmtSeparator)),
						ExpectedValue.Stmt);
					while (Lex.CurrentIs(TokenType.StmtSeparator, TokenType.StrictStmtSeparator))
						Lex.Next();
					if (Lex.CurrentIs(TokenType.Until))
					{
						var u = ParseGenericStmt(
							newScope,
							ExpectedValue.Kw(TokenType.Until),
							ExpectedValue.Expr);
						newStmt = new PostconditionLoopStmt()
						{
							Scope = newScope,
							Body = r.SingleStmt,
							Condition = u.SingleExpr,
						};
					}
					else
					{
						newStmt = new LoopStmt()
						{
							Scope = newScope,
							Body = r.SingleStmt,
						};
					}
					break;
				}
			}

			return newStmt;
		}

		private DecoratorObject ParseDecorator(Scope scope, Token firstToken)
		{
			firstToken = Lex.GoPast(TokenType.DecoratorBegin);
			var decoratorName = Lex.ExpectThis(TokenType.Identifier);

			var decoratorType = scope.FindTypeByName(decoratorName.Range.Text);
			if (decoratorType is null)
				throw new SyntaxError(decoratorName.Range, $"Decorator type '{decoratorName.Range.Text}' not found");

			List<LiteralExpr> args = [];
			if (Lex.NextIs(TokenType.OpenBracket))
			{
				Lex.GoPast(TokenType.OpenBracket);
				Expr? newExpr = null;
				ParseExpr(ref newExpr, Lex.CurrentToken);
				if (newExpr is null)
					throw new MalformedExpr(firstToken.Range);
				
				if (newExpr is CommaExpr comma)
				{
					args = [.. comma.Exprs.Select(e => 
					{
						if (e is LiteralExpr lit) 
							return lit; 
						else 
							throw new ArgumentException("Decorator arguments must be literal expressions");
					})];
				}
				else if (newExpr is LiteralExpr lit)
				{
					args = [lit];
				}
				else
				{
					throw new MalformedExpr(newExpr.GetFullRangeOrThrow());
				}
				
				Lex.ExpectThis(TokenType.CloseBracket);	
			}
			
			Lex.ExpectThis(TokenType.DecoratorEnd);	
			return new DecoratorObject(decoratorType, args);
		}

				private enum CaptureKind { Expr, Stmt, Keywords, OptionalSequence }

		private readonly record struct ExpectedValue(
			CaptureKind Kind,
			TokenType[]? Keywords = null,
			ExpectedValue[]? Inner = null)
		{
			public static ExpectedValue Expr => new(CaptureKind.Expr);
			public static ExpectedValue Stmt => new(CaptureKind.Stmt);
			public static ExpectedValue Kw(TokenType t) => new(CaptureKind.Keywords, Keywords: [t]);

			public static ExpectedValue Kws(params TokenType[] ts)
			{
				if (ts.Length == 0)
					throw new ArgumentException("Expected at least one keyword token type.", nameof(ts));
				return new(CaptureKind.Keywords, Keywords: ts);
			}

			public static ExpectedValue Opt(params ExpectedValue[] inner) =>
				new(CaptureKind.OptionalSequence, Inner: inner);
			
			public static ExpectedValue StmtEnd => 
				new(CaptureKind.Keywords, Keywords: 
					[TokenType.StmtSeparator, TokenType.StrictStmtSeparator, TokenType.EOF]);
		}

		private readonly record struct CapturedItem(CaptureKind Kind, Expr? Expr = null, Stmt? Stmt = null, Token? Keyword = null);

		private readonly record struct CapturedStmt(IReadOnlyList<CapturedItem> Items)
		{
			public readonly IReadOnlyList<Expr> Exprs => [.. Items.Where(i => i.Kind == CaptureKind.Expr).Select(i => i.Expr!).Where(e => e is not null)];
			public readonly IReadOnlyList<Stmt> Stmts => [.. Items.Where(i => i.Kind == CaptureKind.Stmt).Select(i => i.Stmt!).Where(s => s is not null)];
			public readonly IReadOnlyList<Token> Keywords => [.. Items.Where(i => i.Kind == CaptureKind.Keywords && i.Keyword is not null).Select(i => i.Keyword!)];
			public readonly Stmt SingleStmt => Stmts.Count == 1 
				? Stmts[0] 
				: throw new InvalidOperationException($"Expected exactly one statement, got {Stmts.Count}.");
			public readonly Expr SingleExpr => Exprs.Count == 1 
				? Exprs[0] 
				: throw new InvalidOperationException($"Expected exactly one expression, got {Exprs.Count}.");
			
			public readonly bool TryGetSingleStmt(out Stmt? stmt)
			{
				if (Stmts.Count == 1)
				{
					stmt = Stmts[0];
					return true;
				}
				stmt = null;
				return false;
			}
			public readonly bool TryGetSingleExpr(out Expr? expr) 
			{
				if (Exprs.Count == 1)
				{
					expr = Exprs[0];
					return true;
				}
				expr = null;
				return false;
			}
		}

		private CapturedStmt ParseGenericStmt (
			Scope currentScope, 
			params ExpectedValue[] expectedValues
		)
		{
			List<CapturedItem> items = [];

			foreach (var expectedValue in expectedValues)
			{
				switch (expectedValue.Kind)
				{
					case CaptureKind.Expr:
					{
						Expr? newExpr = null;
						ParseExpr(ref newExpr, Lex.CurrentToken);
						items.Add(new CapturedItem(CaptureKind.Expr, Expr: newExpr));
						break;
					}
					case CaptureKind.Stmt:
					{
						var newStmt = ParseStmt(currentScope);
						items.Add(new CapturedItem(CaptureKind.Stmt, Stmt: newStmt));
						break;
					}
					case CaptureKind.OptionalSequence:
					{
						var firstToken = Lex.CurrentToken;
						try 
						{
							var inner = ParseGenericStmt(currentScope, expectedValue.Inner!);
							items.AddRange(inner.Items);
						}
						catch (SyntaxError)
						{
							Lex.Goto(firstToken);
						}
						break;
					}
					case CaptureKind.Keywords:
					{
						var keyword = Lex.ExpectThis(expectedValue.Keywords!);
						items.Add(new CapturedItem(CaptureKind.Keywords, Keyword: keyword));
						break;
					}
					default:
					{
						throw new SyntaxError(Lex.CurrentToken.Range, $"Expected {expectedValue.Kind}. Got {Lex.CurrentToken.Which}.");
					}
				}
			}

			return new CapturedStmt(items);
		}

		private List<Token> CollectSpecifiers(ref Token firstToken)
		{
			List<Token> specifiers = [];
			while (firstToken.IsSpecifier)
			{
				var tt = firstToken.Which;
				if (specifiers.Any(t => t.Which == tt))
					throw new SyntaxError(firstToken.Range, "Duplicate specifiers.");
				specifiers.Add(firstToken);
				firstToken = Lex.Next();
			}
			return specifiers;
		}

		private Stmt? ParseStmt(Scope currentScope)
		{
			var firstToken = Lex.CurrentToken;
			List<Token> specifiers = CollectSpecifiers(ref firstToken);

			Expr? newExpr = null;
			Stmt? newStmt = null;

			switch (firstToken.Which)
			{
				case TokenType.VarDecl:
				{
					// Scope newScope = new(currentScope);
					// Lex.GoPast(TokenType.VarDecl);
					
					// while (!Lex.CurrentIs(TokenType.EOF, ))
					// {

					// }

					// VarDeclStmt varDecl = new()
					// {
					// 	Scope = newScope,
					// 	IsConst = isConst,
					// 	Name = [.. syms],
					// 	Value = valExpr,
					// };
					// foreach (var item in syms)
					// 	item.Declaration = varDecl;

					// newStmt = varDecl;
					// AddToScope(varDecl.Name, newScope);

					break;
				}
				case TokenType.FuncDecl:
				{
					// Lex.GoPast(TokenType.FuncDecl);
					// // var (funcSymbol, argDecls, typeArgs) = ParseFuncSignature(currentScope);
					// //use exprs

					// Scope funcScope = new(currentScope);
					// foreach (var argDecl in argDecls)
					// 	AddToScope(argDecl.Name, funcScope);
					// foreach (var typeArg in typeArgs)
					// 	AddToScope(typeArg, funcScope);

					// Lex.SkipStmtSeparator();
					// var innerStmt = ParseStmt(funcScope);
					// if (innerStmt is null)
					// 	throw new SyntaxError(firstToken.Range, "Expected statement after function declaration.");

					// newStmt = new FuncDeclStmt(funcSymbol, innerStmt) { Scope = funcScope };
					// AddToScope(funcSymbol, currentScope);

					break;
				}
				case TokenType.TraitDecl:
				{
					// Lex.GoPast(TokenType.TraitDecl);
					// var nameToken = Lex.ExpectThis(TokenType.Identifier);
					// var traitName = nameToken.Range.Text;
					// var typeArgs = ParseGenericTypeArguments(nameToken);

					// Lex.ExpectThis(TokenType.OpenCurlyBracket);
					// Lex.SkipStmtSeparator();

					// Scope traitScope = new(currentScope) { AllowShadowingFromParent = true };
					// foreach (var typeArg in typeArgs)
					// 	AddToScope(typeArg, traitScope);

					// var baseTraitSym = new TypeSymbol(traitName, Lex.Filepath);
					// var traitSym = TypeSymbolFactory.GetTypeSymbol(baseTraitSym, typeArgs);
					// var traitStmt = new TraitDeclStmt(traitSym) { Scope = traitScope };

					// while (!Lex.CurrentIs(TokenType.CloseCurlyBracket))
					// {
					// 	switch (Lex.CurrentToken.Which)
					// 	{
					// 		case TokenType.FuncDecl:
					// 		{
					// 			Lex.GoPast(TokenType.FuncDecl);
					// 			// var (sym, _, _) = ParseFuncSignature(traitScope);
					// 			sym.Source = Lex.Filepath;
					// 			sym.Declaration = traitStmt;
					// 			(traitStmt.Name as TypeSymbol)!.Members.Add(sym);
					// 			break;
					// 		}
					// 		case TokenType.VarDecl:
					// 		{
					// 			Lex.GoPast(TokenType.VarDecl);
					// 			var add = ParseNameTypePair(traitScope);
					// 			if (add.Count != 1) 
					// 				throw new SyntaxError(Lex.CurrentToken.Range, "Expected single variable declaration in trait body.");
					// 			var sym = add[0];
					// 			sym.Source = Lex.Filepath;
					// 			sym.Declaration = traitStmt;
					// 			(traitStmt.Name as TypeSymbol)!.Members.Add(sym);
					// 			break;
					// 		}
					// 		default:
					// 		{
					// 			throw new SyntaxError(Lex.CurrentToken.Range, "Expected variable or function declaration in trait body.");
					// 		}
					// 	}
						
					// 	Lex.SkipStmtSeparator();
					// }

					// AddToScope(traitStmt.Name, currentScope);
					// newStmt = traitStmt;
					// //increase depth so ParseBranch doesnt think the } closes the block
					// ++_depth;

					break;
				}
				case TokenType.TypeDecl:
				{
					// Lex.GoPast(TokenType.TypeDecl);
					// var nameToken = Lex.ExpectThis(TokenType.Identifier);
					// var typeName = nameToken.Range.Text;
					// var (inheritedType, traits) = ParseInheritanceAndTraits();
					// var typeArgs = ParseGenericTypeArguments(nameToken);

					// if (!Lex.CurrentIs(TokenType.OpenCurlyBracket))
					// 	throw new UnexpectedToken(nameToken.Range, TokenType.OpenCurlyBracket, nameToken);
					// Lex.GoPast(TokenType.OpenCurlyBracket);
					// Lex.SkipStmtSeparator();

					// //create base type symbol
					// var baseTypeSym = new TypeSymbol(typeName, Lex.Filepath, nameToken, inherits: inheritedType, implementedTraits: traits);

					// //make sure its registered in the factory
					// var typeSym = TypeSymbolFactory.GetTypeSymbol(baseTypeSym);
					// if (typeArgs.Count > 0)
					// 	typeSym = TypeSymbolFactory.GetTypeSymbol(baseTypeSym, typeArgs);

					// Scope newScope = new(currentScope) { AllowShadowingFromParent = true };

					// //potentailly remove self as a variable and use a special self token
					// var selfSym = new VarSymbol("self", typeSym) { Source = Lex.Filepath, Specifiers = [TokenType.PrivateSpec] };
					// newScope.AddSymbol(selfSym);

					// var body = new CompoundStmt() { Scope = newScope, Statements = ParseBranch(newScope) };
					// foreach (var stmt in body.Statements)
					// {
					// 	if (stmt is VarDeclStmt vd)
					// 	{
					// 		if (vd.Name.Count != 1)
					// 			throw new SyntaxError(vd.Name.First().Identifier?.Range ?? firstToken.Range, "Expected single variable declaration in type body.");
					// 		var sym = vd.Name[0];

					// 		if (typeSym.GetMember(sym.Name) is not null && !sym.Specifiers.Contains(TokenType.OverrideSpec))
					// 			NewError(new ShadowedClassMember(sym.Identifier?.Range ?? firstToken.Range, sym));

					// 		typeSym.Members.Add(sym);
					// 		if (!sym.Specifiers.Contains(TokenType.PrivateSpec) && !sym.Specifiers.Contains(TokenType.PublicSpec))
					// 		{
					// 			// if (Result.GlobalDecorators.Any(d => d.DecoratorType == Builtins.PrivateByDefault))
					// 			// 	sym.Specifiers.Add(TokenType.PrivateSpec);
					// 			// else
					// 				sym.Specifiers.Add(TokenType.PublicSpec);
					// 		}
					// 	}
					// 	else if (stmt is DeclStmt decl)
					// 	{
					// 		if (typeSym.GetMember(decl.Name.Name) is not null && !decl.Name.Specifiers.Contains(TokenType.OverrideSpec))
					// 			NewError(new ShadowedClassMember(decl.Name.Identifier?.Range ?? firstToken.Range, decl.Name));

					// 		typeSym.Members.Add(decl.Name);
					// 		if (!decl.Name.Specifiers.Contains(TokenType.PrivateSpec) && !decl.Name.Specifiers.Contains(TokenType.PublicSpec))
					// 		{
					// 			// if (Result.GlobalDecorators.Any(d => d.DecoratorType == Builtins.PrivateByDefault))
					// 			// 	decl.Name.Specifiers.Add(TokenType.PrivateSpec);
					// 			// else
					// 				decl.Name.Specifiers.Add(TokenType.PublicSpec);
					// 		}
					// 	}
					// 	else if (stmt is not null)
					// 		throw new SyntaxError(stmt.GetFullRangeOrThrow(), "Only declarations are allowed in type bodies.");
					// }

					// CheckTraitMethods(typeSym);

					// AddToScope(typeSym, currentScope);
					// newStmt = new TypeDeclStmt(typeSym, body) { Scope = currentScope };

					break;
				}
				case TokenType.Return:
				{
					var returnCont = ParseGenericStmt(
						currentScope, 
						ExpectedValue.Kw(TokenType.Return), 
						ExpectedValue.Opt(ExpectedValue.Expr)
					);
					newStmt = new ReturnStmt()
					{
						Scope = currentScope,
						Value = returnCont.TryGetSingleExpr(out var expr) ? expr : null,
					};
					break;
				}
				case TokenType.Import:
				{
					var newScope = new Scope(currentScope);
					var importCont = ParseGenericStmt(
						newScope,
						ExpectedValue.Kw(TokenType.Import),
						ExpectedValue.Kw(TokenType.StringLiteral),
						ExpectedValue.Opt(
							ExpectedValue.Kw(TokenType.As),
							ExpectedValue.Kw(TokenType.Identifier)
						)
					); 
					var filepath = importCont.Keywords[0].Range.Text;
					var moduleName = importCont.Keywords.Count == 4 
						? importCont.Keywords[3].Range.Text
						: filepath[(filepath.LastIndexOf('/')+1) ..];
					var moduleSym = new VarSymbol(moduleName, Lex.Filepath, Builtins.Module);

					var importStmt = new ImportStmt(moduleName, filepath, moduleSym)
					{
						Scope = newScope,
					};
					newStmt = importStmt;
					newScope.AddSymbol(moduleSym);

					break;
				}
				case TokenType.Break:
				{
					newStmt = new BreakStmt() { Scope = currentScope, };
					break;
				}
				case TokenType.Continue:
				{
					newStmt = new ContinueStmt() { Scope = currentScope, };
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
					var ifCont = ParseGenericStmt(
						currentScope,
						ExpectedValue.Kw(TokenType.If),
						ExpectedValue.Expr,
						ExpectedValue.Stmt);
					var ifStmt = new IfStmt()
					{
						Scope = currentScope,
						Condition = ifCont.SingleExpr,
						NextIf = ifCont.SingleStmt,
					};
					IfStmt lastStmt = ifStmt;
					Lex.SkipStmtSeparator();

					while (Lex.CurrentIs(TokenType.Elif))
					{
						var elifContainer = ParseGenericStmt(
							currentScope,
							ExpectedValue.Kw(TokenType.Elif),
							ExpectedValue.Expr,
							ExpectedValue.Stmt);
						var elifStmt = new IfStmt()
						{
							Scope = currentScope,
							Condition = elifContainer.SingleExpr,
							NextIf = elifContainer.SingleStmt,
						};
						lastStmt.NextElse = elifStmt;
						lastStmt = elifStmt;
						Lex.SkipStmtSeparator();
					}
					if (Lex.CurrentIs(TokenType.Else))
					{
						var elsePart = ParseGenericStmt(
							currentScope,
							ExpectedValue.Kw(TokenType.Else),
							ExpectedValue.Stmt);
						lastStmt.NextElse = elsePart.SingleStmt;
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

					newStmt.InnerRange = firstToken.Range;
					return newStmt;
				}
				case TokenType.Version:
				{
					Lex.GoPast(TokenType.Version);
					Lex.SkipStmtSeparator();
					Lex.ExpectThis(TokenType.OpenCurlyBracket);
					Lex.SkipStmtSeparator();

					while (!Lex.CurrentIs(TokenType.CloseCurlyBracket))
					{
						Lex.SkipStmtSeparator();
						var comparison = Lex.ExpectThis(
							TokenType.Equals, 
							TokenType.Greater, 
							TokenType.GreaterOrEqual, 
							TokenType.Lesser, 
							TokenType.LesserOrEqual, 
							TokenType.Unequals
						);
						var versionToken = Lex.ExpectThis(TokenType.StringLiteral);
						var version = MCVersion.ParseMCVersion(versionToken.Range.Text);
						if (version is null)
							throw new SyntaxError(versionToken.Range, $"Invalid version: '{versionToken.Range.Text}'");
						var stmt = ParseStmt(currentScope);

						switch (comparison.Which)
						{
							case TokenType.Equals:
							{
								if (Args.TargetVersion == version)
									newStmt = stmt;
								break;
							}
							case TokenType.Unequals:
							{
								if (Args.TargetVersion != version)
									newStmt = stmt;
								break;
							}
							case TokenType.Greater:
							{
								if (Args.TargetVersion.Platform == version.Platform && Args.TargetVersion > version)
									newStmt = stmt;
								break;
							}
							case TokenType.GreaterOrEqual:
							{
								if (Args.TargetVersion.Platform == version.Platform && Args.TargetVersion >= version)
									newStmt = stmt;
								break;
							}
							case TokenType.Lesser:
							{
								if (Args.TargetVersion.Platform == version.Platform && Args.TargetVersion < version)
									newStmt = stmt;
								break;
							}
							case TokenType.LesserOrEqual:
							{
								if (Args.TargetVersion.Platform == version.Platform && Args.TargetVersion <= version)
									newStmt = stmt;
								break;
							}
							default:
							{
								throw new UnexpectedToken(comparison.Range, comparison);
							}
						}

						if (newStmt is not null)
							break;
					}

					if (newStmt is null)
						throw new SyntaxError(firstToken.Range, $"No version matched: '{Args.TargetVersion}'");

					break;
				}
				case TokenType.OpenCurlyBracket:
				{
					Scope newScope = new(currentScope);

					Lex.Next();
					var innerStmt = ParseBranch(newScope);
					//Lex.Next();
					
					newStmt = new CompoundStmt()
					{
						Scope = newScope,
						Statements = innerStmt,
					};

					break;
				}
				case TokenType.DecoratorBegin:
				{
					var newDecorator = ParseDecorator(currentScope, firstToken);
					CurrentDecorators.Add(newDecorator);
					
					break;
				}
				case TokenType.EOF:
				case TokenType.StmtSeparator:
				case TokenType.StrictStmtSeparator:
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
			{
				newStmt.InnerRange = firstToken.Range;
				newStmt.Decorators = [.. CurrentDecorators];
				CurrentDecorators.Clear();
			}

			return newStmt;
		}

		private List<Stmt> ParseBranch(Scope parentScope)
		{
			//start token after the opening bracket
			var firstToken = Lex.CurrentToken;
			var startingDepth = ++_depth;
			List<Stmt> innerStmts = [];
			Scope currentScope = parentScope;

			while (true)
			{
				Stmt? newStmt = null;
				if (Args.Throw)
				{
					newStmt = ParseStmt(currentScope);

					if (newStmt is not null)
					{
						innerStmts.Add(newStmt);
						if (newStmt is VarDeclStmt or ImportStmt)
							currentScope = newStmt.Scope;
					}

					if (Lex.CurrentIs(TokenType.EOF))
					{
						if (startingDepth != 1)
							throw new SyntaxError(firstToken.Range, "Unclosed bracket.", ErrorSeverity.Critical);
						break;
					}
					if (Lex.CurrentIs(TokenType.CloseCurlyBracket))
					{
						// _depth > startingDepth: this '}' closes a nested block (inner ParseBranch
						// returned without consuming it). Consume it and decrement; keep parsing.
						if (_depth > startingDepth)
						{
							Lex.Next();
							--_depth;
							continue;
						}
						// _depth == startingDepth: this '}' closes this branch. End on it; do not
						// decrement here so the caller can consume and decrement.
						if (_depth == startingDepth)
							break;
						// _depth < startingDepth: invalid (extra '}' or corrupted state)
						throw new SyntaxError(Lex.CurrentToken.Range, "Unmatched closing bracket.", ErrorSeverity.Critical);
					}

					Lex.Next();
				}
				else
				{
					try
					{
						newStmt = ParseStmt(currentScope);

						if (newStmt is not null)
						{
							innerStmts.Add(newStmt);
							if (newStmt is DeclStmt or ImportStmt)
								currentScope = newStmt.Scope;
						}
						
						if (Lex.CurrentIs(TokenType.EOF))
						{
							if (startingDepth != 1)
								throw new SyntaxError(firstToken.Range, "Unclosed bracket.", ErrorSeverity.Critical);
							break;
						}
						if (Lex.CurrentIs(TokenType.CloseCurlyBracket))
						{
							// _depth > startingDepth: this '}' closes a nested block (inner ParseBranch
							// returned without consuming it). Consume it and decrement; keep parsing.
							if (_depth > startingDepth)
							{
								Lex.Next();
								--_depth;
								continue;
							}
							// _depth == startingDepth: this '}' closes this branch. End on it; do not
							// decrement here so the caller can consume and decrement.
							if (_depth == startingDepth)
								break;
							// _depth < startingDepth: invalid (extra '}' or corrupted state)
							throw new SyntaxError(Lex.CurrentToken.Range, "Unmatched closing bracket.", ErrorSeverity.Critical);
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

			return innerStmts;
		}

		public void ParseFile()
		{
			Result.Statements = ParseBranch(Result.RootScope);
		}

		public Parser(ProgramArgs args, Lexer lex)
		{
			Lex = lex;
			Args = args;
			Result = new();
		}
	}
}

