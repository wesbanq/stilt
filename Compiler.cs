using stilt.AST;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text;

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

		public static FileRange? operator +(FileRange left, FileRange right)
		{
			if (!left.SameFile(right))
				throw new ArgumentException();

			if (left == null)
				return right;

			if (right == null)
				return left;

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
			if (Range != null)
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

	public static class Compiler
	{
		public static A? GetAttributeFromEnum<T, A>(T value)
			where T : Enum
			where A : Attribute
		{
			var a = typeof(T).GetField(value.ToString())?.GetCustomAttributes<A>()?.ToArray();
			if (a is not null && a.Length > 0)
				return a.First();
			else
				return null;
		}

		public static A[]? GetAttributesFromEnum<T, A>(T value)
			where T : Enum
			where A : Attribute
		{
			var a = typeof(T).GetField(value.ToString())?.GetCustomAttributes<A>()?.ToArray();
			if (a is not null && a.Length > 0)
				return a;
			else
				return null;
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
				if (field.GetCustomAttribute<A>()?.Name == toFind)
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
				if (field.GetCustomAttribute<A>()?.Name == toFind)
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
