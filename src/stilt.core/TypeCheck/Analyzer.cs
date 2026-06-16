namespace stilt
{
	// INCOMPLETE: the semantic-analysis stage is a scaffold only — it holds the modules/trees to analyze but
	// performs no checks yet (e.g. symbol-shadowing diagnostics are still to come).
	public class Analyzer
	{
        //TODO
        //scope symbol shadowing
        //

		public ProgramArgs Args;
		public List<Scope> Modules = [];
		public List<List<Stmt>> Trees = [];
		public List<CompilationMessage> Errors = [];

        public Analyzer(ProgramArgs args, List<Scope> modules, List<List<Stmt>> trees)
        {
            Args = args;
            Modules = modules;
            Trees = trees;
        }
	}
}