using stilt;
using stilt.IR;

namespace Stilt.Compiler.Tests;

public static class TestCompilerHarness
{
    private static string RootDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private static string FixturesDir => Path.Combine(RootDir, "Fixtures");

    public static string GetFixturePath(string subfolder, string fixtureName)
    {
        var file = fixtureName.EndsWith(Program.CodeFileExtension, StringComparison.Ordinal)
            ? fixtureName
            : fixtureName + Program.CodeFileExtension;
        return Path.Combine(FixturesDir, subfolder, file);
    }

    public static ParsedFile ParseAst(string fixtureName, string subfolder = "Ast")
    {
        var path = GetFixturePath(subfolder, fixtureName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}", path);

        var args = new ProgramArgs
        {
            MainCodeFilepaths = new[] { path },
            Throw = true
        };

        var fileText = new FileText(path);
        var obj = Compiler.ParseFile(args, fileText);
        if (obj is not ParsedFile parsed)
            throw new InvalidOperationException("Expected ParsedFile from ParseFile in tests.");

        if (parsed.ParserResult.HasErrors)
            throw new InvalidOperationException("AST parse produced errors in test fixture.");

        return parsed;
    }

    public static IRGeneratorResult GenerateIr(string fixtureName, string subfolder = "Ir")
    {
        var path = GetFixturePath(subfolder, fixtureName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}", path);

        var args = new ProgramArgs
        {
            MainCodeFilepaths = new[] { path },
            Throw = true
        };

        var compiler = new Compiler(args);
        compiler.Build();

        if (compiler.Files.Count == 0)
            throw new InvalidOperationException("Compiler produced no files for IR generation.");

        var sourceFile = compiler.Files.First();
        var ir = new IRGenerator(args, sourceFile);
        ir.GenerateIR();
        return ir.Result;
    }
}

