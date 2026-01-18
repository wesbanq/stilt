using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace stilt.AST
{
	public class Symbol
	{
		public string Name;
		//TODO (IMPORTANT VERY)
		//stop using filepaths
		public string Source;
		//actually use this
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

		public void Untemp(string src)
		{
			if (IsTemp)
			{
				Source = src;
			}
		}

		protected Symbol(string name, string src)
		{
			Name = name;
			Source = src;
		}
		protected Symbol(string name)
		{
			Name = name;
			Source = "<TEMP>";
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
			Type = new(type);
		}
		public VarSymbol(string n)
			: base(n) 
		{ }
	}

	public class TypeSymbol : Symbol
	{
		public TypeSymbol? Base;
		public TypeSymbol? Inherits => Base == null ? _inherits : Base.Inherits;
		private TypeSymbol? _inherits = null;

		public int ArgumentCount = 0;
		public TypeSymbol[]? Arguments;

		public bool IsComplete => Base == null ? true : ArgumentCount == Arguments?.Length;
		public bool IsSimple => Base == null;

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

		public TypeSymbol(string n, int argumentCount = 0, TypeSymbol? inherits = null)
			: base(n)
		{
			ArgumentCount = argumentCount;
			_inherits = inherits;
		}
		public TypeSymbol(string n, string s, int argumentCount = 0, TypeSymbol? inherits = null)
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
