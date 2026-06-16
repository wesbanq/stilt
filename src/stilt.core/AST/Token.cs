using System.Reflection;

namespace stilt
{
	/// <summary>Declares a spelling that lexes to the decorated <see cref="TokenType"/>: a literal string, or a regex when <see cref="IsRegex"/> is set. A token may carry several (e.g. <c>and</c> and <c>&amp;</c>).</summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
	public class SymbolAttribute : Attribute, IDescriptable
	{
		public string Symbol;
		public bool IsRegex;
		public string Name => Symbol;

		public SymbolAttribute(string symbol, bool regex = false)
		{
			Symbol = symbol;
			IsRegex = regex;
		}
	}

	/// <summary>Marks a <see cref="TokenType"/> as usable as an operator with the given binding <see cref="Precedence"/> (lower binds tighter). The subclass — <see cref="UnaryOperatorAttribute"/>/<see cref="BinaryOperatorAttribute"/>/<see cref="TernaryOperatorAttribute"/> — fixes its arity.</summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class OperatorAttribute : Attribute
	{
		public int Precedence;

		public OperatorAttribute(int precedence)
		{
			Precedence = precedence;
		}
	}
	public class UnaryOperatorAttribute(int p, bool allowPrefix = true) : OperatorAttribute(p) 
	{ 
		public bool AllowPrefix { get; } = allowPrefix; 
	}
	public class BinaryOperatorAttribute(int p) : OperatorAttribute(p) { }
	public class TernaryOperatorAttribute(int p) : OperatorAttribute(p) { }

	/// <summary>Marks a token/keyword that is recognized by the lexer but not yet handled; using one raises an <see cref="UnimplementedError"/>.</summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class UnimplementedAttribute : Attribute { }

	/// <summary>Marks a token as a declaration specifier (e.g. <c>pub</c>, <c>const</c>) that may lead a statement.</summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class SpecifierAttribute : Attribute { }

	/// <summary>
	/// A lexed token: which <see cref="TokenType"/> it is and the source <see cref="Range"/> it spans. Its role
	/// (operator, specifier, unimplemented) is read on demand from the attributes on that enum value, so the token
	/// stays a thin pairing while <see cref="TokenType"/> remains the single source of truth for the grammar.
	/// </summary>
	public class Token
	{
		public TokenType Which;
		public FileRange Range;

		public bool IsUnimplemented => Utils.GetAttributeFromEnum<TokenType, UnimplementedAttribute>(Which) is not null;
		public bool IsSpecifier => Utils.GetAttributeFromEnum<TokenType, SpecifierAttribute>(Which) is not null;
		public bool IsOperator => OperatorAttributes is not null;

		public OperatorAttribute[]? OperatorAttributes => Utils.GetAttributesFromEnum<TokenType, OperatorAttribute>(Which);
		/// <summary>
		/// Builds the candidate operator <see cref="Expr"/> nodes this token could start, restricted to attributes of
		/// kind <typeparamref name="T"/>. A token may map to several arities (e.g. <c>-</c> is unary or binary), so the
		/// parser asks for the relevant family and picks one based on context. Returns false if the token is not an operator.
		/// </summary>
		public bool TryGetOperatorExprs<T>(out List<Expr> exprs)
			where T : OperatorAttribute
		{
			exprs = [];
			if (OperatorAttributes is null)
				return false;
			var operators = OperatorAttributes.Where(o => o is T);

			foreach (var op in operators)
			{
				Expr expr = op switch
				{
					UnaryOperatorAttribute => new UnaryExpr(op.Precedence, Range, this),
					BinaryOperatorAttribute when Which == TokenType.OpenBracket => 
						new CallExpr(op.Precedence, Range, this),
					BinaryOperatorAttribute when Which == TokenType.Comma => 
						new CommaExpr(op.Precedence, Range, this),
					BinaryOperatorAttribute when Which == TokenType.Access => 
						new AccessExpr(op.Precedence, Range, this),
					BinaryOperatorAttribute when Which == TokenType.NullAccess => 
						new NullAccessExpr(op.Precedence, Range, this),
					BinaryOperatorAttribute when Which == TokenType.Assign => 
						new AssignExpr(op.Precedence, Range, this),
					BinaryOperatorAttribute => new BinaryExpr(op.Precedence, Range, this),
					TernaryOperatorAttribute => new TernaryExpr(op.Precedence, Range, this),
					_ => throw new UnexpectedToken(Range, this)
				};
				exprs.Add(expr);
			}

			return true;
		}
	}

	/// <summary>
	/// Every kind of token in the language and the single source of truth for the grammar: each member's attributes
	/// declare how it lexes (<see cref="SymbolAttribute"/>) and, where applicable, how it parses
	/// (<see cref="OperatorAttribute"/> precedence/arity, <see cref="SpecifierAttribute"/>, <see cref="UnimplementedAttribute"/>).
	/// The lexer and parser read these via reflection, so adding an operator is mostly a matter of annotating a member here.
	/// </summary>
	public enum TokenType
	{
		//regex patterns used CANNOT have backtracking
		EOF,

		[UnaryOperator(2)]
		[BinaryOperator(4)]
		[Symbol("+")]
		Plus,

		[UnaryOperator(2)]
		[BinaryOperator(4)]
		[Symbol("-")]
		Minus,

		[BinaryOperator(3)]
		[Symbol("/")]
		Divide,

		[BinaryOperator(3)]
		[Symbol("*")]
		Star,

		[BinaryOperator(3)]
		[Symbol("%")]
		Modulo,

		[BinaryOperator(2)]
		[Symbol("**")]
		Exponent,

		[Unimplemented]
		[BinaryOperator(5)]
		[Symbol("..")]
		Range,

		[UnaryOperator(1)]
		[Symbol("++")]
		Increment,

		[UnaryOperator(1)]
		[Symbol("--")]
		Decrement,

		[BinaryOperator(16)]
		[Symbol("=")]
		Assign,

		// Comparison Operators
		[BinaryOperator(6)]
		[Symbol(">")]
		Greater,

		[BinaryOperator(6)]
		[Symbol("<")]
		Lesser,

		[BinaryOperator(6)]
		[Symbol(">=")]
		GreaterOrEqual,

		[BinaryOperator(6)]
		[Symbol("<=")]
		LesserOrEqual,

		[BinaryOperator(6)]
		[Symbol("==")]
		Equals,

		[BinaryOperator(6)]
		[Symbol("!=")]
		Unequals,

		// Logical Operators
		[UnaryOperator(2)]
		[Symbol("!")]
		[Symbol("not")]
		LogicalNot,

		[BinaryOperator(2)]
		[Symbol("|")]
		[Symbol("or")]
		LogicalOr,

		[BinaryOperator(2)]
		[Symbol("&")]
		[Symbol("and")]
		LogicalAnd,

		[BinaryOperator(2)]
		[Symbol("^")]
		[Symbol("xor")]
		LogicalXor,

		// Signal Operators
		[Unimplemented]
		[BinaryOperator(14)]
		[Symbol("->")]
		ConnectSignal,

		[Unimplemented]
		[BinaryOperator(14)]
		[Symbol("<-")]
		EmitSignal,

		// Literals
		[Symbol("null")]
		Null,

		[Symbol("true")]
		True,

		[Symbol("false")]
		False,

		[Symbol(@"[rftm]*""(?:\\""|[^""])*""", true)]
		StringLiteral,

		[Symbol(@"(?:\d[\d_]*\.(?:\d[\d_]*)?|(?:\d[\d_]*)?\.\d[\d_]*)[bsilfd]?", true)]
		DecimalNumericLiteral,

		[Symbol(@"\d[\d_]*[bsilfd]?", true)]
		WholeNumericLiteral,

		[Symbol(@"0x[\da-fA-F]+[\da-fA-F_]*[bsilfd]?", true)]
		HexNumericLiteral,

		[Symbol(@"0o[0-8]+[0-8_]*[bsilfd]?", true)]
		OctalNumericLiteral,

		[Symbol(@"0b[01]+[01_]*[bsilfd]?", true)]
		ByteNumericLiteral,

		[Symbol(@"(?:\d[\d_]*\.(?:\d[\d_]*)?|(?:\d[\d_]*)?\.\d[\d_]*)[eE][-+]\d+[bsilfd]?", true)]
		ScientificNumericLiteral,

		// Delimiters
		[Symbol(@";[ \n]*", true)]
		StrictStmtSeparator,

		// Newline runs outside [] only; see Lexer.TryLexStmtSeparator (NonBacktracking regex cannot express this).
		SoftStmtSeparator,

		[BinaryOperator(15)]
		[Symbol(",")]
		Comma,

		[Symbol("{")]
		OpenCurlyBracket,

		[Symbol("}")]
		CloseCurlyBracket,

		[BinaryOperator(1)]
		[Symbol("[")]
		OpenSquareBracket,

		[Symbol("]")]
		CloseSquareBracket,

		[BinaryOperator(1)]
		[Symbol("(")]
		OpenBracket,

		[Symbol(")")]
		CloseBracket,

		// Special
		[UnaryOperator(3)]
		[Symbol("new")]
		New,

		[UnaryOperator(3)]
		[Symbol("clone")]
		Clone,

		[BinaryOperator(1)]
		[Symbol(".")]
		Access,

		[BinaryOperator(1)]
		[Symbol("?.")]
		NullAccess,

		[BinaryOperator(1)]
		[Symbol("??")]
		NullCoalescing,

		[BinaryOperator(2)]
		[Symbol(":")]
		Type,

		[Symbol("then")]
		Then,

		[Unimplemented]
		[BinaryOperator(14)]
		[Symbol("|>")]
		Update,

		[Unimplemented]
		[BinaryOperator(14)]
		[Symbol("!>")]
		Overwrite,

		[Unimplemented]
		[BinaryOperator(14)]
		[Symbol(".>")]
		Composition,

		[Unimplemented]
		[Symbol("=>")]
		NamedTuple,

		[Symbol(@"[@a-zA-Z_][@\w]*|'(?:\\'|[^'])*'", true)]
		Identifier,

		// [Symbol(@"\[\[.*\]\]", true)]
		[Symbol("[[")]
		DecoratorBegin,

		[Symbol("]]")]
		DecoratorEnd,

		// Declarations
		[Symbol("func")]
		FuncDecl,

		[Unimplemented]
		[Symbol("enum")]
		EnumDecl,

		[Symbol("var")]
		VarDecl,

		[Symbol("type")]
		TypeDecl,

		[Symbol("trait")]
		TraitDecl,

		[Unimplemented]
		[Symbol("extend")]
		ExtensionDecl,

		[Unimplemented]
		[Symbol("signal")]
		SignalDecl,

		// Specifiers
		[Specifier]
		[Symbol("prv")]
		[Symbol("private")]
		PrivateSpec,

		[Specifier]
		[Symbol("pub")]
		[Symbol("public")]
		PublicSpec,

		[Specifier]
		[Unimplemented]
		[Symbol("shared")]
		SharedSpec,

		[Specifier]
		[Symbol("const")]
		ConstSpec,

		[Specifier]
		[Symbol("override")]
		OverrideSpec,

		// Keywords
		[Unimplemented]
		[UnaryOperator(13)]
		[Symbol("await")]
		Await,

		[TernaryOperator(12)]
		[Symbol("if")]
		If,

		[Symbol("else")]
		Else,

		[Symbol("elif")]
		Elif,

		[Symbol("return")]
		Return,

		[Symbol("import")]
		Import,

		[Symbol("as")]
		As,

		[Symbol("while")]
		While,

		[Symbol("for")]
		For,

		[Symbol("foreach")]
		Foreach,

		[Symbol("continue")]
		Continue,

		[Symbol("repeat")]
		Repeat,

		[Symbol("until")]
		Until,

		[Symbol("break")]
		Break,

		[Symbol("in")]
		In,

		[Unimplemented]
		[Symbol("match")]
		Match,

		[Unimplemented]
		[Symbol("case")]
		Case,

		[Symbol("version")]
		Version,

		// Special
		[Symbol(@"execute(?:\s*\/.*\n)*", true)]
		[Symbol(@"execute +as +.*?\n(?:\s*\/.*)*", true)]
		ExecuteStmt,
	}
}
