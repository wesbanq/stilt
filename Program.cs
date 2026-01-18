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
		static int Main(string[] args)
		{
			ProgramArgs a;
			try
			{
				a = new ProgramArgs(args);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Error: \n\t{e.Message}");
				return 1;
			}

			switch (a.Action)
			{
				case ProgramArgs.Command.Build:
					Compiler.Build(a); break;
			}

			Assembly.GetExecutingAssembly().GetTypes().Whe

			return 0;
		}
	}
}
