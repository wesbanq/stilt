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

		public OperatorAttribute(int precedence)
		{
			Precedence = precedence;
		}
	}
	public class UnaryOperatorAttribute(int p) : OperatorAttribute(p) { }
	public class BinaryOperatorAttribute(int p) : OperatorAttribute(p) { }
	public class TernaryOperatorAttribute(int p) : OperatorAttribute(p) { }

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

		public static string[] GetRulesFromType(TokenType t)
		{
			var types = typeof(TokenType).GetFields();
			return types[(int)t + 1].GetCustomAttributes<SymbolAttribute>().Select(o => o.Symbol).ToArray();
		}

	}

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

		[BinaryOperator(1)]
		[Symbol("**")]
		Exponent,

		[Unimplemented]
		[BinaryOperator(5)]
		[Symbol("..")]
		Range,

		[UnaryOperator(1)]
		[BinaryOperator(1)]
		[Symbol("++")]
		Increment,

		[UnaryOperator(1)]
		[BinaryOperator(1)]
		[Symbol("--")]
		Decrement,

		// Assignment Operators
		[BinaryOperator(15)]
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

		// Bitwise Operators
		[UnaryOperator(8)]
		[Symbol("!!")]
		BitwiseNot,

		[BinaryOperator(8)]
		[Symbol("&&")]
		BitwiseAnd,

		[BinaryOperator(8)]
		[Symbol("||")]
		BitwiseOr,

		[BinaryOperator(8)]
		[Symbol("^^")]
		BitwiseXor,

		[BinaryOperator(5)]
		[Symbol("<<")]
		BitShiftLeft,

		[BinaryOperator(5)]
		[Symbol(">>")]
		BitShiftRight,

		// Signal Operators
		[Unimplemented]
		[BinaryOperator(15)]
		[Symbol("->")]
		ConnectSignal,

		[Unimplemented]
		[BinaryOperator(15)]
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

		[BinaryOperator(14)]
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
		[Symbol("copy")]
		[Symbol("clone")]
		Clone,

		[Unimplemented]
		[Symbol("@")]
		CurrentExecutor,

		[Unimplemented]
		[Symbol("$")]
		Server,

		[BinaryOperator(1)]
		[Symbol(".")]
		Access,

		[BinaryOperator(1)]
		[Symbol("?.")]
		NullAccess,

		[Symbol(":")]
		Type,

		[Symbol("then")]
		Conditional,

		[Unimplemented]
		[BinaryOperator(15)]
		[Symbol("|>")]
		Update,

		[Unimplemented]
		[BinaryOperator(15)]
		[Symbol("!>")]
		Overwrite,

		[Unimplemented]
		[BinaryOperator(14)]
		[Symbol(".>")]
		Composition,

		[Unimplemented]
		[BinaryOperator(15)]
		[Symbol("><")]
		SwapValue,

		[Unimplemented]
		[BinaryOperator(15)]
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
