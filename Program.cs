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

		public static void PrintHelp()
		{
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
				case "MainCodeFilepath":
				{
					if (!File.Exists(value))
						throw new ArgumentParsingException("Given a non-existing file for option {0}: {1}", opt, value);
					MainCodeFilepath = value;
					break;
				}
				case "Throw":
				{
					Throw = true;
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

			Action = Compiler.GetEnumFromDescription<Command, ActionAttribute>(args[0].ToLower());

			for (int i = 1; i < args.Length; i++)
			{
				var current = WhichOption(args[i]);
				var currentAttribute = typeof(Option).GetField(current.ToString()).GetCustomAttribute<OptionAttribute>();
				var nextAttribute = args.Length > i+1 ? Compiler.GetAttrFromDescription<Option, OptionAttribute>(args[i+1]) : null;

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
					//Console.WriteLine($"DASASDSAD {i}{WhichOption(args[i - 1]) == Option.None}");
					var a = Compiler.GetAttributeFromEnum<Command, ActionAttribute>(Action);
					if (a != null && a.Required != null)
					{
						foreach (var b in a.Required)
						{
							if (!UsedOptions.Contains(b))
							{
								GiveValueTo(
									Compiler.GetAttributeFromEnum<Option, OptionAttribute>(b)?.AssociatedPropertyName ?? ""
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
		}

	}

	internal class Program
	{
		public static void Dump(object? obj, int l = 0)
		{
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
					if (value.GetType().IsPrimitive || value.GetType().IsEnum || value is string)
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name} = " +
						$"{(value is string ? $"\"{Escape((string)value)}\"" : value)}");
					}
					else
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name}:");
						Dump(value, l + 1);
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
					if (value.GetType().IsPrimitive || value.GetType().IsEnum || value is string)
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name} = " +
						$"{(value is string ? $"\"{Escape((string)value)}\"" : value)}");
					}
					else
					{
						Console.WriteLine($"{new string('\t', l + 1)}{prop.Name}:");
						Dump(value, l + 1);
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
					//'\\' => "\\\\",
					//'"' => "\\\"",
					_ when char.IsControl(c) => $"\\x{(int)c:X2}",
					_ => c.ToString()
				});
			}
			return sb.ToString();
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
					Compiler.Build(arg);
					break;
				}
				case ProgramArgs.Command.Tokenize:
				{
					var lex = new Lexer(arg);
					Token t = lex.CurrentToken;
					do Dump(t); while ((t = lex.Next()) != null);
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
					//string g = File.ReadAllText(arg.MainCodeFilepath);
					//FileRange a = new(0, 1, arg.MainCodeFilepath);
					//FileRange b = new(17, 18, arg.MainCodeFilepath);
					//FileRange c = new(20, 23, arg.MainCodeFilepath);
					//throw new Exception();

					var lex = new Lexer(arg);
					var parse = new Parser(lex, arg);
					parse.ParseFile();

					foreach (var stmt in parse.Statements)
					{
						Dump(stmt);
					}

					parse.WriteErrors();
					//Console.WriteLine(parse.Statements.Count);

					break;
				}
			}

			return 0;
		}
	}
}
