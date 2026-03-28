using System.Security.Cryptography;

namespace slate.Compilation
{
    public class FileText
	{
		[JsonIgnore]
		public string Text;
		[JsonIgnore]
		public string Filepath;

		public string Slice(int start, int len) => Text.Substring(start, len);
		[JsonIgnore]
		public FileRange EOF => new(Text.Length-1, Text.Length, Filepath, this);

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
		}

		public FileText(string filename, string text)
		{
			Filepath = filename;
			Text = Lexer.Preprocess(text);
		}
	}
}