using slate.Compilation;
using Xunit;

namespace slate.Tests;

public class AstGoldenTests
{
	public static string TestDataRoot => GoldenTestHelper.GetTestDataRoot();
	public static string AstDir => Path.Combine(TestDataRoot, "Ast");
	private const string GoldenSuffix = ".ast.json";

	[Theory]
	[MemberData(nameof(GetSlateFiles))]
	public void Ast_matches_golden(string slatePath)
	{
		// Console.WriteLine($"Ast_matches_golden: {slatePath}");
		var args = new ProgramArgs { MainCodeFilepath = slatePath, NoStd = true };
		Builtins.PopulateBuiltinScope(args);

		var compiler = new Compiler(args);
		compiler.Build();

		GoldenTestHelper.AssertCompilationSucceeded(compiler, "AST", Path.GetFileName(slatePath), slatePath);

		var statements = compiler.Files[0].ParserResult!.Statements;
		var actual = CompilerJsonSerializer.SerializeToJson(statements, CompilerJsonSerializer.ExclusionPreset.Ast);

		var goldenPath = GoldenTestHelper.GetGoldenPath(slatePath, GoldenSuffix);
		GoldenTestHelper.AssertOrUpdateGolden(actual, goldenPath, Path.GetFileName(slatePath), slatePath);
	}

	public static TheoryData<string> GetSlateFiles()
	{
		var data = new TheoryData<string>();
		if (!Directory.Exists(AstDir))
			return data;
		foreach (var path in Directory.EnumerateFiles(AstDir, "*.slate"))
			data.Add(path);
		return data;
	}
}
