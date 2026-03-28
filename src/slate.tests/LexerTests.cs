using slate;
using slate.Compilation;
using Xunit;

namespace slate.Tests;

public class LexerGoldenTests
{
	public static string TestDataRoot => GoldenTestHelper.GetTestDataRoot();
	public static string LexerDir => Path.Combine(TestDataRoot, "Lexer");
	private const string GoldenSuffix = ".tokens.json";

	[Theory]
	[MemberData(nameof(GetSlateFiles))]
	public void Lexer_matches_golden(string slatePath)
	{
		var args = new ProgramArgs { MainCodeFilepath = slatePath, NoStd = true };
		var file = new FileText(slatePath);
		var lexer = new Lexer(args, file.Filepath, file);
		lexer.Lex();

		var actual = CompilerJsonSerializer.SerializeToJson(lexer.Tokens, CompilerJsonSerializer.ExclusionPreset.Tokens);
		var goldenPath = GoldenTestHelper.GetGoldenPath(slatePath, GoldenSuffix);
		GoldenTestHelper.AssertOrUpdateGolden(actual, goldenPath, Path.GetFileName(slatePath), slatePath);
	}

	public static TheoryData<string> GetSlateFiles()
	{
		var data = new TheoryData<string>();
		if (!Directory.Exists(LexerDir))
			return data;
		foreach (var path in Directory.EnumerateFiles(LexerDir, "*.slate").Order(StringComparer.Ordinal))
			data.Add(path);
		return data;
	}
}
