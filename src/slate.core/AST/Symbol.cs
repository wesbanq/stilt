using System.Linq;

namespace slate.AST
{
	public abstract class Symbol
	{
		public const string BuiltinSource = "<BUILTIN>";
		public const string TempSource = "<TEMP>";

		public string Name;
		//TODO (IMPORTANT VERY)
		//stop using filepaths
		[JsonIgnore]
		public string Source;
		[JsonIgnore]
		public Stmt? Declaration;
		public List<TokenType> Specifiers = [];
		[JsonIgnore]
		public Token? Identifier;

		[JsonIgnore]
		public bool IsBuiltin => Source.StartsWith(BuiltinSource);
		[JsonIgnore]
		public bool IsTemp => Source.StartsWith(TempSource);

		public static bool operator ==(Symbol? left, Symbol? right)
		{
			if (left is null && right is null) return true;
			if (left is null || right is null) return false;
			return left.Name.Equals(right.Name) && left.Source.Equals(right.Source);
		}

		public static bool operator !=(Symbol? left, Symbol? right)
		{
			return !(left == right);
		}

		public override bool Equals(object? other)
		{
			return other is Symbol && this == other as Symbol;
		}

		public override int GetHashCode()
		{
			return (Name + Source).GetHashCode();
		}

		protected Symbol(string name, Token? token, string src = TempSource)
		{
			Name = name;
			Source = src;
			Identifier = token;
		}
		protected Symbol(string name, string src = TempSource)
		{
			Name = name;
			Source = src;
		}
	}

	/// <summary>
	/// Syntactic path to a name before resolution: optional qualifier (<c>a.b</c>),
	/// this segment's identifier, and optional generic type arguments on this segment.
	/// Not a <see cref="Symbol"/> — the linker attaches real symbols after lookup.
	/// </summary>
	public sealed class UnresolvedReference(
        string name,
        Token token,
        UnresolvedReference? qualifier = null,
        IEnumerable<UnresolvedReference>? typeArguments = null
	)
    {
        public string Name { get; } = name;
        public Token Token { get; } = token;
        public UnresolvedReference? Qualifier { get; } = qualifier;
        public IReadOnlyList<UnresolvedReference> TypeArguments { get; } = typeArguments is null
                ? Array.Empty<UnresolvedReference>()
                : typeArguments.ToList();
    }

	/// <summary>
	/// Holds an unresolved name path and, after the linker runs, the <see cref="Symbol"/> it bound to
	/// </summary>
	public sealed class SymbolReference(UnresolvedReference unresolved)
    {
        public UnresolvedReference Unresolved { get; } = unresolved ?? throw new ArgumentNullException(nameof(unresolved));
        private Symbol? _resolved;

		public Symbol? Resolved => _resolved;
		public bool IsResolved => _resolved is not null;

        public void Resolve(Symbol symbol)
		{
			ArgumentNullException.ThrowIfNull(symbol);
			if (_resolved is not null)
				throw new InvalidOperationException("Symbol already resolved.");
			_resolved = symbol;
		}
	}

	/// <summary>
	/// Like <see cref="SymbolReference"/> but resolves only to a <see cref="TypeSymbol"/> (type positions in the AST).
	/// </summary>
	public sealed class TypeSymbolReference(UnresolvedReference unresolved)
    {
        public UnresolvedReference Unresolved { get; } = unresolved ?? throw new ArgumentNullException(nameof(unresolved));
        private TypeSymbol? _resolved;

		public TypeSymbol? Resolved => _resolved;
		public bool IsResolved => _resolved is not null;

        public void Resolve(TypeSymbol typeSymbol)
		{
			if (_resolved is not null)
				throw new InvalidOperationException("Type symbol already resolved.");
			_resolved = typeSymbol;
		}
	}

	public class VarSymbol : Symbol
	{
		public TypeSymbol Type = Builtins.None;

		public override int GetHashCode()
		{
			var hash = new HashCode();
			hash.Add(Name);
			hash.Add(Type.GetHashCode());
			return hash.ToHashCode();
		}

		public VarSymbol(string n, TypeSymbol? type = null, Token? t = null)
			: base(n, t)
		{
			Type = type ?? Builtins.None;
		}
		public VarSymbol(string n, string s, TypeSymbol type, Token t)
			: base(n, t, s)
		{
			Type = type;
		}
		public VarSymbol(string n, string s, TypeSymbol type)
			: base(n, s)
		{
			Type = type;
		}
		public VarSymbol(string n, string s, Token t)
			: base(n, t, s)
		{ }
	}

	public class TypeSymbol : Symbol
	{
		private List<Symbol> _members = [];
		private TypeSymbol? _inherits;
		private List<TypeSymbol> _implementedTraits = [];
		private int _argumentCount = 0;

		// [JsonIgnore]
		public List<Symbol> Members => Base is null ? _members : Base.Members;
		/// <summary>Traits implemented by this type (in addition to single inheritance).</summary>
		[JsonIgnore]
		public List<TypeSymbol> ImplementedTraits => Base is null ? _implementedTraits : Base.ImplementedTraits;
		[JsonIgnore]
		public TypeSymbol? Inherits => Base is null ? _inherits : Base.Inherits;
		[JsonIgnore]
		public int ArgumentCount => Base is null ? _argumentCount : Base.ArgumentCount;

		public TypeSymbol? Base;
		public TypeSymbol[]? Arguments;

		public static bool operator ==(TypeSymbol? left, TypeSymbol? right)
		{
			if (left is null && right is null) return true;
			if (left is null || right is null) return false;
			if (left.ArgumentCount != right.ArgumentCount) return false;

			if (left.Arguments is not null && right.Arguments is not null)
			{
				for (int i = 0; i < left.ArgumentCount; i++)
				{
					if (left.Arguments[i] != right.Arguments[i])
						return false;
				}
			}
			else if (left.Arguments != right.Arguments)
			{
				return false;
			}

			return left.Name.Equals(right.Name) && left.Source.Equals(right.Source);
		}

		public static bool operator !=(TypeSymbol? left, TypeSymbol? right)
		{
			return !(left == right);
		}

		public override bool Equals(object? obj)
		{
			if (obj is not TypeSymbol other) return false;

			return this == other;
		}

		public override int GetHashCode()
		{
			var hash = new HashCode();
			hash.Add(Name);
			hash.Add(ArgumentCount);
			if (Arguments is not null)
			{
				for (int i = 0; i < Arguments.Length; i++)
				{
					hash.Add(Arguments[i].GetHashCode());
				}
			}
			return hash.ToHashCode();
		}

		public void _changeArgCount(int newCount)
		{
			if (Base is null)
				_argumentCount = newCount;
			else
				Base._changeArgCount(newCount);
		}

		public bool InheritsFrom(TypeSymbol from)
		{
			var currentType = this;
			while (currentType is not null)
			{
				if (currentType == from)
					return true;
				currentType = currentType.Inherits;
			}
			return false;
		}

		public Symbol? GetMember(string name)
		{
			var currentType = this;
			while (currentType is not null)
			{
				var member = currentType.Members.Find(m => m.Name == name);
				if (member is not null)
					return member;
				currentType = currentType.Inherits;
			}
			return null;
		}

		public bool Implements(TypeSymbol trait)
		{
			if (!trait.InheritsFrom(Builtins.Trait))
				throw new ArgumentException("Given type does not inherit from Trait");

			foreach (var t in ImplementedTraits)
			{
				if (t == trait)
					return true;
			}
			var currentType = Inherits;
			while (currentType is not null)
			{
				if (currentType == trait)
					return true;
				currentType = currentType.Inherits;
			}
			return false;
		}

		public TypeSymbol(string n, Token? t = null, TypeSymbol? inherits = null, int argumentCount = 0)
			: base(n, t)
		{
			_argumentCount = argumentCount;
			_inherits = inherits;
		}
		public TypeSymbol(string n, string s, Token? t = null, TypeSymbol? inherits = null, int argumentCount = 0, List<TypeSymbol>? implementedTraits = null)
			: base(n, t, s)
		{
			_argumentCount = argumentCount;
			_inherits = inherits;
			_implementedTraits = implementedTraits ?? [];
		}
		public TypeSymbol(TypeSymbol basedef, TypeSymbol[]? arguments = null)
			: base(basedef.Name, basedef.Source)
		{
			Base = basedef;
			_argumentCount = basedef.ArgumentCount;
			Arguments = arguments;

			if (Arguments is not null && Arguments.Length != ArgumentCount)
				throw new ArgumentException();
		}
	}

	/// <summary>
	/// Canonical structural types: one <see cref="TypeSymbol"/> instance per distinct applied generic / tuple shape.
	/// Does not resolve source names — use <see cref="UnresolvedReference"/> + linker for that.
	/// </summary>
	public static class TypeSymbolFactory
	{
		private sealed class TypeSymbolArrayComparer : IEqualityComparer<TypeSymbol[]>
		{
			public bool Equals(TypeSymbol[]? x, TypeSymbol[]? y)
			{
				if (ReferenceEquals(x, y)) return true;
				if (x is null || y is null || x.Length != y.Length) return false;
				for (int i = 0; i < x.Length; i++)
				{
					if (x[i] != y[i]) return false;
				}
				return true;
			}

			public int GetHashCode(TypeSymbol[]? obj)
			{
				if (obj is null) return 0;
				var hash = new HashCode();
				foreach (var t in obj)
					hash.Add(t);
				return hash.ToHashCode();
			}
		}

		private static readonly Dictionary<TypeSymbol, Dictionary<TypeSymbol[], TypeSymbol>> _argTypeSymbols = [];
		private static readonly Dictionary<int, TypeSymbol> _tupleTypeSymbols = [];
		private static readonly List<TypeSymbol> _basicTypeSymbols = [];
		private static readonly Dictionary<string, TypeSymbol> _tempTypeSymbolsByName = [];

		private static Dictionary<TypeSymbol[], TypeSymbol> CreateArgsDict() =>
			new(new TypeSymbolArrayComparer());

		private static TypeSymbol[] PopulateArgs(TypeSymbol baseType, List<TypeSymbol>? args = null)
		{
			args ??= new List<TypeSymbol>(baseType.ArgumentCount);
			if (args.Count != baseType.ArgumentCount)
				for (int i = args.Count; i < baseType.ArgumentCount; i++)
					args.Add(Builtins.None);

			return [.. args];
		}

		private static TypeSymbol GetBasicTypeSymbol(TypeSymbol baseType)
		{
			var found = _basicTypeSymbols.Find(t => t == baseType);
			if (found is null)
			{
				_basicTypeSymbols.Add(baseType);
				found = baseType;
			}
			return found;
		}

		public static TypeSymbol GetTypeSymbol(TypeSymbol baseType, List<TypeSymbol>? args = null)
		{
			if (baseType.ArgumentCount == 0)
				return GetBasicTypeSymbol(baseType);

			var typeArgs = PopulateArgs(baseType, args);
			if (!_argTypeSymbols.TryGetValue(baseType, out var argsDict))
				_argTypeSymbols[baseType] = argsDict = CreateArgsDict();
			if (!argsDict.TryGetValue(typeArgs, out var resultTypeSymbol))
				argsDict[typeArgs] = resultTypeSymbol = new TypeSymbol(baseType, typeArgs);
			return resultTypeSymbol;
		}

		public static TypeSymbol GetTuple(List<TypeSymbol> args)
		{
			if (args.Count == 0)
				throw new ArgumentException("Tuple must have at least one argument");

			if (!_tupleTypeSymbols.TryGetValue(args.Count, out var tupleTypeSymbol))
			{
				_tupleTypeSymbols[args.Count] = tupleTypeSymbol = new TypeSymbol($"Tuple_{args.Count}", Symbol.BuiltinSource, argumentCount: args.Count);
				_argTypeSymbols[tupleTypeSymbol] = CreateArgsDict();
			}

			return GetTypeSymbol(tupleTypeSymbol, args);
		}
	}
}
