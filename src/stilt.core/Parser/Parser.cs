#pragma warning disable CS8601
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace stilt
{
	/// <summary>
	/// Recursive-descent parser that turns the lexer's token stream into an AST (pipeline stage 2).
	/// Statements are parsed by <see cref="ParseStmt"/> — a switch on the leading keyword — with blocks
	/// handled by <see cref="ParseBranch"/>; expressions are parsed by <see cref="ParseExpr"/>, which builds
	/// a precedence-correct tree by handing each new node to <see cref="Expr.InsertIntoTree(Expr?)"/>.
	///
	/// As it parses it also builds the lexical <see cref="Scope"/> tree and records declared symbols, but it
	/// does not resolve name <i>uses</i>: identifiers are captured as unresolved references and bound later by
	/// the <see cref="Linker"/>. Recoverable problems are collected into <see cref="ParserResult.CompilationIssues"/>
	/// (via <see cref="NewError"/>) and parsing skips to the next statement; with <c>--throw</c> they propagate instead.
	/// The finished AST and scopes live in <see cref="Result"/>.
	/// </summary>
	public class Parser
	{
		public readonly ParserResult Result;

		private readonly Lexer Lex;
		private readonly ProgramArgs Args;
		/// <summary>Decorators (<c>[[ … ]]</c>) parsed but not yet attached; flushed onto the next statement that accepts them.</summary>
		private List<DecoratorObject> CurrentDecorators = [];
		/// <summary>Current brace-nesting depth, used by <see cref="ParseBranch"/> to decide which block a <c>}</c> closes.</summary>
		private int _depth = 0;

		/// <summary>Records a recoverable diagnostic on the result instead of throwing, so parsing can continue.</summary>
		private void NewError(SyntaxError err)
		{
			Result.CompilationIssues.Add(err);
		}

		/// <summary>Parses the contents of an array literal <c>[ … ]</c> into an <see cref="ArrayLiteralExpr"/>, treating a top-level comma expression as the element list.</summary>
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

		/// <summary>Evaluates a scientific-notation literal (e.g. <c>1.5e3</c>) to a double: parses the mantissa and exponent around the <c>e</c>/<c>E</c> and combines them.</summary>
		private double ParseScientificLiteral(Token token)
		{
			var tokenText = token.Range.Text.Replace("_", "");
			var splitIndex = tokenText.IndexOfAny(['e', 'E']);

			var mantissa = Convert.ToDouble(tokenText[..splitIndex], CultureInfo.InvariantCulture);
			var exponent = Convert.ToInt64(tokenText[(splitIndex+1)..], CultureInfo.InvariantCulture);

			return mantissa * Math.Pow(10, exponent);
		}

		/// <summary>
		/// Parses one expression, growing it in <paramref name="rootExpr"/>. Walks tokens left to right: each token
		/// becomes a node (<paramref name="newExpr"/>) — a literal/identifier operand, or an operator whose arity is
		/// chosen from the current tree shape (<see cref="Expr.ExpectingOperator"/> distinguishes, say, a grouping
		/// <c>(</c> from a call, and a unary <c>-</c> from a binary one) — which is then spliced into the tree at the
		/// spot its precedence dictates via <see cref="Expr.InsertIntoTree(Expr?, Expr?)"/>. It then recurses on the
		/// next token. Parsing stops when the token is in <paramref name="stopTokens"/> or is one that cannot extend
		/// an expression (a separator, closing bracket, EOF, …), marking the result <see cref="Expr.Bracketed"/> and returning.
		/// </summary>
		private void ParseExpr(ref Expr? rootExpr, Token currentToken, params TokenType[] stopTokens)
		{
			if (stopTokens.Contains(currentToken.Which))
				return;

			Expr? newExpr = null;

			switch (currentToken.Which)
			{
				case TokenType.Identifier:
				{
					newExpr = new IdentityExpr()
					{
						InnerRange = currentToken.Range,
						Identity = SymbolReference.NotResolved(currentToken),
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
					// A trailing type suffix (b/s/i/l/f/d) pins the numeric type; without one, decimals/scientific
					// default to Fractional and the rest to Whole. The token kind picks the radix below.
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

					// A bracket after a complete operand is postfix (a call's argument list, or a `[]` index);
					// otherwise a `(` just groups, and a `[` starts an array literal.
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
				case TokenType.SoftStmtSeparator:
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
						if (e is ITraversible op)
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

		/// <summary>
		/// Parses the four loop forms into their statement nodes: <c>while</c> (<see cref="PreconditionLoopStmt"/>),
		/// <c>for</c> (<see cref="ForLoopStmt"/>), <c>foreach</c> (<see cref="ForeachLoopStmt"/>), and <c>repeat</c>
		/// with an optional trailing <c>until</c> (<see cref="LoopStmt"/> or <see cref="PostconditionLoopStmt"/>).
		/// The body and any condition/iterator are gathered with <see cref="ParseGenericStmt"/> into a new child scope.
		/// </summary>
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
						ExpectedValue.Opt(ExpectedValue.Kws(TokenType.SoftStmtSeparator, TokenType.StrictStmtSeparator)),
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
						ExpectedValue.Kws(TokenType.StrictStmtSeparator, TokenType.SoftStmtSeparator),
						ExpectedValue.Expr,
						ExpectedValue.Kws(TokenType.StrictStmtSeparator, TokenType.SoftStmtSeparator),
						ExpectedValue.Expr,
						ExpectedValue.Kws(TokenType.StrictStmtSeparator, TokenType.SoftStmtSeparator),
						ExpectedValue.Opt(ExpectedValue.Kws(TokenType.SoftStmtSeparator, TokenType.StrictStmtSeparator)),
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
						ExpectedValue.Opt(ExpectedValue.Kws(TokenType.SoftStmtSeparator, TokenType.StrictStmtSeparator)),
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
						ExpectedValue.Opt(ExpectedValue.Kws(TokenType.SoftStmtSeparator, TokenType.StrictStmtSeparator)),
						ExpectedValue.Stmt);
					while (Lex.CurrentIs(TokenType.SoftStmtSeparator, TokenType.StrictStmtSeparator))
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

		/// <summary>
		/// Parses a decorator annotation <c>[[ Name(args…) ]]</c>: resolves <c>Name</c> to a decorator type in
		/// <paramref name="scope"/> and collects its literal arguments. The returned <see cref="DecoratorObject"/>
		/// is buffered in <see cref="CurrentDecorators"/> and attached to the statement that follows.
		/// </summary>
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

		private enum CaptureKind { Expr, Stmt, Keywords, OptionalSequence, Type }

		/// <summary>
		/// One element of a statement's expected shape, used to describe grammar declaratively. A statement parser
		/// lists the pieces it expects — e.g. <c>while</c> is <c>Kw(While), Expr, Stmt</c> — and <see cref="ParseGenericStmt"/>
		/// consumes them in order. <see cref="Opt"/> wraps a sub-sequence that is attempted but rolled back if it fails.
		/// </summary>
		private readonly record struct ExpectedValue(
			CaptureKind Kind,
			TokenType[]? Keywords = null,
			ExpectedValue[]? Inner = null)
		{
			public static ExpectedValue Expr => new(CaptureKind.Expr);
			public static ExpectedValue Stmt => new(CaptureKind.Stmt);
			public static ExpectedValue Type => new(CaptureKind.Type);
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
					[TokenType.SoftStmtSeparator, TokenType.StrictStmtSeparator, TokenType.EOF]);
		}

		private readonly record struct CapturedItem(CaptureKind Kind, Expr? Expr = null, Stmt? Stmt = null, Token? Keyword = null, UnresolvedReference? Type = null);

		/// <summary>
		/// The results of a <see cref="ParseGenericStmt"/> call: the captured items grouped by kind, with helpers
		/// (<see cref="SingleExpr"/>, <see cref="SingleStmt"/>, the <c>TryGet…</c> methods) for the common case of
		/// pulling out exactly one expression/statement/type that the caller then assembles into a concrete node.
		/// </summary>
		private readonly record struct CapturedStmt(IReadOnlyList<CapturedItem> Items)
		{
			public readonly IReadOnlyList<Expr> Exprs => [.. Items.Where(i => i.Kind == CaptureKind.Expr).Select(i => i.Expr!).Where(e => e is not null)];
			public readonly IReadOnlyList<Stmt> Stmts => [.. Items.Where(i => i.Kind == CaptureKind.Stmt).Select(i => i.Stmt!).Where(s => s is not null)];
			public readonly IReadOnlyList<Token> Keywords => [.. Items.Where(i => i.Kind == CaptureKind.Keywords && i.Keyword is not null).Select(i => i.Keyword!)];
			public readonly IReadOnlyList<UnresolvedReference> Types => [.. Items.Where(i => i.Kind == CaptureKind.Type).Select(i => i.Type!).Where(t => t is not null)];
			public readonly Stmt SingleStmt => Stmts.Count == 1 
				? Stmts[0] 
				: throw new InvalidOperationException($"Expected exactly one statement, got {Stmts.Count}.");
			public readonly Expr SingleExpr => Exprs.Count == 1 
				? Exprs[0] 
				: throw new InvalidOperationException($"Expected exactly one expression, got {Exprs.Count}.");
			public readonly UnresolvedReference SingleType => Types.Count == 1 
				? Types[0] 
				: throw new InvalidOperationException($"Expected exactly one type, got {Types.Count}.");

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
			public readonly bool TryGetSingleType(out UnresolvedReference? type)
			{
				if (Types.Count == 1)
				{
					type = Types[0];
					return true;
				}
				type = null;
				return false;
			}
		}

		/// <summary>
		/// Parses a sequence of <see cref="ExpectedValue"/> pieces in order, returning what each produced as a
		/// <see cref="CapturedStmt"/>. Expressions, statements, and types recurse into their parsers; keywords are
		/// asserted with <see cref="Lexer.ExpectThis"/>; an <see cref="ExpectedValue.Opt"/> sub-sequence is tried and,
		/// on a <see cref="SyntaxError"/>, rewound to where it began so the surrounding parse continues unaffected.
		/// </summary>
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
					case CaptureKind.Type:
					{
						var type = ParseType()!;
						items.Add(new CapturedItem(CaptureKind.Type, Type: type));
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

		/// <summary>
		/// Parses a named function declaration: <c>'func' name '('args')'[':' type] body</c>.
		/// The arguments live in the returned <paramref name="funcScope"/> (parentless; the caller
		/// wires its <see cref="Scope.Parent"/> to the enclosing scope). Delegates the argument
		/// list, return type, and body to <see cref="ParseFuncLiteral()"/>.
		/// </summary>
		private (VarSymbol funcSymbol, Stmt body, Scope funcScope) ParseFunc()
		{
			Lex.ExpectThis(TokenType.FuncDecl);
			var funcName = Lex.ExpectThis(TokenType.Identifier);
			var (_, _, body, funcScope) = ParseFuncLiteral();

			var funcSymbol = new VarSymbol(funcName.Range.Text, Lex.Filepath, funcName);

			return (funcSymbol, body, funcScope);
		}

		/// <summary>
		/// Parses an anonymous function expression: <c>'func' '('args')'[':' type] body</c>.
		/// The argument/body scope is parented to <paramref name="currentScope"/> (the scope the
		/// literal lexically appears in). The declared return type is not modelled on the
		/// expression; the type checker infers it from the body.
		/// </summary>
		private FuncLiteralExpr ParseFuncLiteral(Scope currentScope)
		{
			var funcToken = Lex.ExpectThis(TokenType.FuncDecl);
			var (args, _, body, funcScope) = ParseFuncLiteral();
			funcScope.Parent = currentScope;

			return new FuncLiteralExpr
			{
				InnerRange = funcToken.Range,
				Arguments = args,
				Value = body,
			};
		}

		/// <summary>
		/// Parses a function argument list, optional return type, and body:
		/// <c>'('[name[':' type][','...]]')'[':' type] stmt</c>. Arguments are collected into a
		/// fresh, parentless <see cref="Scope"/> that the body is parsed against; the caller wires
		/// its <see cref="Scope.Parent"/> to the enclosing scope. Shared by
		/// <see cref="ParseFuncLiteral(Scope)"/> and <see cref="ParseFunc"/>.
		/// </summary>
		private (List<VarSymbol> args, UnresolvedReference? returnType, Stmt body, Scope funcScope) ParseFuncLiteral()
		{
			Lex.ExpectThis(TokenType.OpenBracket);

			List<VarSymbol> args = [];
			while (!Lex.CurrentIs(TokenType.CloseBracket))
			{
				var argName = Lex.ExpectThis(TokenType.Identifier);

				SymbolReference? argType = null;
				if (Lex.CurrentIs(TokenType.Type))
				{
					Lex.Next();
					argType = SymbolReference.FromUnresolved(ParseType());
				}

				args.Add(new VarSymbol(argName.Range.Text, Lex.Filepath, argType, argName));

				if (!Lex.CurrentIs(TokenType.CloseBracket))
					Lex.ExpectThis(TokenType.Comma);
			}
			Lex.ExpectThis(TokenType.CloseBracket);

			UnresolvedReference? returnType = null;
			if (Lex.CurrentIs(TokenType.Type))
			{
				Lex.Next();
				returnType = ParseType();
			}

			var funcScope = new Scope();
			funcScope.AddSymbols(args);

			var body = ParseStmt(funcScope)
				?? throw new SyntaxError(Lex.CurrentToken.Range, "Expected function body.");

			return (args, returnType, body, funcScope);
		}

		/// <summary>Consumes any leading specifier keywords (e.g. <c>pub</c>, <c>const</c>) before a statement, rejecting duplicates, and advances <paramref name="firstToken"/> to the first non-specifier token.</summary>
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

		/// <summary>
		/// Parses a single statement by switching on its leading token: declarations (<c>var</c>, <c>func</c>),
		/// control flow (<c>if</c>, loops, <c>return</c>, <c>break</c>/<c>continue</c>), <c>import</c>, conditional
		/// <c>version</c> blocks, braces (a nested <see cref="CompoundStmt"/>), decorators, and otherwise an
		/// expression statement. Declarations register their symbols in <paramref name="currentScope"/> (or a fresh
		/// child scope); any buffered decorators and the statement's source range are attached before returning.
		/// Returns null for tokens that produce no statement (a stray separator, <c>}</c>, or EOF).
		/// </summary>
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
					Scope newScope = new(currentScope);
					var varCont = ParseGenericStmt(
						newScope,
						ExpectedValue.Kw(TokenType.VarDecl),
						ExpectedValue.Expr
					);

					if (!Lex.CurrentIs(TokenType.SoftStmtSeparator, TokenType.StrictStmtSeparator))
						throw new SyntaxError(Lex.CurrentToken.Range, "Expected statement separator after variable declaration.");

					if (varCont.SingleExpr is null || varCont.SingleExpr is not AssignExpr assign)
						throw new SyntaxError(varCont.SingleExpr?.GetFullRangeOrThrow(), "Expected assignment expression after variable declaration.");

					if (assign.Left is not (IdentityExpr or CommaExpr))
						throw new SyntaxError(varCont.SingleExpr.GetFullRangeOrThrow(), "Expected an identifier or a series of identifiers after variable declaration.");

					List<Symbol> names;
					if (assign.Left is CommaExpr idents)
					{
						names = [.. idents.Exprs.Select(e =>
						{
							if (e is not IdentityExpr ident)
								throw new SyntaxError(e.GetFullRangeOrThrow(), "Expected an identifier in variable declaration.");
							return new VarSymbol(ident.Identity.Unresolved.Name, t: ident.Identity.Unresolved.Token) as Symbol;
						})];
					}
					else
					{
						var ident = (assign.Left as IdentityExpr)!;
						names = [new VarSymbol(ident.Identity.Unresolved.Name, t: ident.Identity.Unresolved.Token)];
					}

					var varDecl = new VarDeclStmt()
					{
						Scope = newScope,
						Name = names,
						Value = assign.Right,
					};

					newStmt = varDecl;
					newScope.AddSymbols(varDecl.Name);

					break;
				}
				case TokenType.FuncDecl:
				{
					// no default arguments
					var (funcSymbol, body, funcScope) = ParseFunc();
					funcScope.Parent = currentScope;
					Lex.SkipStmtSeparator();

					newStmt = new FuncDeclStmt(funcSymbol, body) { Scope = funcScope };
					currentScope.AddSymbol(funcSymbol);

					break;
				}
				case TokenType.TraitDecl:
				{
					// INCOMPLETE: trait declarations are not parsed yet; the body below is the previous draft, left for reference.
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
					// INCOMPLETE: type declarations are not parsed yet; the body below is the previous draft, left for reference.
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
					var moduleSym = new VarSymbol(moduleName, Lex.Filepath, SymbolReference.AlreadyResolved(Builtins.Module));

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
					// Compile-time switch on the target Minecraft version: each `comparison "x.y.z": stmt` arm is
					// parsed, and the first whose comparison holds against Args.TargetVersion becomes this statement.
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
				case TokenType.SoftStmtSeparator:
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

		/// <summary>
		/// Parses a run of statements until the brace that closes this branch (or EOF), returning them in order.
		/// Uses <see cref="_depth"/> to tell apart a <c>}</c> that closes a nested block — which it consumes before
		/// continuing — from the one that ends this branch, which it leaves for the caller. With <c>--throw</c> any
		/// <see cref="SyntaxError"/> aborts; otherwise errors are recorded and parsing resumes at the next statement
		/// (<see cref="Lexer.SkipStmt"/>), except a <see cref="ErrorSeverity.Critical"/> one which still propagates.
		/// </summary>
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

		/// <summary>Entry point: parses the whole file as one top-level branch into <see cref="ParserResult.Statements"/>, rooted at the file's <see cref="ParserResult.RootScope"/>.</summary>
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

