using System.Diagnostics;

namespace stilt
{
	public class FileText
	{
		public string Text;
		public string Filepath;

		public string Slice(int start, int len) => Text.Substring(start, len);
		public FileRange EOF => new(Text.Length-1, Text.Length, Filepath, this);

		public override string ToString()
		{
			return Text;
		}

		public FileText(string filename)
		{
			if (!File.Exists(filename))
				throw new ArgumentException($"File '{filename} doesn't exist.'");
			Filepath = filename;
			Text = Lexer.Preprocess(File.ReadAllText(filename))
				?? throw new Exception();
		}
	}

	public class FileRange
	{
		public int Start;
		public int End;
		public string Filename;

		private FileText _text;

		public int Length => End - Start;
		public string Text => _text.Slice(Start, Length);
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

		public (int line, int column) StartLineAndColumn => ToLineAndColumn(Start);
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

	public interface IDescriptable
	{
		string Name { get; }
	}

	public abstract class CompilationMessage : Exception
	{
		public FileRange? Range;
		public ErrorSeverity Severity = ErrorSeverity.Info;

		public override string ToString()
		{
			if (Range is not null)
			{
				var (lineS, columnS) = Range.StartLineAndColumn;
				var (lineE, columnE) = Range.EndLineAndColumn;
				var text = Range.TextLines;

				var res = "";
				for (int line = lineS; line <= lineE; ++line)
				{
					//TODO rewrite with StringBuilder
					//magic numbers found via trial and error
					var part1 = $"\n\t{line}| ";
					var part2 = text[line-lineS];
					var part3 = "\n\t" + new String(' ', part1.Length-2);
					var part4 = new String(' ', line == lineS ? columnS-1 : 0)
								+ new String('^', 
								Math.Max(0, line == lineS 
									? (line == lineE ? Range.Length : part2.Length-(columnS-1)) 
									: (line == lineE ? columnE-1 : part2.Length)));
					res += part1+part2+part3+part4;
				}

				return $"{Severity}: " + Message + $"\n  @ {Range.FormatLineAndColumn()}, in file: {Range.Filename}\n" + res;
			}
			else
				return $"{Severity}: " + Message;
		}

		public void Print()
		{
			Console.WriteLine(ToString());
		}

		public CompilationMessage(string message, FileRange? range = null, ErrorSeverity severity = ErrorSeverity.Info)
			: base(message)
		{
			Range = range;
			Severity = severity;
		}
	}

	public enum ErrorSeverity
	{ Info, Warning, Error, Critical }

	public enum TimedEvents
	{ Compilation, Lexing, Parsing, Linking }

	public class ParsedFile
	{
		public string Filepath;
		public readonly FileText Text;
		public Lexer Lexer;
		public Parser Parser;
		public Dictionary<TimedEvents, Timer> Timers = [];
		public List<CompilationMessage> Errors => Parser.CompilationIssues;

		public void Parse(ProgramArgs args)
		{
			Lexer = new Lexer(args, Filepath, Text);
			Timers.Add(TimedEvents.Lexing, new Timer("Lexing"));
			Timers[TimedEvents.Lexing].Run(() =>
			{
				Lexer.Lex();
			});

			Parser = new Parser(args, Lexer);
			Timers.Add(TimedEvents.Parsing, new Timer("Parsing"));
			Timers[TimedEvents.Parsing].Run(() =>
			{
				Parser.ParseFile();
			});
		}

		public ParsedFile(string filepath)
		{
			Filepath = filepath;
			Text = new(Filepath);
		}
	}

	public enum MCPlatform
	{
		Java,
		Bedrock,
	}

	public class MCVersion
	{
		public static readonly MCVersion LatestJava = new(MCPlatform.Java, 21, 9);
		public static readonly MCVersion LatestBedrock = new(MCPlatform.Bedrock, 23, 0);

		public MCPlatform Platform;
		public int Major;
		public int Minor;

		public override string ToString() => $"{Platform}/1.{Major}.{Minor}";

        public override bool Equals(object? obj)
        {
            return obj is MCVersion version && this == version;
        }
		public override int GetHashCode()
		{
			return HashCode.Combine(Platform, Major, Minor);
		}

		public static bool operator ==(MCVersion left, MCVersion right)
		{
			if (left is null && right is null)
				return true;
			if (left is null || right is null)
				return false;
			return left.Platform == right.Platform && left.Major == right.Major && left.Minor == right.Minor;
		}
		public static bool operator !=(MCVersion left, MCVersion right)
		{
			return !(left == right);
		}
		public static bool operator >(MCVersion left, MCVersion right)
		{
			return left.Major > right.Major || (left.Major == right.Major && left.Minor > right.Minor);
		}
		public static bool operator <(MCVersion left, MCVersion right)
		{
			return left.Major < right.Major || (left.Major == right.Major && left.Minor < right.Minor);
		}
		public static bool operator >=(MCVersion left, MCVersion right)
		{
			return left.Major > right.Major || (left.Major == right.Major && left.Minor >= right.Minor);
		}
		public static bool operator <=(MCVersion left, MCVersion right)
		{
			return left.Major < right.Major || (left.Major == right.Major && left.Minor <= right.Minor);
		}

		public static MCVersion? ParseMCVersion(string? version)
		{
			if (version is null)
				return LatestJava;

			const string javaPrefix = "java";
			const string bedrockPrefix = "bedrock";

			var parts = version.Split('/');
			if (parts.Length != 2)
				return null;
			MCPlatform? platform = parts[0] == javaPrefix 
				? MCPlatform.Java 
				: parts[0] == bedrockPrefix
				? MCPlatform.Bedrock
				: null;
			if (platform is null)
				return null;
			var versionParts = parts[1].Split('.');
			if (versionParts.Length != 3)
				return null;
			if (!int.TryParse(versionParts[0], out var major))
				return null;
			if (!int.TryParse(versionParts[1], out var minor))
				return null;

			return new MCVersion(platform.Value, major, minor);
		}

		public MCVersion(MCPlatform platform, int major, int minor)
		{
			Platform = platform;
			Major = major;
			Minor = minor;
		}
	}

	public class Compiler
	{
		public ProgramArgs Args;
		public List<ParsedFile> Files = [];
		public Linker? Linker;
		public Dictionary<TimedEvents, Timer> Timers = [];

		public void WriteTimerReadout()
		{
			foreach (var timer in Timers)
			{
				Console.WriteLine(timer.Value.Time);
			}
			foreach (var file in Files)
			{
				Console.WriteLine($"{file.Filepath}:");
				foreach (var timer in file.Timers)
				{
					Console.WriteLine(timer.Value.Time);
				}
			}
		}

		public void PrintBuildErrors()
		{
			foreach (var file in Files)
			{
				file.Errors.ForEach(e => e.Print());
			}
			Linker?.Errors.ForEach(e => e.Print());
		}

		public static ParsedFile ParseFile(ProgramArgs args, string filepath)
		{
			var file = new ParsedFile(filepath);
			file.Parse(args);
			return file;
		}

		public void Build()
		{
			Timers.Add(TimedEvents.Compilation, new Timer("Compilation"));
			Timers[TimedEvents.Compilation].StartTimer();

			Timers[TimedEvents.Compilation].Run(() =>
			{
				foreach (var filepath in Args.MainCodeFilepaths!)
				{
					if (filepath is not null)
					{
						var file = ParseFile(Args, filepath);
						Files.Add(file);
					}
				}
			});

			Timers.Add(TimedEvents.Linking, new Timer("Linking"));
			Linker = new Linker(
				Args,
                [.. Files.Select(f => f.Parser.RootScope)],
                [.. Files.Select(f => f.Parser.Statements)]
            );
			Timers[TimedEvents.Linking].Run(() => Linker.Link());

			Timers[TimedEvents.Compilation].StopTimer();
		}

		public Compiler(ProgramArgs args)
		{
			Args = args;
		}
	}
}
