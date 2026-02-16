namespace stilt
{
	public class ParserResult
	{
		public List<Stmt> Statements = [];
		public Scope RootScope = new(Builtins.BuiltinScope);
		public List<CompilationMessage> CompilationIssues = [];
		public List<Symbol> AllImportedSymbols = [];
		public List<DecoratorObject> GlobalDecorators = [];

		public bool HasErrors => CompilationIssues.Any(m => m.Severity >= ErrorSeverity.Error);

		public void WriteErrors()
		{
			CompilationIssues.ForEach(m => m.Print());
		}
	}
}
