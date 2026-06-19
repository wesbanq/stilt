using System.Linq;

namespace stilt.AST
{
	/// <summary>
	/// A named entity in the symbol table — see <see cref="VarSymbol"/> (values/functions) and <see cref="TypeSymbol"/>
	/// (types). Identity is <see cref="Name"/> plus <see cref="Source"/> (the file it was declared in, or the sentinel
	/// <see cref="BuiltinSource"/>/<see cref="TempSource"/>), which is also how two symbols are compared for equality.
	/// <see cref="Declaration"/> links back to the AST node that introduced it. (Using file paths as identity is a known
	/// rough edge, flagged for replacement.)
	/// </summary>
	public abstract class Symbol
	{
		public const string BuiltinSource = "<BUILTIN>";

		public string Name;
		//TODO (IMPORTANT VERY)
		//stop using filepaths
		[JsonIgnore]
		public string Source;
		[JsonIgnore]
		public DeclStmt Declaration;
		public List<TokenType> Specifiers = [];
		[JsonIgnore]
		public Token? Token;

		[JsonIgnore]
		public bool IsBuiltin => Source.StartsWith(BuiltinSource);

		public static bool operator ==(Symbol? left, Symbol? right)
		{
			if (left is null && right is null) return true;
			if (left is null || right is null) return false;
			if (left.Declaration is null || right.Declaration is null) return false;



			return left.Declaration.Equals(right.Declaration);
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
			return Declaration.GetHashCode();
		}

		protected Symbol(string name, Token? token, string src)
		{
			Name = name;
			Source = src;
			Token = token;
		}

		protected Symbol(string name, string src)
		{
			Name = name;
			Source = src;
		}
	}

	/// <summary>
	/// Holds an unresolved name path and, after the linker runs, the <see cref="Symbol"/> it bound to
	/// </summary>
	public sealed class SymbolReference
    {
		public Symbol? Resolved => _resolved;
        public UnresolvedReference Unresolved => _unresolved;
		public bool IsResolved => _resolved is not null;

        private Symbol? _resolved;
		private UnresolvedReference _unresolved;

        public void Resolve(Symbol symbol)
		{
			ArgumentNullException.ThrowIfNull(symbol);
			if (IsResolved)
				throw new InvalidOperationException("Symbol already resolved.");
			_resolved = symbol;
		}

		public static SymbolReference AlreadyResolved(Symbol sym) => new()
        {
			_resolved = sym, 
			_unresolved = new UnresolvedReference(sym.Name, sym.Token!),
		};

		public static SymbolReference NotResolved(Token t) => new()
		{
			_unresolved = new UnresolvedReference(t.Range.Text, t),
		};

		public static SymbolReference NotResolved(string name, Token t) => new()
		{
			_unresolved = new UnresolvedReference(name, t),
		};

		public static SymbolReference FromUnresolved(UnresolvedReference unresolvedReference) => new()
		{
			_unresolved = unresolvedReference,
		};
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
	/// A value-binding symbol: variables, function parameters, imported modules, and named functions (a function is a
	/// <see cref="VarSymbol"/> whose <see cref="Type"/> is a <see cref="Builtins.Callable"/>). <see cref="Type"/> is a
	/// <see cref="SymbolReference"/> so it can start unresolved or inferred (<see cref="Builtins.Infer"/>) and be filled in later.
	/// </summary>
	public class VarSymbol : Symbol
	{
		public SymbolReference Type = SymbolReference.AlreadyResolved(Builtins.Infer);

		public override int GetHashCode()
		{
			var hash = new HashCode();
			hash.Add(Name);
			hash.Add(Type.GetHashCode());
			return hash.ToHashCode();
		}

		public VarSymbol(string n, string s, SymbolReference? type = null, Token? t = null)
			: base(n, t, s)
		{
			if (type is not null) Type = type;
		}
		public VarSymbol(string n, string s, SymbolReference? type = null)
			: base(n, s)
		{
			if (type is not null) Type = type;
		}
		public VarSymbol(string n, string s, Token t)
			: base(n, t, s)
		{ }
	}

	/// <summary>
	/// A type. Carries its <see cref="Members"/>, the base types it <see cref="Inherits"/> (a class base and/or traits —
	/// trait conformance is just inheritance), and its generic <see cref="TypeParameters"/> (whose count is the arity
	/// <see cref="ArgumentCount"/>). A generic instance points <see cref="Base"/> at the open type and reads its shared
	/// data through it, supplying one concrete <see cref="Arguments"/> entry per parameter, so each applied shape
	/// (e.g. <c>array[int]</c>) is one canonical instance — see <see cref="TypeSymbolFactory"/>. The helpers
	/// <see cref="InheritsFrom"/>/<see cref="Implements"/>/<see cref="GetMember"/> walk the inheritance graph, while
	/// <see cref="CheckConstraints"/>/<see cref="Substitute"/> back the generic-constraint model.
	/// </summary>
	public class TypeSymbol : Symbol
	{
		private List<Symbol> _members = [];
		private List<TypeParameter> _typeParameters = [];
		protected List<SymbolReference> _inherits = [];

		// [JsonIgnore]
		public List<Symbol> Members => Base is null ? _members : Base.Members;

		/// <summary>
		/// The base types this type derives from: at most one class/struct base plus any number of traits, unified into a
		/// single relationship (implementing a trait is just inheriting it). An applied instance reads these through its
		/// <see cref="Base"/>.
		/// REMINDER: single inheritance (at most one non-trait base) is not enforced here — the analysis stage
		/// (<c>Analyzer</c>) must reject a type that lists more than one non-trait base.
		/// </summary>
		public IReadOnlyList<SymbolReference> Inherits => Base is null ? _inherits : Base.Inherits;
		/// <summary>
		/// The open type's declared generic parameters (empty for non-generic types), each carrying its own
		/// <see cref="TypeParameter.Constraints"/>. An applied instance reads these through its <see cref="Base"/>;
		/// <see cref="Arguments"/> supplies one concrete type per parameter, positionally.
		/// </summary>
		[JsonIgnore]
		public IReadOnlyList<TypeParameter> TypeParameters => Base is null ? _typeParameters : Base.TypeParameters;
		/// <summary>Generic arity: the number of type arguments this type takes.</summary>
		[JsonIgnore]
		public int ArgumentCount => TypeParameters.Count;
		public TypeSymbol? Base;
		public IReadOnlyList<SymbolReference> Arguments = [];

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

			return left.Name.Equals(right.Name) 
				&& left.Source.Equals(right.Source) 
				&& left.Declaration.Equals(right.Declaration);
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
				for (int i = 0; i < Arguments.Count; i++)
				{
					hash.Add(Arguments[i].GetHashCode());
				}
			}
			return hash.ToHashCode();
		}

		public void _changeArgCount(int newCount)
		{
			if (Base is not null)
			{
				Base._changeArgCount(newCount);
				return;
			}

			if (newCount < _typeParameters.Count)
				_typeParameters.RemoveRange(newCount, _typeParameters.Count - newCount);
			else
				for (int i = _typeParameters.Count; i < newCount; i++)
					_typeParameters.Add(TypeParameter.Synthetic(i, Source));
		}

		/// <summary>True if this type is, or transitively inherits (through any base or trait), <paramref name="from"/>.</summary>
		public bool InheritsFrom(TypeSymbol from) => InheritsFrom(from, []);

		private bool InheritsFrom(TypeSymbol from, HashSet<TypeSymbol> visited)
		{
			if (this == from)
				return true;
			if (!visited.Add(this))
				return false;

			foreach (var parent in Inherits.Select(i => i.Resolved))
			{
				if (parent is not null && parent is TypeSymbol type && type.InheritsFrom(from, visited))
					return true;
			}
			return false;
		}

		/// <summary>Finds a member by name on this type or any of its bases/traits (depth-first), or null.</summary>
		public Symbol? GetMember(string name) => GetMember(name, []);

		private Symbol? GetMember(string name, HashSet<TypeSymbol> visited)
		{
			if (!visited.Add(this))
				return null;

			var member = Members.Find(m => m.Name == name);
			if (member is not null)
				return member;

			foreach (var parent in Inherits)
			{
				var found = parent.Resolved is TypeSymbol type ? type.GetMember(name, visited) : null;
				if (found is not null)
					return found;
			}
			return null;
		}

		/// <summary>
		/// True if this type conforms to <paramref name="trait"/>. With trait conformance modelled as ordinary
		/// inheritance this is just <see cref="InheritsFrom"/>, plus a guard that the argument really is a trait.
		/// </summary>
		public bool Implements(TypeSymbol trait)
		{
			if (!trait.InheritsFrom(Builtins.Trait))
				throw new ArgumentException("Given type does not inherit from Trait");

			return InheritsFrom(trait);
		}

		/// <summary>
		/// True if <paramref name="argument"/> satisfies the generic bound <paramref name="constraint"/>. A bound may be
		/// a base type or a trait; since trait conformance is modelled as inheritance, both reduce to "inherits from".
		/// </summary>
		public static bool SatisfiesConstraint(TypeSymbol? argument, TypeSymbol? constraint)
		{
			if (argument is null || constraint is null)
				return false;
			return argument.InheritsFrom(constraint);
		}

		/// <summary>
		/// For an applied generic instance, checks every supplied type argument against the constraints of the
		/// corresponding parameter. Constraints that are not yet resolved to a <see cref="TypeSymbol"/> are skipped
		/// (the linker resolves them before checking). Returns true when no constraint is violated.
		/// </summary>
		public bool CheckConstraints(out List<ConstraintViolation> violations)
		{
			violations = [];
			if (Base is null || Arguments is null)
				return true;

			var parameters = TypeParameters;
			for (int i = 0; i < parameters.Count && i < Arguments.Count; i++)
			{
				foreach (var bound in parameters[i].Inherits)
				{
					if (bound.Resolved is not TypeSymbol c)
						continue;
					if (!SatisfiesConstraint(Arguments[i].Resolved as TypeSymbol, c))
						violations.Add(new ConstraintViolation(parameters[i], (Arguments[i].Resolved as TypeSymbol)!, c));
				}
			}
			return violations.Count == 0;
		}

		/// <summary>
		/// Maps each of <see cref="Base"/>'s type parameters to this applied instance's corresponding type argument.
		/// Empty when this is not an applied generic.
		/// </summary>
		public IReadOnlyDictionary<TypeParameter, TypeSymbol> ArgumentBindings()
		{
			var map = new Dictionary<TypeParameter, TypeSymbol>();
			if (Base is null || Arguments is null)
				return map;

			var parameters = TypeParameters;
			for (int i = 0; i < parameters.Count && i < Arguments.Count; i++)
				map[parameters[i]] = (Arguments[i].Resolved as TypeSymbol)!;
			return map;
		}

		private List<TypeParameter> SynthesizeTypeParameters(int count)
		{
			var parameters = new List<TypeParameter>(count);
			for (int i = 0; i < count; i++)
				parameters.Add(TypeParameter.Synthetic(i, Source));
			return parameters;
		}

		/// <summary>
		/// Declares a (possibly generic) named type. <paramref name="inherits"/> lists every base type — at most one
		/// non-trait base plus any traits (the analysis stage enforces the single non-trait base). Provide
		/// <paramref name="typeParameters"/> for explicit, constrained generic parameters; otherwise
		/// <paramref name="argumentCount"/> synthesizes that many anonymous, unconstrained parameters (builtins, tuples).
		/// </summary>
		public TypeSymbol(
			string n,
			string s,
			Token? t = null,
			IEnumerable<SymbolReference>? inherits = null,
			int argumentCount = 0,
			IEnumerable<TypeParameter>? typeParameters = null
		) : base(n, t, s)
		{
			_inherits = inherits?.ToList() ?? [];
			_typeParameters = typeParameters is not null
				? [.. typeParameters]
                : SynthesizeTypeParameters(argumentCount);
		}

		public TypeSymbol(TypeSymbol basedef, IReadOnlyList<SymbolReference>? arguments = null)
			: base(basedef.Name, basedef.Source)
		{
			Base = basedef;
			Arguments = arguments ?? [];

			if (Arguments is not null && Arguments.Count != ArgumentCount)
				throw new ArgumentException();
		}
	}

	/// <summary>
	/// A declared generic parameter (e.g. <c>T</c> in <c>type Box[T]</c>). It is itself a <see cref="TypeSymbol"/> so it
	/// can be used as a type inside the generic's body and placed in scope, and it carries the <see cref="Constraints"/>
	/// (upper bounds) that any supplied type argument must satisfy.
	/// </summary>
	public sealed class TypeParameter : TypeSymbol
	{
		/// <summary>This parameter's position in its owner's parameter list.</summary>
		[JsonIgnore]
		public int Position;

//TODO


		public TypeParameter(
			string name,
			string source,
			Token? token = null,
			int position = 0,
			IEnumerable<SymbolReference>? constraints = null
		) : base(name, source, token)
		{
			Position = position;
			_inherits = constraints?.ToList() ?? [];
		}

		/// <summary>Creates an anonymous, unconstrained parameter for a type declared only by arity (builtins, tuples).</summary>
		public static TypeParameter Synthetic(int position, string source) =>
			new($"T{position}", source, null, position);
	}

	/// <summary>A type argument that fails one of its parameter's constraints, produced by <see cref="TypeSymbol.CheckConstraints"/>.</summary>
	public sealed record ConstraintViolation(TypeParameter Parameter, TypeSymbol Argument, TypeSymbol Constraint);
}
