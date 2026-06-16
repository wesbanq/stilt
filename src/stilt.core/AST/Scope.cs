namespace stilt.AST
{
	/// <summary>
	/// A lexical scope: the <see cref="Symbol"/>s declared at one level plus a link to the enclosing <see cref="Parent"/>
	/// scope. Lookups walk that parent chain outward, so inner declarations shadow outer ones. The parser builds the
	/// scope tree as it parses (file root → blocks → function bodies, ultimately parented to <see cref="Builtins.BuiltinScope"/>),
	/// and the <see cref="Linker"/> searches it to resolve names.
	/// </summary>
	public class Scope
	{
		public Scope? Parent;
		public List<Symbol> Symbols = [];

		/// <summary>True if a symbol of this name is visible from here (this scope or any ancestor).</summary>
		public bool IsInScope(Symbol sym)
		{
			return FindSymbolByName(sym.Name) is not null;
		}

		/// <summary>Finds the nearest symbol of any kind with this name, searching this scope then each ancestor.</summary>
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

		/// <summary>Like <see cref="FindSymbolByName"/> but only considers variables/values (<see cref="VarSymbol"/>), so a type of the same name is skipped.</summary>
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

		/// <summary>Like <see cref="FindSymbolByName"/> but only considers types (<see cref="TypeSymbol"/>), so a variable of the same name is skipped.</summary>
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
