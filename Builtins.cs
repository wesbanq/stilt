namespace stilt
{
	public static class Builtins
	{
		public static readonly Scope BuiltinScope;

		public static readonly TypeSymbol Any = new("Any", "<BUILTIN>");
		public static readonly TypeSymbol None = new("None", "<BUILTIN>");
		public static readonly TypeSymbol Bool = new("Bool", "<BUILTIN>");
		public static readonly TypeSymbol Num = new("Num", "<BUILTIN>");
		public static readonly TypeSymbol String = new("String", "<BUILTIN>");
		public static readonly TypeSymbol TaggedString = new("TaggedString", "<BUILTIN>");
		public static readonly TypeSymbol UUID = new("UUID", "<BUILTIN>");
		public static readonly TypeSymbol NBT = new("NBT", "<BUILTIN>");
		public static readonly TypeSymbol Attribute = new("Attribute", "<BUILTIN>");
		public static readonly TypeSymbol Tag = new("Tag", "<BUILTIN>");
		public static readonly TypeSymbol Module = new("Module", "<BUILTIN>");
		public static readonly TypeSymbol Decorator = new("Decorator", "<BUILTIN>");
		public static readonly TypeSymbol Array = new("Array", "<BUILTIN>", argumentCount: 1);
		public static readonly TypeSymbol Reference = new("Ref", "<BUILTIN>", argumentCount: 1);
		public static readonly TypeSymbol Table = new("Table", "<BUILTIN>", argumentCount: 1);
		public static readonly TypeSymbol Callable = new("Callable", "<BUILTIN>", argumentCount: 2);

		public static readonly VarSymbol IgnoreWarning = new("IgnoreWarning", "<BUILTIN>", Decorator);
		public static readonly VarSymbol PrivateByDefault = new("PrivateByDefault", "<BUILTIN>", Decorator);
		public static readonly VarSymbol ExplicitByDefault = new("ExplicitByDefault", "<BUILTIN>", Decorator);

		public static readonly TypeSymbol Whole = new("Whole", "<BUILTIN>", inherits: Num);
		public static readonly TypeSymbol Fractional = new("Fractional", "<BUILTIN>", inherits: Num);
		public static readonly TypeSymbol Byte = new("Byte", "<BUILTIN>", inherits: Whole);
		public static readonly TypeSymbol Short = new("Short", "<BUILTIN>", inherits: Whole);
		public static readonly TypeSymbol Int = new("Int", "<BUILTIN>", inherits : Whole);
		public static readonly TypeSymbol Long = new("Long", "<BUILTIN>", inherits : Whole);
		public static readonly TypeSymbol Float = new("Float", "<BUILTIN>", inherits : Fractional);
		public static readonly TypeSymbol Double = new("Double", "<BUILTIN>", inherits : Fractional);

		static Builtins()
		{
			BuiltinScope = new();
			foreach (var prop in typeof(Builtins).GetFields())
			{
				var value = prop.GetValue(null);
				if (value is Symbol symbol)
				{
					BuiltinScope.AddSymbol(symbol);
				}
			}
		}
	}
}
