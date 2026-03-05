namespace Stilt.Compiler.Tests;

public class GoldenGenerationTests
{
    public static IEnumerable<object[]> AstFixtures() => AstGoldenTests.AstFixtures();

    public static IEnumerable<object[]> IrFixtures() => IrGoldenTests.IrFixtures();

    [Fact(Skip = "Utility test – enable to regenerate AST golden files")]
    public void RegenerateAstGoldens()
    {
        foreach (var data in AstFixtures())
        {
            var fixtureName = (string)data[0];
            var parsed = TestCompilerHarness.ParseAst(fixtureName);
            var json = JsonTestSerializer.SerializeAstStatements(parsed.ParserResult.Statements);

            WriteGolden(json, Path.Combine("Ast", $"{fixtureName}.ast.json"));
        }
    }

    [Fact(Skip = "Utility test – enable to regenerate IR golden files")]
    public void RegenerateIrGoldens()
    {
        foreach (var data in IrFixtures())
        {
            var fixtureName = (string)data[0];
            var irResult = TestCompilerHarness.GenerateIr(fixtureName);
            var json = JsonTestSerializer.SerializeIrMain(irResult.MainBlock);

            WriteGolden(json, Path.Combine("Ir", $"{fixtureName}.ir.json"));
        }
    }

    private static void WriteGolden(string json, string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var fullPath = Path.Combine(root, "Expected", relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, JsonTestSerializer.NormalizeJson(json));
    }
}

