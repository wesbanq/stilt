using System;
using System.Collections.Generic;
using System.Text;

namespace stilt
{
	public class FileRange
	{
		public int Start;
		public int End;
		public int Length => End - Start;
		public string Filename;

		public FileRange(int start, int end, string filename)
		{
			Start = start;
			End = end;
			Filename = filename;
		}

		public bool Overlaps(FileRange other)
		{
			if (other is null)
			{
				return false;
			}

			if (!string.Equals(Filename, other.Filename, StringComparison.Ordinal))
			{
				return false;
			}

			return Start < other.End && End > other.Start;
		}

		public static List<Token> RemoveOverlaps(List<Token> priorityRanges, List<Token> ranges)
		{
			//precedence - longest > shortest : symbol > regex
			//assume both ranges are sorted
			var finalList = new List<Token>();
			foreach (Token token in priorityRanges)
			{
				//Token tokenOvelapped = null;
				foreach (Token otherToken in ranges)
				{
					if (!token.Range.Overlaps(otherToken.Range))
					{
						//tokenOvelapped = otherToken;
						//break;
						finalList.Add(otherToken);
					}
				}

				List<Token> overlappingTokens = priorityRanges
					.Where(t => t.Range.Overlaps(token.Range) && !ReferenceEquals(token, t))
					.ToList();
				if (overlappingTokens.Count > 0)
				{
					Token max = overlappingTokens.MaxBy(t => t.Range.Length);
					if (max.Range.Length == token.Range.Length)
					{
						Program.Dump(max);
						Program.Dump(token);
						throw new Lexer.OverlappingTokensException("Overlapping tokens", token, max);
					}
					else
					{
						finalList.Add(max.Range.Length > token.Range.Length ? max : token);
					}
				}
				else
				{
					finalList.Add(token);
				}
			}
			return finalList;
		}
	}

	public abstract class Compiler
	{
		public static void Build(ProgramArgs args)
		{
			Console.WriteLine(args.MainCodeFilepath);

		}
	}
}
