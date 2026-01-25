using stilt.AST;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace stilt
{
	public class ProgramArgs
	{
		public Command Action;
		public int DebugLevel;
		public string MainCodeFilepath;
		public List<Option> UsedOptions = [];
		public bool Throw = false;
		public bool ExpandedDump = false;
		public bool NoTime = false;

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
			//TODO redo this entirely
			//var fields = GetType().GetFields();
			//var a = Array.Find(fields, f => f.Name == opt);
			//if (a == null)
			//	throw new ArgumentException($"Non-existent argument: {opt}");

			//a.SetValue(this, a.FieldType.);
			switch (opt)
			{
				case "DebugLevel":
				{
					if (!int.TryParse(value, out var n))
						throw new ArgumentParsingException(opt, value);
					DebugLevel = n;
					break;
				}
				case "MainCodeFilepath":
				{
					value = Path.GetFullPath(value);
					if (!File.Exists(value))
						throw new ArgumentParsingException("Given a non-existing file for option {0}: {1}", opt, value);
					if (!value.EndsWith(".stilt"))
						Console.WriteLine("The given file does not end in '.stilt'. " +
						"The code will still be compiled, but importing it for use in other files may be problematic." +
						"Consider changing the file's extension to .stilt.");
					MainCodeFilepath = value;
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
				var currentAttribute = typeof(Option).GetField(current.ToString()).GetCustomAttribute<OptionAttribute>();
				var nextAttribute = args.Length > i+1 ? Program.GetAttrFromDescription<Option, OptionAttribute>(args[i+1]) : null;

				if (current != Option.None)
				{
					if (((currentAttribute?.Kind != OptionType.Flag
						&& nextAttribute?.Kind == null)
						|| currentAttribute?.Kind == OptionType.Flag)
						&& !UsedOptions.Contains(current)
						)
					{
						GiveValueTo(currentAttribute.AssociatedPropertyName,
							nextAttribute == null && currentAttribute?.Kind != OptionType.Flag ? args[i + 1] : "");
						UsedOptions.Add(current);
					}
				}
				else if (WhichOption(args[i - 1]) == Option.None)
				{
					var a = Program.GetAttributeFromEnum<Command, ActionAttribute>(Action);
					if (a != null && a.Required != null)
					{
						foreach (var b in a.Required)
						{
							if (!UsedOptions.Contains(b))
							{
								GiveValueTo(
									Program.GetAttributeFromEnum<Option, OptionAttribute>(b)?.AssociatedPropertyName ?? ""
									, args[i]);
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
		{ ValueRequired, ValueOptional, Flag }

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
		}

		public enum Option
		{
			None = 0,

			[Option("-d", "DebugLevel", "Set debug level (for compiler developers)"/*, OptionType.ValueOptional*/)]
			DebugLvl,

			[Option("-i", "MainCodeFilepath", "Sets the main code filepath to use")]
			InputFile,

			[Option("-t", "Throw", "Crash the program instead of printing the error (for debugging)", OptionType.Flag)]
			Throw,

			[Option("-ex", "ExpandedDump", "Additional info in dumps", OptionType.Flag)]
			Expanded,

			[Option("-nt", "NoTime", "Don't show total compilation time.", OptionType.Flag)]
			NoTime,
		}

	}

	public class Timer
	{
		private string _name;

		public Stopwatch Stopwatch { get; private set; }
		public string Time => Stopwatch.IsRunning 
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
					return (T)field.GetValue(null);
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
			//TODO
			//turn into json file
			if (obj == null)
			{
				Console.WriteLine("null");
				return;
			}
			if (l == 0) Console.WriteLine();
			var type = obj.GetType();
			Console.WriteLine($"{new string('\t', l)}{type.Name}");

			foreach (var prop in type.GetProperties())
			{
				try
				{
					var value = prop.GetValue(obj);
					if (value == null)
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name} = null");
						continue;
					}
					if (value is LinkedList<Stmt>)
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name}:");
						foreach (var item in (value as LinkedList<Stmt>))
						{
							Dump(item, l + 1);
						}
					}
					if (value.GetType().IsPrimitive || value.GetType().IsEnum || value is string || value is string[])
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name} = " +
						$"{(value is string ? $"\"{Escape(value.ToString())}\"" : value)}");
					}
					else
					{
						if (!expanded && (value is (FileRange or FileText or Scope or List<Symbol> or Symbol)))
						{
							Console.WriteLine($"{new string('\t', l + 1)}{prop.Name}: <HIDDEN>");
							continue;
						}
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name}:");
						Dump(value, l + 1, expanded);
					}
				}
				catch { }
			}
			foreach (var prop in type.GetFields())
			{
				try
				{
					var value = prop.GetValue(obj);
					if (value == null)
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name} = null");
						continue;
					}
					if (value is LinkedList<Stmt>)
					{
						foreach (var item in (value as LinkedList<Stmt>))
						{
							Dump(item, l + 1);
						}
						continue;
					}
					if (value.GetType().IsPrimitive || value.GetType().IsEnum || value is string)
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name} = " +
						$"{(value is string ? $"\"{Escape(value.ToString())}\"" : value)}");
					}
					else
					{
						if (!expanded && (value is (FileRange or FileText or Scope or List<Symbol> or Symbol)))
						{
							Console.WriteLine($"{new string('\t', l + 1)}{prop.Name}: <HIDDEN>");
							continue;
						}
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name}:");
						Dump(value, l + 1, expanded);
					}
				}
				catch { }
			}
		}

		public static string Escape(string s)
		{
			if (s == "") return "";
			var sb = new StringBuilder(s.Length);
			foreach (char c in s)
			{
				sb.Append(c switch
				{
					'\n' => "\\n",
					'\r' => "\\r",
					'\t' => "\\t",
					'\\' => "\\\\",
					'"' => "\\\"",
					_ when char.IsControl(c) => $"\\x{(int)c:X2}",
					_ => c.ToString()
				});
			}
			return sb.ToString();
		}

		public static void TimerReadout(List<Timer> timers)
		{
			foreach (var timer in timers)
			{
				Console.WriteLine(timer.Time);
			}
		}

		static int Main(string[] args)
		{
			ProgramArgs arg;
			//TODO
			//implement ValueOptional
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
					compiler.Parser.WriteErrors();
					Console.WriteLine();
					if (!arg.NoTime)
						TimerReadout(compiler.Timers);

					break;
				}
				case ProgramArgs.Command.Tokenize:
				{
					var lex = new Lexer(arg);
					lex.Lex();
					Token t = lex.CurrentToken;
					do Dump(t); while ((t = lex.Next()).Which != TokenType.EOF);
					break;
				}
				case ProgramArgs.Command.Preprocess:
				{
					var code = File.ReadAllText(arg.MainCodeFilepath);
					Console.Write(Lexer.Preprocess(code));
					break;
				}
				case ProgramArgs.Command.Tree:
				{
					var comp = new Compiler(arg);
					comp.Build();

					if (!comp.Parser.HasErrors)
					{
						foreach (var stmt in comp.Parser.Statements)
						{
							Dump(stmt, expanded: arg.ExpandedDump);
						}
					}

					Console.WriteLine();
					comp.Parser.WriteErrors();
					Console.WriteLine();
					if (!arg.NoTime)
						TimerReadout(comp.Timers);

					break;
				}
			}

			return 0;
		}
	}
}
