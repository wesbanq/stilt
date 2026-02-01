using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace stilt.AST
{
	public abstract class Symbol
	{
		public string Name;
		//TODO (IMPORTANT VERY)
		//stop using filepaths
		public string Source;
		public Stmt? Declaration;
		public List<TokenType> Specifiers = [];
		public Token? Identifier;

		public bool IsBuiltin => Source.StartsWith("<BUILTIN>");
		public bool IsTemp => Source.StartsWith("<TEMP>");

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

		protected Symbol(string name, Token? token, string src = "<TEMP>")
		{
			Name = name;
			Source = src;
			Identifier = token;
		}
		protected Symbol(string name, string src = "<TEMP>")
		{
			Name = name;
			Source = src;
		}
	}

	public class VarSymbol : Symbol
	{
		public TypeSymbol Type = Builtins.Any;

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
		public List<Symbol> Members = [];

		public TypeSymbol? Base;

		public TypeSymbol? Inherits => Base is null ? _inherits : Base.Inherits;
		private TypeSymbol? _inherits;

		public int ArgumentCount = 0;
		public TypeSymbol[]? Arguments;

		//public bool IsComplete => Base is null ? true : ArgumentCount == Arguments?.Length;
		//public bool IsSimple => Base is null;

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
			hash.Add(Source);
			hash.Add(ArgumentCount);
			if (Arguments is not null)
			{
				for (int i = 0; i < Arguments.Length; i++)
				{
					hash.Add(Arguments[i]);
				}
			}
			return hash.ToHashCode();
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

		public TypeSymbol(string n, Token? t = null, TypeSymbol? inherits = null, int argumentCount = 0)
			: base(n, t)
		{
			ArgumentCount = argumentCount;
			_inherits = inherits;
		}
		public TypeSymbol(string n, string s, Token? t = null, TypeSymbol? inherits = null, int argumentCount = 0)
			: base(n, t, s)
		{
			ArgumentCount = argumentCount;
			_inherits = inherits;
		}
		public TypeSymbol(TypeSymbol basedef, TypeSymbol[]? arguments = null)
			: base(basedef.Name, basedef.Source)
		{
			Base = basedef;
			ArgumentCount = basedef.ArgumentCount;
			Arguments = arguments;

			if (Arguments is not null && Arguments.Length != ArgumentCount)
				throw new ArgumentException();
		}
	}
}