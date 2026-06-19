using System.Security.Cryptography;

namespace stilt.Compilation
{
	/// <summary>
	/// One source file's full text plus its path. Both constructors run the text through <see cref="Lexer.Preprocess"/>,
	/// so <see cref="Text"/> is always the preprocessed form the lexer consumes. <see cref="FileRange"/>s index into this text.
	/// </summary>
	public class FileText
	{
		[JsonIgnore]
		public string Text;
		[JsonIgnore]
		public string Filepath;
		[JsonIgnore]
		public readonly int Length;

		public string Slice(int start, int len) => Text.Substring(start, len);
		[JsonIgnore]
		public FileRange EOF => new(Math.Max(0, Text.Length-1), Text.Length, Filepath, this);

		public char this[int idx]
		{
			get => Text[idx];
		}

		public string Substring(int start, int length)
		{
			return Text.Substring(start, length);
		}

		public override string ToString()
		{
			return Text;
		}

        public string GetSHA256Hash()
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Text));
            return Convert.ToHexString(hash);
        }

		public FileText(string filename)
		{
			if (!File.Exists(filename))
				throw new ArgumentException($"File '{filename} doesn't exist.'");
			Filepath = filename;
			Text = Lexer.Preprocess(File.ReadAllText(filename))
				?? throw new Exception();
			Length = Text.Length;
		}

		public FileText(string filename, string text)
		{
			Filepath = filename;
			Text = Lexer.Preprocess(text);
			Length = Text.Length;
		}
	}
}