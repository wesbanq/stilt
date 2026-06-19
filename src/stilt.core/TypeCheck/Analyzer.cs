namespace stilt
{
	// INCOMPLETE: the semantic-analysis stage is a scaffold only — it holds the modules/trees to analyze but
	// performs no checks yet (e.g. symbol-shadowing diagnostics are still to come).
	public class Analyzer
	{
        //TODO
        //scope symbol shadowing
        //
        //TODO: enforce single inheritance. TypeSymbol.Inherits now holds every base type (class base + traits) in one
        //list; reject any type whose Inherits contains more than one non-trait base (a base that does not inherit from
        //Builtins.Trait). Also flag duplicate/cyclic bases here.

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