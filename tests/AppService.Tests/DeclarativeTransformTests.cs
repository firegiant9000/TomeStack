using System.Text;
using System.Text.Json.Nodes;
using TomeStack.AppService.Extensions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M6 slice 3 (ADR-011 option A): the declarative transform language and the import-file reader. Every bound has a
/// refusal test, and the language is fuzzed: whatever a transform or an input holds, the result is a value or a
/// TransformException, within the time limit, and never anything else.
/// </summary>
public class DeclarativeTransformTests
{
    private static DeclarativeTransform Parse(string json) => DeclarativeTransform.Parse(Encoding.UTF8.GetBytes(json));

    private static string Code(Action action) => Assert.Throws<TransformException>(action).Code;

    private static JsonNode? Run(string transform, string input = "{}") => Parse(transform).Run(JsonNode.Parse(input));

    [Fact]
    public void Each_operator_does_what_the_docs_say()
    {
        var input = """{"name":"Test Mote","level":"2","tags":["a","b"],"nested":{"x":1},"school":"Evocation","list":"a; b ;;c"}""";
        Assert.Equal("Test Mote", Run("""{"output":{"get":"/name"}}""", input)!.GetValue<string>());
        Assert.Equal("Test Mote lv 2 {braces}", Run("""{"output":{"template":"{/name} lv {/level} {{braces}}"}}""", input)!.GetValue<string>());
        Assert.Equal(2, Run("""{"output":{"int":{"get":"/level"}}}""", input)!.GetValue<long>());
        Assert.Null(Run("""{"output":{"int":{"get":"/name"}}}""", input));
        Assert.Equal("evocation", Run("""{"output":{"lower":{"get":"/school"}}}""", input)!.GetValue<string>());
        Assert.Equal("EVO", Run("""{"tables":{"s":{"evocation":"EVO"}},"output":{"lookup":{"table":"s","key":{"lower":{"get":"/school"}}}}}""", input)!.GetValue<string>());
        Assert.Equal("none", Run("""{"tables":{"s":{}},"output":{"lookup":{"table":"s","key":"x","default":"none"}}}""", input)!.GetValue<string>());
        Assert.Equal("""["A","B"]""", Run("""{"output":{"map":{"over":{"get":"/tags"},"emit":{"upper":{"get":""}}}}}""", input)!.ToJsonString());
        Assert.Equal("""["b"]""", Run("""{"output":{"filter":{"over":{"get":"/tags"},"where":{"equals":[{"get":""},"b"]}}}}""", input)!.ToJsonString());
        Assert.Equal("a-b", Run("""{"output":{"join":{"items":{"get":"/tags"},"separator":"-"}}}""", input)!.GetValue<string>());
        Assert.Equal("""["a","b","c"]""", Run("""{"output":{"split":{"text":{"get":"/list"},"separator":";"}}}""", input)!.ToJsonString());
        Assert.Equal("""{"k":1,"l":[true,null]}""", Run("""{"output":{"object":{"k":{"get":"/nested/x"},"l":{"array":[{"exists":{"get":"/name"}},{"get":"/missing"}]}}}}""", input)!.ToJsonString());
        Assert.Equal("yes", Run("""{"output":{"if":{"cond":{"all":[{"startsWith":[{"get":"/name"},"Test"]},{"not":{"exists":{"get":"/missing"}}}]},"then":"yes","else":"no"}}}""", input)!.GetValue<string>());
        Assert.True(Run("""{"output":{"any":[false,{"equals":[{"count":{"get":"/tags"}},2]}]}}""", input)!.GetValue<bool>());
        Assert.Equal("""{"a":[1]}""", Run("""{"output":{"const":{"a":[1]}}}""", input)!.ToJsonString());
        // The root stays reachable inside a loop.
        Assert.Equal("""["Test Mote:a","Test Mote:b"]""", Run("""{"output":{"map":{"over":{"get":"/tags"},"emit":{"template":"{#/name}:{/}"}}}}""".Replace("{/}", "{/0}", StringComparison.Ordinal), """{"name":"Test Mote","tags":[["a"],["b"]]}""")!.ToJsonString());
    }

    [Theory]
    [InlineData("""{"output":{"get":"/a","template":"x"}}""", "transform.invalid")] // two operators
    [InlineData("""{"output":{"eval":"1+1"}}""", "transform.invalid")] // no such operator
    [InlineData("""{"output":{"regex":"^a"}}""", "transform.invalid")] // no regular expressions, ever
    [InlineData("""{"output":[1,2]}""", "transform.invalid")] // lists are written as {"array": …}
    [InlineData("""{"output":{"get":"name"}}""", "transform.invalid")] // not a JSON Pointer
    [InlineData("""{"output":{"template":"{name}"}}""", "transform.invalid")]
    [InlineData("""{"output":{"template":"a } b"}}""", "transform.invalid")]
    [InlineData("""{"output":{"equals":[1]}}""", "transform.invalid")]
    [InlineData("""{"output":{"map":{"over":[]}}}""", "transform.invalid")]
    [InlineData("""{"output":1,"script":"x"}""", "transform.invalid")]
    [InlineData("""{"tables":[],"output":1}""", "transform.invalid")]
    [InlineData("""not json""", "transform.invalid")]
    [InlineData("""{"nothing":1}""", "transform.invalid")]
    public void A_transform_that_is_not_the_language_is_refused_before_it_runs(string transform, string code) =>
        Assert.Equal(code, Code(() => Parse(transform)));

    [Fact]
    public void Each_bound_is_refused_with_its_own_code()
    {
        // Depth.
        var deep = new StringBuilder();
        for (var i = 0; i < DeclarativeTransform.MaxDepth + 2; i++) deep.Append("{\"not\":");
        deep.Append("true").Append('}', DeclarativeTransform.MaxDepth + 2);
        Assert.Contains(Code(() => Parse($$"""{"output":{{deep}}}""")), new[] { "transform.too-deep", "transform.invalid" });
        // Table size.
        var table = string.Join(",", Enumerable.Range(0, DeclarativeTransform.MaxTableEntries + 1).Select(i => $"\"k{i}\":{i}"));
        Assert.Equal("transform.table-too-large", Code(() => Parse("{\"tables\":{\"t\":{" + table + "}},\"output\":1}")));
        // Document size.
        Assert.Equal("transform.too-large", Code(() => DeclarativeTransform.Parse(new byte[DeclarativeTransform.MaxDocumentBytes + 1])));
        // Nested iterations multiply: 400 × 400 = 160,000 > 100,000.
        var rows = "[" + string.Join(",", Enumerable.Range(0, 400)) + "]";
        var nested = Parse("""{"output":{"map":{"over":{"getRoot":"/rows"},"emit":{"map":{"over":{"getRoot":"/rows"},"emit":1}}}}}""");
        Assert.Equal("transform.iterations", Assert.Throws<TransformException>(() => nested.Run(JsonNode.Parse($$"""{"rows":{{rows}}}"""))).Code);
        // Fuel: a small budget runs out.
        var loop = Parse("""{"output":{"map":{"over":{"getRoot":"/rows"},"emit":{"template":"{#/rows/0}{#/rows/1}"}}}}""");
        Assert.Equal("transform.fuel", Assert.Throws<TransformException>(() => loop.Run(JsonNode.Parse($$"""{"rows":{{rows}}}"""), fuel: 500)).Code);
        // Time: a limit of zero is reached at once.
        Assert.Equal("transform.timeout", Assert.Throws<TransformException>(() => loop.Run(JsonNode.Parse($$"""{"rows":{{rows}}}"""), timeLimit: TimeSpan.Zero)).Code);
        // Output: joining a big list again and again passes 5 MB.
        var big = new string('x', 1_000_000);
        var grow = Parse("""{"output":{"join":{"items":{"array":[{"getRoot":"/s"},{"getRoot":"/s"},{"getRoot":"/s"},{"getRoot":"/s"},{"getRoot":"/s"},{"getRoot":"/s"}]}}}}""");
        Assert.Equal("transform.output-too-large", Assert.Throws<TransformException>(() => grow.Run(new JsonObject { ["s"] = big })).Code);
        // A missing table.
        Assert.Equal("transform.table-missing", Code(() => Run("""{"output":{"lookup":{"table":"nope","key":"x"}}}""")));
    }

    [Fact]
    public void The_input_reader_parses_strict_CSV_and_bounded_JSON()
    {
        var csv = ExtensionInput.Parse(Encoding.UTF8.GetBytes("Name,Text\r\n\"A, b\",\"say \"\"hi\"\"\"\nC,\n"), "csv");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"rows":[{"Name":"A, b","Text":"say \"hi\""},{"Name":"C","Text":""}]}"""), csv), csv.ToJsonString());
        Assert.Equal("input.invalid-csv", Code(() => ExtensionInput.Parse(Encoding.UTF8.GetBytes("A,B\n1\n"), "csv")));
        Assert.Equal("input.invalid-csv", Code(() => ExtensionInput.Parse(Encoding.UTF8.GetBytes("A\nx\"y\n"), "csv")));
        Assert.Equal("input.invalid-csv", Code(() => ExtensionInput.Parse(Encoding.UTF8.GetBytes("A\n\"open\n"), "csv")));
        Assert.Equal("input.invalid-csv", Code(() => ExtensionInput.Parse(Encoding.UTF8.GetBytes("A,A\n1,2\n"), "csv")));
        Assert.Equal("input.invalid-csv", Code(() => ExtensionInput.Parse(Encoding.UTF8.GetBytes("A\n" + new string('x', ExtensionInput.MaxCsvFieldChars + 1)), "csv")));
        var manyRows = "A\n" + string.Concat(Enumerable.Repeat("1\n", ExtensionInput.MaxCsvRows + 1));
        Assert.Equal("input.too-many-rows", Code(() => ExtensionInput.Parse(Encoding.UTF8.GetBytes(manyRows), "csv")));
        Assert.Equal("input.too-large", Code(() => ExtensionInput.Parse(new byte[ExtensionInput.MaxBytes + 1], "json")));
        Assert.Equal("input.invalid-json", Code(() => ExtensionInput.Parse(Encoding.UTF8.GetBytes(new string('[', 40) + new string(']', 40)), "json")));
        Assert.Equal("input.encoding", Code(() => ExtensionInput.Parse([0xFF, 0xFE, 0x00], "json")));
    }

    /// <summary>
    /// Random transforms over random inputs: every run ends in a value or a TransformException, quickly. A fixed seed keeps
    /// the run repeatable; a failure prints the transform.
    /// </summary>
    [Fact]
    public void Fuzzed_transforms_and_inputs_only_ever_give_a_value_or_a_transform_error()
    {
        var random = new Random(20260929);
        string[] pointers = ["", "/a", "/b/0", "/rows", "/rows/1/x", "/~0/~1", "/9999"];
        JsonNode? Literal() => random.Next(6) switch
        {
            0 => null, 1 => JsonValue.Create(true), 2 => JsonValue.Create(random.Next(-5, 50)),
            3 => JsonValue.Create("a;b;c"), 4 => JsonValue.Create("{/a}"), _ => JsonValue.Create("x"),
        };
        JsonObject Op(string name, JsonNode? arg) => new() { [name] = arg };
        JsonObject Args(params (string Key, JsonNode? Value)[] pairs)
        {
            var obj = new JsonObject();
            foreach (var (key, value) in pairs) obj[key] = value;
            return obj;
        }
        JsonNode? Expression(int depth)
        {
            if (depth > 6 || random.Next(4) == 0)
                return Literal();
            var p = pointers[random.Next(pointers.Length)];
            JsonNode? E() => Expression(depth + 1);
            return random.Next(20) switch
            {
                0 => Op("get", p),
                1 => Op("getRoot", p),
                2 => Op("template", "{" + p + "} and {#" + p + "}"),
                3 => Op("map", Args(("over", E()), ("emit", E()))),
                4 => Op("filter", Args(("over", E()), ("where", E()))),
                5 => Op("join", Args(("items", E()), ("separator", E()))),
                6 => Op("split", Args(("text", E()), ("separator", E()))),
                7 => Op("object", Args(("k", E()))),
                8 => Op("array", new JsonArray(E(), E())),
                9 => Op("if", Args(("cond", E()), ("then", E()), ("else", E()))),
                10 => Op("equals", new JsonArray(E(), E())),
                11 => Op("startsWith", new JsonArray(E(), E())),
                12 => Op("lookup", Args(("table", "t"), ("key", E()), ("default", E()))),
                13 => Op("int", E()),
                14 => Op("count", E()),
                15 => Op("not", E()),
                16 => Op("all", new JsonArray(E())),
                17 => Op("text", E()),
                18 => Op("exists", E()),
                _ => Op("const", Literal()),
            };
        }
        var input = JsonNode.Parse("""{"a":"x","b":[1,"2",null],"rows":[{"x":1},{"x":"y"},[1,2,3]],"~":{"/":1}}""");
        for (var i = 0; i < 2_000; i++)
        {
            var document = new JsonObject { ["tables"] = new JsonObject { ["t"] = new JsonObject { ["x"] = 1, ["1"] = "one" } }, ["output"] = Expression(0) };
            var text = document.ToJsonString();
            try
            {
                Parse(text).Run(input, fuel: 100_000, timeLimit: TimeSpan.FromSeconds(2));
            }
            catch (TransformException)
            {
                // A refusal with a code is a fine outcome.
            }
            catch (Exception ex)
            {
                Assert.Fail($"{ex.GetType().Name} for transform {text}");
            }
        }
    }
}
