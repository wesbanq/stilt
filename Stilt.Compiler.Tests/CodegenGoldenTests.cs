namespace Stilt.Compiler.Tests;

public class CodegenGoldenTests
{
    public static IEnumerable<object[]> CodegenFixtures()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var dir = Path.Combine(root, "Fixtures", "Codegen");
        if (!Directory.Exists(dir))
            yield break;

        foreach (var file in Directory.GetFiles(dir, $"*{stilt.Program.CodeFileExtension}", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            yield return new object[] { name };
        }
    }

    // Placeholder – will be implemented once code generation exists.
    private static string GenerateTargetCode(string fixtureName)
    {
        // For now, this is a hook: once codegen is implemented, use
        // TestCompilerHarness + IRGenerator (or later stages) to produce
        // the final textual/serialized output.
        throw new NotImplementedException("Code generation is not implemented yet.");
    }

    [Theory(Skip = "Codegen not implemented yet; this is a placeholder for future tests")]
    [MemberData(nameof(CodegenFixtures))]
    public void CodegenMatchesGolden(string fixtureName)
    {
        var output = GenerateTargetCode(fixtureName);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var expectedDir = Path.Combine(root, "Expected", "Codegen");
        var goldenPath = Path.Combine(expectedDir, $"{fixtureName}.out.txt");

        if (!File.Exists(goldenPath))
            throw new FileNotFoundException($"Codegen golden file not found: {goldenPath}", goldenPath);

        var expected = File.ReadAllText(goldenPath);
        Assert.Equal(expected, output);
    }
}

