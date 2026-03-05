using Newtonsoft.Json.Linq;

namespace Stilt.Compiler.Tests;

public static class GoldenFileAssertions
{
    private static string RootDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private static string ExpectedDir => Path.Combine(RootDir, "Expected");

    public static void AssertMatchesGolden(string actualJson, string relativeGoldenPath)
    {
        var fullPath = Path.Combine(ExpectedDir, relativeGoldenPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Golden file not found: {fullPath}", fullPath);

        var expectedJson = File.ReadAllText(fullPath);

        var normalizedActual = JsonTestSerializer.NormalizeJson(actualJson);
        var normalizedExpected = JsonTestSerializer.NormalizeJson(expectedJson);

        if (!string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal))
        {
            var actualToken = JToken.Parse(normalizedActual);
            var expectedToken = JToken.Parse(normalizedExpected);

            throw new Xunit.Sdk.XunitException(
                $"Golden file mismatch for '{relativeGoldenPath}'.\nExpected:\n{Truncate(expectedToken)}\n\nActual:\n{Truncate(actualToken)}");
        }
    }

    private static string Truncate(JToken token, int maxLength = 2000)
    {
        var s = token.ToString();
        if (s.Length <= maxLength)
            return s;
        return s[..maxLength] + "\n... (truncated)";
    }
}

