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
		public static readonly TypeSymbol Array = new("Array", "<BUILTIN>", 1);
		public static readonly TypeSymbol Table = new("Table", "<BUILTIN>", 2);
		public static readonly TypeSymbol Callable = new("Callable", "<BUILTIN>", 2);
	}
}
