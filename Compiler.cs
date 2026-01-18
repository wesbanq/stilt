using stilt.AST;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace stilt
{
	public class FileRange
	{
		public int Start;
		public int End;
		public int Length => End - Start;
		public string Filename;

		public string ToLineAndColumnF()
		{
			var (l, c) = ToLineAndColumn();
			return $"line: {l}, char: {c}";
		}

		public (int line, int column) ToLineAndColumn()
		{
			int line = 1;
			int lastNewline = -1;
			string text = File.ReadAllText(Filename);

			for (int i = 0; i < Start; i++)
			{
				if (text[i] == '\n')
				{
					line++;
					lastNewline = i;
				}
			}

			int column = Start - lastNewline;

			return (line, column);
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
			// precedence - longest > shortest : symbol > regex
			// assume both ranges are sorted
			// ts sucks pls fix
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
							//finalList = finalList.Where(t => !ReferenceEquals(longestOverlap, t)).ToList();
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
