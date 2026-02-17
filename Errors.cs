namespace stilt.Errors
{
	public abstract class CompilationMessage : Exception
	{
		public FileRange? Range;
		public ErrorSeverity Severity = ErrorSeverity.Info;

		public override string ToString()
		{
			if (Range is not null)
			{
				var (lineS, columnS) = Range.StartLineAndColumn;
				var (lineE, columnE) = Range.EndLineAndColumn;
				var text = Range.TextLines;

				var res = "";
				for (int line = lineS; line <= lineE; ++line)
				{
					//TODO rewrite with StringBuilder
					//magic numbers found via trial and error
					var part1 = $"\n\t{line}| ";
					var part2 = text[line-lineS];
					var part3 = "\n\t" + new String(' ', part1.Length-2);
					var part4 = new String(' ', line == lineS ? columnS-1 : 0)
								+ new String('^', 
								Math.Max(0, line == lineS 
									? (line == lineE ? Range.Length : part2.Length-(columnS-1)) 
									: (line == lineE ? columnE-1 : part2.Length)));
					res += part1+part2+part3+part4;
				}

				return $"{Severity}: " + Message + $"\n  @ {Range.FormatLineAndColumn()}, in file: {Range.Filename}\n" + res;
			}
			else
				return $"{Severity}: " + Message;
		}
			public void Print()
		{
			Console.WriteLine(ToString());
		}

		public CompilationMessage(string message, FileRange? range = null, ErrorSeverity severity = ErrorSeverity.Info)
			: base(message)
		{
			Range = range;
			Severity = severity;
		}
	}

	public enum ErrorSeverity
	{ Info, Warning, Error, Critical }

	public class SyntaxError : CompilationMessage
	{
		public SyntaxError(FileRange? range)
			: base("Syntax error", range, ErrorSeverity.Error)
		{ }
		public SyntaxError(FileRange? range, string msg)
			: base(msg, range, ErrorSeverity.Error)
		{ }
		public SyntaxError(FileRange? range, string msg, ErrorSeverity severity)
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
			: base(pos, $"Unexpected token: '{got?.Which}'.\nExpected: '{expected}'.")
		{
			Expected = expected;
			Got = got ?? throw new ArgumentNullException(nameof(got));
		}

		public UnexpectedToken(FileRange? pos, Token got)
			: base(pos ?? got.Range, $"Unexpected token: '{Program.Escape(got.Which.ToString())}'.")
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

	public class UnimplementedTraitMethod : SyntaxError
	{
		public UnimplementedTraitMethod(FileRange? pos, TypeSymbol type, TraitSymbol trait, string methodName)
			: base(pos, $"Type '{type.Name}' implements trait '{trait.Name}', but does not implement method '{methodName}'.")
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

	public class GenerationError : CompilationMessage
	{
		public GenerationError(string msg)
			: base(msg)
		{ }
	}

	public class IRGenerationError : GenerationError
	{
		public IRGenerationError(string msg)
			: base(msg)
		{ }
	}
}
