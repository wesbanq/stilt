using slate.Compilation;
using slate.IR;
using Xunit;

namespace slate.Tests;

public class IrGoldenTests
{
	public static string TestDataRoot => GoldenTestHelper.GetTestDataRoot();
	public static string IrDir => Path.Combine(TestDataRoot, "Ir");
	private const string GoldenSuffix = ".ir.json";

	[Theory]
	[MemberData(nameof(GetSlateFiles))]
	public void Ir_matches_golden(string slatePath)
	{
		var args = new ProgramArgs { MainCodeFilepath = slatePath, NoStd = true };
		Builtins.PopulateBuiltinScope(args);

		var compiler = new Compiler(args);
		compiler.Build();

		GoldenTestHelper.AssertCompilationSucceeded(compiler, "IR", Path.GetFileName(slatePath), slatePath);

		var file = compiler.Files[0];
		var ir = new IRGenerator(args, file);
		ir.GenerateIR();
		var actual = CompilerJsonSerializer.SerializeToJson(ir.Result.MainBlock, CompilerJsonSerializer.ExclusionPreset.Ast);

		var goldenPath = GoldenTestHelper.GetGoldenPath(slatePath, GoldenSuffix);
		GoldenTestHelper.AssertOrUpdateGolden(actual, goldenPath, Path.GetFileName(slatePath), slatePath);
	}

	public static TheoryData<string> GetSlateFiles()
	{
		var data = new TheoryData<string>();
		if (!Directory.Exists(IrDir))
			return data;
		foreach (var path in Directory.EnumerateFiles(IrDir, "*.slate"))
			data.Add(path);
		return data;
	}
}
