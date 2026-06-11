using stilt;
using stilt.Compilation;
using Xunit;

namespace stilt.Tests;

public class LexerGoldenTests
{
	public static string TestDataRoot => GoldenTestHelper.GetTestDataRoot();
	public static string LexerDir => Path.Combine(TestDataRoot, "Lexer");
	private const string GoldenSuffix = ".tokens.json";

	[Theory]
	[MemberData(nameof(GetStiltFiles))]
	public void Lexer_matches_golden(string stiltPath)
	{
		var args = new ProgramArgs { MainCodeFilepath = stiltPath, NoStd = true };
		var file = new FileText(stiltPath);
		var lexer = new Lexer(args, file.Filepath, file);
		lexer.Lex();

		var actual = CompilerJsonSerializer.SerializeToJson(lexer.Tokens, CompilerJsonSerializer.ExclusionPreset.Tokens);
		var goldenPath = GoldenTestHelper.GetGoldenPath(stiltPath, GoldenSuffix);
		GoldenTestHelper.AssertOrUpdateGolden(actual, goldenPath, Path.GetFileName(stiltPath), stiltPath);
	}

	public static TheoryData<string> GetStiltFiles()
	{
		var data = new TheoryData<string>();
		if (!Directory.Exists(LexerDir))
			return data;
		foreach (var path in Directory.EnumerateFiles(LexerDir, "*.stilt").Order(StringComparer.Ordinal))
			data.Add(path);
		return data;
	}
}
