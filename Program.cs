using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace stilt
{
	public class ProgramArgs
	{
		[Required]
		public Command Action { get; set; }

		public int DebugLevel { get; set; }
		public string MainCodeFilepath { get; set; }
		public List<Option> UsedOptions { get; set; } = new List<Option>();
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
						return (Option)(i-1);
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

			switch (args[0].ToLower()) 
			{
				case "build":
					Action = Command.Build; break;
				case "token":
					Action = Command.Tokenize; break;
				case "help":
					Action = Command.Help; PrintHelp(); return;
				case "preprocess":
					Action = Command.Preprocess; break;
				default:
					throw new ArgumentParsingException("Invalid action given: {0}{1}", args[0], "");
			}

			Lexer.GetSymbolAttribute(out var symbols, out var regex);
			var optsa = typeof(Option).GetFields();
			for (int i = 1; i < optsa.Length; i++)
			{
				var sym = optsa[i].GetCustomAttribute<OptionAttribute>();
				var idx = args.IndexOf(sym?.Name);
				if (idx != -1)
				{
					if (sym.Kind != OptionType.Flag && (args.Length <= idx + 1 || 
						WhichOption(args[idx + 1]) != Option.None))
					{
						throw new Exception($"No value given for option '{sym.Name}'");
					}
					GiveValueTo(sym.AssociatedPropertyName, sym.Kind == OptionType.Flag ? "" : args[idx + 1]);
					UsedOptions.Add((Option)(i-1));
				}
			}

			var opts = typeof(Command).GetField(Action.ToString())
				.GetCustomAttribute<ActionRequiredAttribute>()?.Required
				.Where(o => !UsedOptions.Contains(o)).ToArray();
			if (opts?.Length > 0)
			{
				for (int i = 1; i < args.Length && opts.Length > 0; i++)
				{
					if (WhichOption(args[i]) == Option.None && 
						(WhichOption(args[i-1]) == Option.None || 
						(GetEnumAttribute<Option, OptionAttribute>(WhichOption(args[i-1])).Kind == OptionType.Flag) //||
						//(GetEnumAttribute<Option, OptionAttribute>(WhichOption(args[i-1])).Kind == OptionType.ValueOptional)
						))
					{
						Console.WriteLine(args[i]);
						Console.WriteLine(i);
						Console.WriteLine(WhichOption(args[i]));

						GiveValueTo(GetEnumAttribute<Option, OptionAttribute>(opts.First()).AssociatedPropertyName, args[i]);
						opts = opts[1..];
					}
				}
				if (opts.Length != 0)
				{
					throw new NotEnoughArgmunetsException();
				}
			}
		}

		[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
		public class OptionAttribute : Attribute
		{
			[Required]
			public string Name { get; set; }

			[Required]
			public OptionType Kind { get; set; }

			[Required]
			public string AssociatedPropertyName { get; set; }
			public string HelpText { get; set; }
			
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
		public class ActionRequiredAttribute : Attribute
		{
			[Required]
			public Option[] Required { get; set; }

			public ActionRequiredAttribute(Option[] required)
			{
				Required = required;

			}
		}

		public enum OptionType
		{ ValueRequired, ValueOptional, Flag }

		public enum Command 
		{ 
			[ActionRequired(new[] { Option.InputFile })]
			Build, 

			[ActionRequired(new[] { Option.InputFile })]
			Tokenize,

			[ActionRequired(new[] { Option.InputFile })]
			Preprocess,

			Help,
		}

		public enum Option
		{ 
			None = 0,

			[Option("-d", "DebugLevel", "Set debug level (for compiler developers)"/*, OptionType.ValueOptional*/)]
			DebugLvl,

			[Option("-i", "MainCodeFilepath", "Sets the main code filepath to use")]
			InputFile,
		}

	}

	internal class Program
	{
		public static void Dump(object obj, int l = 0)
		{
			if (l == 0) Console.WriteLine();
			var type = obj.GetType();
			Console.WriteLine($"{new string('\t', l)}{type.Name}");

			foreach (var prop in type.GetProperties())
			{
				var value = prop.GetValue(obj);
				if (value.GetType().IsPrimitive || value.GetType().IsEnum || value is string)
				{
					Console.WriteLine($"{new string('\t', l+1)}{prop.Name} = " +
					$"'{(value is string ? $"\"{Escape((string)value)}\"" : value)}'");
				}
				else
				{
					Dump(value, l + 1);
				}
			}
			foreach (var prop in type.GetFields())
			{
				var value = prop.GetValue(obj);
				if (value.GetType().IsPrimitive || value.GetType().IsEnum || value is string)
				{
					Console.WriteLine($"{new string('\t', l+1)}{prop.Name} = '{value}'");
				}
				else
				{
					Dump(value, l + 1);
				}
			}
		}

		public static string Escape(string s)
		{
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
				case ProgramArgs.Command.Build:
				{
					Compiler.Build(arg);
					break;
				}
				case ProgramArgs.Command.Tokenize:
				{
					var lex = new Lexer(arg);
					Token t;
					while ((t = lex.Next()) != null) Dump(t);
					break;
				}
				case ProgramArgs.Command.Preprocess:
				{
					var code = File.ReadAllText(arg.MainCodeFilepath);
					Console.Write(Lexer.Preprocess(code));
					break;
				}
			}

			return 0;
		}
	}
}
