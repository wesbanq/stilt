namespace stilt.AST
{
	public class Scope
	{
		public Scope? Parent;
		public List<Symbol> Symbols = [];

		public bool IsInScope(Symbol sym)
		{
			return FindSymbolByName(sym.Name) is not null;
		}

		public Symbol? FindSymbolByName(string name)
		{
			var currentScope = this;
			while (currentScope is not null)
			{
				var found = currentScope.Symbols.Find(s => s?.Name.Equals(name) ?? throw new Exception($"NULLLSDASD {name} "));
				if (found is not null) return found;
				currentScope = currentScope.Parent;
			}

			return null;
		}

		public VarSymbol? FindVarByName(string name)
		{
			var currentScope = this;
			while (currentScope is not null)
			{
				var found = currentScope.Symbols.Find(s => s is VarSymbol && s.Name == name) as VarSymbol;
				if (found is not null) return found;
				currentScope = currentScope.Parent;
			}
			return null;
		}

		public TypeSymbol? FindTypeByName(string name)
		{
			var currentScope = this;
			while (currentScope is not null)
			{
				var found = currentScope.Symbols.Find(s => s is TypeSymbol && s.Name == name) as TypeSymbol;
				if (found is not null) return found;
				currentScope = currentScope.Parent;
			}
			return null;
		}

		public void AddSymbol(Symbol sym)
		{
			Symbols.Add(sym);
		}

		public void AddSymbols(IEnumerable<Symbol> symbols)
		{
			Symbols.AddRange(symbols);
		}

		public Scope(Scope parent)
		{
			Parent = parent;
		}
		public Scope() { }
	}
}
