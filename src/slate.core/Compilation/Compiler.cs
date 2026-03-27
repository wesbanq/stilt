namespace stilt.Compilation
{
	public interface IDescriptable
	{
		string Name { get; }
	}

	public class Compiler
	{
		public ProgramArgs Args;
		public List<ObjectFile> Files = [];
		public Linker? Linker;
		public Dictionary<TimedEvents, Timer> Timers = [];

		public void WriteTimerReadout()
		{
			foreach (var timer in Timers)
			{
				Console.WriteLine(timer.Value.Time);
			}
			foreach (var file in Files)
			{
				Console.WriteLine($"{file.Filepath}:");
				foreach (var timer in file.Timers)
				{
					Console.WriteLine(timer.Value.Time);
				}
			}
		}

		public void PrintBuildErrors()
		{
			foreach (var file in Files)
			{
				if (file is ParsedFile parsedFile)
					parsedFile.Errors.ForEach(e => e.Print());
			}
			Linker?.Errors.ForEach(e => e.Print());
		}

		private static ObjectFile? SearchForObjectFile(string filepath)
		{
			var objPath = Path.ChangeExtension(filepath, Program.ObjectFileExtension);
			if (!File.Exists(objPath))
				return null;
			try
			{
				var objFileText = new FileText(objPath);
				var deserialized = ObjectFile.Deserialize(objFileText.Text);
				if (deserialized is not null
					&& deserialized.CompilerVersion == Program.CompilerVersion)
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

		private static void GenerateObjectFile(string filepath, ObjectFile objectFile)
		{
			File.WriteAllText(Path.ChangeExtension(filepath, Program.ObjectFileExtension), objectFile.Serialize());
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
			//ParseGenericStatemtent in Parser
			//scope-based parser rewrite

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
			Linker = new Linker(
				Args,
                [.. Files.Select(f => f.ParserResult!.RootScope)],
                [.. Files.Select(f => f.ParserResult!.Statements)]
            );
			Timers[TimedEvents.Linking].Run(() => Linker.Link());

			Timers[TimedEvents.Compilation].StopTimer();
		}

		public Compiler(ProgramArgs args)
		{
			Args = args;
		}
	}
}
