using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace stilt.AST
{
	public class Scope
	{
		public Scope? Parent;
		public List<Symbol> Symbols = new();
		
		public bool IsInScope(Symbol sym)
		{
			var currentScope = this;
			while (currentScope != null)
			{
				if(currentScope.Symbols.Find(s => s == sym) != null)
					return true;
				currentScope = currentScope.Parent;
			}
			return false;
		}
	}

	public abstract class Stmt
	{
		public Scope Scope = new();
		public Stmt? Next;
		public Stmt? Prev;

		protected Stmt(Stmt? prev = null)
		{
			if (prev != null)
			{
				Scope.Parent = prev.Scope;
				Prev = prev;
			}
		}
	}

	public class CompoundStmt(Stmt? prev = null) : Stmt(prev) { }

	public class IfStmt(Stmt? prev = null) : Stmt(prev)
	{
		public Expr Condition;
		public Stmt NextIf;
		public Stmt? NextElse;
	}

	public class ExpressionStmt(Stmt? prev = null) : Stmt(prev)
	{
		public Expr Expression;
	}

	public class VarDeclStmt(Stmt? prev = null) : Stmt(prev)
	{
		[Required] public Symbol Name;
		public bool IsConst = false;
		public Expr? Value;
	}

	public class ClassDeclStmt(Stmt? prev = null) : Stmt(prev)
	{
		[Required] public Symbol Name;
		[Required] public Stmt Value;
	}

		public class FuncDeclStmt(Stmt? prev = null) : Stmt(prev)
	{
		[Required] public Symbol Name;
		public List<Symbol> Arguments = [];
		[Required] public Stmt Value;
	}
}
