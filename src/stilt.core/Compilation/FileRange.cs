namespace stilt.Compilation
{
	/// <summary>
	/// A half-open span <c>[Start, End)</c> within a <see cref="FileText"/>, attached to tokens and AST nodes to point
	/// back at the source they came from. It caches the spanned <see cref="Text"/> and its surrounding source lines, and
	/// converts offsets to line/column for diagnostics. The <c>+</c> operator merges two ranges into one covering both —
	/// how a node derives its full span from its children.
	/// </summary>
	public class FileRange
	{
		[JsonIgnore]
		public readonly int Start;
		[JsonIgnore]
		public readonly int End;
		[JsonIgnore]
		public readonly string Filename;

		[JsonIgnore]
		private readonly FileText _text;

		[JsonIgnore]
		public int Length => End - Start;
		public readonly string Text;
		[JsonIgnore]
		public readonly string[] TextLines;

		private static string[] GetLines(string text, int Start, int End)
		{
			var newStart = Start;
			var newEnd = End-1;
			while (newStart > 0 && text[--newStart] != '\n');
			if (text[newStart] == '\n') ++newStart;
			while (newEnd < text.Length && text[newEnd] != '\n') ++newEnd;
			--newEnd;

			return text.Substring(newStart, newEnd - newStart + 1).Split("\n");
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

		public FileRange(int start, int end, string filename, FileText file)
		{
			Start = start;
			End = end;
			Filename = filename;
			_text = file;

			Text = _text.Slice(start, Length);
			TextLines = GetLines(Text, Start, End);
		}

		public static FileRange? operator +(FileRange? left, FileRange? right)
		{
			if (left is null)
				return right;

			if (right is null)
				return left;

			if (string.Equals(left.Filename, right.Filename, StringComparison.Ordinal))
				throw new ArgumentException("");

			if (left.Start <= right.Start && left.End <= right.Start)
				return new FileRange(left.Start, right.End, left.Filename, left._text);
			else
				return new FileRange(right.Start, left.End, left.Filename, left._text);
		}
	}
}