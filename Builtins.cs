using stilt.AST;
using System;
using System.Collections.Generic;
using System.Text;

namespace stilt
{
	public static class Builtins
	{
		public static readonly TypeSymbol Any = new("Any", "<BUILTIN>");
		public static readonly TypeSymbol None = new("None", "<BUILTIN>");
		public static readonly TypeSymbol Num = new("Num", "<BUILTIN>");
		public static readonly TypeSymbol String = new("String", "<BUILTIN>");
		public static readonly TypeSymbol UUID = new("UUID", "<BUILTIN>");
		public static readonly TypeSymbol Decorator = new("Decorator", "<BUILTIN>");
		public static readonly TypeSymbol Array = new("Array", "<BUILTIN>", argumentCount: 1);
		public static readonly TypeSymbol Table = new("Table", "<BUILTIN>", argumentCount: 1);
		public static readonly TypeSymbol Callable = new("Callable", "<BUILTIN>", argumentCount: 1);

		public static readonly VarSymbol IgnoreWarning = new("IgnoreWarning", "<BUILTIN>", Decorator);
		public static readonly VarSymbol PrivateByDefault = new("PrivateByDefault", "<BUILTIN>", Decorator);
		public static readonly VarSymbol ExplicitByDefault = new("ExplicitByDefault", "<BUILTIN>", Decorator);

		public static readonly TypeSymbol Whole = new("Whole", "<BUILTIN>", Num);
		public static readonly TypeSymbol Fractional = new("Fractional", "<BUILTIN>", Num);
		public static readonly TypeSymbol Byte = new("Callable", "<BUILTIN>", Whole);
		public static readonly TypeSymbol Short = new("Callable", "<BUILTIN>", Whole);
		public static readonly TypeSymbol Int = new("Callable", "<BUILTIN>", Whole);
		public static readonly TypeSymbol Long = new("Callable", "<BUILTIN>", Whole);
		public static readonly TypeSymbol Float = new("Callable", "<BUILTIN>", Fractional);
		public static readonly TypeSymbol Double = new("Callable", "<BUILTIN>", Fractional);
	}
}
