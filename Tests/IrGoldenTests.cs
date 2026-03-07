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

		if (compiler.Files.Count == 0)
			Assert.Fail($"No files built for {stiltPath}");
		if (compiler.Files.OfType<ParsedFile>().Any(f => f.HasErrors))
			Assert.Fail($"Build had errors for {stiltPath}. Fix the source or run with -p:RegenerateGoldens=true after fixing.");

		var file = compiler.Files[0];
		var ir = new IRGenerator(args, file);
		ir.GenerateIR();
		var actual = CompilerJsonSerializer.SerializeToJson(ir.Result.MainBlock, CompilerJsonSerializer.ExclusionPreset.Ast);

		var goldenPath = GoldenTestHelper.GetGoldenPath(stiltPath, GoldenSuffix);
		GoldenTestHelper.AssertOrUpdateGolden(actual, goldenPath, Path.GetFileName(stiltPath));
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
