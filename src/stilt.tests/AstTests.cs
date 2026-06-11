using stilt.Compilation;
using Xunit;

namespace stilt.Tests;

public class AstGoldenTests
{
	public static string TestDataRoot => GoldenTestHelper.GetTestDataRoot();
	public static string AstDir => Path.Combine(TestDataRoot, "Ast");
	private const string GoldenSuffix = ".ast.json";

	[Theory]
	[MemberData(nameof(GetStiltFiles))]
	public void Ast_matches_golden(string stiltPath)
	{
		// Console.WriteLine($"Ast_matches_golden: {stiltPath}");
		var args = new ProgramArgs { MainCodeFilepath = stiltPath, NoStd = true };
		Builtins.PopulateBuiltinScope(args);

		var compiler = new Compiler(args);
		compiler.Build();

		GoldenTestHelper.AssertCompilationSucceeded(compiler, "AST", Path.GetFileName(stiltPath), stiltPath);

		var statements = compiler.Files[0].ParserResult!.Statements;
		var actual = CompilerJsonSerializer.SerializeToJson(statements, CompilerJsonSerializer.ExclusionPreset.Ast);

		var goldenPath = GoldenTestHelper.GetGoldenPath(stiltPath, GoldenSuffix);
		GoldenTestHelper.AssertOrUpdateGolden(actual, goldenPath, Path.GetFileName(stiltPath), stiltPath);
	}

	public static TheoryData<string> GetStiltFiles()
	{
		var data = new TheoryData<string>();
		if (!Directory.Exists(AstDir))
			return data;
		foreach (var path in Directory.EnumerateFiles(AstDir, "*.stilt"))
			data.Add(path);
		return data;
	}
}
