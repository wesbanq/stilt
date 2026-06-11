using System.Reflection;
using System.Text;
using stilt;
using stilt.Compilation;
using stilt.Errors;
using Xunit;

namespace stilt.Tests;

/// <summary>
/// Shared logic for golden file comparison, regeneration, and readable test failures.
/// </summary>
internal static class GoldenTestHelper
{
	private const string FlagFileName = "RegenerateGoldens.flag";
	private const int MaxLineDisplayLength = 96;
	private const int SnippetRadius = 32;

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

	/// <summary>
	/// Fails with full compiler/linker diagnostics when the build did not succeed cleanly.
	/// </summary>
	public static void AssertCompilationSucceeded(Compiler compiler, string suite, string shortName, string stiltPath)
	{
		if (compiler.Files.Count == 0)
		{
			Assert.Fail(
				FormatFailureBanner(suite, shortName, stiltPath, "No files were produced by the compiler.") +
				"\nCheck MainCodeFilepath and that the test data was copied to the output directory.");
			return;
		}

		var parseFailed = compiler.Files.OfType<ParsedFile>().Any(f => f.HasErrors);
		var linkFailed = compiler.Link?.Errors is { Count: > 0 };

		if (!parseFailed && !linkFailed)
			return;

		var body = new StringBuilder();
		body.AppendLine(FormatFailureBanner(suite, shortName, stiltPath, "Compilation failed."));
		body.AppendLine();

		if (parseFailed)
		{
			body.AppendLine("---------- Parser / semantic messages (error level) ----------");
			foreach (var pf in compiler.Files.OfType<ParsedFile>())
			{
				if (!pf.HasErrors)
					continue;
				body.AppendLine();
				body.AppendLine($"File: {pf.Filepath}");
				var errs = pf.Errors.Where(e => e.Severity >= ErrorSeverity.Error).ToList();
				for (var i = 0; i < errs.Count; i++)
				{
					body.AppendLine();
					body.AppendLine($"--- Error {i + 1} of {errs.Count} ---");
					body.AppendLine(errs[i].ToString());
				}
			}
			body.AppendLine();
		}

		if (linkFailed && compiler.Link is { Errors: { } linkErrors })
		{
			body.AppendLine("---------- Linker messages ----------");
			for (var i = 0; i < linkErrors.Count; i++)
			{
				body.AppendLine();
				body.AppendLine($"--- Linker issue {i + 1} of {linkErrors.Count} ---");
				body.AppendLine(linkErrors[i].ToString());
			}
			body.AppendLine();
		}

		body.AppendLine("---------- Hint ----------");
		body.AppendLine("Fix the .stilt source (or compiler). To refresh expected JSON after intentional output changes:");
		body.AppendLine("  dotnet test src/stilt.Tests/stilt.Tests.csproj -p:RegenerateGoldens=true");

		Assert.Fail(body.ToString());
	}

	private static string FormatFailureBanner(string suite, string shortName, string stiltPath, string headline)
	{
		return $"""

			========== stilt.Tests | {suite} | {headline} ==========
			Test file:  {shortName}
			Source:     {stiltPath}
			""";
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
			var message = FormatGoldenMismatch(normalizedExpected, normalizedActual, testDisplayName, goldenPath, sourcePath);
			throw new Xunit.Sdk.XunitException(message);
		}
	}

	private static string FormatGoldenMismatch(
		string normalizedExpected,
		string normalizedActual,
		string testDisplayName,
		string goldenPath,
		string sourcePath)
	{
		var diff = FindFirstDiff(normalizedExpected, normalizedActual);
		var (line, col) = GetLineAndColumn(normalizedExpected, diff.Index);
		var expectedLine = GetLineAt(normalizedExpected, diff.Index);
		var actualLine = GetLineAt(normalizedActual, diff.Index);

		var sb = new StringBuilder();
		sb.AppendLine("========== Golden JSON mismatch ==========");
		sb.AppendLine($"Test file:     {testDisplayName}");
		sb.AppendLine($"Golden (path): {goldenPath}");
		sb.AppendLine($"Source .stilt: {sourcePath}");
		sb.AppendLine();
		sb.AppendLine($"First difference in golden file: line {line}, column {col} (0-based offset {diff.Index})");
		sb.AppendLine();
		sb.AppendLine("---------- Expected line (from golden) ----------");
		sb.AppendLine(TruncateForDisplay(expectedLine));
		sb.AppendLine(PointerLine(expectedLine, col));
		sb.AppendLine();
		sb.AppendLine("---------- Actual line (from compiler) ----------");
		sb.AppendLine(TruncateForDisplay(actualLine));
		sb.AppendLine(PointerLine(actualLine, col));
		sb.AppendLine();
		sb.AppendLine("---------- Snippet at diff (compact) ----------");
		sb.AppendLine("Expected: " + SnippetAround(normalizedExpected, diff.Index));
		sb.AppendLine("Actual:   " + SnippetAround(normalizedActual, diff.Index));
		sb.AppendLine();
		if (normalizedExpected.Length != normalizedActual.Length)
		{
			sb.AppendLine($"Length: expected {normalizedExpected.Length} chars, actual {normalizedActual.Length} chars.");
			sb.AppendLine();
		}
		sb.AppendLine("---------- Hint ----------");
		sb.AppendLine("dotnet test src/stilt.Tests/stilt.Tests.csproj -p:RegenerateGoldens=true");

		return sb.ToString().TrimEnd();
	}

	private static string TruncateForDisplay(string line)
	{
		if (line.Length <= MaxLineDisplayLength)
			return line;
		var keep = MaxLineDisplayLength / 2 - 2;
		return line[..keep] + " … " + line[^keep..];
	}

	private static string PointerLine(string line, int col1Based)
	{
		var display = TruncateForDisplay(line);
		if (display.Length < line.Length)
			return "(pointer approximate — line was truncated for display)";
		var col = Math.Clamp(col1Based, 1, line.Length + 1);
		var spaces = col <= 1 ? 0 : col - 1;
		return new string(' ', spaces) + "^";
	}

	private static string SnippetAround(string text, int index)
	{
		if (text.Length == 0)
			return "(empty)";
		var i = Math.Clamp(index, 0, text.Length);
		var start = Math.Max(0, i - SnippetRadius);
		var end = Math.Min(text.Length, i + SnippetRadius);
		var left = start > 0 ? "…" : "";
		var right = end < text.Length ? "…" : "";
		return left + text[start..end].Replace('\n', '↓').Replace('\r', ' ') + right;
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
				col++;
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
		return text[start..end];
	}

	private static void WriteGolden(string actual, string goldenPath)
	{
		var dir = Path.GetDirectoryName(goldenPath);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		File.WriteAllText(goldenPath, actual, Encoding.UTF8);
		var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		if (!string.IsNullOrEmpty(assemblyDir))
		{
			var testDataRoot = GetTestDataRoot();
			if (goldenPath.StartsWith(testDataRoot, StringComparison.OrdinalIgnoreCase))
			{
				var relative = Path.GetRelativePath(testDataRoot, goldenPath);
				// net10.0 output: bin/Debug/net10.0 → project dir is three levels up from assemblyDir
				var sourceGolden = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "TestData", relative));
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
