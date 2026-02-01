using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

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
	}

	public class CompoundStmt : Stmt
	{
		public required LinkedList<Stmt> Statements;
	}

	public class ReturnStmt : Stmt
	{
		public Expr? Value;
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

	public abstract class DeclStmt : Stmt
	{
		public Symbol Name;
		public List<TokenType> Specifiers = [];
	}

	public class VarDeclStmt : DeclStmt
	{
		public new List<Symbol> Name;
		public required Expr? Value;
		public bool IsConst = false;
	}

	public class TypeDeclStmt : DeclStmt
	{
		public required Stmt Value;

		[SetsRequiredMembers]
		public TypeDeclStmt(string name, string source, Stmt v, TypeSymbol? inherits = null)
		{
			Value = v;
			Name = new TypeSymbol(name, source, inherits: new(name, source, inherits: inherits))
			{ Declaration = this };
		}
	}

	public class FuncDeclStmt : DeclStmt
	{
		public required Stmt Value;

		[SetsRequiredMembers]
		public FuncDeclStmt(string name, string source, Stmt v, TypeSymbol? args = null, TypeSymbol? returns = null)
		{
			Value = v;
			Name = new VarSymbol(name, source, new TypeSymbol(Builtins.Callable, [args ?? Builtins.Any, returns ?? Builtins.Any]))
			{ Declaration = this };
		}
	}
}
