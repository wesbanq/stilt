using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using stilt.AST;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using stilt.IR;
using System.Text.RegularExpressions;
using System.Globalization;

namespace stilt
{
	//TODO switch to NuArgs
	public class ProgramArgs
	{
		public Command Action;
		public int DebugLevel;
		public string[]? MainCodeFilepaths = null;
		public List<Option> UsedOptions = [];
		public bool Throw = false;
		public bool ExpandedDump = false;
		public bool NoTime = false;
		public string? JsonDumpFilepath = null;
		public MCVersion TargetVersion = MCVersion.LatestJava;

		public static void PrintHelp()
		{
			//TODO dynamically generate help text for specific options/actions using reflection
			Console.WriteLine("help text");
		}

		static Option WhichOption(string arg)
		{
			var fields = typeof(Option).GetFields();
			for (int i = 1; i < fields.Length; i++)
			{
				var sym = fields[i].GetCustomAttributes<OptionAttribute>();
				foreach (var s in sym)
				{
					if (String.Compare(s.Name, arg) == 0)
					{
						return (Option)(i - 1);
					}
				}
			}
			return Option.None;
		}

		void GiveValueTo(string opt, string value)
		{
			switch (opt)
			{
				case "DebugLevel":
				{
					if (!int.TryParse(value, out var n))
						throw new ArgumentParsingException(opt, value);
					DebugLevel = n;
					break;
				}
				case "MainCodeFilepaths":
				{
					value = Path.GetFullPath(value);
					if (!File.Exists(value))
						throw new ArgumentParsingException("Given a non-existing file for option {0}: {1}", opt, value);
					if (!value.EndsWith(".stilt"))
						Console.WriteLine("The given file does not end in '.stilt'. " +
						"The code will still be compiled, but importing it for use in other files may be problematic." +
						"Consider changing the file's extension to .stilt.");
					
					MainCodeFilepaths ??= [];
					MainCodeFilepaths = [.. MainCodeFilepaths, value];
					break;
				}
				case "TargetVersion":
				{
                    TargetVersion = MCVersion.ParseMCVersion(value) 
						?? throw new ArgumentException($"Invalid target version: '{value}'");
					break;
				}
				case "Throw":
				{
					Throw = true;
					break;
				}
				case "ExpandedDump":
				{
					ExpandedDump = true;
					break;
				}
				case "NoTime":
				{
					NoTime = true;
					break;
				}
				case "JsonDumpPath":
				{
					JsonDumpFilepath = Path.GetFullPath(value);
					if (!Directory.Exists(Path.GetDirectoryName(JsonDumpFilepath)))
						throw new ArgumentParsingException("Given a non-existing directory for option {0}: {1}", opt, JsonDumpFilepath);
					break;
				}
				default:
				{
					throw new ArgumentException($"Non-existent argument: {opt}");
				}
			}
		}

		public class NotEnoughArgmunetsException : Exception
		{
			public NotEnoughArgmunetsException() : base("Not enough arguments passed") { }
		}

		public class ArgumentParsingException : Exception
		{
			public string OptionName { get; set; }
			public string? GivenValue { get; set; }

			public ArgumentParsingException(string optName, string givenValue)
				: base($"Invalid value given to option '{optName}': '{givenValue}'")
			{
				OptionName = optName;
				GivenValue = givenValue;
			}

			public ArgumentParsingException(string optName)
				: base($"No value given to option '{optName}'")
			{
				OptionName = optName;
			}

			public ArgumentParsingException(string customMessage, string optName, string givenValue)
				: base(String.Format(customMessage, optName, givenValue))
			{
				OptionName = optName;
				GivenValue = givenValue;
			}
		}

		public ProgramArgs(string[] args)
		{
			if (args.Length == 0)
			{
				throw new ArgumentParsingException("No action given.{0}{1}", "", "");
			}

			Action = Program.GetEnumFromDescription<Command, ActionAttribute>(args[0].ToLower());

			for (int i = 1; i < args.Length; i++)
			{
				var current = WhichOption(args[i]);
				var currentAttribute = typeof(Option).GetField(current.ToString())?.GetCustomAttribute<OptionAttribute>();
				var nextAttribute = args.Length > i+1 ? Program.GetAttrFromDescription<Option, OptionAttribute>(args[i+1]) : null;

				if (currentAttribute?.Kind == OptionType.ValueMultiple)
				{
					++i;
					for (; i < args.Length; ++i)
					{
						var newCurrent = WhichOption(args[i]);
						
						if (newCurrent != Option.None) break;
						GiveValueTo(currentAttribute.AssociatedPropertyName, args[i]);
					}
					UsedOptions.Add(current);
					continue;
				}
				else if (WhichOption(args[i - 1]) == Option.None)
				{
					var a = Program.GetAttributeFromEnum<Command, ActionAttribute>(Action);
					if (a is not null && a.Required is not null)
					{
						foreach (var b in a.Required)
						{
							if (!UsedOptions.Contains(b))
							{
								GiveValueTo(
									Program.GetAttributeFromEnum<Option, OptionAttribute>(b)
										?.AssociatedPropertyName ?? "", args[i]
								);
								UsedOptions.Add(b);
							}
						}
					}
				}
			}
		}

		[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
		public class OptionAttribute : Attribute, IDescriptable
		{
			public string Name { get; set; }
			public OptionType Kind;
			public string AssociatedPropertyName;
			public string HelpText;

			public OptionAttribute(string optChar, string propName, string helpText, OptionType tpe = OptionType.ValueRequired)
			{
				Name = optChar;
				Kind = tpe;
				AssociatedPropertyName = propName;
				HelpText = helpText;
			}
		}

		public static A? GetEnumAttribute<T, A>(T opt)
			where T : Enum
			where A : Attribute
		{
			return typeof(T).GetField(opt.ToString())?.GetCustomAttribute<A>();
		}

		[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
		public class ActionAttribute : Attribute, IDescriptable
		{
			public Option[]? Required;
			public string Name { get; set; }

			public ActionAttribute(string name, Option[]? required = null)
			{
				Required = required;
				Name = name;
			}
		}

		public enum OptionType
		{ ValueRequired, ValueMultiple, ValueOptional, Flag }

		public enum Command
		{
			[Action("help")]
			Help,

			[Action("build", [Option.InputFile])]
			Build,

			[Action("token", [Option.InputFile])]
			Tokenize,

			[Action("preprocess", [Option.InputFile])]
			Preprocess,

			[Action("tree", [Option.InputFile])]
			Tree,

			[Action("ir", [Option.InputFile])]
			IR,
		}

		public enum Option
		{
			None = 0,

			[Option("-d", "DebugLevel", "Set debug level (for compiler developers)"/*, OptionType.ValueOptional*/)]
			DebugLvl,

			[Option("-i", "MainCodeFilepaths", "Sets the main code filepath to use", OptionType.ValueMultiple)]
			InputFile,

			[Option("-t", "Throw", "Crash the program instead of printing the error (for debugging)", OptionType.Flag)]
			Throw,

			[Option("-ex", "ExpandedDump", "Additional info in dumps", OptionType.Flag)]
			Expanded,

			[Option("-nt", "NoTime", "Don't show total compilation time.", OptionType.Flag)]
			NoTime,

			[Option("-j", "JsonDumpPath", "Dump the output to a JSON file (for debugging)")]
			JsonDumpFilepath,

			[Option("-v", "TargetVersion", "Set the target version of the language")]
			TargetVersion,
		}
	}

	public class Timer
	{
		private string _name;

		public Stopwatch? Stopwatch { get; private set; }
		public string Time => Stopwatch is null
			? $"{_name} has not been started."
			: Stopwatch.IsRunning
			? $"{_name} has been running for ({Stopwatch.Elapsed.TotalSeconds}s)."
			: $"{_name} finished in ({Stopwatch.Elapsed.TotalSeconds}s).";

		public void StartTimer()
		{
			Stopwatch ??= new Stopwatch();
			Stopwatch.Start();
		}

		public void StopTimer()
		{
			Stopwatch?.Stop();
		}

		public void Run(Action action)
		{
			StartTimer();
			action.Invoke();
			StopTimer();
		}

		public Timer(string name, Action action)
		{
			_name = name;
			Run(action);
		}
		public Timer(string name)
		{
			_name = name;
		}
	}

	internal static class Program
	{

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
			//TODO
			//turn into json file
			if (obj is null)
			{
				Console.WriteLine("null");
				return;
			}

			if (l == 0)
				Console.WriteLine();

			var type = obj.GetType();
			var indent = new string('\t', l);

			// Handle arrays specially to avoid infinite loops
			if (type.IsArray)
			{
				Console.WriteLine($"{indent}{type.Name}");
				var array = (Array)obj;
				for (int i = 0; i < array.Length; i++)
				{
					Console.WriteLine($"{indent}\t[{i}]:");
					Dump(array.GetValue(i), l + 2, expanded, visited);
				}
				return;
			}

			// Check for circular references (only for reference types)
			if (!type.IsValueType)
			{
				if (visited.Contains(obj))
				{
					Console.WriteLine($"{indent}{type.Name} <CYCLE>");
					return;
				}
				visited.Add(obj);
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

			// Handle List<Stmt> specially
			if (value is List<Stmt> stmtList)
			{
				Console.WriteLine($"{indent}{name}:");
				foreach (var item in stmtList)
				{
					Dump(item, level + 1, expanded, visited);
				}
				return;
			}

			// Handle arrays
			if (valueType.IsArray)
			{
				Console.WriteLine($"{indent}{name}:");
				var array = (Array)value;
				for (int i = 0; i < array.Length; i++)
				{
					Console.WriteLine($"{indent}\t[{i}]:");
					Dump(array.GetValue(i), level + 2, expanded, visited);
				}
				return;
			}

			// Handle simple types (primitives, enums, strings, string arrays)
			if (valueType.IsPrimitive || valueType.IsEnum || value is string || value is string[])
			{
				var displayValue = value is string str ? $"\"{Escape(str)}\"" : value.ToString();
				Console.WriteLine($"{indent}{name} = {displayValue}");
				return;
			}

			// Handle types that should be hidden when not expanded
			if (!expanded && (value is FileRange or FileText or Scope or List<Symbol> or Symbol))
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
			ProgramArgs arg;
			try
			{
				arg = new ProgramArgs(args);
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				return 1;
			}

			switch (arg.Action)
			{
				case ProgramArgs.Command.Help:
				{
					ProgramArgs.PrintHelp();
					break;
				}
				case ProgramArgs.Command.Build:
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
					if (arg.JsonDumpFilepath is not null)
						WriteObjectToJson(compiler.Files.Select(f => f.Lexer.Tokens).ToList(), Path.Combine(arg.JsonDumpFilepath, "lexer_tokens.json"), ExclusionPreset.Lexer);
					if (arg.JsonDumpFilepath is not null)
						WriteObjectToJson(compiler.Files.Select(f => f.Parser.CompilationIssues).ToList(), Path.Combine(arg.JsonDumpFilepath, "parser_compilation_issues.json"));

					break;
				}
				case ProgramArgs.Command.Tokenize:
				{
					var file = Compiler.ParseFile(arg, arg.MainCodeFilepaths!.First());
					var lex = file.Lexer;
					Token t = lex.CurrentToken;
					do Console.WriteLine($"{lex.CurrentPos}: {t.Which}"); while ((t = lex.Next()).Which != TokenType.EOF);

					if (arg.JsonDumpFilepath is not null)
						WriteObjectToJson(file.Lexer.Tokens, Path.Combine(arg.JsonDumpFilepath, "lexer_tokens.json"), ExclusionPreset.Lexer);
						
					break;
				}
				case ProgramArgs.Command.Preprocess:
				{
					var code = File.ReadAllText(arg.MainCodeFilepaths!.First());
					Console.Write(Lexer.Preprocess(code));
					break;
				}
				case ProgramArgs.Command.Tree:
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
				case ProgramArgs.Command.IR:
				{
					var file = Compiler.ParseFile(arg, arg.MainCodeFilepaths!.First());
					var ir = new IRGenerator();
					ir.Generate(file);
					Dump(ir, expanded: arg.ExpandedDump);
					break;
				}
			}

			return 0;
		}
	}
}
