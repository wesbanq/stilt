using stilt.AST;
using System;
using System.Collections.Generic;
using System.Text;

namespace stilt.Errors
{
	public class SyntaxError : CompilationMessage
	{
		public SyntaxError(FileRange range)
			: base("Syntax error", range, ErrorSeverity.Error)
		{ }
		public SyntaxError(FileRange range, string msg)
			: base(msg, range, ErrorSeverity.Error)
		{ }
		public SyntaxError(FileRange range, string msg, ErrorSeverity severity)
			: base(msg, range, severity)
		{ }
	}

	public class RedeclaredSymbol : SyntaxError
	{
		public RedeclaredSymbol(FileRange range, Symbol symbol)
			: base(range, $"Multiple definitions for symbol: '{symbol.Name}'")
		{ }
	}

	public class UnimplementedError : SyntaxError
	{
		public UnimplementedError(Token token)
			: base(token.Range, $"Use of unimlemented fearure: '{token.Which}'.")
		{ }
	}

	public class UndefinedSymbolError : SyntaxError
	{
		public string Got;

		public UndefinedSymbolError(FileRange pos, Symbol symbol)
			: base(pos, $"Use of undefined symbol: '{symbol.Name}'")
		{
			Got = symbol.Name;
		}
		public UndefinedSymbolError(FileRange pos, string symbolName)
			: base(pos, $"Use of undefined symbol: '{symbolName}'")
		{
			Got = symbolName;
		}
	}

	public class MalformedExpr : SyntaxError
	{
		public MalformedExpr(FileRange start)
			: base(start, "Malformed expression.")
		{ }
	}

	public class MalformedDecl : SyntaxError
	{
		public MalformedDecl(FileRange start)
			: base(start, "Malformed declaration.")
		{ }
	}

	public class RunonStatement : SyntaxError
	{
		public RunonStatement(FileRange range)
			: base(range, "Statement is expected to end here, but it keeps going.")
		{ }
	}

	public class UnexpectedToken : SyntaxError
	{
		public TokenType Expected;
		public Token Got;

		public UnexpectedToken(FileRange pos, TokenType expected, Token? got)
			: base(pos, $"Unexpected token: '{got.Which}'.\nExpected: '{expected}'.")
		{
			Expected = expected;
			Got = got;
		}

		public UnexpectedToken(FileRange? pos, Token got)
			: base(pos, $"Unexpected token: '{Program.Escape(got.Which.ToString())}'.")
		{ }
	}

	public class UnexpectedEOF : SyntaxError
	{
		public UnexpectedEOF(FileRange range)
			: base(range, "File unexpectedly ended.")
		{ }
	}

	public class UnexpectedSpecifier : SyntaxError
	{
		public UnexpectedSpecifier(FileRange pos)
			: base(pos, $"Unexpected specifier '{pos.Text}'.")
		{ }
	}

	public class SyntaxWarning : SyntaxError
	{
		public SyntaxWarning(FileRange pos, string msg)
			: base(pos, msg)
		{
			Severity = ErrorSeverity.Warning;
		}
	}

	public class ShadowedSymbol : SyntaxWarning
	{
		public ShadowedSymbol(FileRange pos, Symbol symbol)
			: base(pos, $"Shadowed symbol: '{symbol.Name}'.")
		{ }
	}

	public class ShadowedBuiltinSymbol : SyntaxWarning
	{
		public ShadowedBuiltinSymbol(FileRange pos, Symbol symbol)
			: base(pos, $"Shadowed builtin symbol: '{symbol.Name}'.\nWarning treated as error. Suppress it if you know what you're doing.")
		//you cant suppress warnings yet
		{
			Severity = ErrorSeverity.Error;
		}
	}
}
