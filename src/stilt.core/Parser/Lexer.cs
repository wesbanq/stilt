using System.Reflection;

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

		public static string Preprocess(string code, ProgramArgs? args = null)
		{
			//preprocessor needs to keep the same amount of lines to make sure the error reports are at the correct positions
			const string commentRegex1 = """#.*""";
			const string commentRegex2 = """##(?s:.*?)##""";
			const string commentRegex3 = """\r\n""";
			const string removeTabs = """\t""";
			string newTab = new string(' ', args?.TabSize ?? 4);
			const string linebreakRegex = """ ?\\\s+""";
			return Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(
				code, commentRegex3, "\n")
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

		public void SkipStmt()
		{
			while (CurrentToken.Which is not 
				(TokenType.EOF or TokenType.SoftStmtSeparator or TokenType.StrictStmtSeparator or TokenType.CloseCurlyBracket or TokenType.OpenCurlyBracket)
			) 
			{
				Next();
			}
		}

		public bool CurrentIs(params TokenType[] types) => types.Contains(CurrentToken.Which);

		public bool NextIs(params TokenType[] types) => 
			types.Contains(PeekNext(1).Which) || 
			((PeekNext(1).Which == TokenType.SoftStmtSeparator || PeekNext(1).Which == TokenType.StrictStmtSeparator) && types.Contains(PeekNext(2).Which));

		public void SkipStmtSeparator()
		{
			if (CurrentIs(TokenType.SoftStmtSeparator, TokenType.StrictStmtSeparator))
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

		/// <summary>
		/// Expects the next token to be one of the given types and returns it.
		/// </summary>
		/// <returns>The next token, which is one of the expected types.</returns>
		/// <exception cref="ArgumentException">If no token types are provided.</exception>
		/// <exception cref="UnexpectedToken">If the current token is not the expected token.</exception>
		public Token ExpectNext(params TokenType[] expected)
		{
			if (expected.Length == 0)
				throw new ArgumentException("Expected at least one token type");
			var next = Next();
			if (!expected.Contains(next.Which))
				throw new UnexpectedToken(next.Range, expected.First(), next);
			return next;
		}

		/// <summary>
		/// Expects the given token types and returns the next token.
		/// </summary>
		/// <returns>The token after the expected token.</returns>
		/// <exception cref="ArgumentException">If no token types are provided.</exception>
		/// <exception cref="UnexpectedToken">If the current token is not the expected token.</exception>
		public Token Expect(params TokenType[] expected)
		{
			if (expected.Length == 0)
				throw new ArgumentException("Expected at least one token type");
			if (!expected.Contains(CurrentToken.Which))
				throw new UnexpectedToken(CurrentToken.Range, expected.First(), CurrentToken);
			return Next();
		}

		/// <summary>
		/// Expects the current token and advances the lexer.
		/// </summary>
		/// <returns>The expected token.</returns>
		/// <exception cref="ArgumentException">If no token types are provided.</exception>
		/// <exception cref="UnexpectedToken">If the current token is not the expected token.</exception>
		public Token ExpectThis(params TokenType[] expected)
		{
			if (expected.Length == 0)
				throw new ArgumentException("Expected at least one token type");
			if (!expected.Contains(CurrentToken.Which))
				throw new UnexpectedToken(CurrentToken.Range, expected.First(), CurrentToken);
			var result = CurrentToken; 
			Next();
			return result;
		}

		public Token PeekNext(int n = 1)
		{
			CurrentPos += n;
			var nextToken = CurrentToken;
			CurrentPos -= n;
			return nextToken;
		}

		private static readonly List<LexRule> LexRules = BuildLexRules();

		private static void ApplySquareBracketDepth(TokenType which, ref int depth)
		{
			if (which == TokenType.OpenSquareBracket) depth++;
			else if (which == TokenType.CloseSquareBracket && depth > 0) depth--;
		}

		/// <summary>
		/// <see cref="TokenType.SoftStmtSeparator"/>: newline runs at square-bracket depth 0 (semicolons are <see cref="TokenType.StrictStmtSeparator"/> via regex).
		/// </summary>
		private static bool TryLexStmtSeparator(string code, int pos, int squareBracketDepth, out int length)
		{
			length = 0;
			if (squareBracketDepth > 0 || pos >= code.Length)
				return false;

			var c = code[pos];
			if (c is not ('\r' or '\n'))
				return false;

			var q = pos;
			while (q < code.Length)
			{
				if (code[q] == '\r')
				{
					q++;
					if (q < code.Length && code[q] == '\n')
						q++;
				}
				else if (code[q] == '\n')
					q++;
				else
					break;
			}
			length = q - pos;
			return true;
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
			var code = Text.ToString();
			var tokens = new List<Token>();
			var pos = 0;
			var squareBracketDepth = 0;

			while (pos < code.Length)
			{
				if (TryLexStmtSeparator(code, pos, squareBracketDepth, out var sepLen))
				{
					tokens.Add(new Token
					{
						Which = TokenType.SoftStmtSeparator,
						Range = new FileRange(pos, pos + sepLen, Filepath, Text),
					});
					pos += sepLen;
					continue;
				}

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
					ApplySquareBracketDepth(bestType, ref squareBracketDepth);
					pos += bestLen;
				}
				else
				{
					var ch = code[pos];
					if (ch == '[') squareBracketDepth++;
					else if (ch == ']' && squareBracketDepth > 0) squareBracketDepth--;
					pos += 1;
				}
			}

			Tokens = tokens;
			Tokens.Add(new Token()
			{
				Which = TokenType.EOF,
				Range = Text.EOF,
			});
		}

		public Lexer(ProgramArgs args, string filepath, FileText text)
		{
			Args = args;
			Filepath = filepath;
			Text = text;
		}
	}
}
