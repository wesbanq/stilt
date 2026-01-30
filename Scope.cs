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
			return FindSymbolByName(sym.Name) is not null;
		}

		public Symbol? FindSymbolByName(string name)
		{
			var currentScope = this;
			while (currentScope is not null)
			{
				var found = currentScope.Symbols.Find(s => s.Name == name);
				if (found is not null) return found;
				currentScope = currentScope.Parent;
			}

			return null;
		}

		public void AddSymbol(Symbol sym)
		{
			Symbols.Add(sym);
		}

		public Scope(Scope parent)
		{
			Parent = parent;
		}
		public Scope() { }
	}
}
