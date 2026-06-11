using stilt.Compilation;
using stilt.IR;
using Xunit;

namespace stilt.Tests;

public class IrGoldenTests
{
	public static string TestDataRoot => GoldenTestHelper.GetTestDataRoot();
	public static string IrDir => Path.Combine(TestDataRoot, "Ir");
	private const string GoldenSuffix = ".ir.json";

	[Theory]
	[MemberData(nameof(GetStiltFiles))]
	public void Ir_matches_golden(string stiltPath)
	{
		var args = new ProgramArgs { MainCodeFilepath = stiltPath, NoStd = true };
		Builtins.PopulateBuiltinScope(args);

		var compiler = new Compiler(args);
		compiler.Build();

		GoldenTestHelper.AssertCompilationSucceeded(compiler, "IR", Path.GetFileName(stiltPath), stiltPath);

		var file = compiler.Files[0];
		var ir = new IRGenerator(args, file);
		ir.GenerateIR();
		var actual = CompilerJsonSerializer.SerializeToJson(ir.Result.MainBlock, CompilerJsonSerializer.ExclusionPreset.Ast);

		var goldenPath = GoldenTestHelper.GetGoldenPath(stiltPath, GoldenSuffix);
		GoldenTestHelper.AssertOrUpdateGolden(actual, goldenPath, Path.GetFileName(stiltPath), stiltPath);
	}

	public static TheoryData<string> GetStiltFiles()
	{
		var data = new TheoryData<string>();
		if (!Directory.Exists(IrDir))
			return data;
		foreach (var path in Directory.EnumerateFiles(IrDir, "*.stilt"))
			data.Add(path);
		return data;
	}
}
