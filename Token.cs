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
		public string Symbol { get; set; }
		public bool IsRegex { get; set; }

		public string GetDescription()
		{
			return Symbol;
		}

		public SymbolAttribute(string symbol, bool regex = false) 
		{
			Symbol = symbol;
			IsRegex = regex;
		}
	}

	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class OperatorAttribute : Attribute
	{
		public int Precedence { get; set; }
		public Type AssociatedExpr;

		public OperatorAttribute(int precedence, Type associatedExpr)
		{
			Precedence = precedence;
			AssociatedExpr = associatedExpr;
		}
	}

	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class UnimplementedAttribute : Attribute
	{ }

	public class TernaryOperatorAttribute(int p, Type e) : OperatorAttribute(p, e) { }
	public class BinaryOperatorAttribute(int p, Type e) : OperatorAttribute(p, e) { }
	public class UnaryOperatorAttribute(int p, Type e) : OperatorAttribute(p, e) { }

	public class Token
	{
		public TokenType Which { get; set; }
		public FileRange Range { get; set; }
		public string Text { get; set; }

		public static string[] GetRulesFromType(TokenType t)
		{
			var types = typeof(TokenType).GetFields();

			Activator.CreateInstance(typeof(AdditionExpr));
			return types[(int)t + 1].GetCustomAttributes<SymbolAttribute>().Select(o => o.Symbol).ToArray();
		}
	}

	public enum TokenType
	{
		//None = 0,

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
		[BinaryOperator(14, typeof(AssignExpr))]
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

		[BinaryOperator(6, typeof(EqualExpr))]
		[Symbol("==")]
		EqualTo,

		[BinaryOperator(6, typeof(UnequalExpr))]
		[Symbol("!=")]
		NotEqualTo,

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
		//[Symbol("""[a-z0-9]*("(?:\\.|[^\\"])*"|'(?:\\.|[^\\'])*')""", true)]
		[Symbol("""(?:"(?:\\.|[^\\"])*"|'(?:\\.|[^\\'])*')""", true)]
		StringLiteral,

		[Unimplemented]
		[Symbol("""(?:\$"(?:\\.|[^\\"])*"|\$'(?:\\.|[^\\'])*')""", true)]
		FormatStringLiteral,

		// [Symbol(r"\d\d*(?:\.\d*|[bsilfd])?")]
		[Symbol("""\d\d*(?:\.\d*|[bsilfd])?""", true)]
		NumericLiteral,

		// Delimiters
		[Symbol("""[\r\n;]+""", true)]
		StmtSeparator,

		[BinaryOperator(15, typeof(CommaExpr))]
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
		[Symbol(":")]
		SelfAccess,

		[Symbol("::")]
		Type,

		[Unimplemented]
		[TernaryOperator(13, typeof(ConditionalExpr))]
		[Symbol("?")]
		Conditional,

		[Unimplemented]
		[BinaryOperator(14, typeof(UpdateExpr))]
		[Symbol("|>")]
		Update,

		[Unimplemented]
		[BinaryOperator(14, typeof(SwapExpr))]
		[Symbol("><")]
		SwapValue,

		[Unimplemented]
		[BinaryOperator(14, typeof(CopyExpr))]
		[Symbol("=>")]
		CopyTo,

		[Symbol("""[a-zA-Z_]\w*""", true)]
		Identifier,

		[Unimplemented]
		[Symbol("""\[.*\]""", true)]
		Decorator,

		// Declarations
		[Symbol("func")]
		FuncDecl,

		[Symbol("var")]
		VarDecl,

		[Symbol("prototype")]
		[Symbol("type")]
		[Symbol("class")]
		TokenDecl,

		[Symbol("const")]
		ConstDecl,

		[Unimplemented]
		[Symbol("trait")]
		TraitDecl,

		[Unimplemented]
		[Symbol("target")]
		Target,

		[Unimplemented]
		[Symbol("signal")]
		Signal,

		// Keywords
		[Symbol("internal")]
		DudeItTotallyExistsTrustMe,

		[Symbol("if")]
		If,

		[Symbol("else")]
		Else,

		[Symbol("elif")]
		Elif,

		[Symbol("""execute *{(?:.*\s*)*}""", true)]
		ExecuteStmt,

		[Symbol("return")]
		Return,

		[Symbol("import")]
		Import,

		[Unimplemented]
		[Symbol("use")]
		Use,

		[Symbol("while")]
		While,

		[Symbol("for")]
		For,

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
