namespace stilt.Compilation
{
	public class FileRange
	{
		public int Start;
		public int End;
		[JsonIgnore]
		public string Filename;

		[JsonIgnore]
		private readonly FileText _text;

		[JsonIgnore]
		public int Length => End - Start;
		public string Text => _text.Slice(Start, Length);
		[JsonIgnore]
		public string[] TextLines 
		{
			get
			{
				var text = _text.ToString();
				var newStart = Start;
				var newEnd = End-1;
				while (newStart > 0 && text[--newStart] != '\n');
				if (text[newStart] == '\n') ++newStart;
				while (newEnd < text.Length && text[newEnd] != '\n') ++newEnd;
				--newEnd;

				return text.Substring(newStart, newEnd - newStart + 1).Split("\n");
			}
		}

		public string FormatLineAndColumn()
		{
			var (l, c) = StartLineAndColumn;
			return $"line: {l}, char: {c}";
		}

		[JsonIgnore]
		public (int line, int column) StartLineAndColumn => ToLineAndColumn(Start);
		[JsonIgnore]
		public (int line, int column) EndLineAndColumn => ToLineAndColumn(End-1);
		public (int line, int column) ToLineAndColumn(int charAt)
		{
			var line = 1;
			var column = 1;

			for (int i = 0; i < charAt; ++i) 
			{
				++column;
				if (_text.Text[i] == '\n')
				{
					++line;
					column = 1;
				}
			}

			return (line, column);
		}

		public static FileRange? operator +(FileRange? left, FileRange? right)
		{
			if (left is null)
				return right;

			if (right is null)
				return left;

			if (!left.SameFile(right))
				throw new ArgumentException();

			if (left.Before(right))
				return new FileRange(left.Start, right.End, left.Filename, left._text);
			else
				return new FileRange(right.Start, left.End, left.Filename, left._text);
		}

		public FileRange(int start, int end, string filename, FileText file)
		{
			Start = start;
			End = end;
			Filename = filename;
			_text = file;
		}

		public bool SameFile (FileRange other)
		{
			if (other is null)
				return false;

			return string.Equals(Filename, other.Filename, StringComparison.Ordinal);
		}

		public bool Before(FileRange other)
		{
			if (other is null)
				return false;

			return SameFile(other) && Start <= other.Start && End <= other.Start;
		}

		public bool After(FileRange other)
		{
			if (other is null)
				return false;

			return SameFile(other) && Start >= other.End && End >= other.End;
		}

		public bool Overlaps(FileRange other)
		{
			if (other is null)
				return false;

			return SameFile(other) && Start < other.End && End > other.Start;
		}

		public static List<Token> RemoveOverlaps(List<Token> priorityRanges, List<Token> ranges)
		{
			//precedence - longest > shortest : symbol > regex
			//assume both ranges are sorted
			var finalList = ranges.Concat(priorityRanges);

			foreach (Token token in finalList)
			{
				Token longestOverlap = token;
				foreach (Token otherToken in finalList)
				{
					if (ReferenceEquals(token, otherToken))
					{
						continue;
					}
					if (token.Range.Overlaps(otherToken.Range))
					{
						if (otherToken.Range.Length >= longestOverlap.Range.Length)
						{
							longestOverlap = otherToken;
						}
					}
				}
				if (!ReferenceEquals(token, longestOverlap))
				{
					finalList = finalList.Where(t => !ReferenceEquals(token, t)).ToList();
				}
			}

			return finalList.ToList();
		}
	}
}