using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;

namespace stilt.AST
{
	public abstract class Stmt : IRanged
	{
		public required Scope Scope;
		//public List<TypeSymbol> Decorators = [];

		public FileRange? InnerRange { get; set; }
		public FileRange? FullRange
		{
			get
			{
				var sum = InnerRange;
				foreach (var fld in GetType().GetFields())
				{
					if (fld is IRanged ranged)
					{
						if (ranged.FullRange is null) continue;
						sum = sum is null ? ranged.FullRange : sum + ranged.FullRange;
					}
				}

				return sum;
			}
		}
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
	public class NewScopeAttribute : Attribute
	{ }

	[NewScope]
	public class CompoundStmt : Stmt
	{
		public required LinkedList<Stmt> Statements = new();
	}

	[NewScope]
	public class IfStmt : Stmt
	{
		public required Expr Condition;
		public required Stmt NextIf;
		public Stmt? NextElse;
	}

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
			var executorStr = Regex.Match(tokenText, """as +(.*) +{""").ToString().Trim();
			var comStr = Regex.Match(tokenText, """{(?:.*\s)*}""").ToString()?.Split('\n');
			for (var i = 0; i < comStr.Length; i++)
			{
				comStr[i] = comStr[i].Trim();
			}

			Commands = comStr;
			if (executorStr.Length > 0)
				Executor = executorStr != null ? new(executorStr) : null;
		}
	}

	public class DeclStmt : Stmt
	{
		public Symbol Name;
		public Stmt Value;
		public List<TokenType> Specifiers = [];
	}

	public class VarDeclStmt : DeclStmt
	{
		public new List<Symbol> Name;
		public new Expr Value;
		public bool IsConst = false;
	}

	[NewScope]
	public class TypeDeclStmt : DeclStmt
	{
		public TypeDeclStmt(string name, string source, Stmt v, TypeSymbol? inherits = null)
		{
			Value = v;
			Name = new TypeSymbol(name, source, new(name, source, inherits));
		}
	}

	[NewScope]
	public class FuncDeclStmt : DeclStmt
	{
		public FuncDeclStmt(string name, string source, Stmt v, TypeSymbol? args = null, TypeSymbol? returns = null)
		{
			Value = v;
			Name = new VarSymbol(name, source, new(Builtins.Callable, [args ?? Builtins.Any, returns ?? Builtins.Any]));
		}
	}
}
