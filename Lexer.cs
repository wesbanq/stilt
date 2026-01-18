using System.Reflection;
using System.Text.RegularExpressions;

namespace stilt
{
	public abstract class Lexer
	{
		static void GetStringAttribute(out List<string[]> symbols, out List<string[]> regex)
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

		static string StripComments(string code)
		{
			const string commentRegex1 = """#.*""";
			const string commentRegex2 = """##(?:.*\s)*##""";
			const string commentRegex3 = """\n\n+""";
			return Regex.Replace(Regex.Replace(Regex.Replace(code, commentRegex2, ""), commentRegex1, ""), commentRegex3, "\n");
		}

		static List<Token> GetTokenMatches(string code, List<string[]> rules, string filepath)
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

						newToken.Range = new FileRange(match.Index, match.Index + match.Length, filepath);
						newToken.Which = (Token.Tokens)i;
						newToken.Text = match.Value;

						//Program.Dump(newToken);
						tokens.Add(newToken);
					}
				}
			}

			return tokens;
		}

		public static List<Token> Tokenize(string filepath)
		{
			var code = StripComments(File.ReadAllText(filepath));
			var tokens = new List<Token>();

			GetStringAttribute(out var symbols, out var regex);
			tokens = FileRange.RemoveOverlaps(
				GetTokenMatches(code, symbols, filepath).OrderBy(t => t.Range.Start).ThenBy(t => t.Range.End).ToList(),
				GetTokenMatches(code, regex, filepath).OrderBy(t => t.Range.Start).ThenBy(t => t.Range.End).ToList()
			);
			//tokens = GetTokenMatches(code, regex, filepath).OrderBy(t => t.Range.Start).ThenBy(t => t.Range.End).ToList();

			tokens.ForEach(t => Program.Dump(t));

			return tokens;
		}
	}
}
