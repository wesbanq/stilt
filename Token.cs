using System;
using System.Collections.Generic;
using System.Text;

namespace stilt
{
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
	public class SymbolAttribute : Attribute
	{
		public string Symbol { get; set; }
		public bool IsRegex { get; set; }
		
		public SymbolAttribute(string symbol, bool regex = false) 
		{
			Symbol = symbol;
			IsRegex = regex;
		}
	}

	//[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
	//public class RegexAttribute : Attribute
	//{
	//	public string Symbol { get; set; }

	//	public RegexAttribute(string regex)
	//	{
	//		Symbol = regex;
	//	}
	//}

	public class Token
	{
		public Tokens Which { get; set; }
		public FileRange Range { get; set; }
		public string Text { get; set; }

		public enum Tokens
		{
			None = 0,

			[Symbol("+")]
			Plus,

			[Symbol("-")]
			Minus,

			[Symbol("/")]
			Divide,

			[Symbol("*")]
			Star,

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

			//[Symbol(":=")]
			//TypedAssign,

			//[Symbol("+=")]
			//AddAssign,

			//[Symbol("-=")]
			//SubtractAssign,

			//[Symbol("*=")]
			//MultiplyAssign,

			//[Symbol("/=")]
			//DivideAssign,

			//[Symbol("%=")]
			//ModuloAssign,

			//[Symbol("**=")]
			//ExponentAssign,

			//[Symbol("&=")]
			//BitwiseAndAssign,

			//[Symbol("|=")]
			//BitwiseOrAssign,

			//[Symbol("^=")]
			//BitwiseXorAssign,

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

			[Symbol("|")]
			[Symbol("or")]
			LogicalOr,

			[Symbol("&")]
			[Symbol("and")]
			LogicalAnd,

			[Symbol("^")]
			[Symbol("xor")]
			LogicalXor,

			// Bitwise Operators
			[Symbol("!!")]
			BitwiseNot,

			[Symbol("&&")]
			BitwiseAnd,

			[Symbol("||")]
			BitwiseOr,

			[Symbol("^^")]
			BitwiseXor,

			[Symbol("<<")]
			BitShiftLeft,

			[Symbol(">>")]
			BitShiftRight,

			// Signal Operators
			[Symbol("->")]
			ConnectSignal,

			[Symbol("<-")]
			EmitSignal,

			// Literals
			[Symbol("""[a-z0-9]*("(?:\\.|[^\\"])*"|'(?:\\.|[^\\'])*')""", true)]
			StringLiteral,

			// [Symbol(r"\d\d*(?:\.\d*|[bsilfd])?")]
			[Symbol("""\d\d*(?:\.\d*|[bsilfd])?""", true)]
			NumericLiteral,

			// [Symbol(r"\d+b")]
			// ByteLiteral,

			// [Symbol(r"\d+i")]
			// IntLiteral,

			// [Symbol(r"\d+l")]
			// LongLiteral,

			// [Symbol(r"\d+.\d+f")]
			// FloatLiteral,

			// [Symbol(r"\d+.\d+d")]
			// DoubleLiteral,

			// Delimiters
			[Symbol("""[\r\n;]+""", true)]
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

			[Symbol("><")]
			SwapMemory,

			[Symbol("=>")]
			CopyTo,

			[Symbol("""(?:[a-zA-Z_]\w*)""", true)]
			Indentifier,

			[Symbol("""\[.*\]""", true)]
			Attribute,

			//[Symbol("""#.*""", true)]
			//[Symbol("""##(?:.*\s)*##""", true)]
			//Comment,

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

			//[Symbol("""execute *{(?:.*\s*)*}""", true)]
			//[Symbol("""execute *{[^}]*}""", true)]
			//[Symbol("""\|\|(?:[^\|]*)\|\|""", true)]
			//ExecuteStmt,

			[Symbol("func")]
			FuncDecl,

			[Symbol("var")]
			VarDecl,

			[Symbol("const")]
			ConstDecl,

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
