using System.Reflection;
using Microsoft.Extensions.FileProviders;

namespace slate
{
	public static class Builtins
	{
		public static Scope BuiltinScope;

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
		public static readonly TypeSymbol Whole = new("whole", Symbol.BuiltinSource, inherits: Num);
		public static readonly TypeSymbol Fractional = new("fract", Symbol.BuiltinSource, inherits: Num);
		public static readonly TypeSymbol Byte = new("byte", Symbol.BuiltinSource, inherits: Whole);
		public static readonly TypeSymbol Short = new("short", Symbol.BuiltinSource, inherits: Whole);
		public static readonly TypeSymbol Int = new("int", Symbol.BuiltinSource, inherits : Whole);
		public static readonly TypeSymbol Long = new("long", Symbol.BuiltinSource, inherits : Whole);
		public static readonly TypeSymbol Float = new("float", Symbol.BuiltinSource, inherits : Fractional);
		public static readonly TypeSymbol Double = new("double", Symbol.BuiltinSource, inherits : Fractional);

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

		public static Scope ImportBuiltins(ProgramArgs args)
		{
			Scope stdLibScope = new(BuiltinScope);

			var provider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "slate.Builtins");
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
