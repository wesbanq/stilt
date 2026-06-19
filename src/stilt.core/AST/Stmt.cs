using System.Diagnostics.CodeAnalysis;

namespace stilt.AST
{
	/// <summary>An applied decorator <c>[[ Name(args…) ]]</c>: the decorator's type (which must inherit <see cref="Builtins.Decorator"/>) and its literal arguments. Attached to the statement it precedes.</summary>
	public class DecoratorObject
	{
		public readonly TypeSymbol DecoratorType;
		public readonly List<LiteralExpr> Arguments;

		public DecoratorObject(TypeSymbol decoratorType, List<LiteralExpr> arguments) 
		{
			if (!decoratorType.InheritsFrom(Builtins.Decorator))
				throw new ArgumentException("Decorator type must inherit from Decorator");
			DecoratorType = decoratorType;
			Arguments = arguments;
		}
	}

	/// <summary>
	/// Base of every statement node. Each statement remembers the lexical <see cref="Scope"/> it was parsed in
	/// (which the <see cref="Linker"/> searches for name lookups) and any <see cref="Decorators"/> attached to it.
	/// Like <see cref="Expr"/>, it tracks an inner span and a full span covering its sub-nodes.
	/// </summary>
	public abstract class Stmt : IRanged
	{
		public required Scope Scope;
		public List<DecoratorObject> Decorators = [];

		public FileRange? InnerRange { get; set; }
		/// <summary>The span covering this statement and all its <see cref="IRanged"/> children, discovered by reflecting over fields and summing their ranges.</summary>
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

	public class CompoundStmt : Stmt
	{
		public List<Stmt> Statements = [];
	}

	public class ReturnStmt : Stmt
	{
		public required Expr Value;
	}

	public class IfStmt : Stmt
	{
		public required Expr Condition;
		public required Stmt NextIf;
		public Stmt? NextElse;
	}

	

	public class LoopStmt : Stmt
	{
		public Stmt Body;
	}

	public class PostconditionLoopStmt : LoopStmt
	{
		public Expr Condition;
	}

	public class PreconditionLoopStmt : LoopStmt
	{
		public Expr Condition;
	}

	public class ForLoopStmt : LoopStmt
	{
		public VarDeclStmt? LoopVariable;
		public Expr? Condition;
		public Expr? Iterator;
	}

	public class ForeachLoopStmt : LoopStmt
	{
		public required VarDeclStmt LoopVariable;
		public required Expr Iterator;
	}

	public class BreakStmt : Stmt
	{ }

	public class ContinueStmt : Stmt
	{ }

	public class ExpressionStmt : Stmt
	{
		public required Expr Expression;
	}

	/// <summary>
	/// A raw command block — the <c>execute</c> escape hatch. The constructor parses the single token's text into
	/// individual <see cref="Commands"/> (the lines beginning with <c>/</c>), to be emitted verbatim into the datapack.
	/// (Parsing an <c>execute as &lt;target&gt;</c> executor is sketched out but not yet enabled.)
	/// </summary>
	public class ExecuteStmt : Stmt
	{
		public string[] Commands;
		// public VarSymbol? Executor;

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
			// if (!string.IsNullOrEmpty(executorStr))
			// 	Executor = new VarSymbol(executorStr);
		}
	}

	/// <summary>Base for declarations that introduce a named <see cref="Symbol"/> (variables, functions, types). <see cref="Specifiers"/> holds leading modifiers like <c>pub</c>/<c>const</c>.</summary>
	public abstract class DeclStmt : Stmt
	{
		public Symbol Name;
		public Stmt Value;
		public List<TokenType> Specifiers = [];
	}

	public class VarDeclStmt : DeclStmt
	{
		public new List<Symbol> Name;
		public new Expr? Value;
		public bool IsConst = false;
	}

	// INCOMPLETE: defined for the type system, but the parser does not emit TypeDeclStmt yet (see ParseStmt's TypeDecl case).
	public class TypeDeclStmt : DeclStmt
	{
		[SetsRequiredMembers]
		public TypeDeclStmt(TypeSymbol typeSym, Stmt v)
		{
			Value = v;
			Name = typeSym;
			typeSym.Declaration = this;
		}
	}

	// INCOMPLETE: defined for the type system, but the parser does not emit TraitDeclStmt yet (see ParseStmt's TraitDecl case).
	public class TraitDeclStmt : DeclStmt
	{
		[SetsRequiredMembers]
		public TraitDeclStmt(TypeSymbol traitSym)
		{
			Name = traitSym;
			traitSym.Declaration = this;
		}
	}

	public class FuncDeclStmt : DeclStmt
	{
		[SetsRequiredMembers]
		public FuncDeclStmt(string name, string source, Stmt v, TypeSymbol? args = null, TypeSymbol? returns = null)
		{
			Value = v;
			Name = new VarSymbol(name, source, SymbolReference.AlreadyResolved(new TypeSymbol(Builtins.Callable, [args ?? Builtins.None, returns ?? Builtins.None])))
			{ Declaration = this };
		}

		[SetsRequiredMembers]
		public FuncDeclStmt(VarSymbol funcSymbol, Stmt v)
		{
			Value = v;
			Name = funcSymbol;
			funcSymbol.Declaration = this;
		}
	}

	/// <summary>An <c>import "path" [as name]</c>: the source <see cref="Filepath"/>, the <see cref="ModuleName"/> it is bound to, and the module <see cref="Symbol"/> introduced into scope. The <see cref="Linker"/> loads the file and wires up its scope.</summary>
	public class ImportStmt : DeclStmt
	{
		public required string Filepath;
		public required string ModuleName;
		[JsonIgnore]
		public Scope? ImportedScope;

		[SetsRequiredMembers]
		public ImportStmt(string moduleName, string filepath, Symbol name)
		{
			ModuleName = moduleName;
			Filepath = filepath;
			Name = name;
			name.Declaration = this;
		}
	}
}
