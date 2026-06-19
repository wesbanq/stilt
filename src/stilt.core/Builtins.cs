using System.Reflection;
using Microsoft.Extensions.FileProviders;

namespace stilt
{
	/// <summary>
	/// The language's built-in types (<see cref="Num"/>, <see cref="String"/>, <see cref="Bool"/>, the numeric tower,
	/// …) and the <see cref="BuiltinScope"/> that holds them. That scope is the ultimate parent of every file's root
	/// scope, so these names resolve everywhere. <see cref="PopulateBuiltinScope"/> must run once before compiling;
	/// unless disabled, it also folds in the embedded standard library.
	/// </summary>
	public static class Builtins
	{
		public static Scope BuiltinScope;

		public static readonly TypeSymbol Infer = new("infer", Symbol.BuiltinSource);
		public static readonly TypeSymbol None = new("none", Symbol.BuiltinSource);
		public static readonly TypeSymbol Bool = new("bool", Symbol.BuiltinSource);
		public static readonly TypeSymbol Num = new("num", Symbol.BuiltinSource);
		public static readonly TypeSymbol String = new("string", Symbol.BuiltinSource);
		public static readonly TypeSymbol Proto = new("proto", Symbol.BuiltinSource);
		public static readonly TypeSymbol Trait = new("trait", Symbol.BuiltinSource);
		public static readonly TypeSymbol Decorator = new("Decorator", Symbol.BuiltinSource);
		public static readonly TypeSymbol TaggedString = new("TaggedString", Symbol.BuiltinSource);
		public static readonly TypeSymbol Array = new("array", Symbol.BuiltinSource, argumentCount: 1);
		public static readonly TypeSymbol Table = new("table", Symbol.BuiltinSource);
		public static readonly TypeSymbol Callable = new("Callable", Symbol.BuiltinSource, argumentCount: 2);
		public static readonly TypeSymbol Module = new("Module", Symbol.BuiltinSource);
		public static readonly TypeSymbol Object = new("Object", Symbol.BuiltinSource);
		public static readonly TypeSymbol Whole = new("whole", Symbol.BuiltinSource, inherits: [Num]);
		public static readonly TypeSymbol Fractional = new("fract", Symbol.BuiltinSource, inherits: [Num]);
		public static readonly TypeSymbol Byte = new("byte", Symbol.BuiltinSource, inherits: [Whole]);
		public static readonly TypeSymbol Short = new("short", Symbol.BuiltinSource, inherits: [Whole]);
		public static readonly TypeSymbol Int = new("int", Symbol.BuiltinSource, inherits : [Whole]);
		public static readonly TypeSymbol Long = new("long", Symbol.BuiltinSource, inherits : [Whole]);
		public static readonly TypeSymbol Float = new("float", Symbol.BuiltinSource, inherits : [Fractional]);
		public static readonly TypeSymbol Double = new("double", Symbol.BuiltinSource, inherits : [Fractional]);

		/// <summary>Builds <see cref="BuiltinScope"/> by reflecting every builtin <see cref="TypeSymbol"/> field into it, then (unless <c>--no-std</c>) adding the standard-library symbols. Call once at startup.</summary>
		public static void PopulateBuiltinScope(ProgramArgs args)
		{
			BuiltinScope = new();
			foreach (var prop in typeof(Builtins).GetFields())
			{
				var value = prop.GetValue(null);
				if (value is Symbol symbol)
					BuiltinScope.AddSymbol(symbol);
			}

			if (!args.NoStd)
				BuiltinScope.AddSymbols(ImportBuiltins(args).Symbols);

			if (args.DebugLevel >= 1)
			{
				Console.WriteLine("BuiltinScope:");
				Utils.Dump(BuiltinScope, expanded: args.ExpandedDump);
				Console.WriteLine();
			}
		}

		/// <summary>Compiles the standard-library <c>.stilt</c> files embedded in the assembly and returns a scope holding all their top-level symbols.</summary>
		public static Scope ImportBuiltins(ProgramArgs args)
		{
			Scope stdLibScope = new(BuiltinScope);

			var provider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "stilt.Builtins");
			IDirectoryContents folderContents = provider.GetDirectoryContents("");

			if (!folderContents.Exists)
			{
				throw new Exception("Could not find the standard library!");
			}

			List<ObjectFile> files = [];

			foreach (IFileInfo file in folderContents)
			{
				if (file.IsDirectory || !file.Name.EndsWith(args.CodeFileExtension)) continue;

				using Stream stream = file.CreateReadStream();
				using StreamReader reader = new StreamReader(stream);
				string content = reader.ReadToEnd();

				files.Add(Compiler.ParseFile(args, new FileText(Path.Join(Symbol.BuiltinSource, file.Name), content)));
			}

			files.ForEach(f => stdLibScope.AddSymbols(f.ParserResult!.RootScope.Symbols));

			return stdLibScope;
		}
	}
}
