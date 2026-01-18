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
				if(currentScope.Symbols.Find(s => s == sym && sym.GetType() == s.GetType()) != null)
					return true;
				currentScope = currentScope.Parent;
			}
			return false;
		}

		public T? FindSymbolByName<T>(string name)
			where T : Symbol
		{
			var currentScope = this;
			while (currentScope != null)
			{
				var found = currentScope.Symbols.Find(s => s.Name == name && s is T);
				if (found != null) return found as T;
				currentScope = currentScope.Parent;
			}
			return null;
		}

		public void AddSymbol(Symbol? sym)
		{
			if (sym != null) Symbols.Add(sym);
		}

		public Scope(Scope? parent = null)
		{
			this.Parent = parent;
		}
	}

	public abstract class Stmt
	{
		public Scope Scope = new();
		//public Stmt? Next;
		//public Stmt? Prev;

		//protected Stmt(Stmt? prev = null)
		//{
		//	if (prev != null)
		//	{
		//		Scope.Parent = prev.Scope;
		//		Prev = prev;
		//	}
		//}
	}

	public class CompoundStmt : Stmt { }

	public class IfStmt : Stmt
	{
		public Expr Condition;
		public Stmt NextIf;
		public Stmt? NextElse;
	}

	public class ExpressionStmt : Stmt
	{
		public Expr Expression;
	}

	public class VarDeclStmt : Stmt
	{
		[Required] public Symbol Name;
		public bool IsConst = false;
		public Expr? Value;
	}

	public class ClassDeclStmt : Stmt
	{
		[Required] public Symbol Name;
		[Required] public Stmt Value;
	}

		public class FuncDeclStmt : Stmt
	{
		[Required] public Symbol Name;
		public List<Symbol> Arguments = [];
		[Required] public Stmt Value;
	}
}
