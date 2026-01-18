using stilt.AST;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text;

namespace stilt
{
	public class FileRange
	{
		public int Start;
		public int End;
		public string Filename;

		public int Length => End - Start;
		//bad
		public string Text => Lexer.Preprocess(File.ReadAllText(Filename)).Substring(Start, Length);
		public string[] TextLines 
		{
			get
			{
				var text = Lexer.Preprocess(File.ReadAllText(Filename));
				var newStart = Start;
				var newEnd = End;
				while (text[newStart] != '\n') newStart--;
				newStart++;
				while (text[newEnd] != '\n') newEnd++;
				newEnd--;

				return text.Substring(newStart, newEnd - newStart).Split("\n");
			}
		}

		public string FormatLineAndColumn()
		{
			var (l, c) = StartLineAndColumn;
			return $"line: {l}, char: {c}";
		}

		public (int line, int column) StartLineAndColumn => ToLineAndColumn(Start, Filename);
		public (int line, int column) EndLineAndColumn => ToLineAndColumn(End, Filename);
		public static (int line, int column) ToLineAndColumn(int charAt, string Filename)
		{
			if (!File.Exists(Filename))
				throw new ArgumentException();

			int line = 1;
			int lastNewline = -1;
			string text = File.ReadAllText(Filename);

			for (int i = 0; i < charAt; i++)
			{
				if (text[i] == '\n')
				{
					line++;
					lastNewline = i;
				}
			}

			int column = charAt - lastNewline;

			return (line, column);
		}

		public static FileRange? operator +(FileRange left, FileRange right)
		{
			if (left == null || right == null || !left.SameFile(right))
				throw new ArgumentException();

			if (left.Before(right))
				return new FileRange(left.Start, right.End, left.Filename);
			else
				return new FileRange(right.Start, left.End, left.Filename);
		}

		public FileRange(int start, int end, string filename)
		{
			Start = start;
			End = end;
			Filename = filename;
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
			//ts sucks pls fix
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
		string GetDescription();
	}

	public class CompilationMessage
	{
		public string Message;
		public FileRange? Range;
		public ErrorSeverity Severity = ErrorSeverity.Info;

		public override string ToString()
		{
			if (Range != null)
			{
				var (lineS, columnS) = Range.StartLineAndColumn;
				var (lineE, columnE) = Range.EndLineAndColumn;
				var text = Range.TextLines;

				var res = "";
				for (int line = lineS; line <= lineE; line++)
				{
					var part1 = $"\t{line}| ";
					var part2 = text[lineS-line];
					var part3 = "\n\t" + new String(' ', part1.Length-1);
					var part4 = new String(' ', line == lineS ? columnS : 0)
								+ new String('^', line == lineS ? columnS-part2.Length : (line == lineE ? columnE : part2.Length));
					res += part1+part2+part3+part4;
				}

				return Message + $"\n @ {Range.FormatLineAndColumn()}, in file: {Range.Filename}\n" + res;
			}
			else
				return Message;
		}

		public CompilationMessage(string message, FileRange? range = null, ErrorSeverity severity = ErrorSeverity.Info)
		{
			Message = message;
			Range = range;
			Severity = severity;
		}
	}

	public enum ErrorSeverity
	{ Info, Warning, Error, Critical }

	public abstract class Compiler
	{
		

		public static A? GetAttributeFromEnum<T, A>(T value)
			where T : Enum
			where A : Attribute
		{
			return typeof(T)
				.GetField(value.ToString())
				?.GetCustomAttributes<A>().First();
		}

		public static List<A?> GetAttributesFromType<A>(Type t)
			where A : Attribute
		{
			List<A?> res = [];
			foreach (var field in t.GetFields())
			{
				res.Add(field.GetCustomAttribute<A>());
			}
			return res;
		}

		public static T GetEnumFromDescription<T, A>(string toFind)
			where T : Enum
			where A : Attribute, IDescriptable
		{
			foreach (var field in typeof(T).GetFields())
			{
				if (field.GetCustomAttribute<A>()?.GetDescription() == toFind)
					return (T)field.GetValue(null);
			}
			//return default;
			throw new ArgumentException($"Enum value with description '{toFind}' not found.");
		}

		public static A? GetAttrFromDescription<T, A>(string toFind)
			where T : Enum
			where A : Attribute, IDescriptable
		{
			foreach (var field in typeof(T).GetFields())
			{
				if (field.GetCustomAttribute<A>()?.GetDescription() == toFind)
					return field.GetCustomAttribute<A>();
			}
			return null;
		}

		public static void Build(ProgramArgs args)
		{
			Console.WriteLine($"Currently building: {args.MainCodeFilepath}");
			var lex = new Lexer(args);
			// ...
		}
	}
}
