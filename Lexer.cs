using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using stilt.Errors;

namespace stilt
{
	public class Lexer
	{
		public List<Token> Tokens;
		public int CurrentPos = 0;
		public readonly string Filepath;
		public readonly ProgramArgs Args;
		public FileText Text;

		public Token CurrentToken => Tokens.Count > CurrentPos 
			? Tokens[CurrentPos] 
			: throw new UnexpectedEOF(Text.EOF);
		
		public static void GetSymbolAttribute(out List<string[]> symbols, out List<string[]> regex)
		{
			symbols = new List<string[]>();
			regex = new List<string[]>();

			foreach (var name in typeof(TokenType).GetEnumNames())
			{
				var field = typeof(TokenType).GetField(name);
				if (field is not null)
				{
					var symbolArray = field.GetCustomAttributes<SymbolAttribute>()?.Where(a => !a.IsRegex).Select(a => Regex.Escape(a.Symbol)).ToArray();
					var regexArray = field.GetCustomAttributes<SymbolAttribute>()?.Where(a => a.IsRegex).Select(a => a.Symbol).ToArray();
					if (symbolArray is not null)
						symbols.Add(symbolArray);
					if (regexArray is not null)
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

		/// <summary>Compiled regex + metadata for O(n) single-pass lexing. isSymbol: true = literal/symbol (wins ties over regex).</summary>
		private sealed record LexRule(Regex Regex, TokenType Type, bool IsSymbol);

		private static List<LexRule> BuildLexRules()
		{
			GetSymbolAttribute(out var symbols, out var regex);
			var options = RegexOptions.NonBacktracking;
			var rules = new List<LexRule>();
			for (int i = 0; i < symbols.Count; i++)
			{
				if (symbols[i] is null || symbols[i].Length == 0) continue;
				foreach (var pat in symbols[i])
					rules.Add(new LexRule(new Regex(pat, options), (TokenType)i, true));
			}
			for (int i = 0; i < regex.Count; i++)
			{
				if (regex[i] is null || regex[i].Length == 0) continue;
				foreach (var pat in regex[i])
					rules.Add(new LexRule(new Regex(pat, options), (TokenType)i, false));
			}
			return rules;
		}

		/// <summary>Legacy: get all regex matches over full code (used only for comparison / fallback).</summary>
		protected List<Token> GetTokenMatches(string code, List<string[]> rules)
		{
			List<Token> tokens = [];

			for (int i = 0; i < rules.Count; ++i)
			{
				if (rules[i] is null || rules[i].Length == 0) continue;
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
			do { Next(); } while (CurrentToken.Which is not 
				(TokenType.EOF or TokenType.StmtSeparator or TokenType.CloseCurlyBracket or TokenType.OpenCurlyBracket)
			);
		}

		public bool CurrentIs(TokenType type) => CurrentToken.Which == type;

		public bool NextIs(TokenType type) => 
			PeekNext(1).Which == type || 
			(PeekNext(1).Which == TokenType.StmtSeparator && PeekNext(2).Which == type);

		public void SkipStmtSeparator()
		{
			if (CurrentIs(TokenType.StmtSeparator))
				Next();
		}

		public Token Prev()
		{
			if (CurrentPos > 0)
				CurrentPos--;
			return CurrentToken;
		}

		public Token Next()
		{
			if (CurrentPos < Tokens.Count - 1)
				CurrentPos++;
			return CurrentToken;
		}

		public Token GoPast(TokenType type)
		{
			while (!CurrentIs(type) && !CurrentIs(TokenType.EOF))
				Next();
			return Next();
		}

		public Token ExpectNext(TokenType expected)
		{
			var next = Next();
			if (!CurrentIs(expected))
				throw new UnexpectedToken(next.Range, expected, next);
			return next;
		}

		public Token Expect(TokenType expected)
		{
			if (!CurrentIs(expected))
				throw new UnexpectedToken(CurrentToken.Range, expected, CurrentToken);
			return Next();
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

		private static List<LexRule>? _lexRules;
		private static List<LexRule> LexRules => _lexRules ??= BuildLexRules();

		public void Lex()
		{
			var code = Text.ToString();
			var tokens = new List<Token>();
			var pos = 0;

			while (pos < code.Length)
			{
				int bestLen = 0;
				TokenType bestType = 0;
				bool bestIsSymbol = false;

				foreach (var rule in LexRules)
				{
					var m = rule.Regex.Match(code, pos);
					if (!m.Success || m.Index != pos) continue;
					var len = m.Length;
					var better = len > bestLen
						|| (len == bestLen && rule.IsSymbol && !bestIsSymbol);
					if (better)
					{
						bestLen = len;
						bestType = rule.Type;
						bestIsSymbol = rule.IsSymbol;
					}
				}

				if (bestLen > 0)
				{
					tokens.Add(new Token
					{
						Which = bestType,
						Range = new FileRange(pos, pos + bestLen, Filepath, Text),
					});
					pos += bestLen;
				}
				else
					pos += 1;
			}

			Tokens = tokens;
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
