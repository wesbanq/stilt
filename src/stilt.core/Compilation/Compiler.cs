namespace stilt.Compilation
{
	public interface IDescriptable
	{
		string Name { get; }
	}

	/// <summary>
	/// Immutable bag of settings for one compiler run, built from the CLI arguments
	/// (see <c>ConsoleArgs.ToCompilerArgs</c>). Passed by value through every stage so
	/// the lexer, parser, linker, and IR generator all see the same configuration.
	/// </summary>
	public readonly record struct ProgramArgs(
		int DebugLevel,
		string? MainCodeFilepath,
		bool Throw,
		bool ExpandedDump,
		bool NoTime,
		string? JsonDumpFilepath,
		MCVersion TargetVersion,
		string? OutputFilepath,
		bool NoObjectFile,
		bool RegenObjectFile,
		bool NoStd,
		string OutputFileExtension,
		string CodeFileExtension,
		string ObjectFileExtension,
		int TabSize = 4
	);

	/// <summary>
	/// Top-level driver of the compilation pipeline that turns Stilt source into a Minecraft
	/// datapack. A run flows through these stages, each implemented by its own class:
	/// <list type="number">
	/// <item>Preprocess + lex (<see cref="Lexer"/>): source text becomes a flat token list.</item>
	/// <item>Parse (<see cref="Parser"/>): tokens become an AST of <see cref="Stmt"/>/<see cref="Expr"/> nodes;
	///       each source file produces one <see cref="ParsedFile"/>.</item>
	/// <item>Link (<see cref="Linker"/>): names are resolved against scopes and <c>import</c>ed files are pulled in.</item>
	/// <item>IR generation (<see cref="IRGenerator"/>): the AST is lowered to a tree of instruction <see cref="Block"/>s.</item>
	/// <item>Code generation (<c>CodeGen</c>): IR becomes datapack commands — not yet implemented.</item>
	/// </list>
	/// <see cref="Build"/> runs stages 1–3; IR generation is driven separately (see the CLI's <c>ir</c> command).
	/// </summary>
	public class Compiler
	{
		public static readonly string CompilerVersion =
			System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
		public ProgramArgs Args;
		public Dictionary<TimedEvents, Timer> Timers = [];
		/// <summary>The main file plus every file reached transitively through imports; one per source file.</summary>
		public List<ObjectFile> Files = [];
		public Linker? Link;
		/// <summary>All diagnostics gathered across the run: per-file parse issues plus linker errors.</summary>
		public IEnumerable<CompilationMessage> Errors => Files.SelectMany(f => f is ParsedFile p ? p.Errors : []).Concat(Link?.Errors ?? []);

		private static ObjectFile? SearchForObjectFile(string filepath, string extension)
		{
			var objPath = Path.ChangeExtension(filepath, extension);
			if (!File.Exists(objPath))
				return null;
			try
			{
				var objFileText = new FileText(objPath);
				var deserialized = ObjectFile.Deserialize(objFileText.Text);
				if (deserialized is not null
					&& deserialized.CompilerVersion == CompilerVersion)
				{
					// TextChecksum is the hash of the source file; validate against source, not object file
					var sourceFile = new FileText(filepath);
					if (deserialized.TextChecksum == sourceFile.GetSHA256Hash())
						return deserialized;
				}
				return null;
			}
			catch (Exception e)
			{
				Console.WriteLine($"Error searching for object file: {e.Message}");
				return null;
			}
		}

		private static void GenerateObjectFile(string filepath, string extension, ObjectFile objectFile)
		{
			File.WriteAllText(Path.ChangeExtension(filepath, extension), objectFile.Serialize());
		}

		/// <summary>Loads and parses the file at <paramref name="filepath"/> into a <see cref="ParsedFile"/>.</summary>
		public static ObjectFile ParseFile(ProgramArgs args, string filepath)
		{
			// dont use ObjectFile for now
			ObjectFile? objectFile = null;
			// if (!args.NoObjectFile && !args.RegenObjectFile)
			// 	objectFile = SearchForObjectFile(filepath);

			if (objectFile is not null)
			{
				// Console.WriteLine("Using obj file");
				objectFile.Filepath = filepath;
				return objectFile;
			}
			else
			{
				var filetext = new FileText(filepath);
				var parsedfile = ParseFile(args, filetext);

				// if (!args.NoObjectFile
				// 	&& (objectFile is null || args.RegenObjectFile
				// 		|| objectFile.InterfaceChecksum != file.InterfaceChecksum
				// 		|| objectFile.TextChecksum != file.TextChecksum))
				// {
				// 	GenerateObjectFile(filepath, new ObjectFile(file.TextChecksum, file.InterfaceChecksum, file.IR.Result, file.Result!));
				// }

				return parsedfile;
			}	
		}

		/// <summary>Lexes and parses already-loaded source text into a fresh <see cref="ParsedFile"/> (stages 1–2 for a single file).</summary>
		public static ObjectFile ParseFile(ProgramArgs args, FileText filetext)
		{
			var file = new ParsedFile(filetext);
			file.Parse(args);
			return file;
		}

		/// <summary>
		/// Runs the front end of the pipeline: parses the main file, and if it has no errors, links it
		/// (which resolves names and recursively parses imports). Stops early on parse errors. Stage timings
		/// are recorded in <see cref="Timers"/>. IR generation and code generation are not run here.
		/// </summary>
		public void Build()
		{
			//TODO
			//remove recursion from ParseExpr
			//multiline exprs
			//evaluate constant values at compile time
			//virtual filerange and error reports for them
			//object file deserialization
			//separate ParserResult from Parser
			//add ability touse functions before theyre defined

			Timers.Add(TimedEvents.Compilation, new Timer("Compilation"));
			Timers[TimedEvents.Compilation].StartTimer();

			Timers[TimedEvents.Compilation].Run(() =>
			{
				var file = ParseFile(Args, Args.MainCodeFilepath!);
				Files.Add(file);
			});

			if (Files.OfType<ParsedFile>().Any(f => f.HasErrors))
			{
				Timers[TimedEvents.Compilation].StopTimer();
				return;
			}

			Timers.Add(TimedEvents.Linking, new Timer("Linking"));
			Link = new Linker(
				Args,
                [.. Files.Select(f => f.ParserResult!.RootScope)],
                [.. Files.Select(f => f.ParserResult!.Statements)]
            );
			Timers[TimedEvents.Linking].Run(() => Link.Link());

			Timers[TimedEvents.Compilation].StopTimer();
		}

		public Compiler(ProgramArgs args)
		{
			Args = args;
		}
	}
}
