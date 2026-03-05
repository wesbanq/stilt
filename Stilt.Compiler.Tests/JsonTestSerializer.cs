using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using stilt;
using stilt.AST;
using stilt.IR;

namespace Stilt.Compiler.Tests;

public static class JsonTestSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new AstJsonContractResolver(ExclusionPreset.Ast),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static string SerializeAst(ParserResult result) =>
        SerializeObject(result.Statements);

    public static string SerializeAstStatements(List<Stmt> stmts) =>
        SerializeObject(stmts);

    public static string SerializeIrMain(Block mainBlock) =>
        SerializeObject(mainBlock);

    public static string SerializeIrGraph(IRGeneratorResult result) =>
        SerializeObject(result);

    public static string NormalizeJson(string json)
    {
        var token = JToken.Parse(json);
        SortProperties(token);
        return token.ToString(Formatting.Indented);
    }

    private static string SerializeObject(object obj) =>
        JsonConvert.SerializeObject(obj, Settings);

    private static void SortProperties(JToken token)
    {
        if (token is JObject obj)
        {
            var props = obj.Properties().ToList();
            foreach (var p in props)
            {
                p.Remove();
            }

            foreach (var p in props.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                SortProperties(p.Value);
                obj.Add(p);
            }
        }
        else if (token is JArray arr)
        {
            foreach (var child in arr)
                SortProperties(child);
        }
    }
}

