using System.Reflection;

namespace stilt
{
	public class ProgramArgs
	{
		public enum Command {Build,Help,}
		public Command Action;
		public string MainCodeFilepath;

		public void PrintHelp()
		{
			Console.WriteLine("help text");
		}

		public ProgramArgs(string[] args)
		{
			if (args.Length == 0)
			{
				throw new Exception("No action given.");
			}

			switch (args[0].ToLower()) 
			{
				case "build":
					Action = Command.Build; break;
				case "help":
					Action = Command.Help; PrintHelp(); return;
				default:
					throw new Exception(String.Format("Invalid action given: {0}", args[0]));
			}

			if (Action == Command.Build)
			{
				if (args.Length > 1)
				{
					if (File.Exists(args[1]))
					{
						MainCodeFilepath = args[1];
					}
					else
					{
						throw new Exception(String.Format("Given file does not exist: {0}", args[1]));
					}
				}
				else
				{
					throw new Exception("No filepath given to build.");
				}
			}
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
					Console.WriteLine($"{new string('\t', l+1)}{prop.Name} = {value}");
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
					Console.WriteLine($"{new string('\t', l+1)}{prop.Name} = {value}");
				}
				else
				{
					Dump(value, l + 1);
				}
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
				Console.WriteLine($"Error: {e.Message}");
				return 1;
			}

			switch (arg.Action)
			{
				case ProgramArgs.Command.Build:
					Compiler.Build(arg); break;
			}

			Lexer.Tokenize(arg.MainCodeFilepath);

			return 0;
		}
	}
}
