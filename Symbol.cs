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

		protected Symbol(string name, string src = "<TEMP>")
		{
			Name = name;
			Source = src;
		}
	}

	public class VarSymbol : Symbol
	{
		public TypeSymbol Type = Builtins.Any;

		public VarSymbol(string n, string s, TypeSymbol type)
			: base(n, s)
		{
			Type = type;
		}
		public VarSymbol(string n, TypeSymbol type)
			: base(n)
		{
			Type = type;
		}
		public VarSymbol(string n)
			: base(n)
		{ }
	}

	public class TypeSymbol : Symbol
	{
		public TypeSymbol? Base;

		public TypeSymbol? Inherits => Base is null ? _inherits : Base.Inherits;
		private TypeSymbol? _inherits;

		public int ArgumentCount = 0;
		public TypeSymbol[]? Arguments;

		//public bool IsComplete => Base == null ? true : ArgumentCount == Arguments?.Length;
		//public bool IsSimple => Base == null;

		public static bool operator ==(TypeSymbol? left, TypeSymbol? right)
		{
			if (left is null && right is null) return true;
			if (left is null || right is null) return false;
			if (left.ArgumentCount != right.ArgumentCount) return false;

			for (int i = 0; i < left.ArgumentCount; i++)
			{
				if (left.Arguments[i] != right.Arguments[i])
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

		public bool InheritsFrom(TypeSymbol from)
		{
			var currentType = this;
			while (currentType != null)
			{
				if (currentType == from)
					return true;
				currentType = currentType.Inherits;
			}
			return false;
		}

		public TypeSymbol(string n, TypeSymbol? inherits = null, int argumentCount = 0)
			: base(n)
		{
			ArgumentCount = argumentCount;
			_inherits = inherits;
		}
		public TypeSymbol(string n, string s, TypeSymbol? inherits = null, int argumentCount = 0)
			: base(n, s)
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

			if (Arguments != null && Arguments.Length != ArgumentCount)
				throw new ArgumentException();
		}
	}
}