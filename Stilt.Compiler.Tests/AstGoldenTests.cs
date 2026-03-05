using stilt.AST;

namespace Stilt.Compiler.Tests;

public class AstGoldenTests
{
    public static IEnumerable<object[]> AstFixtures()
    {
        // For now, drive from the Filesystem: every .stilt file in Fixtures/Ast
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var dir = Path.Combine(root, "Fixtures", "Ast");
        if (!Directory.Exists(dir))
            yield break;

        foreach (var file in Directory.GetFiles(dir, $"*{stilt.Program.CodeFileExtension}", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            yield return new object[] { name };
        }
    }

    [Theory]
    [MemberData(nameof(AstFixtures))]
    public void AstMatchesGolden(string fixtureName)
    {
        var parsed = TestCompilerHarness.ParseAst(fixtureName);
        var json = JsonTestSerializer.SerializeAstStatements(parsed.ParserResult.Statements);
        var goldenRelPath = Path.Combine("Ast", $"{fixtureName}.ast.json");

        GoldenFileAssertions.AssertMatchesGolden(json, goldenRelPath);
    }

    [Theory]
    [MemberData(nameof(AstFixtures))]
    public void StatementsHaveScopesAndRanges(string fixtureName)
    {
        var parsed = TestCompilerHarness.ParseAst(fixtureName);

        foreach (var stmt in parsed.ParserResult.Statements)
        {
            Assert.NotNull(stmt.Scope);
            _ = stmt.GetInnerRangeOrFullRangeOrThrow();
        }
    }
}

