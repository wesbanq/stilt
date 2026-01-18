using System.Reflection;
using System.Text.RegularExpressions;

namespace stilt
{
	public class Lexer
	{
		public readonly List<Token> Tokens = new List<Token>();
		protected int CurrentPos = 0;
		public readonly string Filepath = "No file";
		public Token CurrentToken => Tokens[CurrentPos];

		public static void GetSymbolAttribute(out List<string[]> symbols, out List<string[]> regex)
		{
			symbols = new List<string[]>();
			regex = new List<string[]>();

			foreach (var name in typeof(Token.Tokens).GetEnumNames())
			{
				symbols.Add(typeof(Token.Tokens).GetField(name).GetCustomAttributes<SymbolAttribute>()?.Where(a => !a.IsRegex).Select(a => Regex.Escape(a.Symbol)).ToArray());
				regex.Add(typeof(Token.Tokens).GetField(name).GetCustomAttributes<SymbolAttribute>()?.Where(a => a.IsRegex).Select(a => a.Symbol).ToArray());
			}
		}

		public class OverlappingTokensException : Exception
		{
			Token Token1 { get; set; }
			Token Token2 { get; set; }


			public OverlappingTokensException(string message, Token token1, Token token2) : base(message)
			{
				Token1 = token1;
				Token2 = token2;
			}
		}

		static string Preprocess(string code)
		{
			const string commentRegex1 = """#.*""";
			const string commentRegex2 = """##(?:.*\s)*##""";
			const string commentRegex3 = """\n\n+""";
			const string linebreakRegex = """ ?\\\s+""";
			return Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(
				code, commentRegex2, "")
				, commentRegex1, "")
				, linebreakRegex, "")
				, commentRegex3, "\n");
		}

		protected List<Token> GetTokenMatches(string code, List<string[]> rules)
		{
			var tokens = new List<Token>();

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
						newToken.Which = (Token.Tokens)i;
						newToken.Text = match.Value;

						//Program.Dump(newToken);
						tokens.Add(newToken);
					}
				}
			}

			return tokens;
		}

		public Token Next()
		{
			CurrentPos++;
			return CurrentToken;
		}

		public Token PeekNext()
		{
			CurrentPos++;
			var nextToken = CurrentToken;
			CurrentPos--;
			return nextToken;
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
