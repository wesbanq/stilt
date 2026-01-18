using System;
using System.Collections.Generic;
using System.Text;

namespace stilt.AST
{
	public abstract class Symbol
	{
		public string Name;
		public string Source;

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

	}

	public class TempSymbol(string n, string s) : Symbol(n, s) { }

	public class VarSymbol(string n, string s) : Symbol(n, s)
	{
		public TypeSymbol Type = TypeSymbol.Any;
		public VarDeclStmt? Declaration = null;

		public VarSymbol(string n, string s, TypeSymbol type)
			: this(n, s)
		{
			Type = type;
		}
	}
	public class FuncSymbol(string n, string s) : Symbol(n, s)
	{
		public FuncDeclStmt? Declaration = null;
		public TypeSymbol ReturnType = TypeSymbol.Any;

		public FuncSymbol(string n, string s, FuncDeclStmt stmt)
			: this(n, s)
		{
			Declaration = stmt;
		}
	}
	public class TypeSymbol(string n, string s) : Symbol(n, s)
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
	}
}
