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
		
		public SymbolAttribute(string symbol, bool regex) 
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

		public enum Tokens
		{
			None = 0,

			[Symbol("+", false)]
			Add,

			[Symbol("-", false)]
			Subtract,

			[Symbol("/", false)]
			Divide,

			[Symbol("*", false)]
			Multiply,

			[Symbol("%", false)]
			Modulo,

			[Symbol("**", false)]
			Exponent,

			[Symbol("..", false)]
			Range,

			[Symbol("++", false)]
			Increment,

			[Symbol("--", false)]
			Decrement,

			// Assignment Operators
			[Symbol("=", false)]
			Assign,

			//[Symbol(":=", false)]
			//TypedAssign,

			//[Symbol("+=", false)]
			//AddAssign,

			//[Symbol("-=", false)]
			//SubtractAssign,

			//[Symbol("*=", false)]
			//MultiplyAssign,

			//[Symbol("/=", false)]
			//DivideAssign,

			//[Symbol("%=", false)]
			//ModuloAssign,

			//[Symbol("**=", false)]
			//ExponentAssign,

			//[Symbol("&=", false)]
			//BitwiseAndAssign,

			//[Symbol("|=", false)]
			//BitwiseOrAssign,

			//[Symbol("^=", false)]
			//BitwiseXorAssign,

			// Comparison Operators
			[Symbol(">", false)]
			Greater,

			[Symbol("<", false)]
			Lesser,

			[Symbol(">=", false)]
			GreaterOrEqual,

			[Symbol("<=", false)]
			LesserOrEqual,

			[Symbol("==", false)]
			EqualTo,

			[Symbol("!!=", false)]
			NotEqualTo,

			// Logical Operators
			[Symbol("!!", false)]
			[Symbol("not", false)]
			LogicalNot,

			[Symbol("||", false)]
			[Symbol("or", false)]
			LogicalOr,

			[Symbol("&&", false)]
			[Symbol("and", false)]
			LogicalAnd,

			[Symbol("^^", false)]
			[Symbol("xor", false)]
			LogicalXor,

			// Bitwise Operators
			[Symbol("!", false)]
			BitwiseNot,

			[Symbol("&", false)]
			BitwiseAnd,

			[Symbol("|", false)]
			BitwiseOr,

			[Symbol("^", false)]
			BitwiseXor,

			// Signal Operators
			[Symbol("->", false)]
			ConnectSignal,

			[Symbol("<-", false)]
			EmitSignal,

			// Literals
			[Symbol("""[a-z0-9]*("(?:\\.|[^\\"])*"|'(?:\\.|[^\\'])*')""", true)]
			StringLiteral,

			// [Symbol(r"\d\d*(?:\.\d*|[bsilfd])?", false)]
			[Symbol("""\d\d*(?:\.\d*|[bsilfd])?""", true)]
			NumericLiteral,

			// [Symbol(r"\d+b", false)]
			// ByteLiteral,

			// [Symbol(r"\d+i", false)]
			// IntLiteral,

			// [Symbol(r"\d+l", false)]
			// LongLiteral,

			// [Symbol(r"\d+.\d+f", false)]
			// FloatLiteral,

			// [Symbol(r"\d+.\d+d", false)]
			// DoubleLiteral,

			// Delimiters
			[Symbol("""[\r\n;]+""", true)]
			StmtSeparator,

			[Symbol(",", false)]
			Comma,

			[Symbol("{", false)]
			OpenCurlyBracket,

			[Symbol("}", false)]
			CloseCurlyBracket,

			[Symbol("[", false)]
			OpenSquareBracket,

			[Symbol("]", false)]
			CloseSquareBracket,

			[Symbol("(", false)]
			OpenBracket,

			[Symbol(")", false)]
			CloseBracket,

			// Special
			[Symbol("@", false)]
			CurrentExecutor,

			[Symbol("$", false)]
			Server,

			[Symbol(".", false)]
			Access,

			[Symbol(":", false)]
			Type,

			[Symbol("|>", false)]
			Update,

			[Symbol("""(?:[a-zA-Z_]\w*)""", true)]
			Indentifier,

			[Symbol("""\[.*\]""", true)]
			Attribute,

			[Symbol("""#.*""", true)]
			[Symbol("""##(?:.*\s)*##""", true)]
			Comment,

			// Keywords
			[Symbol("if", false)]
			If,

			[Symbol("else", false)]
			Else,

			[Symbol("elif", false)]
			Elif,

			[Symbol("signal", false)]
			Signal,

			[Symbol("execute", false)]
			Execute,

			[Symbol("var", false)]
			Variable,

			[Symbol("const", false)]
			Constant,

			[Symbol("return", false)]
			Return,

			[Symbol("import", false)]
			Import,

			[Symbol("while", false)]
			While,

			[Symbol("for", false)]
			For,

			[Symbol("in", false)]
			In,

			[Symbol("match", false)]
			Match,

			[Symbol("case", false)]
			Case,

			[Symbol("null", false)]
			Null,

			[Symbol("target", false)]
			Target,
		}
	}
}
