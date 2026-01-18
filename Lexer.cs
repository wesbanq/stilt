using System.Reflection;
using System.Text.RegularExpressions;

namespace stilt
{
	public class Lexer
	{
		protected readonly List<Token> Tokens = new List<Token>();
		protected int CurrentPos = 0;
		public readonly string Filepath;
		public Token? CurrentToken => Tokens.Count > CurrentPos ? Tokens[CurrentPos] : null;

		public static void GetSymbolAttribute(out List<string[]> symbols, out List<string[]> regex)
		{
			symbols = new List<string[]>();
			regex = new List<string[]>();

			foreach (var name in typeof(TokenType).GetEnumNames())
			{
				symbols.Add(typeof(TokenType).GetField(name).GetCustomAttributes<SymbolAttribute>()?.Where(a => !a.IsRegex).Select(a => Regex.Escape(a.Symbol)).ToArray());
				regex.Add(typeof(TokenType).GetField(name).GetCustomAttributes<SymbolAttribute>()?.Where(a => a.IsRegex).Select(a => a.Symbol).ToArray());
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
			//changes to the preprocessor to keep the amount of lines
			const string commentRegex1 = """#.*""";
			const string commentRegex2 = """##(?:.*\s)*##""";
			//const string commentRegex3 = """\n{2,}""";
			const string commentRegex4 = """\r\n""";
			const string linebreakRegex = """ ?\\\s+""";
			return Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(//Regex.Replace(
				code + "\n", commentRegex4, "\n")
				, commentRegex2, m => { var lines = m.Value.Count(c => c == '\n'); return new String('\n', lines); })
				, commentRegex1, "")
				// will still ruin compilarion error reports, but its a crutch to deal with the lack of multiline expression support
				// so ill remove once thats added
				, linebreakRegex, " ");
				//, commentRegex3, "\n");
		}

		protected List<Token> GetTokenMatches(string code, List<string[]> rules)
		{
			List<Token> tokens = new();

			for (int i = 0; i < rules.Count; ++i)
			{
				if (rules[i] == null || rules[i].Length == 0) continue;
				foreach (var rule in rules[i])
				{
					var matchCollection = Regex.Matches(code, rule);
					foreach (Match match in matchCollection)
					{
						var newToken = new Token();

						newToken.Range = new FileRange(match.Index, match.Index + match.Length, Filepath);
						newToken.Which = (TokenType)i;
						newToken.Text = match.Value;

						//Program.Dump(newToken);
						tokens.Add(newToken);
					}
				}
			}

			return tokens;
		}

		public void SkipStmt()
		{
			while (Next()?.Which switch { null or TokenType.StmtSeparator or TokenType.CloseCurlyBracket => false, _ => true}) { }
		}

		public Token Prev()
		{
			CurrentPos--;
			return CurrentToken;
		}

		public Token? Next()
		{
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

		public Lexer(ProgramArgs args)
		{
			Filepath = args.MainCodeFilepath;
			var code = Preprocess(File.ReadAllText(args.MainCodeFilepath));

			GetSymbolAttribute(out var symbols, out var regex);
			Tokens = FileRange.RemoveOverlaps(
				GetTokenMatches(code, symbols),
				GetTokenMatches(code, regex)
			)
			.OrderBy(t => t.Range.Start)
			.ThenBy(t => t.Range.End)
			.ToList();
		}
	}
}
