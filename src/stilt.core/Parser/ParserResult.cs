namespace stilt
{
	/// <summary>
	/// Everything one file's <see cref="Parser"/> run produces: the top-level <see cref="Statements"/>, the file's
	/// <see cref="RootScope"/> (parented to the builtins so global names resolve), and the diagnostics gathered along the way.
	/// </summary>
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
