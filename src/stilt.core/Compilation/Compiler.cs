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
	public class Compiler(ProgramArgs args)
    {
		public static readonly string CompilerVersion =
			System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
		public ProgramArgs Args = args;
		public Dictionary<TimedEvents, Timer> Timers = [];
		/// <summary>The main file plus every file reached transitively through imports; one per source file.</summary>
		public Dictionary<string, ObjectFile> Files = [];
		public Linker? Link;
		/// <summary>All diagnostics gathered across the run: per-file parse issues plus linker errors.</summary>
		public List<CompilationMessage> Errors => Files.Values.SelectMany(f => f.ParserResult!.CompilationIssues).Concat(Link?.Errors ?? []).ToList();
		public bool HasErrors => Errors.Any(e => e.Severity >= ErrorSeverity.Error);

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

		private List<T> ScanStmt<T>(IEnumerable<Stmt?> stmts)
			where T : Stmt
		{
			var list = new List<T>();
			foreach (var stmt in stmts)
			{
				if (stmt is null)
					continue;

				switch (stmt)
				{
					case T t:
					{
						list.Add(t);
						break;
					}
					case CompoundStmt compound:
					{
						list.AddRange(ScanStmt<T>(compound.Statements));
						break;
					}
					case IfStmt ifStmt:
					{
						list.AddRange(ScanStmt<T>([ifStmt.NextIf, ifStmt.NextElse]));
						break;
					}
					case ForLoopStmt forLoop:
					{
						list.AddRange(ScanStmt<T>([forLoop.LoopVariable, forLoop.Body]));
						break;
					}
					case ForeachLoopStmt foreachLoop:
					{
						list.AddRange(ScanStmt<T>([foreachLoop.LoopVariable, foreachLoop.Body]));
						break;
					}
					case LoopStmt loop:
					{
						list.AddRange(ScanStmt<T>([loop.Body]));
						break;
					}
					case VarDeclStmt:
					{
						break;
					}
					case DeclStmt decl:
					{
						list.AddRange(ScanStmt<T>([decl.Value]));
						break;
					}
				}
			}
			return list;
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
			//object file deserialization
			//add ability touse functions before theyre defined

			Timers.Add(TimedEvents.Compilation, new Timer("Compilation"));
			Timers[TimedEvents.Compilation].StartTimer();

			Queue<string> parseQueue = new([Args.MainCodeFilepath!]);
			Timers.Add(TimedEvents.Parsing, new Timer("Parsing"));
			Timers[TimedEvents.Parsing].Run(() =>
			{
				while (parseQueue.Count > 0)
				{
					var filepath = parseQueue.Dequeue();
					var file = ParseFile(Args, filepath);
					Files.Add(filepath, file);

					var importStmts = ScanStmt<ImportStmt>(file.ParserResult!.Statements);
					foreach (var importStmt in importStmts)
					{
						var importPath = Path.GetFullPath(importStmt.Filepath[1..^1]);

						if (!File.Exists(importPath))
                            Errors.Add(new ImportError(importStmt.InnerRange, importPath));

						if (!Files.ContainsKey(importPath))
							parseQueue.Enqueue(importPath);
					}

					file.ParserResult!.ImportedFiles.ForEach(f => parseQueue.Enqueue(f));
				}
			});

			if (HasErrors)
			{
				Timers[TimedEvents.Compilation].StopTimer();
				return;
			}

			Link = new Linker(Args, Files);
			Timers.Add(TimedEvents.Linking, new Timer("Linking"));
			Timers[TimedEvents.Linking].Run(() => Link.Link());

			Timers[TimedEvents.Compilation].StopTimer();
		}
    }
}
