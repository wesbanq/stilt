using System.Diagnostics.CodeAnalysis;

namespace stilt.AST
{
	public abstract class Stmt : IRanged
	{
		public required Scope Scope;
		public List<TypeSymbol> Decorators = [];

		public FileRange? InnerRange { get; set; }
		public FileRange? FullRange
		{
			get
			{
				var sum = InnerRange;
				foreach (var fld in GetType().GetFields())
				{
					if (typeof(IRanged).IsAssignableFrom(fld.FieldType))
					{
					var ranged = fld.GetValue(this) as IRanged;

					if (ranged?.FullRange is null) continue;
					sum = sum is null ? ranged.FullRange : sum + ranged.FullRange;
					}
				}

				return sum;
			}
		}

		public FileRange GetFullRangeOrThrow() =>
			FullRange ?? throw new InvalidOperationException("Statement has no FullRange");

		public FileRange GetInnerRangeOrFullRangeOrThrow() =>
			InnerRange ?? FullRange ?? throw new InvalidOperationException("Statement has no range");
	}

	public interface IContainerStmt
	{
		public List<Stmt?> Contained { get; }
	}

	public interface IExpressionStmt
	{
		public List<Expr?> Expressions { get; }
	}

	public class CompoundStmt : Stmt, IContainerStmt
	{
		public List<Stmt> Statements = [];
		public List<Stmt?> Contained => Statements!;
	}

	public class ReturnStmt : Stmt, IExpressionStmt
	{
		public required Expr Value;
		public List<Expr?> Expressions => [Value];
	}

	public class IfStmt : Stmt, IContainerStmt, IExpressionStmt
	{
		public required Expr Condition;
		public required Stmt NextIf;
		public Stmt? NextElse;
		public List<Stmt?> Contained => [NextIf, NextElse];
		public List<Expr?> Expressions => [Condition];
	}

	

	public class LoopStmt : Stmt, IContainerStmt
	{
		public Stmt Body;
		public List<Stmt?> Contained => [Body];
	}

	public class PostconditionLoopStmt : LoopStmt, IExpressionStmt
	{
		public Expr Condition;
		public List<Expr?> Expressions => [Condition];
	}

	public class PreconditionLoopStmt : LoopStmt, IExpressionStmt
	{
		public Expr Condition;
		public List<Expr?> Expressions => [Condition];
	}

	public class ForLoopStmt : LoopStmt, IExpressionStmt
	{
		public VarDeclStmt? LoopVariable;
		public Expr? Condition;
		public Expr? Iterator;
		public List<Expr?> Expressions => [Condition, Iterator];
	}

	public class ForeachLoopStmt : LoopStmt, IExpressionStmt
	{
		public required VarDeclStmt LoopVariable;
		public required Expr Iterator;
		public List<Expr?> Expressions => [Iterator];
	}

	public class BreakStmt : Stmt
	{ }

	public class ContinueStmt : Stmt
	{ }

	public class ExpressionStmt : Stmt, IExpressionStmt
	{
		public required Expr Expression;
		public List<Expr?> Expressions => [Expression];
	}

	public class ExecuteStmt : Stmt
	{
		public string[] Commands;
		public VarSymbol? Executor;

		public ExecuteStmt(Token token)
		{
			var tokenText = token.Range.Text.Trim();
			// New syntax: "execute\n/line1\n/line2" or "execute as <target>\n/line1\n/line2"
			string? executorStr = null;
			var firstNewline = tokenText.IndexOf('\n');
			if (firstNewline >= 0)
			{
				var firstLine = tokenText[..firstNewline].Trim();
				if (firstLine.StartsWith("execute as ", StringComparison.Ordinal))
					executorStr = firstLine["execute as ".Length..].Trim();
			}

			// Collect lines that look like \s*\/.* (optional whitespace, slash, rest = command)
			var commandLines = new List<string>();
			var lineRegex = new Regex(@"^\s*/(.*)$", RegexOptions.Multiline);
			foreach (Match m in lineRegex.Matches(tokenText))
			{
				var cmd = m.Groups[1].Value.Trim();
				if (cmd.Length > 0)
					commandLines.Add(cmd);
			}
			if (commandLines.Count == 0)
				throw new ArgumentException("Invalid execute statement format: no command lines");

			Commands = [.. commandLines];
			if (!string.IsNullOrEmpty(executorStr))
				Executor = new VarSymbol(executorStr);
		}
	}

	public abstract class DeclStmt : Stmt, IContainerStmt
	{
		public Symbol Name;
		public Stmt Value;
		public List<TokenType> Specifiers = [];
		public List<Stmt?> Contained => [Value];
	}

	public class VarDeclStmt : DeclStmt, IExpressionStmt
	{
		public new List<Symbol> Name;
		public new Expr? Value;
		public bool IsConst = false;
		public List<Expr?> Expressions => [Value];
	}

	public class TypeDeclStmt : DeclStmt
	{

		[SetsRequiredMembers]
		public TypeDeclStmt(string name, string source, Stmt v, TypeSymbol? inherits = null)
		{
			Value = v;
			Name = new TypeSymbol(name, source, inherits: new(name, source, inherits: inherits))
			{ Declaration = this };
		}

		[SetsRequiredMembers]
		public TypeDeclStmt(TypeSymbol typeSym, Stmt v)
		{
			Value = v;
			Name = typeSym;
			typeSym.Declaration = this;
		}
	}

	public class FuncDeclStmt : DeclStmt
	{

		[SetsRequiredMembers]
		public FuncDeclStmt(string name, string source, Stmt v, TypeSymbol? args = null, TypeSymbol? returns = null)
		{
			Value = v;
			Name = new VarSymbol(name, source, new TypeSymbol(Builtins.Callable, [args ?? Builtins.Any, returns ?? Builtins.Any]))
			{ Declaration = this };
		}
	}

	public class ImportStmt : Stmt
	{
		public required string Filepath;
		public required string ModuleName;
		public Scope? ImportedScope;
	}
}
