using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using stilt.IR;
using System.Globalization;
using NuArgs;
using stilt.CodeGen;
using stilt.Compilation;

namespace stilt.Cli
{
	public enum ProgramCommand
	{
		None = 0,
		[Command<ProgramOption>("build", "Builds the given file.", [ProgramOption.InputFile])]
		Build,
		[Command<ProgramOption>("token", "Does only preprocessing and lexing", [ProgramOption.InputFile])]
		Tokenize,
		[Command<ProgramOption>("preprocess", "Does only preprocessing.", [ProgramOption.InputFile])]
		Preprocess,
		[Command<ProgramOption>("tree", "Does only preprocessing, lexing, and parsing.", [ProgramOption.InputFile])]
		Tree,
		[Command<ProgramOption>("ir", "Does only preprocessing, lexing, parsing, and IR generation.", [ProgramOption.InputFile])]
		IR,
	}

	public enum ProgramOption
	{
		None = 0,
		[Option("d", OptionType.SingleValue, "Set debug level (for debugging)")]
		DebugLvl,
		[Option(["i", "input"], OptionType.SingleValue, "Sets the main code filepath to use")]
		InputFile,
		[Option("t", OptionType.Flag, "Crash the program instead of printing the error (for debugging)")]
		Throw,
		[Option(["x", "ex"], OptionType.Flag, "Additional info in dumps")]
		Expanded,
		[Option(["no-time"], OptionType.Flag, "Don't show total compilation time.")]
		NoTime,
		[Option("j", OptionType.SingleValue, "Dump the output to a JSON file (for debugging)")]
		JsonDumpFilepath,
		[Option(["v", "mc-version"], OptionType.SingleValue, "Set the target Minecraft version to compile to.")]
		TargetMCVersion,
		[Option(["o", "output"], OptionType.SingleValue, "Set the output filepath")]
		OutputFilepath,
		[Option(["n", "no-obj"], OptionType.Flag, "Don't create an object file")]
		NoObjectFile,
		[Option(["r", "regen-obj", "regen-object-file"], OptionType.Flag, "Regenerate the object file")]
		RegenObjectFile,
		[Option(["no-builtin", "no-std"], OptionType.Flag, "Don't load the builtins library")]
		NoBuiltin,
	}

	[NuArgsExtra<ProgramCommand>(aboutText: """
		Stilt is a language for Minecraft.
		It is a statically typed, compiled language that is designed to be easy to learn and use.
		It is still in early development and is not yet ready for use.
	""", unixStyle: true)]
	public class ConsoleArgs : Args<ProgramOption, ProgramCommand>
	{
		[OptionTarget<ProgramOption>(ProgramOption.DebugLvl)]
		public int DebugLevel;
		[OptionTarget<ProgramOption>(ProgramOption.InputFile, nameof(BuiltInConverters.FilesVerifyPaths))]
		public string? MainCodeFilepath = null;
		[OptionTarget<ProgramOption>(ProgramOption.Throw)]
		public bool Throw = false;
		[OptionTarget<ProgramOption>(ProgramOption.Expanded)]
		public bool ExpandedDump = false;
		[OptionTarget<ProgramOption>(ProgramOption.NoTime)]
		public bool NoTime = false;
		[OptionTarget<ProgramOption>(ProgramOption.JsonDumpFilepath, nameof(BuiltInConverters.File))]
		public string? JsonDumpFilepath = null;
		[OptionTarget<ProgramOption>(ProgramOption.TargetMCVersion, nameof(ConvertMCVersion))]
		public MCVersion TargetVersion = MCVersion.LatestJava;
		[OptionTarget<ProgramOption>(ProgramOption.OutputFilepath, nameof(BuiltInConverters.File))]
		public string? OutputFilepath = null;
		[OptionTarget<ProgramOption>(ProgramOption.NoObjectFile)]
		public bool NoObjectFile = false;
		[OptionTarget<ProgramOption>(ProgramOption.RegenObjectFile)]
		public bool RegenObjectFile = false;
		[OptionTarget<ProgramOption>(ProgramOption.NoBuiltin)]
		public bool NoStd = false;

		public ProgramArgs ToCompilerArgs()
		{
			return new ProgramArgs(
				DebugLevel,
				MainCodeFilepath,
				Throw,
				ExpandedDump,
				NoTime,
				JsonDumpFilepath,
				TargetVersion,
				OutputFilepath,
				NoObjectFile,
				RegenObjectFile,
				NoStd,
				Program.OutputFileExtension,
				Program.CodeFileExtension,
				Program.ObjectFileExtension
			);
		}

		private static MCVersion ConvertMCVersion(string[] arg) {
			var ver = MCVersion.ParseMCVersion(arg[0]);
			if (ver is null)
				throw new ArgumentParsingException(ArgumentParsingExceptionType.InvalidOptionValue, "-v", arg[0]);
			return ver;
		}

		public ConsoleArgs()
		{
			if (MainCodeFilepath is not null)
				OutputFilepath ??= Path.ChangeExtension(MainCodeFilepath, ".zip");
		}
	}

	/// <summary>
	/// Command-line entry point. Parses arguments into <see cref="ProgramArgs"/>, sets up the builtins, and dispatches
	/// on the chosen <see cref="ProgramCommand"/>. The commands expose the pipeline at increasing depth — <c>preprocess</c>,
	/// <c>token</c> (lex), <c>tree</c> (parse), <c>ir</c>, and full <c>build</c> — and can dump each stage's output to JSON for inspection.
	/// </summary>
	internal static class Program
	{
		public static readonly string OutputFileExtension = ".zip";
		public static readonly string CodeFileExtension = ".stilt";
		public static readonly string ObjectFileExtension = CodeFileExtension + ".o";

		private class ReferenceEqualityComparer : IEqualityComparer<object>
		{
			public static readonly ReferenceEqualityComparer Instance = new();

			public new bool Equals(object? x, object? y)
			{
				return ReferenceEquals(x, y);
			}

			public int GetHashCode(object obj)
			{
				return RuntimeHelpers.GetHashCode(obj);
			}
		}

		private static void WriteObjectToJson(object? obj, string filepath, CompilerJsonSerializer.ExclusionPreset preset = CompilerJsonSerializer.ExclusionPreset.None)
		{
			File.WriteAllText(filepath, CompilerJsonSerializer.SerializeToJson(obj, preset));
		}

		private static void PrintBuildErrors(Compiler compiler)
		{
			foreach (var error in compiler.Errors)
			{
				error.Print();
			}
		}

		private static void PrintTimerReadout(Compiler compiler)
		{
			foreach (var timer in compiler.Timers)
			{
				Console.WriteLine(timer.Value.Time);
			}
		}

		/// <summary>Parses CLI args, populates the builtin scope, then runs the selected command — building/dumping the program to the depth that command implies.</summary>
		static int Main(string[] rawArgs)
		{
			var consoleArgs = new ConsoleArgs();
			consoleArgs.ParseArgsOrExit(rawArgs);

			var args = consoleArgs.ToCompilerArgs();
			Builtins.PopulateBuiltinScope(args);

			switch (consoleArgs.Command)
			{
				case ProgramCommand.Build:
				{
					var compiler = new Compiler(args);
					compiler.Build();

					Console.WriteLine();
					PrintBuildErrors(compiler);
					Console.WriteLine();
					if (!args.NoTime)
						PrintTimerReadout(compiler);

					if (args.JsonDumpFilepath is not null)
						WriteObjectToJson(compiler.Files.Select(p => p.ParserResult!.Statements).ToList(), Path.Combine(args.JsonDumpFilepath, "parser_statements.json"), CompilerJsonSerializer.ExclusionPreset.Ast);
					if (args.JsonDumpFilepath is not null && compiler.Files.OfType<ParsedFile>().Any(f => f.Lexer is not null))
						WriteObjectToJson(compiler.Files.OfType<ParsedFile>().Where(f => f.Lexer is not null).Select(f => f.Lexer!.Tokens).ToList(), Path.Combine(args.JsonDumpFilepath, "lexer_tokens.json"), CompilerJsonSerializer.ExclusionPreset.Lexer);
					if (args.JsonDumpFilepath is not null)
						WriteObjectToJson(compiler.Files.Select(f => f.ParserResult!.CompilationIssues).ToList(), Path.Combine(args.JsonDumpFilepath, "parser_compilation_issues.json"));

					break;
				}
				case ProgramCommand.Tokenize:
					var file = (ParsedFile)Compiler.ParseFile(args, new FileText(args.MainCodeFilepath!));
					var lex = file.Lexer!;
					Token t = lex.CurrentToken;
					do Console.WriteLine($"{lex.CurrentPos}: {t.Which}"); while ((t = lex.Next()).Which != TokenType.EOF);

					if (args.JsonDumpFilepath is not null)
						WriteObjectToJson(lex.Tokens, Path.Combine(args.JsonDumpFilepath, "lexer_tokens.json"), CompilerJsonSerializer.ExclusionPreset.Lexer);
						
					break;
				case ProgramCommand.Preprocess:
				{
					var code = File.ReadAllText(args.MainCodeFilepath!);
					Console.Write(Lexer.Preprocess(code));
					break;
				}
				case ProgramCommand.Tree:
				{
					var comp = new Compiler(args);
					comp.Build();
					
					foreach (var filecomp in comp.Files)
					{
						Console.WriteLine($"Module: {filecomp.Filepath}");
						foreach (var stmt in filecomp.ParserResult!.Statements.ToArray())
						{
							Utils.Dump(stmt, expanded: args.ExpandedDump);
						}
					}

					Console.WriteLine();
					PrintBuildErrors(comp);
					Console.WriteLine();
					if (!args.NoTime)
						PrintTimerReadout(comp);

					if (args.JsonDumpFilepath is not null)
						WriteObjectToJson(comp.Files.Select(f => f.ParserResult!.Statements).ToList(), Path.Combine(args.JsonDumpFilepath, "parser_statements.json"), CompilerJsonSerializer.ExclusionPreset.Ast);
					
					break;
				}
				case ProgramCommand.IR:
				{
					var compiler = new Compiler(args);
					compiler.Build();
					
					if (compiler.Files.Count == 0)
						throw new Exception("No files built");
					
					var fileir = compiler.Files.First();
					var ir = new IRGenerator(args, fileir);
					ir.GenerateIR();
					Utils.Dump(ir.Result.MainBlock, expanded: args.ExpandedDump);

					if (args.JsonDumpFilepath is not null)
						WriteObjectToJson(ir.Result.MainBlock, Path.Combine(args.JsonDumpFilepath, "ir_main_block.json"), CompilerJsonSerializer.ExclusionPreset.Ast);

					break;
				}
			}

			return 0;
		}
	}
}
