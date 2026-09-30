using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Extensions;

/// <summary>
/// ADR-011 option A (owner, 2026-09-29): the declarative transform language extensions carry. It is data that this
/// interpreter reads; no third-party code runs, and nothing it evaluates can reach a file, the network, the clipboard or
/// anything outside the one input it is given. Every construct is bounded: a step budget (fuel) that also counts template
/// expansion, a wall-clock limit, a cap on the product of nested iterations, bounded expression depth, mapping tables of at
/// most <see cref="MaxTableEntries"/> entries, an output limit, and no regular expressions (matching is exact or by
/// prefix). Documented in docs/features/extensions.md ("The transform language").
/// </summary>
/// <remarks>
/// A transform document is <c>{ "tables"?: { name: { key: value } }, "output": expression }</c>. An expression is a JSON
/// literal (a string, number, boolean or null is itself) or an object with exactly one operator key:
/// <c>const, get, getRoot, template, lookup, map, filter, join, object, array, if, equals, startsWith, exists, not, all,
/// any, int, text, lower, upper, split, count</c>. See docs/schemas/extension-transform.v1.schema.json.
/// </remarks>
public sealed class DeclarativeTransform
{
    public const long MaxDocumentBytes = 1024 * 1024;
    public const int MaxTableEntries = 10_000;
    public const int MaxDepth = 64;
    /// <summary>Steps per run. Copying data costs one step per node and one per 16 characters, so this also bounds memory.</summary>
    public const long DefaultFuel = 4_000_000;
    public const long MaxIterationProduct = 100_000;
    public const int MaxOutputChars = 5 * 1024 * 1024;
    public static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(5);

    private readonly JsonNode _output;
    private readonly Dictionary<string, Dictionary<string, JsonNode?>> _tables;

    private DeclarativeTransform(JsonNode output, Dictionary<string, Dictionary<string, JsonNode?>> tables)
    {
        _output = output;
        _tables = tables;
    }

    /// <summary>Reads and checks a transform document. Nothing is evaluated here.</summary>
    /// <exception cref="TransformException">The document is not a valid transform (<c>transform.invalid</c>, <c>transform.table-too-large</c>).</exception>
    public static DeclarativeTransform Parse(ReadOnlySpan<byte> document)
    {
        if (document.Length > MaxDocumentBytes)
            throw new TransformException("transform.too-large", $"A transform is at most {MaxDocumentBytes / 1024} KB.");
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(document, documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth + 8 });
        }
        catch (JsonException)
        {
            throw new TransformException("transform.invalid", "The transform is not valid JSON.");
        }
        if (root is not JsonObject obj || obj["output"] is not { } output)
            throw new TransformException("transform.invalid", "A transform is an object with an \"output\" expression.");
        foreach (var key in obj.Select(p => p.Key).Where(k => k is not ("output" or "tables" or "$schema" or "description")))
            throw new TransformException("transform.invalid", $"A transform has no \"{Clip(key)}\" property.");

        var tables = new Dictionary<string, Dictionary<string, JsonNode?>>(StringComparer.Ordinal);
        var entries = 0;
        if (obj["tables"] is { } tablesNode)
        {
            if (tablesNode is not JsonObject tableObjects)
                throw new TransformException("transform.invalid", "\"tables\" is an object of named tables.");
            foreach (var (name, table) in tableObjects)
            {
                if (table is not JsonObject values)
                    throw new TransformException("transform.invalid", $"Table \"{Clip(name)}\" is an object of key and value pairs.");
                entries += values.Count;
                if (entries > MaxTableEntries)
                    throw new TransformException("transform.table-too-large", $"Mapping tables hold at most {MaxTableEntries:N0} entries in all.");
                tables[name] = values.ToDictionary(p => p.Key, p => p.Value?.DeepClone(), StringComparer.Ordinal);
            }
        }
        CheckShape(output, 0);
        return new DeclarativeTransform(output.DeepClone(), tables);
    }

    /// <summary>
    /// Evaluates the transform's output against <paramref name="input"/> (the root, and the first current item).
    /// </summary>
    /// <exception cref="TransformException">A bound was reached, or the transform does not fit its input.</exception>
    public JsonNode? Run(JsonNode? input, long fuel = DefaultFuel, TimeSpan? timeLimit = null)
    {
        var run = new Evaluation(this, input, fuel, timeLimit ?? DefaultTimeLimit);
        var result = run.Evaluate(_output, input, 0);
        CheckOutput(result);
        return result;
    }

    /// <summary>Nesting of the output (and of anything copied); deeper is refused, never passed to a JSON writer.</summary>
    public const int MaxOutputDepth = 128;

    /// <summary>
    /// The output as it would be written, through a writer that stops at <see cref="MaxOutputChars"/> bytes (review fix: it
    /// used to build the whole text first) and at <see cref="MaxOutputDepth"/> levels.
    /// </summary>
    private static void CheckOutput(JsonNode? result)
    {
        try
        {
            using var counter = new BoundedCount(MaxOutputChars);
            using var writer = new Utf8JsonWriter(counter, new JsonWriterOptions { MaxDepth = MaxOutputDepth, SkipValidation = false });
            if (result is null) writer.WriteNullValue();
            else result.WriteTo(writer);
            writer.Flush();
        }
        catch (OutputTooLargeException)
        {
            throw new TransformException("transform.output-too-large", $"The output is larger than {MaxOutputChars / (1024 * 1024)} MB.");
        }
        catch (InvalidOperationException)
        {
            throw new TransformException("transform.too-deep", $"The output nests deeper than {MaxOutputDepth} levels.");
        }
    }

    /// <summary>A write-only stream that only counts, and stops the writer once it passes its limit.</summary>
    private sealed class BoundedCount(long limit) : Stream
    {
        private long _length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Add(count);

        public override void Write(ReadOnlySpan<byte> buffer) => Add(buffer.Length);

        private void Add(long count)
        {
            _length += count;
            if (_length > limit)
                throw new OutputTooLargeException();
        }
    }

    private sealed class OutputTooLargeException : Exception;

    private static readonly HashSet<string> Operators =
    [
        "const", "get", "getRoot", "template", "lookup", "map", "filter", "join", "object", "array", "if",
        "equals", "startsWith", "exists", "not", "all", "any", "int", "text", "lower", "upper", "split", "count",
    ];

    /// <summary>Checks the expression tree's shape and depth once, before anything runs.</summary>
    private static void CheckShape(JsonNode? node, int depth)
    {
        if (depth > MaxDepth)
            throw new TransformException("transform.too-deep", $"Expressions nest at most {MaxDepth} levels.");
        if (node is JsonArray)
            throw new TransformException("transform.invalid", "A list is written as {\"array\": [...]}, so it is never mistaken for data.");
        if (node is not JsonObject obj)
            return; // a literal
        if (obj.Count != 1 || !Operators.Contains(obj.First().Key))
            throw new TransformException("transform.invalid", $"An expression object has exactly one operator ({string.Join(", ", Operators.Order(StringComparer.Ordinal))}).");
        var (op, arg) = obj.First();
        switch (op)
        {
            case "const":
                return;
            case "get" or "getRoot":
                if (arg?.GetValueKind() != JsonValueKind.String || !IsPointer((string)arg!))
                    throw new TransformException("transform.invalid", $"\"{op}\" takes a JSON Pointer such as \"/name\".");
                return;
            case "template":
                if (arg?.GetValueKind() != JsonValueKind.String)
                    throw new TransformException("transform.invalid", "\"template\" takes a string.");
                Template.Parse((string)arg!);
                return;
            case "object":
                if (arg is not JsonObject fields)
                    throw new TransformException("transform.invalid", "\"object\" takes an object of expressions.");
                foreach (var (_, value) in fields)
                    CheckShape(value, depth + 1);
                return;
            case "array" or "all" or "any" or "equals" or "startsWith":
                if (arg is not JsonArray items || (op is "equals" or "startsWith" && items.Count != 2))
                    throw new TransformException("transform.invalid", $"\"{op}\" takes a list{(op is "equals" or "startsWith" ? " of two expressions" : " of expressions")}.");
                foreach (var item in items)
                    CheckShape(item, depth + 1);
                return;
            case "lookup":
                Require(arg, op, ["table", "key"], ["default"], depth);
                if (arg!["table"]?.GetValueKind() != JsonValueKind.String)
                    throw new TransformException("transform.invalid", "\"lookup\" names its table with a string.");
                return;
            case "map":
                Require(arg, op, ["over", "emit"], [], depth);
                return;
            case "filter":
                Require(arg, op, ["over", "where"], [], depth);
                return;
            case "join":
                Require(arg, op, ["items"], ["separator"], depth);
                return;
            case "split":
                Require(arg, op, ["text", "separator"], [], depth);
                return;
            case "if":
                Require(arg, op, ["cond", "then"], ["else"], depth);
                return;
            default: // exists, not, int, text, lower, upper, count: one expression
                CheckShape(arg, depth + 1);
                return;
        }
    }

    private static void Require(JsonNode? arg, string op, string[] required, string[] optional, int depth)
    {
        if (arg is not JsonObject obj || required.Any(r => !obj.ContainsKey(r)) || obj.Any(p => !required.Contains(p.Key) && !optional.Contains(p.Key)))
            throw new TransformException("transform.invalid", $"\"{op}\" takes {string.Join(", ", required.Select(r => $"\"{r}\""))}{(optional.Length > 0 ? $" and optionally {string.Join(", ", optional.Select(o => $"\"{o}\""))}" : "")}.");
        foreach (var (_, value) in obj)
            CheckShape(value, depth + 1);
    }

    private static bool IsPointer(string pointer) => pointer.Length == 0 || (pointer[0] == '/' && pointer.Length <= 512);

    private static string Clip(string text) => text.Length <= 40 ? text : text[..40] + "…";

    /// <summary>One run: its fuel, clock and iteration product.</summary>
    private sealed class Evaluation(DeclarativeTransform transform, JsonNode? root, long fuel, TimeSpan timeLimit)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _fuel = fuel;
        private long _iterationProduct = 1;
        private long _ticks;

        private void Spend(long steps)
        {
            _fuel -= steps;
            if (_fuel < 0)
                throw new TransformException("transform.fuel", "The transform took too many steps or built too much data. It may loop over too much data, copy it again and again, or build text that grows without end.");
            // The clock is read every 256 steps, and at once after any large charge (review fix).
            if ((++_ticks % 256 == 0 || steps >= 1024) && _clock.Elapsed > timeLimit)
                throw new TransformException("transform.timeout", $"The transform ran longer than {timeLimit.TotalSeconds:0} seconds.");
        }

        /// <summary>
        /// What producing a copy of <paramref name="node"/> costs: one step per node and one per 16 characters of text
        /// (review fix: every copy, literal and lookup result pays for its whole size, so fuel also bounds memory). The
        /// walk stops as soon as it passes the fuel left, and anything deeper than <see cref="MaxOutputDepth"/> is refused.
        /// </summary>
        private long Weigh(JsonNode? node)
        {
            long weight = 0;
            var pending = new Stack<(JsonNode? Node, int Depth)>();
            pending.Push((node, 0));
            while (pending.Count > 0)
            {
                var (current, depth) = pending.Pop();
                if (depth > MaxOutputDepth)
                    throw new TransformException("transform.too-deep", $"The data nests deeper than {MaxOutputDepth} levels.");
                weight++;
                switch (current)
                {
                    case JsonObject obj:
                        foreach (var (key, value) in obj)
                        {
                            weight += key.Length / 16;
                            pending.Push((value, depth + 1));
                        }
                        break;
                    case JsonArray array:
                        foreach (var value in array)
                            pending.Push((value, depth + 1));
                        break;
                    case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                        weight += ((string)value!).Length / 16;
                        break;
                }
                if (weight > _fuel)
                    break; // Spend refuses it; no need to walk the rest
            }
            return weight;
        }

        /// <summary>A copy of <paramref name="node"/>, paid for by its whole size, so copying large data is never free.</summary>
        private JsonNode? Copy(JsonNode? node)
        {
            Spend(Weigh(node));
            return node?.DeepClone();
        }

        /// <summary>The value a <c>get</c> or <c>getRoot</c> reads, without a copy (for iterating only).</summary>
        private JsonNode? ReadOnly(JsonObject source, JsonNode? item)
        {
            Spend(1);
            var (op, arg) = source.First();
            return Resolve(op == "get" ? item : root, (string)arg!);
        }

        /// <summary>A new text value, paid for by its length.</summary>
        private JsonValue Text(string text)
        {
            Spend(text.Length / 16);
            return JsonValue.Create(text);
        }

        public JsonNode? Evaluate(JsonNode? expression, JsonNode? item, int depth)
        {
            Spend(1);
            if (expression is not JsonObject obj)
                return Copy(expression); // a literal (CheckShape refused lists)
            var (op, arg) = obj.First();
            switch (op)
            {
                case "const":
                    return Copy(arg);
                case "get":
                    return Copy(Resolve(item, (string)arg!));
                case "getRoot":
                    return Copy(Resolve(root, (string)arg!));
                case "template":
                    return JsonValue.Create(Template.Parse((string)arg!).Render(p => p.Root ? Resolve(root, p.Pointer) : Resolve(item, p.Pointer), Spend));
                case "lookup":
                {
                    var name = (string)arg!["table"]!;
                    if (!transform._tables.TryGetValue(name, out var table))
                        throw new TransformException("transform.table-missing", $"There is no table \"{Clip(name)}\".");
                    var key = AsText(Evaluate(arg["key"], item, depth + 1));
                    if (key is not null && table.TryGetValue(key, out var found))
                        return Copy(found);
                    return arg.AsObject().ContainsKey("default") ? Evaluate(arg["default"], item, depth + 1) : null;
                }
                case "map" or "filter":
                {
                    // A list read straight from the input is walked where it is, not copied first: its elements are only read.
                    var over = (arg!["over"] is JsonObject source && source.Count == 1 && source.First().Key is "get" or "getRoot"
                        ? ReadOnly(source, item)
                        : Evaluate(arg["over"], item, depth + 1)) as JsonArray ?? [];
                    Iterate(over.Count);
                    try
                    {
                        var result = new JsonArray();
                        foreach (var element in over)
                        {
                            if (op == "map")
                                result.Add(Evaluate(arg["emit"], element, depth + 1));
                            else if (Truthy(Evaluate(arg["where"], element, depth + 1)))
                                result.Add(Copy(element));
                        }
                        return result;
                    }
                    finally
                    {
                        _iterationProduct /= Math.Max(1, over.Count);
                    }
                }
                case "join":
                {
                    var items = Evaluate(arg!["items"], item, depth + 1) as JsonArray ?? [];
                    var separator = arg.AsObject().ContainsKey("separator") ? AsText(Evaluate(arg["separator"], item, depth + 1)) ?? "" : "";
                    var parts = items.Select(i => AsText(i) ?? "").ToList();
                    Spend(parts.Sum(p => (long)p.Length + separator.Length) / 16);
                    var joined = string.Join(separator, parts);
                    return joined.Length > MaxOutputChars ? throw new TransformException("transform.output-too-large", "Joined text is too long.") : JsonValue.Create(joined);
                }
                case "split":
                {
                    var text = AsText(Evaluate(arg!["text"], item, depth + 1)) ?? "";
                    var separator = AsText(Evaluate(arg["separator"], item, depth + 1));
                    if (string.IsNullOrEmpty(separator))
                        throw new TransformException("transform.invalid", "\"split\" needs a separator of at least one character.");
                    Spend(text.Length / 16);
                    var result = new JsonArray();
                    foreach (var part in text.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        Spend(1);
                        result.Add(JsonValue.Create(part));
                    }
                    return result;
                }
                case "object":
                {
                    var result = new JsonObject();
                    foreach (var (key, value) in arg!.AsObject())
                        result[key] = Evaluate(value, item, depth + 1);
                    return result;
                }
                case "array":
                    return new JsonArray([.. arg!.AsArray().Select(e => Evaluate(e, item, depth + 1))]);
                case "if":
                    return Truthy(Evaluate(arg!["cond"], item, depth + 1))
                        ? Evaluate(arg["then"], item, depth + 1)
                        : arg.AsObject().ContainsKey("else") ? Evaluate(arg["else"], item, depth + 1) : null;
                case "equals":
                {
                    var pair = arg!.AsArray();
                    return JsonValue.Create(JsonNode.DeepEquals(Evaluate(pair[0], item, depth + 1), Evaluate(pair[1], item, depth + 1)));
                }
                case "startsWith":
                {
                    var pair = arg!.AsArray();
                    var text = AsText(Evaluate(pair[0], item, depth + 1));
                    var prefix = AsText(Evaluate(pair[1], item, depth + 1));
                    return JsonValue.Create(text is not null && prefix is not null && text.StartsWith(prefix, StringComparison.Ordinal));
                }
                case "exists":
                    return JsonValue.Create(Evaluate(arg, item, depth + 1) is not null);
                case "not":
                    return JsonValue.Create(!Truthy(Evaluate(arg, item, depth + 1)));
                case "all":
                    return JsonValue.Create(arg!.AsArray().All(c => Truthy(Evaluate(c, item, depth + 1))));
                case "any":
                    return JsonValue.Create(arg!.AsArray().Any(c => Truthy(Evaluate(c, item, depth + 1))));
                case "int":
                {
                    var value = Evaluate(arg, item, depth + 1);
                    if (value is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue(out long whole))
                        return JsonValue.Create(whole);
                    if (AsText(value) is { } text && long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
                        return JsonValue.Create(parsed);
                    return null; // not a whole number: absent, so "exists" and "if" can handle it
                }
                case "text":
                    return AsText(Evaluate(arg, item, depth + 1)) is { } asText ? Text(asText) : null;
                case "lower":
                    return AsText(Evaluate(arg, item, depth + 1)) is { } lower ? Text(lower.ToLowerInvariant()) : null;
                case "upper":
                    return AsText(Evaluate(arg, item, depth + 1)) is { } upper ? Text(upper.ToUpperInvariant()) : null;
                case "count":
                    return Evaluate(arg, item, depth + 1) switch
                    {
                        JsonArray array => JsonValue.Create(array.Count),
                        JsonObject map => JsonValue.Create(map.Count),
                        _ => JsonValue.Create(0),
                    };
                default:
                    throw new TransformException("transform.invalid", $"Unknown operator \"{Clip(op)}\".");
            }
        }

        /// <summary>Nested iterations multiply: a loop inside a loop over n and m items costs n × m, bounded as a whole.</summary>
        private void Iterate(int count)
        {
            Spend(count);
            _iterationProduct *= Math.Max(1, count);
            if (_iterationProduct > MaxIterationProduct)
            {
                _iterationProduct /= Math.Max(1, count);
                throw new TransformException("transform.iterations", $"Nested loops would run more than {MaxIterationProduct:N0} times.");
            }
        }

        private JsonNode? Resolve(JsonNode? from, string pointer)
        {
            if (pointer.Length == 0)
                return from;
            var node = from;
            foreach (var raw in pointer[1..].Split('/'))
            {
                Spend(1);
                var token = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                node = node switch
                {
                    JsonObject obj => obj.TryGetPropertyValue(token, out var child) ? child : null,
                    JsonArray array when int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count => array[index],
                    _ => null,
                };
                if (node is null)
                    return null;
            }
            return node;
        }
    }

    /// <summary>Strings are themselves; numbers and booleans are written as JSON writes them; null, lists and objects are no text.</summary>
    internal static string? AsText(JsonNode? node) => node is JsonValue value
        ? value.GetValueKind() switch
        {
            JsonValueKind.String => (string)value!,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToJsonString(),
            _ => null,
        }
        : null;

    /// <summary>False, null, 0, "" and an empty list are false; everything else is true.</summary>
    internal static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonArray array => array.Count > 0,
        JsonObject => true,
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.False or JsonValueKind.Null => false,
            JsonValueKind.String => ((string)value!).Length > 0,
            JsonValueKind.Number => value.ToJsonString() is not ("0" or "0.0"),
            _ => true,
        },
        _ => false,
    };

    /// <summary>
    /// <c>"Text {/name} and {#/character/name}"</c>: <c>{/…}</c> reads the current item, <c>{#/…}</c> the root. <c>{{</c> and
    /// <c>}}</c> are literal braces. Values are written as <see cref="AsText"/> writes them; a missing one is empty.
    /// </summary>
    private sealed record Template(IReadOnlyList<object> Parts)
    {
        public sealed record Placeholder(bool Root, string Pointer);

        public static Template Parse(string text)
        {
            var parts = new List<object>();
            var literal = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { literal.Append('{'); i++; continue; }
                if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { literal.Append('}'); i++; continue; }
                if (c == '}')
                    throw new TransformException("transform.invalid", "A template has a \"}\" with no \"{\"; write \"}}\" for a brace.");
                if (c != '{') { literal.Append(c); continue; }
                var end = text.IndexOf('}', i + 1);
                if (end < 0)
                    throw new TransformException("transform.invalid", "A template has a \"{\" with no \"}\"; write \"{{\" for a brace.");
                var inside = text[(i + 1)..end];
                var root = inside.StartsWith('#');
                var pointer = root ? inside[1..] : inside;
                if (!IsPointer(pointer) || pointer.Length == 0)
                    throw new TransformException("transform.invalid", "A template placeholder is a JSON Pointer: {/name} for the current item, {#/name} for the whole input.");
                if (literal.Length > 0) { parts.Add(literal.ToString()); literal.Clear(); }
                parts.Add(new Placeholder(root, pointer));
                i = end;
            }
            if (literal.Length > 0)
                parts.Add(literal.ToString());
            return new Template(parts);
        }

        public string Render(Func<Placeholder, JsonNode?> resolve, Action<long> spend)
        {
            var output = new StringBuilder();
            foreach (var part in Parts)
            {
                var text = part is Placeholder placeholder ? AsText(resolve(placeholder)) ?? "" : (string)part;
                spend(1 + (text.Length / 16));
                output.Append(text);
                if (output.Length > MaxOutputChars)
                    throw new TransformException("transform.output-too-large", "A template's text is too long.");
            }
            return output.ToString();
        }
    }
}

/// <summary>A transform that cannot be read, or reached one of its bounds. The message never quotes the input's data.</summary>
public sealed class TransformException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public Diagnostic ToDiagnostic() => new(Code, Message);
}
