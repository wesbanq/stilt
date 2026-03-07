using System.Reflection;
using System.Text;
using Xunit;

namespace stilt.Tests;

/// <summary>
/// Shared logic for golden file comparison and regeneration.
/// </summary>
internal static class GoldenTestHelper
{
	public const string RegenerateGoldensEnvVar = "REGENERATE_GOLDENS";

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

	public static void AssertOrUpdateGolden(string actual, string goldenPath, string testDisplayName)
	{
		if (string.Equals(Environment.GetEnvironmentVariable(RegenerateGoldensEnvVar), "1", StringComparison.Ordinal))
		{
			WriteGolden(actual, goldenPath);
			return;
		}

		if (!File.Exists(goldenPath))
			throw new FileNotFoundException($"Golden file not found: {goldenPath}. Run tests with {RegenerateGoldensEnvVar}=1 to create it.", goldenPath);

		var expected = File.ReadAllText(goldenPath, Encoding.UTF8);
		var normalizedActual = NormalizeLineEndings(actual);
		var normalizedExpected = NormalizeLineEndings(expected);
		Assert.Equal(normalizedExpected, normalizedActual);
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
