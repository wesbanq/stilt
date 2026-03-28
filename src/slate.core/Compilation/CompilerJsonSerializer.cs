using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace slate
{
	/// <summary>
	/// JSON serialization for compiler dumps (AST, IR, etc.). Used by the CLI -j option and by the test suite for golden comparison.
	/// </summary>
	public static class CompilerJsonSerializer
	{
		public enum ExclusionPreset
		{
			None,
			Base,
			Ast,
			Lexer,
			/// <summary>Lexer token dumps: string enum names, FileRange keeps Start/End/Text only (via Base exclusions).</summary>
			Tokens
		}

		private static IEnumerable<string>? GetExcludedPropertyNames(ExclusionPreset preset)
		{
			IEnumerable<string> baseProps = ["FullRange", "InnerRange", "TextLines", "StartLineAndColumn", "EndLineAndColumn", "Length"];
			return preset switch
			{
				ExclusionPreset.None => null,
				ExclusionPreset.Base => baseProps,
				ExclusionPreset.Ast => baseProps.Concat(new[] { "Scope", "RootScope", "Args" }),
				ExclusionPreset.Lexer => baseProps.Concat(new[] { "Filepath", "Args", "CurrentToken", "CurrentPos", "Text" }),
				ExclusionPreset.Tokens => baseProps.Concat(new[] { "IsUnimplemented", "IsSpecifier" }),
				_ => null
			};
		}

		/// <summary>
		/// Serializes an object to JSON using the same settings and optional property exclusions as the CLI -j dump.
		/// </summary>
		public static string SerializeToJson(object? obj, ExclusionPreset preset = ExclusionPreset.None)
		{
			var settings = new JsonSerializerSettings
			{
				ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
				Formatting = Formatting.Indented
			};
			if (preset == ExclusionPreset.Tokens)
				settings.Converters.Add(new StringEnumConverter());
			if (GetExcludedPropertyNames(preset) is { } names && names.Any())
			{
				settings.ContractResolver = new ExcludePropertiesContractResolver(names);
			}
			return JsonConvert.SerializeObject(obj, settings);
		}

		private sealed class ExcludePropertiesContractResolver : DefaultContractResolver
		{
			private readonly HashSet<string> _exclude;

			internal ExcludePropertiesContractResolver(IEnumerable<string> propertyNames)
			{
				_exclude = new HashSet<string>(propertyNames, StringComparer.Ordinal);
			}

			protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
			{
				var prop = base.CreateProperty(member, memberSerialization);
				if (prop.PropertyName is { } name && _exclude.Contains(name))
					prop.ShouldSerialize = _ => false;
				return prop;
			}
		}
	}
}
