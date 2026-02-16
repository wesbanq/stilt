using Newtonsoft.Json.Serialization;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using stilt.IR;
using System.Globalization;
using NuArgs;

namespace stilt
{
	public enum ProgramCommand
	{
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
		[Option("i", OptionType.MultipleValues, "Sets the main code filepath to use")]
		InputFile,
		[Option("t", OptionType.Flag, "Crash the program instead of printing the error (for debugging)")]
		Throw,
		[Option("ex", OptionType.Flag, "Additional info in dumps")]
		Expanded,
		[Option("nt", OptionType.Flag, "Don't show total compilation time.")]
		NoTime,
		[Option("j", OptionType.SingleValue, "Dump the output to a JSON file (for debugging)")]
		JsonDumpFilepath,
		[Option("v", OptionType.SingleValue, "Set the target version of the language")]
		TargetMCVersion,
		[Option("o", OptionType.SingleValue, "Set the output filepath")]
		OutputFilepath,
		[Option(["no", "no-obj"], OptionType.Flag, "Don't create an object file")]
		NoObjectFile,
		[Option(["ro", "regen-obj"], OptionType.Flag, "Regenerate the object file")]
		RegenObjectFile,
	}

	public class ProgramArgs : Args<ProgramOption, ProgramCommand>
	{
		[OptionTarget<ProgramOption>(ProgramOption.DebugLvl)]
		public int DebugLevel;
		[OptionTarget<ProgramOption>(ProgramOption.InputFile, nameof(BuiltInConverters.FilesVerifyPaths))]
		public string[]? MainCodeFilepaths = null;
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

		private static MCVersion ConvertMCVersion(string[] arg) {
			var ver = MCVersion.ParseMCVersion(arg[0]);
			if (ver is null)
				throw new ArgumentParsingException(ArgumentParsingExceptionType.InvalidOptionValue, "-v", arg[0]);
			return ver;
		}

		public ProgramArgs()
		{
			if (MainCodeFilepaths is not null && MainCodeFilepaths.Length > 0)
				OutputFilepath ??= Path.ChangeExtension(MainCodeFilepaths[0], ".zip");
		}
	}

	internal static class Program
	{
		public static readonly string CompilerVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString()!;
		public static readonly string OutputFileExtension = ".zip";
		public static readonly string CodeFileExtension = ".stilt";
		public static readonly string ObjectFileExtension = CodeFileExtension + ".o";

		public static A? GetAttributeFromEnum<T, A>(T value)
				where T : Enum
				where A : Attribute
		{
			var a = typeof(T).GetField(value.ToString())?.GetCustomAttributes<A>()?.ToArray();
			if (a is not null && a.Length > 0)
				return a.First();
			else
				return null;
		}

		public static A[]? GetAttributesFromEnum<T, A>(T value)
			where T : Enum
			where A : Attribute
		{
			var a = typeof(T).GetField(value.ToString())?.GetCustomAttributes<A>()?.ToArray();
			if (a is not null && a.Length > 0)
				return a;
			else
				return null;
		}

		public static List<A?> GetAttributesFromType<A>(Type t)
			where A : Attribute
		{
			List<A?> res = [];
			foreach (var field in t.GetFields())
			{
				res.Add(field.GetCustomAttribute<A>());
			}
			return res;
		}

		public static T GetEnumFromDescription<T, A>(string toFind)
			where T : Enum
			where A : Attribute, IDescriptable
		{
			foreach (var field in typeof(T).GetFields())
			{
				if (field.GetCustomAttribute<A>()?.Name == toFind)
					return (T)field.GetValue(null)!;
			}
			//return default;
			throw new ArgumentException($"Enum value with description '{toFind}' not found.");
		}

		public static A? GetAttrFromDescription<T, A>(string toFind)
			where T : Enum
			where A : Attribute, IDescriptable
		{
			foreach (var field in typeof(T).GetFields())
			{
				if (field.GetCustomAttribute<A>()?.Name == toFind)
					return field.GetCustomAttribute<A>();
			}
			return null;
		}

		public static void Dump(object? obj, int l = 0, bool expanded = false)
		{
			var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
			Dump(obj, l, expanded, visited);
		}

		private static void Dump(object? obj, int l, bool expanded, HashSet<object> visited)
		{
			if (obj is null)
			{
				Console.WriteLine("null");
				return;
			}

			if (l == 0)
				Console.WriteLine();

			var type = obj.GetType();
			var indent = new string('\t', l);

			// Check for circular references (only for reference types) - do this before handling enumerables
			if (!type.IsValueType)
			{
				if (visited.Contains(obj))
				{
					Console.WriteLine($"{indent}{type.Name} <CYCLE>");
					return;
				}
				visited.Add(obj);
			}

			// Handle enumerable collections (arrays, lists, etc.) - but not strings
			if (obj is IEnumerable enumerable && obj is not string)
			{
				Console.WriteLine($"{indent}{type.Name}");
				int index = 0;
				foreach (var item in enumerable)
				{
					Console.WriteLine($"{indent}\t[{index}]:");
					Dump(item, l + 2, expanded, visited);
					index++;
				}
				return;
			}

			Console.WriteLine($"{indent}{type.Name}");

			// Dump properties
			foreach (var prop in type.GetProperties())
			{
				try
				{
					DumpMember(prop.Name, prop.GetValue(obj), l, expanded, visited);
				}
				catch { }
			}

			// Dump fields
			foreach (var field in type.GetFields())
			{
				try
				{
					DumpMember(field.Name, field.GetValue(obj), l, expanded, visited);
				}
				catch { }
			}
		}

		private static void DumpMember(string name, object? value, int level, bool expanded, HashSet<object> visited)
		{
			var indent = new string('\t', level + 1);

			if (value is null)
			{
				Console.WriteLine($"{indent}{name} = null");
				return;
			}

			var valueType = value.GetType();

			// Handle enumerable collections (arrays, lists, linked lists, etc.) - but not strings
			if (value is IEnumerable enumerable && value is not string)
			{
				Console.WriteLine($"{indent}{name}:");
				int index = 0;
				foreach (var item in enumerable)
				{
					Console.WriteLine($"{indent}\t[{index}]:");
					Dump(item, level + 2, expanded, visited);
					index++;
				}
				return;
			}

			// Handle simple types (primitives, enums, strings)
			if (valueType.IsPrimitive || valueType.IsEnum || value is string)
			{
				var displayValue = value is string str ? $"\"{Escape(str)}\"" : value.ToString();
				Console.WriteLine($"{indent}{name} = {displayValue}");
				return;
			}

			// Handle types that should be hidden when not expanded
			if (!expanded && (value is FileRange or FileText or Scope or List<Symbol> or Symbol or Type))
			{
				Console.WriteLine($"{indent}{name}: <HIDDEN>");
				return;
			}

			// Recursively dump complex objects
			Console.WriteLine($"{indent}{name}:");
			Dump(value, level + 1, expanded, visited);
		}

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

		public static string Escape(string s)
		{
			return s
				.Replace("\n", "\\n")
				.Replace("\r\n", "\\r\\n")
				.Replace("\t", "\\t")
				.Replace("\"", "\\\"")
				.Replace("\'", "\\\'")
				.Replace("\\", "\\\\");
		}

		public static string Unescape(string s)
		{
			var str = s
				.Replace("\\n", "\n")
				.Replace("\\r\\n", "\r\n")
				.Replace("\\t", "\t")
				.Replace("\\\"", "\"")
				.Replace("\\\'", "\'")
				.Replace("\\\\", "\\");
			
			str = Regex.Replace(Regex.Replace(Regex.Replace(
				str, @"\\u[\da-fA-F]{4}", m => { return ((char)int.Parse(m.Value.Substring(2), NumberStyles.HexNumber)).ToString(); }), 
				@"\\U[\da-fA-F]{8}", m => { return ((char)int.Parse(m.Value.Substring(2), NumberStyles.HexNumber)).ToString(); }),
				@"\\x[\da-fA-F]{2}", m => { return ((char)int.Parse(m.Value.Substring(2), NumberStyles.HexNumber)).ToString(); });

			return str;
		}

		public enum ExclusionPreset
		{ None, Base, Ast, Lexer }

		private static IEnumerable<string>? GetExcludedPropertyNames(ExclusionPreset preset)
		{
			IEnumerable<string> baseProps = ["FullRange", "InnerRange", "TextLines", "StartLineAndColumn", "EndLineAndColumn", "Length"];
			return preset switch
			{
				ExclusionPreset.None => null,
				ExclusionPreset.Base => baseProps,
				ExclusionPreset.Ast => baseProps.Concat(new[] { "Scope", "RootScope", "Args" }),
				ExclusionPreset.Lexer => baseProps.Concat(new[] { "Filepath", "Args", "CurrentToken", "CurrentPos", "Text" }),
				_ => null
			};
		}

		private static void WriteObjectToJson(object? obj, string filepath, ExclusionPreset preset = ExclusionPreset.None)
		{
			var settings = new JsonSerializerSettings
			{
				ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
				Formatting = Formatting.Indented
			};
			if (GetExcludedPropertyNames(preset) is { } names && names.Any())
			{
				settings.ContractResolver = new ExcludePropertiesContractResolver(names);
			}
			string json = JsonConvert.SerializeObject(obj, settings);
			File.WriteAllText(filepath, json);
		}

		private sealed class ExcludePropertiesContractResolver : DefaultContractResolver
		{
			private readonly HashSet<string> _exclude;

			internal ExcludePropertiesContractResolver(IEnumerable<string> propertyNames)
			{
				_exclude = new HashSet<string>(propertyNames, StringComparer.Ordinal);
			}

			protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
			{
				var prop = base.CreateProperty(member, memberSerialization);
				if (prop.PropertyName is { } name && _exclude.Contains(name))
					prop.ShouldSerialize = _ => false;
				return prop;
			}
		}

		static int Main(string[] args)
		{
			var arg = new ProgramArgs();
			arg.ParseArgsOrExit(args);

			switch (arg.Command)
			{
				case ProgramCommand.Build:
				{
					var compiler = new Compiler(arg);
					compiler.Build();

					Console.WriteLine();
					compiler.PrintBuildErrors();
					Console.WriteLine();
					if (!arg.NoTime)
						compiler.WriteTimerReadout();

					if (arg.JsonDumpFilepath is not null)
						WriteObjectToJson(compiler.Files.Select(p => p.Parser.Statements).ToList(), Path.Combine(arg.JsonDumpFilepath, "parser_statements.json"), ExclusionPreset.Ast);
					if (arg.JsonDumpFilepath is not null && compiler.Files.Any(f => f.Lexer is not null))
						WriteObjectToJson(compiler.Files.Where(f => f.Lexer is not null).Select(f => f.Lexer!.Tokens).ToList(), Path.Combine(arg.JsonDumpFilepath, "lexer_tokens.json"), ExclusionPreset.Lexer);
					if (arg.JsonDumpFilepath is not null)
						WriteObjectToJson(compiler.Files.Select(f => f.Parser.CompilationIssues).ToList(), Path.Combine(arg.JsonDumpFilepath, "parser_compilation_issues.json"));

					break;
				}
				case ProgramCommand.Tokenize:
				{
					var file = Compiler.ParseFile(arg, arg.MainCodeFilepaths!.First());
					var lex = file.Lexer!;
					Token t = lex.CurrentToken;
					do Console.WriteLine($"{lex.CurrentPos}: {t.Which}"); while ((t = lex.Next()).Which != TokenType.EOF);

					if (arg.JsonDumpFilepath is not null)
						WriteObjectToJson(lex.Tokens, Path.Combine(arg.JsonDumpFilepath, "lexer_tokens.json"), ExclusionPreset.Lexer);
						
					break;
				}
				case ProgramCommand.Preprocess:
				{
					var code = File.ReadAllText(arg.MainCodeFilepaths!.First());
					Console.Write(Lexer.Preprocess(code));
					break;
				}
				case ProgramCommand.Tree:
				{
					var comp = new Compiler(arg);
					comp.Build();
					
					foreach (var file in comp.Files)
					{
						Console.WriteLine($"Module: {file.Filepath}");
						foreach (var stmt in file.Parser.Statements.ToArray())
						{
							Dump(stmt, expanded: arg.ExpandedDump);
						}
					}

					Console.WriteLine();
					comp.PrintBuildErrors();
					Console.WriteLine();
					if (!arg.NoTime)
						comp.WriteTimerReadout();

					if (arg.JsonDumpFilepath is not null)
						WriteObjectToJson(comp.Files.Select(f => f.Parser.Statements).ToList(), Path.Combine(arg.JsonDumpFilepath, "parser_statements.json"), ExclusionPreset.Ast);
					
					break;
				}
				case ProgramCommand.IR:
				{
					var compiler = new Compiler(arg);
					compiler.Build();
					
					if (compiler.Files.Count == 0)
						throw new Exception("No files parsed");
					
					var file = compiler.Files.First();
					var ir = new IRGenerator(arg, file);
					ir.GenerateIR();
					Dump(ir.Result.MainBlock, expanded: arg.ExpandedDump);

					if (arg.JsonDumpFilepath is not null)
						WriteObjectToJson(ir.Result.MainBlock, Path.Combine(arg.JsonDumpFilepath, "ir_main_block.json"), ExclusionPreset.Ast);

					break;
				}
			}

			return 0;
		}
	}
}
