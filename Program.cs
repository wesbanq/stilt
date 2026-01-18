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
				default:
				{
					
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

			Action = Compiler.GetEnumFromDescription<Command, ActionAttribute>(args[0].ToLower());

			//Lexer.GetSymbolAttribute(out var symbols, out var regex);
			//var optsa = typeof(Option).GetFields();
			//for (int i = 1; i < optsa.Length; i++)
			//{
			//	var sym = optsa[i].GetCustomAttribute<OptionAttribute>();
			//	var idx = args.IndexOf(sym?.Name);
			//	if (idx != -1)
			//	{
			//		if (sym.Kind != OptionType.Flag && (args.Length <= idx + 1 || 
			//			WhichOption(args[idx + 1]) != Option.None))
			//		{
			//			throw new Exception($"No value given for option '{sym.Name}'");
			//		}
			//		GiveValueTo(sym.AssociatedPropertyName, sym.Kind == OptionType.Flag ? "" : args[idx + 1]);
			//		UsedOptions.Add((Option)(i-1));
			//	}
			//}

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
						GiveValueTo(currentAttribute.AssociatedPropertyName, nextAttribute == null ? args[i + 1] : "");
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
			[Required]
			public string Name { get; set; }

			[Required]
			public OptionType Kind { get; set; }

			[Required]
			public string AssociatedPropertyName { get; set; }
			public string HelpText { get; set; }

			public string GetDescription()
			{
				return Name;
			}
			
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
			[Required] public Option[]? Required;
			[Required] public string Name;

			public string GetDescription()
			{
				return Name;
			}

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
