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
		public readonly int Start;
		public readonly int End;
		public readonly string Text;
		
		[JsonIgnore]
		public readonly string Filename;

		[JsonIgnore]
		private readonly FileText _text;

		[JsonIgnore]
		public int Length => End - Start;
		[JsonIgnore]
		public readonly string[] SurroundingText;

		private static string[] GetLines(FileText text, int Start, int End)
		{
			if (text.Length == 0)
				return [""];

			// Walk back to the first character of the line containing Start.
			int lineStart = Math.Clamp(Start, 0, text.Length - 1);
			while (lineStart > 0 && text[lineStart - 1] != '\n')
				--lineStart;

			// Walk forward to the newline (or EOF) ending the line containing the last
			// character of the range; End is exclusive, so inspect End - 1.
			int lineEnd = Math.Clamp(End - 1, lineStart, text.Length);
			while (lineEnd < text.Length && text[lineEnd] != '\n')
				++lineEnd;

			return text.Substring(lineStart, lineEnd - lineStart).Split("\n");
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
			SurroundingText = GetLines(_text, Start, End);
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