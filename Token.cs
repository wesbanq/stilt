using System;
using System.Collections.Generic;
using System.Text;

namespace stilt
{
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
	public class SymbolAttribute : Attribute
	{
		public string Symbol { get; set; }
		
		public SymbolAttribute(string symbol) 
		{
			Symbol = symbol;
		}
	}

	[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
	public class RegexAttribute : Attribute
	{
		public string Regex { get; set; }

		public RegexAttribute(string regex)
		{
			Regex = regex;
		}
	}

	public class Token
	{
		public enum Tokens
		{
			[Symbol("+")]
			Add,

			[Symbol("-")]
			Subtract,

			[Symbol("/")]
			Divide,

			[Symbol("*")]
			Multiply,

			[Symbol("%")]
			Modulo,

			[Symbol("**")]
			Exponent,

			[Symbol("..")]
			Range,

			[Symbol("++")]
			Increment,

			[Symbol("--")]
			Decrement,

			// Assignment Operators
			[Symbol("=")]
			Assign,

			[Symbol(":=")]
			TypedAssignment,

			[Symbol("+=")]
			AddAssign,

			[Symbol("-=")]
			SubtractAssign,

			[Symbol("*=")]
			MultiplyAssign,

			[Symbol("/=")]
			DivideAssign,

			[Symbol("%=")]
			ModuloAssign,

			[Symbol("**=")]
			ExponentAssign,

			[Symbol("&=")]
			BitwiseAndAssign,

			[Symbol("|=")]
			BitwiseOrAssign,

			[Symbol("^=")]
			BitwiseXorAssign,

			// Comparison Operators
			[Symbol(">")]
			Greater,

			[Symbol("<")]
			Lesser,

			[Symbol(">=")]
			GreaterOrEqual,

			[Symbol("<=")]
			LesserOrEqual,

			[Symbol("==")]
			EqualTo,

			[Symbol("!=")]
			NotEqualTo,

			// Logical Operators
			[Symbol("!")]
			[Symbol("not")]
			LogicalNot,

			[Symbol("||")]
			[Symbol("or")]
			LogicalOr,

			[Symbol("&&")]
			[Symbol("and")]
			LogicalAnd,

			[Symbol("^^")]
			[Symbol("xor")]
			LogicalXor,

			// Bitwise Operators
			[Symbol("!!")]
			BitwiseNot,

			[Symbol("&")]
			BitwiseAnd,

			[Symbol("|")]
			BitwiseOr,

			[Symbol("^")]
			BitwiseXor,

			// Signal Operators
			[Symbol("->")]
			ConnectSignal,

			[Symbol("<-")]
			EmitSignal,

			// Literals
			[Regex("""[a-z0-9]*("(?:\\.|[^\\"])*"|'(?:\\.|[^\\'])*')""")]
			StringLiteral,

			// [Regex(r"\d\d*(?:\.\d*|[bsilfd])?")]
			[Regex("""\d\d*(?:\.\d*|[bsilfd])?""")]
			NumericLiteral,

			// [Regex(r"\d+b")]
			// ByteLiteral,

			// [Regex(r"\d+i")]
			// IntLiteral,

			// [Regex(r"\d+l")]
			// LongLiteral,

			// [Regex(r"\d+.\d+f")]
			// FloatLiteral,

			// [Regex(r"\d+.\d+d")]
			// DoubleLiteral,

			// Delimiters
			[Regex("""[\r\n;]+""")]
			StmtSeparator,

			[Symbol(",")]
			Comma,

			[Symbol("{")]
			OpenCurlyBracket,

			[Symbol("}")]
			CloseCurlyBracket,

			[Symbol("[")]
			OpenSquareBracket,

			[Symbol("]")]
			CloseSquareBracket,

			[Symbol("(")]
			OpenBracket,

			[Symbol(")")]
			CloseBracket,

			// Special
			[Symbol("@")]
			CurrentExecutor,

			[Symbol("$")]
			Server,

			[Symbol(".")]
			Access,

			[Symbol(":")]
			Type,

			[Symbol("|>")]
			Update,

			[Regex("""(?:[a-zA-Z_]\w*)""")]
			Indentifier,

			[Regex("""\[.*\]""")]
			Attribute,

			// Keywords
			[Symbol("if")]
			If,

			[Symbol("else")]
			Else,

			[Symbol("elif")]
			Elif,

			[Symbol("signal")]
			Signal,

			[Symbol("execute")]
			Execute,

			[Symbol("var")]
			Variable,

			[Symbol("const")]
			Constant,

			[Symbol("return")]
			Return,

			[Symbol("import")]
			Import,

			[Symbol("while")]
			While,

			[Symbol("for")]
			For,

			[Symbol("in")]
			In,

			[Symbol("match")]
			Match,

			[Symbol("case")]
			Case,

			[Symbol("null")]
			Null,

			[Symbol("target")]
			Target,
		}
	}
}
