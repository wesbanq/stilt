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
			// precedence - longest > shortest : symbol > regex
			// assume both ranges are sorted
			var finalList = new List<Token>();

			// Add tokens from `ranges` that don't overlap any token in `priorityRanges`
			foreach (Token otherToken in ranges)
			{
				bool overlapsAnyPriority = false;
				foreach (Token p in priorityRanges)
				{
					if (p.Range.Overlaps(otherToken.Range))
					{
						overlapsAnyPriority = true;
						break;
					}
				}

				if (!overlapsAnyPriority && !finalList.Contains(otherToken))
				{
					finalList.Add(otherToken);
				}
			}

			// Process priority ranges, resolving overlaps within priorityRanges by selecting the longest
			foreach (Token token in priorityRanges)
			{
				Token? maxOverlappingToken = null;
				foreach (Token candidate in priorityRanges)
				{
					if (ReferenceEquals(candidate, token))
					{
						continue;
					}

					if (candidate.Range.Overlaps(token.Range))
					{
						if (maxOverlappingToken == null || candidate.Range.Length > maxOverlappingToken.Range.Length)
						{
							maxOverlappingToken = candidate;
						}
					}
				}

				if (maxOverlappingToken != null)
				{
					if (maxOverlappingToken.Range.Length == token.Range.Length)
					{
						Program.Dump(maxOverlappingToken);
						Program.Dump(token);
						throw new Lexer.OverlappingTokensException("Overlapping tokens", token, maxOverlappingToken);
					}
					else
					{
						Token winner = maxOverlappingToken.Range.Length > token.Range.Length ? maxOverlappingToken : token;
						if (!finalList.Contains(winner))
						{
							finalList.Add(winner);
						}
					}
				}
				else
				{
					if (!finalList.Contains(token))
					{
						finalList.Add(token);
					}
				}
			}

			return finalList.OrderBy(t => t.Range.Start).ThenBy(t => t.Range.End).ToList();
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
