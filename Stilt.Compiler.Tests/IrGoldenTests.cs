using stilt.IR;

namespace Stilt.Compiler.Tests;

public class IrGoldenTests
{
    public static IEnumerable<object[]> IrFixtures()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var dir = Path.Combine(root, "Fixtures", "Ir");
        if (!Directory.Exists(dir))
            yield break;

        foreach (var file in Directory.GetFiles(dir, $"*{stilt.Program.CodeFileExtension}", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            yield return new object[] { name };
        }
    }

    [Theory]
    [MemberData(nameof(IrFixtures))]
    public void IrMatchesGolden(string fixtureName)
    {
        var irResult = TestCompilerHarness.GenerateIr(fixtureName);
        var json = JsonTestSerializer.SerializeIrMain(irResult.MainBlock);
        var goldenRelPath = Path.Combine("Ir", $"{fixtureName}.ir.json");

        GoldenFileAssertions.AssertMatchesGolden(json, goldenRelPath);
    }

    [Theory]
    [MemberData(nameof(IrFixtures))]
    public void IrRespectsInvariants(string fixtureName)
    {
        var irResult = TestCompilerHarness.GenerateIr(fixtureName);
        var main = irResult.MainBlock;

        // Ensure all instructions have opcodes set and valid operand lists.
        foreach (var instr in main.Instructions)
        {
            Assert.NotNull(instr);
            Assert.IsType<Instruction>(instr.Opcode);
            Assert.NotNull(instr.Operands);
        }
    }
}

