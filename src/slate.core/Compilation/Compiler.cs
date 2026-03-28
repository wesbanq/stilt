namespace slate.Compilation
{
	public interface IDescriptable
	{
		string Name { get; }
	}

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

	public class Compiler
	{
		public static readonly string CompilerVersion = 
			System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
		public ProgramArgs Args;
		public Dictionary<TimedEvents, Timer> Timers = [];
		public List<ObjectFile> Files = [];
		public Linker? Link;
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

		public static ObjectFile ParseFile(ProgramArgs args, FileText filetext)
		{
			var file = new ParsedFile(filetext);
			file.Parse(args);
			return file;
		}

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
