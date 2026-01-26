using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace stilt
{
	public class Lexer
	{
		protected List<Token> Tokens;
		protected int CurrentPos = 0;
		public readonly string Filepath;
		public readonly ProgramArgs Args;
		public FileText Text;

		public Token CurrentToken => Tokens.Count > CurrentPos ? Tokens[CurrentPos] : throw new Exception();
		
		public static void GetSymbolAttribute(out List<string[]> symbols, out List<string[]> regex)
		{
			symbols = new List<string[]>();
			regex = new List<string[]>();

			foreach (var name in typeof(TokenType).GetEnumNames())
			{
				var field = typeof(TokenType).GetField(name);
				if (field != null)
				{
					var symbolArray = field.GetCustomAttributes<SymbolAttribute>()?.Where(a => !a.IsRegex).Select(a => Regex.Escape(a.Symbol)).ToArray();
					var regexArray = field.GetCustomAttributes<SymbolAttribute>()?.Where(a => a.IsRegex).Select(a => a.Symbol).ToArray();
					if (symbolArray != null)
						symbols.Add(symbolArray);
					if (regexArray != null)
						regex.Add(regexArray);
				}
			}
		}

		public class OverlappingTokensException : Exception
		{
			Token Token1;
			Token Token2;

			public OverlappingTokensException(string message, Token token1, Token token2) : base(message)
			{
				Token1 = token1;
				Token2 = token2;
			}
		}

		public static string Preprocess(string code)
		{
			//preprocessor needs to keep the same amount of lines to make sure the error reports are at the correct positions
			const string commentRegex1 = """#.*""";
			const string commentRegex2 = """##(?s:.*?)##""";
			const string commentRegex3 = """\r\n""";
			const string removeTabs = """\t""";
			const string newTab = "    ";
			const string linebreakRegex = """ ?\\\s+""";
			return Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(
				code + "\n", commentRegex3, "\n")
				, removeTabs, newTab)
				, commentRegex2, m => { var lines = m.Value.Count(c => c == '\n'); return new String('\n', lines); })
				, commentRegex1, "")
				// will still ruin compilation error reports, temporary fix to deal with the lack of multiline expression support
				// ill remove once this thats added
				, linebreakRegex, " ");
		}

		protected List<Token> GetTokenMatches(string code, List<string[]> rules)
		{
			List<Token> tokens = new();

			for (int i = 0; i < rules.Count; ++i)
			{
				if (rules[i] == null || rules[i].Length == 0) continue;
				foreach (var rule in rules[i])
				{
					var matchCollection = Regex.Matches(code, rule, RegexOptions.NonBacktracking);
					try
					{
						foreach (Match match in matchCollection)
						{
							var newToken = new Token();

							newToken.Range = new FileRange(match.Index, match.Index + match.Length, Filepath, Text);
							newToken.Which = (TokenType)i;

							tokens.Add(newToken);
						}
					}
					catch (RegexMatchTimeoutException e)
					{
						Console.WriteLine(e);
						throw;
					}
				}
			}

			return tokens;
		}

		public void SkipStmt()
		{
			do { Next(); } while (CurrentToken.Which is not (TokenType.EOF or TokenType.StmtSeparator or TokenType.CloseCurlyBracket));
		}

		public Token Prev()
		{
			if (CurrentPos > 0)
				CurrentPos--;
			return CurrentToken;
		}

		public Token Next()
		{
			if (CurrentPos < Tokens.Count-1)
				CurrentPos++;
			return CurrentToken;
		}

		public Token PeekNext(int n = 1)
		{
			CurrentPos += n;
			var nextToken = CurrentToken;
			CurrentPos -= n;
			return nextToken;
		}

		public void Goto(Token to)
		{
			var t = Tokens.FindIndex(t => ReferenceEquals(t, to));
			if (t == -1)
				throw new ArgumentException($"Couldn't find token {to}");
			CurrentPos = t;
		}

		public void Lex()
		{
			GetSymbolAttribute(out var symbols, out var regex);
			Tokens = [.. FileRange.RemoveOverlaps(
				GetTokenMatches(Text.ToString(), symbols),
				GetTokenMatches(Text.ToString(), regex)
			)
			.OrderBy(t => t.Range.Start)
			.ThenBy(t => t.Range.End)];

			Tokens.Add(new Token()
			{
				Which = TokenType.EOF,
				Range = Text.EOF,
			});
		}

		public Lexer(ProgramArgs args)
		{
			Args = args;
			Filepath = args.MainCodeFilepath!;
			Text = new(Filepath);
		}
	}
}
