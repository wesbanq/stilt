using System;
using System.Collections.Generic;
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
				if (currentScope.Symbols.Find(s => s == sym /*&& sym.GetType() == s.GetType()*/) != null)
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

			//throw new Parser.UndefinedSymbolException(name, );
			return null;
		}

		public void AddSymbol(Symbol? sym)
		{
			if (sym != null) Symbols.Add(sym);
		}

		public Scope(Scope parent)
		{
			Parent = parent;
		}
		public Scope() { }
	}
}
