using Newtonsoft.Json.Linq;

namespace slate.Compilation
{
	public class ObjectFile
	{
		public string Filepath;
		public string TextChecksum;
		public string InterfaceChecksum;
		public string CompilerVersion = Compiler.CompilerVersion;
		public IRGeneratorResult Result;
		public ParserResult ParserResult;

		[Newtonsoft.Json.JsonIgnore]
		public Dictionary<TimedEvents, Timer> Timers = [];

		private static readonly JsonSerializerSettings JsonSerializeSettings = new()
		{
			Formatting = Formatting.None,
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

		public IRGeneratorResult GenerateIR(ProgramArgs args)
		{
			Timers[TimedEvents.IRGeneration] = new Timer("IR generation");
			Result = Timers[TimedEvents.IRGeneration].Run(() => new IRGenerator(args, this).Generate());
			return Result;
		}

		protected ObjectFile() { } // For JSON deserialization

		public ObjectFile(string filepath, string textChecksum, string interfaceChecksum, IRGeneratorResult result, ParserResult parserResult)
		{
			Filepath = filepath;
			TextChecksum = textChecksum;
			InterfaceChecksum = interfaceChecksum;
			Result = result;
			ParserResult = parserResult;
		}
	}

	public class ParsedFile : ObjectFile
	{
		public Lexer? Lexer;
		public readonly FileText Text;
		public List<CompilationMessage> Errors => ParserResult?.CompilationIssues ?? [];
		public bool HasErrors => Errors.Any(e => e.Severity >= ErrorSeverity.Error);

		public new string TextChecksum => Text.GetSHA256Hash();
		public new string InterfaceChecksum => global::slate.Compilation.InterfaceChecksum.Compute(ParserResult?.RootScope, Filepath);

		public void Parse(ProgramArgs args)
		{
			Lexer = new Lexer(args, Filepath, Text);
			Timers.Add(TimedEvents.Lexing, new Timer("Lexing"));
			Timers[TimedEvents.Lexing].Run(() =>
			{
				Lexer.Lex();
			});

			Timers.Add(TimedEvents.Parsing, new Timer("Parsing"));
			Timers[TimedEvents.Parsing].Run(() =>
			{
				var parser = new Parser(args, Lexer);
				parser.ParseFile();
				ParserResult = parser.Result;
			});
		}

		public ParsedFile(string filepath, ObjectFile file) : base(filepath, file.TextChecksum, file.InterfaceChecksum, file.Result, file.ParserResult)
		{
			Text = new FileText(filepath);
		}

		public ParsedFile(string filepath)
		{
			Filepath = filepath;
			Text = new FileText(filepath);
		}

		public ParsedFile(FileText filetext)
		{
			Filepath = filetext.Filepath;
			Text = filetext;
		}
	}
}
