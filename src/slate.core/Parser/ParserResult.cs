namespace slate
{
	public class ParserResult
	{
		public List<Stmt> Statements = [];
		public Scope RootScope = new(Builtins.BuiltinScope);
		public List<CompilationMessage> CompilationIssues = [];
		public List<DecoratorObject> GlobalDecorators = [];
		public List<string> ImportedFiles = [];

		public bool HasErrors => CompilationIssues.Any(m => m.Severity >= ErrorSeverity.Error);

		public void WriteErrors()
		{
			CompilationIssues.ForEach(m => m.Print());
		}
	}
}
