using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace stilt;

public static class Utils
{
	public static A? GetAttributeFromEnum<T, A>(T value)
		where T : Enum
		where A : Attribute
	{
		var a = typeof(T).GetField(value.ToString())?.GetCustomAttributes<A>()?.ToArray();
		if (a is not null && a.Length > 0)
			return a.First();
		else
			return null;
	}

	public static A[]? GetAttributesFromEnum<T, A>(T value)
		where T : Enum
		where A : Attribute
	{
		var a = typeof(T).GetField(value.ToString())?.GetCustomAttributes<A>()?.ToArray();
		if (a is not null && a.Length > 0)
			return a;
		else
			return null;
	}

	public static void Dump(object? obj, int l = 0, bool expanded = false)
	{
		var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
		Dump(obj, l, expanded, visited);
	}

	private static void Dump(object? obj, int l, bool expanded, HashSet<object> visited)
	{
		if (obj is null)
		{
			Console.WriteLine("null");
			return;
		}

		if (l == 0)
			Console.WriteLine();

		var type = obj.GetType();
		var indent = new string('\t', l);

		if (!type.IsValueType)
		{
			if (visited.Contains(obj))
			{
				Console.WriteLine($"{indent}{type.Name} <CYCLE>");
				return;
			}
			visited.Add(obj);
		}

		if (type.IsEnum)
		{
			Console.WriteLine($"{indent}{type.Name} = {obj}");
			return;
		}

		if (obj is IEnumerable enumerable && obj is not string)
		{
			Console.WriteLine($"{indent}{type.Name}");
			int index = 0;
			foreach (var item in enumerable)
			{
				Console.WriteLine($"{indent}\t[{index}]:");
				Dump(item, l + 2, expanded, visited);
				index++;
			}
			return;
		}

		Console.WriteLine($"{indent}{type.Name}");

		foreach (var prop in type.GetProperties())
		{
			try
			{
				DumpMember(prop.Name, prop.GetValue(obj), l, expanded, visited);
			}
			catch { }
		}

		foreach (var field in type.GetFields())
		{
			try
			{
				DumpMember(field.Name, field.GetValue(obj), l, expanded, visited);
			}
			catch { }
		}
	}

	private static void DumpMember(string name, object? value, int level, bool expanded, HashSet<object> visited)
	{
		var indent = new string('\t', level + 1);

		if (value is null)
		{
			Console.WriteLine($"{indent}{name} = null");
			return;
		}

		var valueType = value.GetType();

		if (value is IEnumerable enumerable && value is not string && value is not Enum)
		{
			Console.WriteLine($"{indent}{name}:");
			int index = 0;
			foreach (var item in enumerable)
			{
				Console.WriteLine($"{indent}\t[{index}]:");
				Dump(item, level + 2, expanded, visited);
				index++;
			}
			return;
		}

		if (valueType.IsPrimitive || valueType.IsEnum || value is string)
		{
			var displayValue = value is string str ? $"\"{Escape(str)}\"" : value.ToString();
			Console.WriteLine($"{indent}{name} = {displayValue}");
			return;
		}

		if (!expanded && (value is FileRange or FileText or Scope or List<Symbol> or Symbol or Type))
		{
			Console.WriteLine($"{indent}{name}: <HIDDEN>");
			return;
		}

		Console.WriteLine($"{indent}{name}:");
		Dump(value, level + 1, expanded, visited);
	}

	private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		public static readonly ReferenceEqualityComparer Instance = new();

		public new bool Equals(object? x, object? y)
		{
			return ReferenceEquals(x, y);
		}

		public int GetHashCode(object obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	public static string Escape(string s)
	{
		return s
			.Replace("\n", "\\n")
			.Replace("\r\n", "\\r\\n")
			.Replace("\t", "\\t")
			.Replace("\"", "\\\"")
			.Replace("\'", "\\\'")
			.Replace("\\", "\\\\");
	}

	public static string Unescape(string s)
	{
		var str = s
			.Replace("\\n", "\n")
			.Replace("\\r\\n", "\r\n")
			.Replace("\\t", "\t")
			.Replace("\\\"", "\"")
			.Replace("\\\'", "\'")
			.Replace("\\\\", "\\");

		str = Regex.Replace(Regex.Replace(Regex.Replace(
			str, @"\\u[\da-fA-F]{4}", m => ((char)int.Parse(m.Value.Substring(2), NumberStyles.HexNumber)).ToString()),
			@"\\U[\da-fA-F]{8}", m => ((char)int.Parse(m.Value.Substring(2), NumberStyles.HexNumber)).ToString()),
			@"\\x[\da-fA-F]{2}", m => ((char)int.Parse(m.Value.Substring(2), NumberStyles.HexNumber)).ToString());

		return str;
	}
}
