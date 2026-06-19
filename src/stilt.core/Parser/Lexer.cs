using System.Reflection;

namespace stilt
{
	/// <summary>
	/// Turns preprocessed source text into a flat list of <see cref="Token"/>s (pipeline stage 1).
	/// The token vocabulary is not hard-coded here: each <see cref="TokenType"/> enum field carries
	/// <see cref="SymbolAttribute"/>s describing the literal(s) or regex(es) that produce it, and the lexer
	/// reflects over them to build its rules (see <see cref="BuildLexRules"/>). The same enum also carries the
	/// operator-precedence attributes the parser reads, keeping each token's spelling and grammar in one place.
	///
	/// After <see cref="Lex"/> fills <see cref="Tokens"/>, this object doubles as the parser's cursor over that
	/// list: <see cref="CurrentPos"/> is the read head and <see cref="Next"/>/<see cref="PeekNext"/>/<see cref="Expect"/>
	/// (etc.) move and inspect it.
	/// </summary>
	public class Lexer
	{
		public List<Token> Tokens;
		/// <summary>Index of the token the parser is currently looking at; the lexer is also the token cursor.</summary>
		public int CurrentPos = 0;
		public readonly string Filepath;
		public readonly ProgramArgs Args;
		public FileText Text;

		public Token CurrentToken => Tokens.Count > CurrentPos
			? Tokens[CurrentPos]
			: throw new UnexpectedEOF(Text.EOF);

		/// <summary>
		/// Reflects over the <see cref="TokenType"/> enum and collects each field's <see cref="SymbolAttribute"/>s,
		/// split into literal <paramref name="symbols"/> (escaped to match verbatim) and raw <paramref name="regex"/>
		/// patterns. Both lists are indexed by the enum value, so list slot <c>i</c> holds the patterns for <c>(TokenType)i</c>.
		/// </summary>
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

		/// <summary>
		/// Cleans raw source before lexing: strips line (<c>#</c>) and block (<c>## … ##</c>) comments, normalizes
		/// CRLF to LF, expands tabs to spaces, and joins backslash line-continuations. Comment and newline
		/// replacements deliberately preserve the original line count so diagnostics still point at the right line.
		/// </summary>
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

		/// <summary>
		/// Compiles the patterns gathered by <see cref="GetSymbolAttribute"/> into one anchored, non-backtracking
		/// <see cref="Regex"/> per pattern, tagged with its <see cref="TokenType"/> and whether it is a literal symbol.
		/// Built once and cached in <see cref="LexRules"/>; <see cref="Lex"/> tries every rule at each position.
		/// </summary>
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

		/// <summary>Advances to the next statement boundary (separator, brace, or EOF). Used to recover after a syntax error so parsing can resume at the following statement.</summary>
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

		/// <summary>True if the next meaningful token is one of <paramref name="types"/>, transparently looking past a single statement separator.</summary>
		public bool NextIs(params TokenType[] types) =>
			types.Contains(PeekNext(1).Which) ||
			((PeekNext(1).Which == TokenType.SoftStmtSeparator || PeekNext(1).Which == TokenType.StrictStmtSeparator) && types.Contains(PeekNext(2).Which));

		/// <summary>Consumes the current token if it is a statement separator; otherwise does nothing.</summary>
		public void SkipStmtSeparator()
		{
			if (CurrentIs(TokenType.SoftStmtSeparator))
				Next();
		}

		/// <summary>Steps the cursor back one token (clamped at the start) and returns the new current token.</summary>
		public Token Prev()
		{
			if (CurrentPos > 0)
				CurrentPos--;
			return CurrentToken;
		}

		/// <summary>Steps the cursor forward one token (clamped at EOF) and returns the new current token.</summary>
		public Token Next()
		{
			if (CurrentPos < Tokens.Count - 1)
				CurrentPos++;
			return CurrentToken;
		}

		/// <summary>Advances to the first token of <paramref name="type"/> (or EOF), then steps once past it, returning the token that follows.</summary>
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

		/// <summary>Returns the token <paramref name="n"/> positions ahead without moving the cursor (temporarily shifts and restores <see cref="CurrentPos"/>).</summary>
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

		/// <summary>Moves the cursor back to a specific token instance. Lets the parser save a position and backtrack to it (e.g. when an optional construct fails to parse).</summary>
		public void Goto(Token to)
		{
			var t = Tokens.FindIndex(t => ReferenceEquals(t, to));
			if (t == -1)
				throw new ArgumentException($"Couldn't find token {to}");
			CurrentPos = t;
		}

		/// <summary>
		/// Scans the whole source once, left to right, and fills <see cref="Tokens"/> (terminated with an EOF token).
		/// At each position it first checks for a statement-separating newline run, then tries every <see cref="LexRule"/>
		/// and keeps the longest match (maximal munch); ties are broken in favor of literal symbols over regex, so e.g.
		/// the keyword <c>func</c> wins over the identifier pattern. Bytes that match nothing are skipped. Square-bracket
		/// depth is tracked throughout so newlines inside <c>[ … ]</c> are not treated as statement separators.
		/// </summary>
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
