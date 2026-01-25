using stilt.AST;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace stilt
{
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

	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class OperatorAttribute : Attribute
	{
		public int Precedence;
		public Type AssociatedExpr;

		public OperatorAttribute(int precedence, Type associatedExpr)
		{
			Precedence = precedence;
			AssociatedExpr = associatedExpr;
		}
	}
	public class UnaryOperatorAttribute(int p, Type e) : OperatorAttribute(p, e) { }
	public class BinaryOperatorAttribute(int p, Type e) : OperatorAttribute(p, e) { }
	public class TernaryOperatorAttribute(int p, Type e) : OperatorAttribute(p, e) { }

	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class UnimplementedAttribute : Attribute { }

	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class SpecifierAttribute : Attribute { }

	public class Token
	{
		public TokenType Which;
		public FileRange Range;

		public bool IsUnimplemented => Program.GetAttributeFromEnum<TokenType, UnimplementedAttribute>(Which) != null;
		public bool IsSpecifier => Program.GetAttributeFromEnum<TokenType, SpecifierAttribute>(Which) != null;

		public Expr[]? GetOperators()
		{
			var l = Program.GetAttributesFromEnum<TokenType, OperatorAttribute>(Which);
			return l != null ? [.. l.Select(static a => 
				Activator.CreateInstance(a.AssociatedExpr, a.Precedence) as Expr ?? throw new Exception())] : null;
		}

		public static string[] GetRulesFromType(TokenType t)
		{
			var types = typeof(TokenType).GetFields();

			Activator.CreateInstance(typeof(AdditionExpr), 0);
			return types[(int)t + 1].GetCustomAttributes<SymbolAttribute>().Select(o => o.Symbol).ToArray();
		}

	}

	public enum TokenType
	{
		//regex patterns used CANNOT have backtracking
		EOF,

		[UnaryOperator(2, typeof(PlusExpr))]
		[BinaryOperator(4, typeof(AdditionExpr))]
		[Symbol("+")]
		Plus,

		[UnaryOperator(2, typeof(NegationExpr))]
		[BinaryOperator(4, typeof(SubtractionExpr))]
		[Symbol("-")]
		Minus,

		[BinaryOperator(3, typeof(DivisionExpr))]
		[Symbol("/")]
		Divide,

		[BinaryOperator(3, typeof(MultiplicationExpr))]
		[Symbol("*")]
		Star,

		[BinaryOperator(3, typeof(ModuloExpr))]
		[Symbol("%")]
		Modulo,

		[BinaryOperator(1, typeof(ExponentExpr))]
		[Symbol("**")]
		Exponent,

		[Unimplemented]
		[BinaryOperator(5, typeof(RangeExpr))]
		[Symbol("..")]
		Range,

		[BinaryOperator(1, typeof(IncrementExpr))]
		[Symbol("++")]
		Increment,

		[BinaryOperator(1, typeof(DecrementExpr))]
		[Symbol("--")]
		Decrement,

		// Assignment Operators
		[BinaryOperator(15, typeof(AssignExpr))]
		[Symbol("=")]
		Assign,

		// Comparison Operators
		[BinaryOperator(6, typeof(GreaterExpr))]
		[Symbol(">")]
		Greater,

		[BinaryOperator(6, typeof(LesserExpr))]
		[Symbol("<")]
		Lesser,

		[BinaryOperator(6, typeof(GreaterOrEqualExpr))]
		[Symbol(">=")]
		GreaterOrEqual,

		[BinaryOperator(6, typeof(LesserOrEqualExpr))]
		[Symbol("<=")]
		LesserOrEqual,

		[BinaryOperator(6, typeof(EqualityExpr))]
		[Symbol("==")]
		Equals,

		[BinaryOperator(6, typeof(InequalityExpr))]
		[Symbol("!=")]
		Unequals,

		// Logical Operators
		[UnaryOperator(2, typeof(BNotExpr))]
		[Symbol("!")]
		[Symbol("not")]
		LogicalNot,

		[BinaryOperator(2, typeof(LOrExpr))]
		[Symbol("|")]
		[Symbol("or")]
		LogicalOr,

		[BinaryOperator(2, typeof(LAndExpr))]
		[Symbol("&")]
		[Symbol("and")]
		LogicalAnd,

		[BinaryOperator(2, typeof(LXorExpr))]
		[Symbol("^")]
		[Symbol("xor")]
		LogicalXor,

		// Bitwise Operators
		[UnaryOperator(8, typeof(BNotExpr))]
		[Symbol("!!")]
		BitwiseNot,

		[BinaryOperator(8, typeof(BAndExpr))]
		[Symbol("&&")]
		BitwiseAnd,

		[BinaryOperator(8, typeof(BOrExpr))]
		[Symbol("||")]
		BitwiseOr,

		[BinaryOperator(8, typeof(BXorExpr))]
		[Symbol("^^")]
		BitwiseXor,

		[BinaryOperator(5, typeof(BSLExpr))]
		[Symbol("<<")]
		BitShiftLeft,

		[BinaryOperator(5, typeof(BSRExpr))]
		[Symbol(">>")]
		BitShiftRight,

		// Signal Operators
		[Unimplemented]
		[BinaryOperator(15, typeof(SignalConnectExpr))]
		[Symbol("->")]
		ConnectSignal,

		[Unimplemented]
		[BinaryOperator(15, typeof(SignalEmitExpr))]
		[Symbol("<-")]
		EmitSignal,

		// Literals
		[Symbol("""(?:"(?:\\.|[^\\"])*"|'(?:\\.|[^\\'])*')""", true)]
		StringLiteral,

		[Symbol("""r"(?:\\.|[^\\"])*"|r'(?:\\.|[^\\'])*'""", true)]
		RawStringLiteral,

		[Unimplemented]
		[Symbol("""f"(?:\\.|[^\\"])*"|f'(?:\\.|[^\\'])*'""", true)]
		FormatStringLiteral,

		[Symbol("""(?:\d[\d_]*\.(?:\d[\d_]*)?|(?:\d[\d_]*)?\.\d[\d_]*)[bsilfd]?""", true)]
		DecimalNumericLiteral,

		[Symbol("""\d[\d_]*[bsilfd]?""", true)]
		WholeNumericLiteral,

		[Symbol("""0x[\da-fA-F]+[\da-fA-F_]*[bsilfd]?""", true)]
		HexNumericLiteral,

		[Symbol("""0o[0-8]+[0-8_]*[bsilfd]?""", true)]
		OctalNumericLiteral,

		[Symbol("""0b[01]+[01_]*[bsilfd]?""", true)]
		ByteNumericLiteral,

		[Symbol("""(?:\d[\d_]*\.(?:\d[\d_]*)?|(?:\d[\d_]*)?\.\d[\d_]*)[e|E][-|+]\d+[bsilfd]?""", true)]
		ScientificNumericLiteral,

		// Delimiters
		[Symbol("""[\r\n;]+""", true)]
		StmtSeparator,

		[BinaryOperator(14, typeof(CommaExpr))]
		[Symbol(",")]
		Comma,

		[Symbol("{")]
		OpenCurlyBracket,

		[Symbol("}")]
		CloseCurlyBracket,

		[BinaryOperator(1, typeof(IndexExpr))]
		[Symbol("[")]
		OpenSquareBracket,

		[Symbol("]")]
		CloseSquareBracket,

		[BinaryOperator(1, typeof(CallExpr))]
		[Symbol("(")]
		OpenBracket,

		[Symbol(")")]
		CloseBracket,

		// Special
		[UnaryOperator(3, typeof(NewExpr))]
		[Symbol("new")]
		New,

		[UnaryOperator(3, typeof(CloneExpr))]
		[Symbol("copy")]
		[Symbol("clone")]
		Clone,

		[Unimplemented]
		[Symbol("@")]
		CurrentExecutor,

		[Unimplemented]
		[Symbol("$")]
		Server,

		[BinaryOperator(1, typeof(AccessExpr))]
		[Symbol(".")]
		Access,

		[BinaryOperator(1, typeof(AccessExpr))]
		[Symbol("?.")]
		NullAccess,

		[Symbol(":")]
		Type,

		[Symbol("then")]
		Conditional,

		[Unimplemented]
		[BinaryOperator(15, typeof(UpdateExpr))]
		[Symbol("|>")]
		Update,

		[Unimplemented]
		[BinaryOperator(15, typeof(OverwriteExpr))]
		[Symbol("!>")]
		Overwrite,

		[Unimplemented]
		[BinaryOperator(14, typeof(CompositionExpr))]
		[Symbol(".>")]
		Composition,

		[Unimplemented]
		[BinaryOperator(15, typeof(SwapExpr))]
		[Symbol("><")]
		SwapValue,

		[Unimplemented]
		[BinaryOperator(15, typeof(CopyExpr))]
		[Symbol("=>")]
		CopyTo,

		[Symbol("""[a-zA-Z_]\w*""", true)]
		Identifier,

		[Unimplemented]
		[Symbol("""\[\[.*\]\]""", true)]
		Decorator,

		// Declarations
		[Symbol("func")]
		FuncDecl,

		[Symbol("macro")]
		MacroDecl,

		[Unimplemented]
		[Symbol("enum")]
		EnumDecl,

		[Symbol("var")]
		VarDecl,

		//TODO decide which one of these words i like the most
		[Symbol("prototype")]
		[Symbol("type")]
		[Symbol("class")]
		TypeDecl,

		[Unimplemented]
		[Symbol("trait")]
		TraitDecl,

		[Unimplemented]
		[Symbol("impl")]
		ImplDef,

		[Unimplemented]
		[Symbol("extend")]
		ExtensionDef,

		[Unimplemented]
		[Symbol("target")]
		TargetFuncDecl,

		[Unimplemented]
		[Symbol("signal")]
		SignalDecl,

		[Unimplemented]
		[Symbol("select")]
		SelectStmt,

		[Symbol("""execute *{(?:.*\s)*?}""", true)]
		[Symbol("""execute +as +.* *{(?:.*\s)*?}""", true)]
		ExecuteStmt,

		// Keywords
		[Specifier]
		[Symbol("internal")]
		InternalSpec,

		[Specifier]
		[Symbol("priv")]
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

		[Unimplemented]
		[UnaryOperator(13, typeof(AwaitExpr))]
		[Symbol("await")]
		Await,

		[TernaryOperator(12, typeof(ConditionalExpr))]
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

		[Unimplemented]
		[Symbol("where")]
		Where,

		[Unimplemented]
		[Symbol("with")]
		With,

		[Unimplemented]
		[Symbol("as")]
		As,

		[Unimplemented]
		[Symbol("use")]
		Use,

		[Symbol("while")]
		While,

		[Symbol("for")]
		For,

		[Unimplemented]
		[Symbol("in")]
		In,

		[Unimplemented]
		[Symbol("match")]
		Match,

		[Unimplemented]
		[Symbol("case")]
		Case,

		[Symbol("null")]
		Null,
	}
}
