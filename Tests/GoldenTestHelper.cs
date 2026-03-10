using System.Reflection;
using System.Text;
using Xunit;

namespace stilt.Tests;

/// <summary>
/// Shared logic for golden file comparison and regeneration.
/// </summary>
internal static class GoldenTestHelper
{
	private const string FlagFileName = "RegenerateGoldens.flag";

	public static bool RegenerateGoldens => File.Exists(GetFlagPath());

	private static string GetFlagPath()
	{
		var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		return string.IsNullOrEmpty(dir) ? "" : Path.Combine(dir, FlagFileName);
	}

	public static string GetTestDataRoot()
	{
		var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		if (string.IsNullOrEmpty(dir))
			throw new InvalidOperationException("Could not determine test assembly directory.");
		return Path.Combine(dir, "TestData");
	}

	public static string GetGoldenPath(string stiltFilePath, string suffix)
	{
		return stiltFilePath + suffix;
	}

	public static void AssertOrUpdateGolden(string actual, string goldenPath, string testDisplayName, string sourcePath)
	{
		if (RegenerateGoldens)
		{
			WriteGolden(actual, goldenPath);
			return;
		}

		if (!File.Exists(goldenPath))
			throw new FileNotFoundException(
				$"Golden file not found for {testDisplayName}.\nGolden: {goldenPath}\nSource: {sourcePath}\nRun tests with -p:RegenerateGoldens=true to create it.",
				goldenPath);

		var expected = File.ReadAllText(goldenPath, Encoding.UTF8);
		var normalizedActual = NormalizeLineEndings(actual);
		var normalizedExpected = NormalizeLineEndings(expected);
		if (!string.Equals(normalizedExpected, normalizedActual, StringComparison.Ordinal))
		{
			var diff = FindFirstDiff(normalizedExpected, normalizedActual);
			var (line, col) = GetLineAndColumn(normalizedExpected, diff.Index);
			var expectedLine = GetLineAt(normalizedExpected, diff.Index);
			var actualLine = GetLineAt(normalizedActual, diff.Index);

			var pointer = col <= 1 ? "^" : new string(' ', col - 1) + "^";
			throw new Xunit.Sdk.XunitException(
				$"Golden mismatch for {testDisplayName} at line {line}, col {col} (offset {diff.Index}).\n" +
				$"Golden: {goldenPath}\n" +
				$"Source: {sourcePath}\n" +
				"\nExpected line:\n" +
				expectedLine + "\n" +
				pointer + "\n" +
				"\nActual line:\n" +
				actualLine + "\n" +
				pointer + "\n" +
				"\nRun tests with -p:RegenerateGoldens=true to overwrite.");
		}
	}

	private readonly record struct DiffInfo(int Index);

	private static DiffInfo FindFirstDiff(string expected, string actual)
	{
		var len = Math.Min(expected.Length, actual.Length);
		for (int i = 0; i < len; i++)
		{
			if (expected[i] != actual[i])
				return new DiffInfo(i);
		}
		return new DiffInfo(len);
	}

	private static (int Line, int Column) GetLineAndColumn(string text, int index)
	{
		// 1-based line/column
		int line = 1;
		int col = 1;
		int i = 0;
		int limit = Math.Min(index, text.Length);
		while (i < limit)
		{
			if (text[i] == '\n')
			{
				line++;
				col = 1;
			}
			else
			{
				col++;
			}
			i++;
		}
		return (line, col);
	}

	private static string GetLineAt(string text, int index)
	{
		if (text.Length == 0)
			return "";

		int safeIndex = Math.Clamp(index, 0, text.Length);
		int start = safeIndex == 0 ? 0 : text.LastIndexOf('\n', Math.Min(safeIndex - 1, text.Length - 1)) + 1;
		int end = safeIndex < text.Length ? text.IndexOf('\n', safeIndex) : -1;
		if (end < 0) end = text.Length;
		return text.Substring(start, end - start);
	}

	private static void WriteGolden(string actual, string goldenPath)
	{
		var dir = Path.GetDirectoryName(goldenPath);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		File.WriteAllText(goldenPath, actual, Encoding.UTF8);
		// Also write to source TestData so the file is committed (assembly is in .../Tests/bin/Debug/net10.0/)
		var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		if (!string.IsNullOrEmpty(assemblyDir))
		{
			var testDataRoot = GetTestDataRoot();
			if (goldenPath.StartsWith(testDataRoot, StringComparison.OrdinalIgnoreCase))
			{
				var relative = Path.GetRelativePath(testDataRoot, goldenPath);
				var sourceGolden = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "TestData", relative));
				Directory.CreateDirectory(Path.GetDirectoryName(sourceGolden)!);
				File.WriteAllText(sourceGolden, actual, Encoding.UTF8);
			}
		}
	}

	private static string NormalizeLineEndings(string s)
	{
		return s.Replace("\r\n", "\n").Replace("\r", "\n");
	}
}
