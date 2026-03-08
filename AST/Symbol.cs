using System.Linq;

namespace stilt.AST
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

	public class VarSymbol : Symbol
	{
		public TypeSymbol Type = Builtins.Any;

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
			Type = type ?? Builtins.Any;
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
		private List<TraitSymbol> _implementedTraits = [];
		private int _argumentCount = 0;

		// [JsonIgnore]
		public List<Symbol> Members => Base is null ? _members : Base.Members;
		/// <summary>Traits implemented by this type (in addition to single inheritance).</summary>
		[JsonIgnore]
		public List<TraitSymbol> ImplementedTraits => Base is null ? _implementedTraits : Base.ImplementedTraits;
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

		public bool Implements(TraitSymbol trait)
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
		public TypeSymbol(string n, string s, Token? t = null, TypeSymbol? inherits = null, int argumentCount = 0, List<TraitSymbol>? implementedTraits = null)
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

	public class TraitSymbol : TypeSymbol
	{
		public TraitSymbol(string n, Token? t = null, TypeSymbol? inherits = null, int argumentCount = 0)
			: base(n, t, inherits ?? Builtins.Trait, argumentCount) { }
		public TraitSymbol(string n, string s, Token? t = null, TypeSymbol? inherits = null, int argumentCount = 0)
			: base(n, s, t, inherits ?? Builtins.Trait, argumentCount) { }
		public TraitSymbol(TypeSymbol basedef, TypeSymbol[]? arguments = null)
			: base(basedef, arguments) { }
	}

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

		private static Dictionary<TypeSymbol, Dictionary<TypeSymbol[], TypeSymbol>> _argTypeSymbols = [];
		private static List<TypeSymbol> _basicTypeSymbols = [];
		private static Dictionary<int, TypeSymbol> _tupleTypeSymbols = [];
		private static Dictionary<string, TypeSymbol> _tempTypeSymbols = [];

		private static Dictionary<TypeSymbol[], TypeSymbol> CreateArgsDict() =>
    		new Dictionary<TypeSymbol[], TypeSymbol>(new TypeSymbolArrayComparer());

		private static TypeSymbol[] PopulateArgs(TypeSymbol baseType, List<TypeSymbol>? args = null)
		{
			args ??= new List<TypeSymbol>(baseType.ArgumentCount);
			if (args.Count != baseType.ArgumentCount)
				for (int i = args.Count; i < baseType.ArgumentCount; i++)
					args.Add(Builtins.Any);

			return args.ToArray();
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

		public static TypeSymbol GetTempTypeSymbol(string name, List<TypeSymbol>? args = null)
		{
			if (!_tempTypeSymbols.TryGetValue(name, out var baseType))
			{
				baseType = new TypeSymbol(name, Symbol.TempSource, argumentCount: args?.Count ?? 0);
				_tempTypeSymbols[name] = baseType;
				baseType = GetTypeSymbol(baseType, args);
			}
			else
			{
				if (args is not null) 
				{
					if (args.Count != baseType.ArgumentCount)
					{
						if (baseType.ArgumentCount > 0)
							throw new ArgumentException("Inconsistent argument count for temp type symbol");
						else
						{
							//change preexisting uses of type symbol to have Any as the type args
							baseType._changeArgCount(args.Count);
							baseType.Arguments = Enumerable.Repeat(Builtins.Any, args.Count).ToArray();
							_basicTypeSymbols.Remove(baseType);
						}
					}
				}

				baseType = GetTypeSymbol(baseType, args);
			}

			return baseType;
		}
	}
}