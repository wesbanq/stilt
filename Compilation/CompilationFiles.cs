using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using stilt.IR;

namespace stilt.Compilation
{
    public class ObjectFile
	{
		public string TextChecksum;
		public string InterfaceChecksum;
		public string CompilerVersion = Program.CompilerVersion;
		public IRGeneratorResult Result;
		public Parser Parser;


		private static readonly JsonSerializerSettings JsonSerializeSettings = new()
		{
			Formatting = Formatting.None,
			// Use Objects (not All) to avoid forward-reference resolution issues.
			// All adds $id to every object including collection elements, which can cause
			// "Error reading object reference" when $ref is encountered before $id in document order.
			PreserveReferencesHandling = PreserveReferencesHandling.Objects,
			TypeNameHandling = TypeNameHandling.All,
			ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
			MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
		};

		private static readonly JsonSerializerSettings JsonDeserializeSettings = new()
		{
			Formatting = Formatting.None,
			PreserveReferencesHandling = PreserveReferencesHandling.None,
			TypeNameHandling = TypeNameHandling.All,
			ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
			MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
		};

		public string Serialize()
		{
			return JsonConvert.SerializeObject(this, JsonSerializeSettings);
		}

		/// <summary>
		/// Inlines all $ref in the JSON by replacing them with deep clones of the referenced objects.
		/// This avoids "Error reading object reference" during deserialization when the default
		/// reference resolver fails on complex/forward references.
		/// </summary>
		private static string InlineJsonReferences(string json)
		{
			var root = JToken.Parse(json);
			var idToToken = new Dictionary<string, JToken>();
			CollectIds(root, idToToken);
			return InlineRefs(root, idToToken).ToString();
		}

		private static void CollectIds(JToken token, Dictionary<string, JToken> idToToken)
		{
			if (token is JObject obj)
			{
				if (obj["$id"] is JValue idVal && idVal.Value is string id)
					idToToken[id] = obj;
				foreach (var prop in obj.Properties().ToList())
					CollectIds(prop.Value, idToToken);
			}
			else if (token is JArray arr)
			{
				foreach (var item in arr)
					CollectIds(item, idToToken);
			}
		}

		private static JToken InlineRefs(JToken token, Dictionary<string, JToken> idToToken)
		{
			if (token is JObject obj)
			{
				if (obj["$ref"] is JValue refVal && refVal.Value is string refId
					&& idToToken.TryGetValue(refId, out var referenced))
				{
					return InlineRefs(referenced.DeepClone(), idToToken);
				}
				var result = new JObject();
				foreach (var prop in obj.Properties())
				{
					if (prop.Name == "$id" || prop.Name == "$ref")
						continue;
					result[prop.Name] = InlineRefs(prop.Value, idToToken);
				}
				return result;
			}
			if (token is JArray arr)
			{
				return new JArray(arr.Select(item => InlineRefs(item, idToToken)));
			}
			return token.DeepClone();
		}

		public static ObjectFile? Deserialize(string serialized)
		{
			try
			{
				var inlined = InlineJsonReferences(serialized);
				return JsonConvert.DeserializeObject<ObjectFile>(inlined, JsonDeserializeSettings);
			}
			catch (JsonException e)
			{
				Console.WriteLine($"Error deserializing object file: {e.Message}");
				return null;
			}
		}

		public ObjectFile(string textChecksum, string interfaceChecksum, IRGeneratorResult result, Parser parser)
		{
			TextChecksum = textChecksum;
			InterfaceChecksum = interfaceChecksum;
			Result = result;
			Parser = parser;
		}
	}

	public class ParsedFile
	{
		public Lexer? Lexer;
		public Parser Parser;
		public IRGenerator IR;

		public string Filepath;
		public readonly FileText Text;
		public Dictionary<TimedEvents, Timer> Timers = [];
		public List<CompilationMessage> Errors => Parser?.CompilationIssues ?? [];

		public string TextChecksum => Text.GetSHA256Hash();
		public string InterfaceChecksum => 
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", Parser?.RootScope.Symbols.Select(s => s.GetHashCode()) ?? []))));

		public void Parse(ProgramArgs args)
		{
			Lexer = new Lexer(args, Filepath, Text);
			Timers.Add(TimedEvents.Lexing, new Timer("Lexing"));
			Timers[TimedEvents.Lexing].Run(() =>
			{
				Lexer.Lex();
			});

			Parser = new Parser(args, Lexer);
			Timers.Add(TimedEvents.Parsing, new Timer("Parsing"));
			Timers[TimedEvents.Parsing].Run(() =>
			{
				Parser.ParseFile();
			});
		}

		public void Generate(ProgramArgs args)
		{
			IR = new IRGenerator(args, this);
			Timers.Add(TimedEvents.IRGeneration, new Timer("IRGeneration"));
			Timers[TimedEvents.IRGeneration].Run(() =>
			{
				IR.GenerateIR();
			});
		}

		public ParsedFile(string filepath, ObjectFile file)
		{
			Filepath = filepath;
			Text = new(Filepath);
			Parser = file.Parser;
			IR = new IRGenerator(file.Result);
		}

		public ParsedFile(string filepath)
		{
			Filepath = filepath;
			Text = new(Filepath);
		}
	}
}