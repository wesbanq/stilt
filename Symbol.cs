using System;
using System.Collections.Generic;
using System.Text;

namespace stilt.AST
{
	public class Symbol
	{
		public string Name;
		public string Source;
		public bool IsTemp => Source.StartsWith("<TEMP>");

		public static bool operator ==(Symbol? left, Symbol? right)
		{
			if (left == null || right == null) return false;
			return left.Name.Equals(right.Name) && left.Source.Equals(right.Source);
		}

		public override bool Equals(object? other)
		{
			return other is Symbol && this == other as Symbol;
		}

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		public static bool operator !=(Symbol? left, Symbol? right)
		{
			return !(left == right);
		}

		public Symbol(string name, string src)
		{
			Name = name;
			Source = src;
		}
		public Symbol(string name)
		{
			Name = name;
			Source = "<TEMP>";
		}
	}

	public class VarSymbol : Symbol
	{
		public List<TypeSymbol>? Type = [TypeSymbol.Any];
		public VarDeclStmt? Declaration = null;
		public TypeSymbol? SingletonType => Type?.Count == 1 ? Type.First() : null;

		public VarSymbol(string n, string s, TypeSymbol type)
			: base(n, s)
		{
			Type = [type];
		}
		public VarSymbol(string n, string s, List<TypeSymbol>? type)
			: base(n, s)
		{
			Type = type;
		}
		public VarSymbol(string n)
			: base(n) { }
		public VarSymbol(string n, TypeSymbol t)
			: base(n) { Type = [t]; }
		public VarSymbol(string n, List<TypeSymbol> t)
			: base(n) { Type = t; }
	}
	public class FuncSymbol : Symbol
	{
		public FuncDeclStmt? Declaration = null;

		//return variables
		//public List<VarSymbol>? Return;
		public List<TypeSymbol>? Return;
		public List<VarSymbol>? Arguments;

		public FuncSymbol(string n, string s, FuncDeclStmt stmt)
			: base(n, s)
		{
			Declaration = stmt;
		}
		public FuncSymbol(string n, string s)
			: base(n, s)
		{ }
		public FuncSymbol(string n)
			: base(n)
		{ }
	}
	public class TypeSymbol : Symbol
	{
		public static readonly TypeSymbol Any = new("Any", "<BUILTIN>");
		public static readonly TypeSymbol None = new("None", "<BUILTIN>");
		public static readonly TypeSymbol Num = new("Num", "<BUILTIN>");
		public static readonly TypeSymbol String = new("String", "<BUILTIN>");
		public static readonly TypeSymbol Table = new("Table", "<BUILTIN>");
		public static readonly TypeSymbol Array = new("Array", "<BUILTIN>");
		public static readonly TypeSymbol Callable = new("Callable", "<BUILTIN>");

		public bool IsBuiltin => Source.StartsWith("<BUILTIN>");

		//public bool Explicit = false;
		//public bool Strong = false;
		public List<TypeSymbol>? Inherited = null;
		public TypeDeclStmt? Declaration = null;

		public TypeSymbol(string n, string s)
			: base(n, s) { }
		public TypeSymbol(string n)
			: base(n) { }
	}
}
