using System.Reflection;
using Microsoft.Extensions.FileProviders;

namespace stilt
{
	public static class Builtins
	{
		public static Scope BuiltinScope;

		public static readonly TypeSymbol Any = new("Any", Symbol.BuiltinSource);
		public static readonly TypeSymbol None = new("None", Symbol.BuiltinSource);
		public static readonly TypeSymbol Bool = new("Bool", Symbol.BuiltinSource);
		public static readonly TypeSymbol Num = new("Num", Symbol.BuiltinSource);
		public static readonly TypeSymbol String = new("String", Symbol.BuiltinSource);
		public static readonly TypeSymbol TaggedString = new("TaggedString", Symbol.BuiltinSource);
		public static readonly TypeSymbol UUID = new("UUID", Symbol.BuiltinSource);
		public static readonly TypeSymbol NBT = new("NBT", Symbol.BuiltinSource);
		public static readonly TypeSymbol Attribute = new("Attribute", Symbol.BuiltinSource);
		public static readonly TypeSymbol Tag = new("Tag", Symbol.BuiltinSource);
		public static readonly TypeSymbol Module = new("Module", Symbol.BuiltinSource);
		public static readonly TypeSymbol Decorator = new("Decorator", Symbol.BuiltinSource);
		public static readonly TypeSymbol Trait = new("Trait", Symbol.BuiltinSource);
		public static readonly TypeSymbol Array = new("Array", Symbol.BuiltinSource, argumentCount: 1);
		public static readonly TypeSymbol Reference = new("Ref", Symbol.BuiltinSource, argumentCount: 1);
		public static readonly TypeSymbol Table = new("Table", Symbol.BuiltinSource, argumentCount: 1);
		public static readonly TypeSymbol Generator = new("Generator", Symbol.BuiltinSource, argumentCount: 2);
		public static readonly TypeSymbol Callable = new("Callable", Symbol.BuiltinSource, argumentCount: 2);

		public static readonly VarSymbol IgnoreWarning = new("IgnoreWarning", Symbol.BuiltinSource, Decorator);
		public static readonly VarSymbol PrivateByDefault = new("PrivateByDefault", Symbol.BuiltinSource, Decorator);
		public static readonly VarSymbol ExplicitByDefault = new("ExplicitByDefault", Symbol.BuiltinSource, Decorator);

		public static readonly TypeSymbol Whole = new("Whole", Symbol.BuiltinSource, inherits: Num);
		public static readonly TypeSymbol Fractional = new("Fractional", Symbol.BuiltinSource, inherits: Num);
		public static readonly TypeSymbol Byte = new("Byte", Symbol.BuiltinSource, inherits: Whole);
		public static readonly TypeSymbol Short = new("Short", Symbol.BuiltinSource, inherits: Whole);
		public static readonly TypeSymbol Int = new("Int", Symbol.BuiltinSource, inherits : Whole);
		public static readonly TypeSymbol Long = new("Long", Symbol.BuiltinSource, inherits : Whole);
		public static readonly TypeSymbol Float = new("Float", Symbol.BuiltinSource, inherits : Fractional);
		public static readonly TypeSymbol Double = new("Double", Symbol.BuiltinSource, inherits : Fractional);

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
