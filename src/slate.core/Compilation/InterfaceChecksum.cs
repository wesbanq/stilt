using System.Security.Cryptography;
using System.Text;
using stilt;
using stilt.AST;

namespace stilt.Compilation
{
	/// <summary>
	/// Computes a deterministic, content-based checksum of a module's public interface
	/// for object-file cache invalidation. Uses SHA256; does not rely on GetHashCode().
	/// </summary>
	public static class InterfaceChecksum
	{
		/// <summary>
		/// Returns a deterministic hex string (SHA256) of the canonical interface of the given scope
		/// for the specified file. Returns empty string if rootScope is null or no symbols belong to the file.
		/// </summary>
		public static string Compute(Scope? rootScope, string filepath)
		{
			if (rootScope is null)
				return "";

			var symbols = rootScope.Symbols
				.Where(s => !s.IsBuiltin && s.Source == filepath)
				.OrderBy(s => s.Name, StringComparer.Ordinal)
				.ToList();

			if (symbols.Count == 0)
				return "";

			var visited = new HashSet<TypeSymbol>();
			var parts = symbols.Select(s => CanonicalizeSymbol(s, visited));
			var canonical = string.Join("|", parts);
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
			return Convert.ToHexString(hash);
		}

		private static string CanonicalizeSymbol(Symbol s, HashSet<TypeSymbol> visited)
		{
			return s switch
			{
				VarSymbol v => $"var:{v.Name}:{CanonicalizeType(v.Type, visited)}",
				TypeSymbol t => CanonicalizeTypeSymbol(t, visited),
				_ => $"sym:{s.Name}:{s.Source}"
			};
		}

		private static string CanonicalizeTypeSymbol(TypeSymbol t, HashSet<TypeSymbol> visited)
		{
			var basePart = t.Inherits is null ? "" : CanonicalizeType(t.Inherits, visited);
			var argsPart = t.Arguments is null || t.ArgumentCount == 0
				? ""
				: string.Join(",", t.Arguments.Take(t.ArgumentCount).Select(a => CanonicalizeType(a, visited)));
			var membersPart = t.Members.Count == 0
				? ""
				: string.Join(",", t.Members.OrderBy(m => m.Name, StringComparer.Ordinal).Select(m => CanonicalizeSymbol(m, visited)));

			return $"type:{t.Name}:{t.Source}:base:{basePart}:args:[{argsPart}]:members:[{membersPart}]";
		}

		private static string CanonicalizeType(TypeSymbol t, HashSet<TypeSymbol> visited)
		{
			if (t is null)
				return "";

			if (t.IsBuiltin)
			{
				if (t.ArgumentCount == 0 || t.Arguments is null)
					return $"<BUILTIN>:{t.Name}";
				var args = string.Join(",", t.Arguments.Take(t.ArgumentCount).Select(a => CanonicalizeType(a, visited)));
				return $"<BUILTIN>:{t.Name}({args})";
			}

			if (visited.Contains(t))
				return $"<cycle>:{t.Source}:{t.Name}";

			visited.Add(t);
			try
			{
				var basePart = t.Inherits is null ? "" : CanonicalizeType(t.Inherits, visited);
				var argsPart = t.Arguments is null || t.ArgumentCount == 0
					? ""
					: string.Join(",", t.Arguments.Take(t.ArgumentCount).Select(a => CanonicalizeType(a, visited)));

				if (string.IsNullOrEmpty(argsPart))
					return $"{t.Source}:{t.Name}:base:{basePart}";
				return $"{t.Source}:{t.Name}({argsPart}):base:{basePart}";
			}
			finally
			{
				visited.Remove(t);
			}
		}
	}
}
