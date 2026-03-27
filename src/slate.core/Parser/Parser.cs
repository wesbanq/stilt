#pragma warning disable CS8601
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

		private static Expr GetLastExprInTree(Expr root)
		{
			// Prefer the most-recently-added leaf node in the current tree.
			// Child ordering in GetChildren() is already "newest-first" for operators.
			var current = root;
			while (!current.Bracketed && current is IOperator op)
			{
				var next = op.GetChildren().First(c => c is not null);
				if (next is null)
					break;
				current = next;
			}
			return current;
		}

		private void InsertIntoExprTree(ref Expr? rootExpr, Expr? newExpr)
		{
			if (rootExpr is null && newExpr is not null)
			{
				if (!newExpr.Bracketed && newExpr is not UnaryExpr && newExpr is not CommaExpr && newExpr is IOperator)
					throw new MalformedExpr(newExpr.GetFullRangeOrThrow());
				rootExpr = newExpr;
				return;
			}

			//get rid of null derefence warnings
			if (newExpr is null || rootExpr is null)
				return;

			//add more comments to explain all of this later
			var toReplace = rootExpr.FindFirstPrecedenceOrNull(newExpr.Precedence, out var parent);
			if (toReplace is null && parent is null)
			{
				if (newExpr is IOperator exprSpreadable)
				{
					exprSpreadable.InsertChild(rootExpr);
					rootExpr = newExpr;
				}
				else
					throw new MalformedExpr(newExpr.GetFullRangeOrThrow());
			}

			if (newExpr is IOperator spreadable)
			{
				if (toReplace is not null)
				{
					//with the old CommaExpr foundSym tuple of 3 will evaluate to foundSym type of ((Type, Type), Type) instead of (Type, Type, Type)
					//there might be foundSym better way to accomplishing this with BinaryExpr
					//by looking if the child CommaExpr is bracketed during type eval
					if (toReplace is CommaExpr rootComma && newExpr is CommaExpr)
					{
						++rootComma.ExprLength;
						return;
					}

					spreadable.InsertChild(toReplace);
					if (parent is not null)
					{
						if (parent is IOperator op)
						{
							op.ReplaceChild(toReplace, newExpr);
						}
						else
							throw new MalformedExpr((toReplace ?? newExpr)!.GetFullRangeOrThrow());
					}
					else
					{
						rootExpr = newExpr;
					}

					return;
				}

				if (parent is not null)
				{
					if (parent is IOperator sParent)
					{
						if (toReplace is null && (newExpr.Bracketed || newExpr is (UnaryExpr or TernaryExpr)))
							sParent.InsertChild(newExpr);
						else
						{
							var range = newExpr.GetFullRangeOrThrow();
							throw new MalformedExpr(range);
						}
					}
					else
					{
						var range = newExpr.GetFullRangeOrThrow();
						throw new MalformedExpr(range);
					}

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
					throw new MalformedExpr(newExpr.GetFullRangeOrThrow());
			}
		}

		private List<Expr> CreateOperatorExpr<T>(Token token)
			where T : OperatorAttribute
		{
			var operatorAttr = Utils.GetAttributesFromEnum<TokenType, T>(token.Which);
			if (operatorAttr is null)
				throw new UnexpectedToken(token.Range, token);

			List<Expr> exprs = [];
			foreach (var op in operatorAttr)
			{
				Expr expr = op switch
				{
					UnaryOperatorAttribute => new UnaryExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.OpenBracket => 
						new CallExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.Comma => 
						new CommaExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.Access => 
						new AccessExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.NullAccess => 
						new NullAccessExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute when token.Which == TokenType.Assign => 
						new AssignExpr(op.Precedence, token.Range, token),
					BinaryOperatorAttribute => new BinaryExpr(op.Precedence, token.Range, token),
					TernaryOperatorAttribute => new TernaryExpr(op.Precedence, token.Range, token),
					_ => throw new UnexpectedToken(token.Range, token)
				};
				exprs.Add(expr);
			}

			return exprs;
		}

		public static List<IdentityExpr> GetIdentities(Expr? expr)
		{
			if (expr is null)
				return [];

			switch (expr)
			{
				case CommaExpr comma:
				{
					return [.. comma.GetChildren().SelectMany(GetIdentities)];
				}
				case IOperator op:
				{
					List<IdentityExpr> res = [];
					foreach (var child in op.GetChildren())
					{
						res.AddRange(GetIdentities(child));
					}
					return res;
				}
				case IdentityExpr id:
				{
					return [id];
				}
				default:
				{
					throw new MalformedExpr(expr.GetFullRangeOrThrow());
				}
			}
		}

		private bool ExpectingOperator(ref Expr? expr)
		{
			if (expr is null)
				return false;
			var a = expr.FindFirstNull(out var p);
			//foundSym is null - expecting operand / foundSym is not null - expecting operator
			return a is null && p is null;
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

		private TableLiteralExpr ParseTableLiteral(Token currentToken)
		{
			if (currentToken.Which is TokenType.EOF)
				throw new UnexpectedEOF(currentToken.Range);

			Expr? newExpr = null;
			Dictionary<Symbol, Expr> dict = [];
			ParseExpr(ref newExpr, currentToken);
			var list = ParseArrayLiteral(currentToken).Value as List<Expr>;
			
			if (list is null)
				throw new MalformedExpr(currentToken.Range);
			
			foreach (var expr in list)
			{
				if (expr is AssignExpr assign)
				{
					if (assign.Operation is not null)
						throw new SyntaxError(assign.GetInnerRangeOrFullRangeOrThrow(), "Self-assingment operators are not permitted inside tables");

					Symbol newSym;
					//keys in tables may not neccessarily be strings
					//support for non string keys??
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
							//think of something for other string literal types
							newSym = new VarSymbol((str.Value as String) ?? throw new MalformedExpr(str.FullRange ?? str.InnerRange ?? throw new InvalidOperationException("String literal has no range")), Lex.Filepath, Builtins.Any);
							break;
						}
						default:
						{
							throw new SyntaxError(assign.GetFullRangeOrThrow(), "Invalid key in table");
						}
					}
					dict.Add(newSym, expr);
				}
				else
					throw new SyntaxError(expr.GetInnerRangeOrFullRangeOrThrow(), "Only assingnment-type expressions allowed inside a table literal");
			}

			return new(currentToken.Range, dict);
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
					var newSym = new VarSymbol(currentToken.Range.Text, t: currentToken);
					newExpr = new IdentityExpr()
					{
						InnerRange = currentToken.Range,
						Identity = newSym,
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

					if (ExpectingOperator(ref rootExpr))
					{
						if (newExpr is null && currentToken.Which == TokenType.OpenSquareBracket)
							throw new SyntaxError(currentToken.Range, "No valid expression given as an index.");
						var opExpr = CreateOperatorExpr<BinaryOperatorAttribute>(currentToken).First() as BinaryExpr;
						if (opExpr is null)
							throw new MalformedExpr(currentToken.Range);
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
					{
						if (rootExpr is not null)
							rootExpr.Bracketed = true;
						return;
					}
					else
					{
						newExpr = ParseTableLiteral(Lex.Next());
					}

					break;
				}
				case TokenType.In:
				case TokenType.EOF:
				case TokenType.Then:
				case TokenType.Else:
				case TokenType.CloseBracket:
				case TokenType.StmtSeparator:
				case TokenType.CloseCurlyBracket:
				case TokenType.CloseSquareBracket:
				{
					//FIX FullRange will ignore the closing bracket
					if (rootExpr is not null)
						rootExpr.Bracketed = true;
					return;
				}
				case TokenType.Type:
				{
					if (rootExpr is null)
						throw new SyntaxError(currentToken.Range, "Expected an expression before ':'");

					var targetExpr = GetLastExprInTree(rootExpr);

					// Move onto the first token of the type.
					Lex.Next();
					var parsedType = ParseType();
					targetExpr.Type = parsedType;
					targetExpr.Explicit = true;

					// ParseType already advanced the lexer to the token after the type.
					ParseExpr(ref rootExpr, Lex.CurrentToken);
					return;
				}
				default:
				{
					if (currentToken.IsUnimplemented)
						throw new UnimplementedError(currentToken);

					var possibleExprs = CreateOperatorExpr<OperatorAttribute>(currentToken)
						?? throw new UnexpectedToken(currentToken.Range, currentToken);
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
							throw new Exception();
					})];

					foreach (var expr in possibleExprs)
					{
						Expr? parent = null;
						var a = rootExpr?.FindFirstPrecedenceOrNull(expr.Precedence, out parent);

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
			
			InsertIntoExprTree(ref rootExpr, newExpr);
			ParseExpr(ref rootExpr, Lex.Next());
		}

		private void AddToScope(List<Symbol> symbols, Scope scope)
		{
			foreach (var sym in symbols)
			{
				AddToScope(sym, scope);
			}
		}

		private void AddToScope(Symbol symbol, Scope scope)
		{
			var foundSymbol = scope.FindSymbolByName(symbol.Name);
			if (foundSymbol is not null)
			{
				var range = symbol.Identifier?.Range 
					?? throw new Exception();
				if (foundSymbol.IsBuiltin)
					NewError(new ShadowedBuiltinSymbol(range, symbol));
				else if (!scope.IsInScopeThatAllowsShadowingFromParent())
					NewError(new ShadowedSymbol(range, symbol));
			}
			scope.AddSymbol(symbol);
		}

		private void CheckTraitMethods(TypeSymbol typeSym)
		{
			var traits = new List<TypeSymbol>(typeSym.ImplementedTraits);

			var typeMemberNames = new HashSet<string>();
			var current = typeSym;
			while (current is not null)
			{
				foreach (var m in current.Members)
					typeMemberNames.Add(m.Name);
				current = current.Inherits;
			}

			foreach (var trait in traits)
			{
				foreach (var member in trait.Members)
				{
					if (!typeMemberNames.Contains(member.Name))
						NewError(new UnimplementedTraitMethod(typeSym.Identifier?.Range, typeSym, trait, member.Name));
				}
			}
		}

		/// <summary>
		/// Parses a type: '('type[','...]')' | identifier['('type[','...]')'].
		/// Tuples use TypeSymbolFactory.GetTuple.
		/// </summary>
		private TypeSymbol ParseType()
		{
			if (Lex.CurrentIs(TokenType.OpenBracket))
			{
				Lex.Next();
				var tupleArgs = new List<TypeSymbol>();
				while (true)
				{
					tupleArgs.Add(ParseType());
					if (Lex.CurrentIs(TokenType.CloseBracket))
						break;
					Lex.ExpectThis(TokenType.Comma);
				}
				Lex.Next();
				return TypeSymbolFactory.GetTuple(tupleArgs);
			}

			var nameToken = Lex.ExpectThis(TokenType.Identifier);
			var typeName = nameToken.Range.Text;

			List<TypeSymbol>? typeArgs = null;
			if (Lex.CurrentIs(TokenType.OpenBracket))
			{
				Lex.Next();
				var args = new List<TypeSymbol>();
				while (true)
				{
					args.Add(ParseType());
					if (Lex.CurrentIs(TokenType.CloseBracket))
						break;
					Lex.ExpectThis(TokenType.Comma);
				}
				Lex.Next();
				typeArgs = args.ToList();
			}

			var baseType = TypeSymbolFactory.GetTempTypeSymbol(typeName, typeArgs);
			return TypeSymbolFactory.GetTypeSymbol(baseType, typeArgs);
		}

		/// <summary>
		/// Parses the inheritance and implemented traits for a type declaration after the name.
		/// Example: <c>: BaseType & TraitA & TraitB</c>.
		/// Returns the single inherited type (or null) and a list of trait symbols.
		/// </summary>
		private (TypeSymbol? inheritedType, List<TypeSymbol> traits) ParseInheritanceAndTraits()
		{
			TypeSymbol? inheritedType = null;
			List<TypeSymbol> traits = [];

			if (Lex.CurrentIs(TokenType.Type))
			{
				Lex.Next();
				do
				{
					var item = ParseType();
					if (item is TypeSymbol traitSym)
						traits.Add(traitSym);
					else
					{
						if (inheritedType is not null)
							throw new SyntaxError(item.Identifier?.Range!, "Cannot inherit from multiple types. Only one inherited type is allowed.");
						inheritedType = item;
					}
				}
				while (Lex.CurrentIs(TokenType.LogicalAnd) && Lex.Next() is { });
			}

			return (inheritedType, traits);
		}

		/// <summary>
		/// Parses generic type parameters for a type declaration after the name.
		/// Example: <c>[T, U : Base & Trait]</c>.
		/// Adds the generic type symbols to the given scope and returns them.
		/// Assumes the current token is <see cref="TokenType.OpenBracket"/>.
		/// </summary>
		private List<TypeSymbol> ParseGenericTypeArguments(Token? nameToken = null)
		{
			List<TypeSymbol> genericTypes = [];

			Lex.GoPast(TokenType.OpenBracket);
			while (true)
			{
				var genericTypeToken = Lex.ExpectThis(TokenType.Identifier);
				TypeSymbol genericTypeSymbol;

				if (Lex.CurrentIs(TokenType.Type))
				{
					var (argInheritedType, argTraits) = ParseInheritanceAndTraits();
					if (argInheritedType is null)
						throw new SyntaxError(nameToken?.Range ?? Lex.CurrentToken.Range, "Expected type arguments after type declaration.");

					genericTypeSymbol = TypeSymbolFactory.GetTypeSymbol(
						new TypeSymbol(genericTypeToken.Range.Text, Lex.Filepath),
						[argInheritedType]
					);
				}
				else
				{
					genericTypeSymbol = TypeSymbolFactory.GetTypeSymbol(
						new TypeSymbol(genericTypeToken.Range.Text, Lex.Filepath)
					);
				}

				genericTypes.Add(genericTypeSymbol);

				if (!Lex.CurrentIs(TokenType.Comma))
					break;

				Lex.Next();
			}

			Lex.ExpectThis(TokenType.CloseBracket);
			return genericTypes;
		}

		/// <summary>
		/// Parses a name-type pair or tuple of pairs.
		/// Single: varName : Type | varName : Type(TypeArg, ...)
		/// Tuple of pairs: (varName1 : Type1, varName2 : Type2)
		/// Tuple names with tuple type: (varName1, varName2) : (Type1, Type2)
		/// Only continues parsing multiple pairs when inside a tuple.
		/// </summary>
		private List<VarSymbol> ParseNameTypePair(Scope scope)
		{
			if (!Lex.CurrentIs(TokenType.OpenBracket))
			{
				var nameToken = Lex.ExpectThis(TokenType.Identifier);
				var name = nameToken.Range.Text;
				TypeSymbol type = Builtins.Any;
				if (Lex.CurrentIs(TokenType.Type))
				{
					Lex.Next();
					type = ParseType();
				}
				return [new VarSymbol(name, Lex.Filepath, type, nameToken)];
			}

			Lex.ExpectThis(TokenType.OpenBracket);
			var firstName = Lex.ExpectThis(TokenType.Identifier);

			if (Lex.CurrentIs(TokenType.Type))
			{
				Lex.Next();
				var type = ParseType();
				var result = new List<VarSymbol> { new VarSymbol(firstName.Range.Text, Lex.Filepath, type, firstName) };
				while (Lex.CurrentIs(TokenType.Comma))
				{
					Lex.Next();
					var nameToken = Lex.ExpectThis(TokenType.Identifier);
					Lex.ExpectThis(TokenType.Type);
					var t = ParseType();
					result.Add(new VarSymbol(nameToken.Range.Text, Lex.Filepath, t, nameToken));
				}
				Lex.ExpectThis(TokenType.CloseBracket);
				return result;
			}

			if (Lex.CurrentIs(TokenType.Comma) || Lex.CurrentIs(TokenType.CloseBracket))
			{
				var names = new List<(Token token, string text)> { (firstName, firstName.Range.Text) };
				while (Lex.CurrentIs(TokenType.Comma))
				{
					Lex.Next();
					var nameToken = Lex.ExpectThis(TokenType.Identifier);
					names.Add((nameToken, nameToken.Range.Text));
				}
				Lex.ExpectThis(TokenType.CloseBracket);
				Lex.ExpectThis(TokenType.Type);
				if (!Lex.CurrentIs(TokenType.OpenBracket))
					throw new SyntaxError(Lex.CurrentToken.Range, "Expected tuple type '(Type1, Type2, ...)' for multiple names.");
				var types = new List<TypeSymbol>();
				Lex.Next();
				while (true)
				{
					types.Add(ParseType());
					if (Lex.CurrentIs(TokenType.CloseBracket))
						break;
					Lex.ExpectThis(TokenType.Comma);
				}
				Lex.Next();
				if (names.Count != types.Count)
					throw new SyntaxError(Lex.CurrentToken.Range, $"Name count ({names.Count}) does not match type count ({types.Count}).");
				return names.Select((n, i) => new VarSymbol(n.text, Lex.Filepath, types[i], n.token)).ToList();
			}

			throw new SyntaxError(Lex.CurrentToken.Range, "Expected ':' type or ',' after name in tuple.");
		}

		/// <summary>
		/// Parses a function signature: funcName(arg1: Type1, arg2, arg3: Type2, arg4): FuncReturnType
		/// Stops when the argument bracket closes or the return type is read.
		/// Returns (VarSymbol of type Callable, List of VarDeclStmt for each argument).
		/// </summary>
		private (VarSymbol FuncSymbol, List<VarDeclStmt> ArgDecls, List<TypeSymbol> TypeArgs) ParseFuncSignature(Scope scope)
		{
			var funcNameToken = Lex.ExpectThis(TokenType.Identifier);
			var funcName = funcNameToken.Range.Text;
			var typeArgs = ParseGenericTypeArguments(funcNameToken);
			var typeArgTuple = typeArgs.Count == 0 ? Builtins.None : TypeSymbolFactory.GetTuple(typeArgs);

			Lex.ExpectThis(TokenType.OpenBracket);

			var argSymbols = new List<VarSymbol>();
			var argDecls = new List<VarDeclStmt>();

			while (!Lex.CurrentIs(TokenType.CloseBracket))
			{
				var startRange = Lex.CurrentToken.Range;
				var syms = ParseNameTypePair(scope);
				var endRange = Lex.PeekNext(-1).Range;

				var declRange = syms.Count == 1 ? (startRange + endRange) : (syms.Last().Identifier?.Range ?? startRange + endRange);
				var decl = new VarDeclStmt
				{
					Scope = scope,
					Name = [.. syms],
					Value = null,
					IsConst = false,
					InnerRange = declRange,
				};

				foreach (var sym in syms)
				{
					sym.Source = Lex.Filepath;
					sym.Declaration = decl;
					argSymbols.Add(sym);
				}

				argDecls.Add(decl);
				if (!Lex.CurrentIs(TokenType.Comma))
					break;
				
				Lex.Next();
			}

			Lex.ExpectThis(TokenType.CloseBracket);
			TypeSymbol returnType = Builtins.Any;
			if (Lex.CurrentIs(TokenType.Type))
			{
				Lex.Next();
				returnType = ParseType();
			}

			TypeSymbol argsTuple = argSymbols.Count == 0
				? Builtins.None
				: TypeSymbolFactory.GetTuple(argSymbols.Select(s => s.Type).ToList());

			var callableType = TypeSymbolFactory.GetTypeSymbol(Builtins.Callable, [argsTuple, returnType]);
			if (typeArgs.Count > 0)
				callableType = TypeSymbolFactory.GetTypeSymbol(Builtins.Generator, [typeArgTuple, callableType]);
			
			var funcVar = new VarSymbol(funcName, Lex.Filepath, callableType, funcNameToken);

			return (funcVar, argDecls, typeArgs);
		}

		private ImportStmt ParseImport(Scope scope, Token firstToken)
		{
			// import "filepath" as name  |  import filepath as name
			// import "filepath"         |  import filepath  (uses filename as name)
			Lex.GoPast(TokenType.Import);

			Token pathToken;
			if (Lex.CurrentIs(TokenType.StringLiteral))
			{
				pathToken = Lex.ExpectThis(TokenType.StringLiteral);
			}
			else if (Lex.CurrentIs(TokenType.Identifier))
			{
				var next = Lex.PeekNext(1);
				if (next.Which != TokenType.As && next.Which != TokenType.StmtSeparator && next.Which != TokenType.EOF)
					throw new UnexpectedToken(Lex.CurrentToken.Range, TokenType.StringLiteral, Lex.CurrentToken);
				pathToken = Lex.ExpectThis(TokenType.Identifier);
			}
			else
			{
				throw new UnexpectedToken(Lex.CurrentToken.Range, TokenType.StringLiteral, Lex.CurrentToken);
			}

			var filepath = pathToken.Which == TokenType.StringLiteral
				? pathToken.Range.Text.Trim('"', '\'')
				: pathToken.Range.Text;

			string moduleName;
			if (Lex.CurrentIs(TokenType.As))
			{
				Lex.ExpectThis(TokenType.As);
				var nameToken = Lex.ExpectThis(TokenType.Identifier);
				moduleName = nameToken.Range.Text;
			}
			else
			{
				moduleName = Path.GetFileNameWithoutExtension(filepath);
				if (!System.Text.RegularExpressions.Regex.IsMatch(moduleName, @"^[a-zA-Z_]\w*$"))
					throw new SyntaxError(pathToken.Range, $"Filename '{moduleName}' is not a valid identifier. Use 'as <name>' to specify a module name.");
			}

			var resolvedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Lex.Filepath) ?? "", filepath));

			return new ImportStmt
			{
				Scope = scope,
				Filepath = resolvedPath,
				ModuleName = moduleName,
				InnerRange = firstToken.Range
			};
		}

		private Stmt? ParseLoopStmt(Scope currentScope, Token firstToken)
		{
			Scope newScope = new(currentScope);
			Stmt? newStmt = null;

			switch (firstToken.Which)
			{
				case TokenType.While:
				{
					Expr? conditionExpr = null;
					ParseExpr(ref conditionExpr, Lex.Next());
					if (conditionExpr is null)
						throw new MalformedExpr(firstToken.Range);
					
					Lex.SkipStmtSeparator();
					var bodyStmt = ParseStmt(newScope);
					if (bodyStmt is null)
						throw new SyntaxError(firstToken.Range, "Expected statement after while condition");
					
					newStmt = new PreconditionLoopStmt()
					{
						Scope = newScope,
						Condition = conditionExpr,
						Body = bodyStmt,
					};

					break;
				}
				case TokenType.For:
				{
					VarDeclStmt? loopVar = null;
					Expr? conditionExpr = null;
					Expr? iteratorExpr = null;
					
					var nextToken = Lex.Next();

					if (Lex.CurrentIs(TokenType.VarDecl))
					{
						loopVar = (ParseStmt(newScope) as VarDeclStmt)
							?? throw new UnexpectedToken(nextToken.Range, TokenType.VarDecl, nextToken);
					}
					
					nextToken = Lex.Expect(TokenType.StmtSeparator);
					ParseExpr(ref conditionExpr, nextToken);

					nextToken = Lex.Expect(TokenType.StmtSeparator);
					ParseExpr(ref iteratorExpr, nextToken);
					
					nextToken = Lex.Expect(TokenType.StmtSeparator);

					Lex.SkipStmtSeparator();
					var bodyStmt = ParseStmt(newScope);

					newStmt = new ForLoopStmt()
					{
						Scope = newScope,
						LoopVariable = loopVar,
						Condition = conditionExpr,
						Iterator = iteratorExpr,
						Body = bodyStmt,
					};

					break;
				}
				case TokenType.Foreach:
				{
					Lex.ExpectNext(TokenType.VarDecl);
					var loopSyms = ParseNameTypePair(newScope);
					if (loopSyms.Count == 0)
						throw new SyntaxError(Lex.CurrentToken.Range, "Expected variable declaration.");
					foreach (var sym in loopSyms)
						sym.Source = Lex.Filepath;

					VarDeclStmt lopVar = new()
					{
						Scope = newScope,
						IsConst = false,
						Name = [.. loopSyms],
						Value = null,
					};
					foreach (var item in loopSyms)
						item.Declaration = lopVar;

					firstToken = Lex.Expect(TokenType.In);
					
					Expr? iteratorExpr = null;
					ParseExpr(ref iteratorExpr, firstToken);
					if (iteratorExpr is null)
						throw new SyntaxError(firstToken.Range, "Expected expression after foreach iterator.");
					
					//firstToken = Lex.Expect(TokenType.StmtSeparator);
					//FIX newline b4 { will error 
					Lex.SkipStmtSeparator();
					var bodyStmt = ParseStmt(newScope)
						/*?? throw new SyntaxError(firstToken.Range, "Expected statement after foreach header")*/;
                    
					newStmt = new ForeachLoopStmt()
					{
						Scope = newScope,
						LoopVariable = lopVar,
						Iterator = iteratorExpr,
						Body = bodyStmt,
					};

					break;
				}
				case TokenType.Repeat:
				{
					Lex.GoPast(TokenType.Repeat);
					Lex.SkipStmtSeparator();
					var bodyStmt = ParseStmt(newScope);

					if (Lex.NextIs(TokenType.Until))
					{
						Lex.GoPast(TokenType.Until);
						Lex.SkipStmtSeparator();
						Expr? conditionExpr = null;
						ParseExpr(ref conditionExpr, Lex.CurrentToken);
						if (conditionExpr is null)
							throw new MalformedExpr(firstToken.Range);
						
						newStmt = new PostconditionLoopStmt()
						{
							Scope = newScope,
							Condition = conditionExpr,
							Body = bodyStmt,
						};
					}
					else
					{
						newStmt = new LoopStmt()
						{
							Scope = newScope,
							Body = bodyStmt,
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

		private enum CaptureKind {Expr, Stmt, Keyword}
		private readonly record struct ExpectedValue(CaptureKind Kind, TokenType? Token = null)
		{
			public static ExpectedValue Expr => new(CaptureKind.Expr);
			public static ExpectedValue Stmt => new(CaptureKind.Stmt);
			public static ExpectedValue Kw(TokenType t) => new(CaptureKind.Keyword, t);
		}

		private readonly record struct CapturedItem(CaptureKind Kind, Expr? Expr = null, Stmt? Stmt = null, TokenType? Keyword = null);

		private readonly record struct CapturedStmt(IReadOnlyList<CapturedItem> Items)
		{
			public static CapturedStmt Empty => new([]);
			public readonly IReadOnlyList<Expr> Exprs => [.. Items.Where(i => i.Kind == CaptureKind.Expr).Select(i => i.Expr!).Where(e => e is not null)];
			public readonly IReadOnlyList<Stmt> Stmts => [.. Items.Where(i => i.Kind == CaptureKind.Stmt).Select(i => i.Stmt!).Where(s => s is not null)];
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

		private CapturedStmt? ParseContainerStmt (
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
						if (newExpr is null)
							return null;
						items.Add(new CapturedItem(CaptureKind.Expr, Expr: newExpr));
						break;
					}
					case CaptureKind.Stmt:
					{
						Stmt? newStmt = null;
						newStmt = ParseStmt(currentScope);
						if (newStmt is null)
							return null;
						items.Add(new CapturedItem(CaptureKind.Stmt, Stmt: newStmt));
						break;
					}
					case CaptureKind.Keyword:
					{
						var keyword = Lex.ExpectThis(expectedValue.Token!.Value);
						items.Add(new CapturedItem(CaptureKind.Keyword, Keyword: keyword.Which));
						break;
					}
				}
			}

			return items.Count > 0 ? new CapturedStmt(items) : null;
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
					Lex.GoPast(TokenType.VarDecl);
					var isConst = specifiers.Any(t => t.Which == TokenType.ConstSpec);

					Scope newScope = new(currentScope);
					var syms = ParseNameTypePair(newScope);

					Expr? valExpr = null;
					if (Lex.CurrentIs(TokenType.Assign))
					{
						Lex.Next();
						ParseExpr(ref valExpr, Lex.CurrentToken);
					}

					if (valExpr is null && isConst)
						throw new SyntaxError(syms.First().Identifier?.Range ?? firstToken.Range, "No value given to initialize constant.");

					foreach (var sym in syms)
						sym.Source = Lex.Filepath;

					VarDeclStmt varDecl = new()
					{
						Scope = newScope,
						IsConst = isConst,
						Name = [.. syms],
						Value = valExpr,
					};
					foreach (var item in syms)
						item.Declaration = varDecl;

					newStmt = varDecl;
					AddToScope(varDecl.Name, newScope);

					break;
				}
				case TokenType.FuncDecl:
				{
					Lex.GoPast(TokenType.FuncDecl);
					var (funcSymbol, argDecls, typeArgs) = ParseFuncSignature(currentScope);
					//use exprs

					Scope funcScope = new(currentScope);
					foreach (var argDecl in argDecls)
						AddToScope(argDecl.Name, funcScope);
					foreach (var typeArg in typeArgs)
						AddToScope(typeArg, funcScope);

					Lex.SkipStmtSeparator();
					var innerStmt = ParseStmt(funcScope);
					if (innerStmt is null)
						throw new SyntaxError(firstToken.Range, "Expected statement after function declaration.");

					newStmt = new FuncDeclStmt(funcSymbol, innerStmt) { Scope = funcScope };
					AddToScope(funcSymbol, currentScope);

					break;
				}
				case TokenType.TraitDecl:
				{
					Lex.GoPast(TokenType.TraitDecl);
					var nameToken = Lex.ExpectThis(TokenType.Identifier);
					var traitName = nameToken.Range.Text;
					var typeArgs = ParseGenericTypeArguments(nameToken);

					Lex.ExpectThis(TokenType.OpenCurlyBracket);
					Lex.SkipStmtSeparator();

					Scope traitScope = new(currentScope) { AllowShadowingFromParent = true };
					foreach (var typeArg in typeArgs)
						AddToScope(typeArg, traitScope);

					var baseTraitSym = new TypeSymbol(traitName, Lex.Filepath);
					var traitSym = TypeSymbolFactory.GetTypeSymbol(baseTraitSym, typeArgs);
					var traitStmt = new TraitDeclStmt(traitSym) { Scope = traitScope };

					while (!Lex.CurrentIs(TokenType.CloseCurlyBracket))
					{
						switch (Lex.CurrentToken.Which)
						{
							case TokenType.FuncDecl:
							{
								Lex.GoPast(TokenType.FuncDecl);
								var (sym, _, _) = ParseFuncSignature(traitScope);
								sym.Source = Lex.Filepath;
								sym.Declaration = traitStmt;
								(traitStmt.Name as TypeSymbol)!.Members.Add(sym);
								break;
							}
							case TokenType.VarDecl:
							{
								Lex.GoPast(TokenType.VarDecl);
								var add = ParseNameTypePair(traitScope);
								if (add.Count != 1) 
									throw new SyntaxError(Lex.CurrentToken.Range, "Expected single variable declaration in trait body.");
								var sym = add[0];
								sym.Source = Lex.Filepath;
								sym.Declaration = traitStmt;
								(traitStmt.Name as TypeSymbol)!.Members.Add(sym);
								break;
							}
							default:
							{
								throw new SyntaxError(Lex.CurrentToken.Range, "Expected variable or function declaration in trait body.");
							}
						}
						
						Lex.SkipStmtSeparator();
					}

					AddToScope(traitStmt.Name, currentScope);
					newStmt = traitStmt;
					//increase depth so ParseBranch doesnt think the } closes the block
					++_depth;

					break;
				}
				case TokenType.TypeDecl:
				{
					Lex.GoPast(TokenType.TypeDecl);
					var nameToken = Lex.ExpectThis(TokenType.Identifier);
					var typeName = nameToken.Range.Text;
					var (inheritedType, traits) = ParseInheritanceAndTraits();
					var typeArgs = ParseGenericTypeArguments(nameToken);

					if (!Lex.CurrentIs(TokenType.OpenCurlyBracket))
						throw new UnexpectedToken(nameToken.Range, TokenType.OpenCurlyBracket, nameToken);
					Lex.GoPast(TokenType.OpenCurlyBracket);
					Lex.SkipStmtSeparator();

					//create base type symbol
					var baseTypeSym = new TypeSymbol(typeName, Lex.Filepath, nameToken, inherits: inheritedType, implementedTraits: traits);

					//make sure its registered in the factory
					var typeSym = TypeSymbolFactory.GetTypeSymbol(baseTypeSym);
					if (typeArgs.Count > 0)
						typeSym = TypeSymbolFactory.GetTypeSymbol(baseTypeSym, typeArgs);

					Scope newScope = new(currentScope) { AllowShadowingFromParent = true };

					//potentailly remove self as a variable and use a special self token
					var selfSym = new VarSymbol("self", typeSym) { Source = Lex.Filepath, Specifiers = [TokenType.PrivateSpec] };
					newScope.AddSymbol(selfSym);

					var body = new CompoundStmt() { Scope = newScope, Statements = ParseBranch(newScope) };
					foreach (var stmt in body.Statements)
					{
						if (stmt is VarDeclStmt vd)
						{
							if (vd.Name.Count != 1)
								throw new SyntaxError(vd.Name.First().Identifier?.Range ?? firstToken.Range, "Expected single variable declaration in type body.");
							var sym = vd.Name[0];

							if (typeSym.GetMember(sym.Name) is not null && !sym.Specifiers.Contains(TokenType.OverrideSpec))
								NewError(new ShadowedClassMember(sym.Identifier?.Range ?? firstToken.Range, sym));

							typeSym.Members.Add(sym);
							if (!sym.Specifiers.Contains(TokenType.PrivateSpec) && !sym.Specifiers.Contains(TokenType.PublicSpec))
							{
								if (Result.GlobalDecorators.Any(d => d.DecoratorType == Builtins.PrivateByDefault))
									sym.Specifiers.Add(TokenType.PrivateSpec);
								else
									sym.Specifiers.Add(TokenType.PublicSpec);
							}
						}
						else if (stmt is DeclStmt decl)
						{
							if (typeSym.GetMember(decl.Name.Name) is not null && !decl.Name.Specifiers.Contains(TokenType.OverrideSpec))
								NewError(new ShadowedClassMember(decl.Name.Identifier?.Range ?? firstToken.Range, decl.Name));

							typeSym.Members.Add(decl.Name);
							if (!decl.Name.Specifiers.Contains(TokenType.PrivateSpec) && !decl.Name.Specifiers.Contains(TokenType.PublicSpec))
							{
								if (Result.GlobalDecorators.Any(d => d.DecoratorType == Builtins.PrivateByDefault))
									decl.Name.Specifiers.Add(TokenType.PrivateSpec);
								else
									decl.Name.Specifiers.Add(TokenType.PublicSpec);
							}
						}
						else if (stmt is not null)
							throw new SyntaxError(stmt.GetFullRangeOrThrow(), "Only declarations are allowed in type bodies.");
					}

					CheckTraitMethods(typeSym);

					AddToScope(typeSym, currentScope);
					newStmt = new TypeDeclStmt(typeSym, body) { Scope = currentScope };

					break;
				}
				case TokenType.Return:
				{
					Expr? returnExpr = null;
					ParseExpr(ref returnExpr, Lex.GoPast(TokenType.Return));
					newStmt = new ReturnStmt()
					{ 
						Scope = currentScope,
						Value = returnExpr,
					};
					break;
				}
				case TokenType.Import:
				{
					var newScope = new Scope(currentScope);
					newStmt = ParseImport(newScope, firstToken);
					if (newStmt is ImportStmt importStmt)
					{
						var moduleSym = new VarSymbol(importStmt.ModuleName, Lex.Filepath, Builtins.Module);
						// moduleSym.Declaration = importStmt;
						AddToScope(moduleSym, newScope);
					}
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
					Expr? conditionExpr = null;
					ParseExpr(ref conditionExpr, Lex.GoPast(TokenType.If));
					if (conditionExpr is null)
						throw new MalformedExpr(firstToken.Range);
					Scope newScope = new(currentScope);
					Lex.SkipStmtSeparator();

					var compounds = 0;
					var nextIfStmt = ParseStmt(newScope);
					if (nextIfStmt is null)
						throw new SyntaxError(firstToken.Range, "Expected statement after if condition");
					if (nextIfStmt is CompoundStmt compound)
						++compounds;

					var ifStmt = new IfStmt()
					{
						Scope = currentScope,
						Condition = conditionExpr,
						NextIf = nextIfStmt,
					};

					var lastIf = ifStmt;
					while (Lex.NextIs(TokenType.Elif))
					{
						var elifToken = Lex.GoPast(TokenType.Elif);
						Expr? elifCondition = null;
						ParseExpr(ref elifCondition, elifToken);
						if (elifCondition is null)
							throw new MalformedExpr(firstToken.Range);
						Lex.SkipStmtSeparator();
						var elifStmt = ParseStmt(newScope);
						if (elifStmt is CompoundStmt)
							++compounds;
						
						if (elifStmt is null)
							throw new SyntaxError(firstToken.Range, "Expected statement after elif condition");
						var newIf = new IfStmt()
						{
							Scope = currentScope,
							Condition = elifCondition,
							NextIf = elifStmt,
						};
						lastIf.NextElse = newIf;
						lastIf = newIf;
					}

					if (Lex.NextIs(TokenType.Else))
					{
						Lex.GoPast(TokenType.Else);
						lastIf.NextElse = ParseStmt(newScope);
						if (lastIf.NextElse is CompoundStmt)
							++compounds;
					}
					newStmt = ifStmt;
					_depth -= compounds - 
						(lastIf.NextElse is CompoundStmt || 
						(lastIf.NextElse is null && lastIf.NextIf is CompoundStmt)
						? 1 : 0);
					
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

			// Lex.Next();
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
