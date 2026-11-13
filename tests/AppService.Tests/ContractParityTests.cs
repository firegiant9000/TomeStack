using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Row 4.6: the gate fails when a DTO the dispatcher returns gains a property that <c>src/Ui/src/api/types.ts</c> does not declare.
/// The C# side is read by reflection (the return type of every <c>app.</c>-rooted command in <c>CommandDispatcher.Execute</c>, then the JSON
/// property names from <c>RulesJson.Compact</c>); the TS side is read as plain text. Every allow-list entry carries a reason and is checked for staleness.
/// Exempt by rule: get-only computed properties (no setter or init accessor) and <c>[JsonExtensionData]</c> members; positional record
/// parameters have init accessors, so they stay checked. A C# type whose TS name is an opaque alias (Record, a union of literals) counts as matched with no field check.
/// </summary>
public sealed partial class ContractParityTests
{
    // C# type name -> TS name, where they differ.
    private static readonly Dictionary<string, string> NameMap = new()
    {
    };

    // C# types the dispatcher returns that have no TS counterpart.
    private static readonly Dictionary<string, string> UnmatchedTypes = new()
    {
        // Typed inline in the TS parent, so there is no named interface to compare (the parent's own fields are checked).
        ["ActiveToggle"] = "inline in PlayState.toggles",
        ["Concentration"] = "inline in PlayState.concentration",
        ["DeathSaves"] = "inline in PlayState.deathSaves",
        ["EncounterState"] = "inline in PlayState.encounter",
        ["HitDiceUse"] = "inline in PlayState.hitDiceSpent",
        ["SpellSlotUse"] = "inline in PlayState.spellSlotsSpent",
        ["PackageEntry"] = "inline in PackageManifest.entries",
        ["Repinned"] = "inline in PackagePreview.repinned",
        ["SourceAttestation"] = "inline in PackageManifest.attestations",
        // No named TS interface yet; the UI reads these structurally or not at all. Each is a follow-up candidate, not a decision.
        ["Appearance"] = "no named TS interface",
        ["AttachOutcome"] = "no named TS interface",
        ["CandidateGrant"] = "no named TS interface",
        ["ChoiceRef"] = "no named TS interface",
        ["ChoiceReplaceable"] = "no named TS interface",
        ["ContentDiff"] = "no named TS interface",
        ["CoverageScope"] = "no named TS interface",
        ["DailyCopiesState"] = "no named TS interface",
        ["ExtensionDraft"] = "no named TS interface",
        ["ExtensionImportResult"] = "no named TS interface",
        ["ExtensionUpdate"] = "no named TS interface",
        ["FeatureRef"] = "no named TS interface",
        ["FormulaError"] = "no named TS interface",
        ["ImportAuditEntry"] = "no named TS interface",
        ["ImportJobRecord"] = "no named TS interface",
        ["ItemChargeRecoveryInfo"] = "no named TS interface",
        ["Mechanic"] = "no named TS interface (the UI's MechanicEntry is a separate shape)",
        ["PermissionInfo"] = "no named TS interface",
        ["PropertyChange"] = "no named TS interface",
        ["Provenance"] = "no named TS interface",
        ["ResponseTrigger"] = "no named TS interface",
        ["SessionNote"] = "no named TS interface",
        ["SpellSummary"] = "no named TS interface",
        ["TextBlock"] = "no named TS interface",
        ["TextLine"] = "no named TS interface",
        ["ToggleRef"] = "no named TS interface",
    };

    // "CSharpType.jsonName": a property the UI has no field for.
    private static readonly Dictionary<string, string> KnownGaps = new()
    {
    };

    // Commands whose result is not typed by an app. call or a typed helper.
    private static readonly Dictionary<string, string> UntypedCommands = new()
    {
        ["campaign.delete"] = "private helper returns object (anonymous result)",
        ["content.diagnose"] = "private helper returns object",
        ["content.sandbox"] = "private helper returns object",
        ["content.validate"] = "private helper returns object",
        ["ddb.discard"] = "anonymous object { discarded }",
        ["ddb.read"] = "private helper returns object",
        ["diagnostics.copy"] = "anonymous object { text }",
        ["export.download"] = "private helper returns object",
        ["extension.chooseInput"] = "private helper returns object",
        ["extension.installChoose"] = "private helper returns object",
        ["extension.remove"] = "private helper returns object",
        ["extension.runExport"] = "private helper returns object",
        ["gap.delete"] = "private helper returns object",
        ["library.backupSaveAs"] = "private helper returns object",
        ["library.restoreChoose"] = "private helper returns object",
        ["package.apply"] = "private helper returns object",
        ["package.campaignPackExport"] = "private helper returns object",
        ["package.export"] = "private helper returns object",
        ["package.sourcePackExport"] = "private helper returns object",
        ["package.targets"] = "private helper returns object",
        ["source.detach"] = "private helper returns object",
    };

    private static readonly HashSet<Type> Leaf =
    [
        typeof(string), typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Version),
        typeof(JsonElement), typeof(JsonNode), typeof(object), typeof(byte[]),
    ];

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("TomeStack.slnx not found above the test output folder.");
        }
    }

    [Fact]
    public void Every_dispatcher_dto_property_has_a_ts_field()
    {
        var walk = Analyse();
        var gaps = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (type, names) in walk.Shapes)
        {
            var tsName = TsNameOf(type);
            if (!walk.Ts.TryGetValue(tsName, out var fields))
            {
                if (walk.TsOpaque.Contains(tsName)) continue; // declared in TS as an alias (Record, union): matched, no field check
                if (!UnmatchedTypes.ContainsKey(type.Name))
                    gaps.Add($"{type.Name} (whole type) → {tsName}");
                continue;
            }
            foreach (var name in names.Where(n => !fields.Contains(n)))
                if (!KnownGaps.ContainsKey($"{type.Name}.{name}"))
                    gaps.Add($"{type.Name}.{name} → {tsName}");
        }
        Assert.True(gaps.Count == 0,
            "types.ts is missing fields the service sends (add the field, or an allow-list entry with a reason):" + Environment.NewLine + string.Join(Environment.NewLine, gaps));
    }

    [Fact]
    public void Allow_lists_have_no_stale_entries()
    {
        var walk = Analyse();
        var stale = new List<string>();
        var byName = walk.Shapes.Keys.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var (key, target) in NameMap)
        {
            if (!byName.ContainsKey(key)) stale.Add($"NameMap {key}: no longer walked");
            else if (key == target) stale.Add($"NameMap {key}: maps to itself");
            else if (!walk.Ts.ContainsKey(target)) stale.Add($"NameMap {key}: TS has no {target}");
        }
        foreach (var key in UnmatchedTypes.Keys)
        {
            if (!byName.TryGetValue(key, out var types)) stale.Add($"UnmatchedTypes {key}: no longer walked");
            else if (types.Any(t => walk.Ts.ContainsKey(TsNameOf(t)) || walk.TsOpaque.Contains(TsNameOf(t)))) stale.Add($"UnmatchedTypes {key}: TS now has it");
        }
        foreach (var key in KnownGaps.Keys)
        {
            var dot = key.LastIndexOf('.');
            var typeName = key[..dot];
            var json = key[(dot + 1)..];
            var types = byName.GetValueOrDefault(typeName);
            if (types is null) { stale.Add($"KnownGaps {key}: type no longer walked"); continue; }
            var stillGap = types.Any(t => walk.Shapes[t].Contains(json)
                && walk.Ts.TryGetValue(TsNameOf(t), out var fields) && !fields.Contains(json));
            if (!stillGap) stale.Add($"KnownGaps {key}: not a gap any more");
        }
        foreach (var key in UntypedCommands.Keys)
            if (!walk.Commands.Contains(key) || walk.TypedCommands.Contains(key)) stale.Add($"UntypedCommands {key}: not an untyped command any more");
        Assert.True(stale.Count == 0, "Stale allow-list entries:" + Environment.NewLine + string.Join(Environment.NewLine, stale.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Every_dispatcher_command_is_typed_or_listed_untyped()
    {
        var walk = Analyse();
        var missing = walk.Commands.Where(c => !walk.TypedCommands.Contains(c) && !UntypedCommands.ContainsKey(c)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Commands with no typed result and no UntypedCommands entry: " + string.Join(", ", missing));
        Assert.True(walk.TypedCommands.Count > 50, "The Execute parser found suspiciously few typed commands.");
    }

    [Fact]
    public void Walker_reaches_derived_types()
    {
        var shapes = WalkJsonShapes([typeof(PolymorphicBase)]);
        Assert.Contains(typeof(PolymorphicDerived), shapes.Keys);
        Assert.Contains("extra", shapes[typeof(PolymorphicDerived)]);
        Assert.Contains("common", shapes[typeof(PolymorphicBase)]);
    }

    [Fact]
    public void Ts_parser_reads_interfaces_extends_and_aliases()
    {
        const string sample = """
            export interface A { a: string; b?: number; nested: { x: string } }
            export interface B extends A { c: boolean }
            export type C = A & { d: string }
            export type D = 'x' | 'y'
            """;
        var shapes = TsShapes(sample);
        Assert.Equal(["a", "b", "nested"], shapes["A"].Order(StringComparer.Ordinal));
        Assert.Equal(["a", "b", "c", "nested"], shapes["B"].Order(StringComparer.Ordinal));
        Assert.Equal(["a", "b", "d", "nested"], shapes["C"].Order(StringComparer.Ordinal));
        Assert.DoesNotContain("D", shapes.Keys);
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(PolymorphicDerived), "derived")]
    internal class PolymorphicBase
    {
        public int Common { get; init; }
    }

    internal sealed class PolymorphicDerived : PolymorphicBase
    {
        public int Extra { get; init; }
    }

    private sealed record Analysis(
        IReadOnlyDictionary<Type, IReadOnlyList<string>> Shapes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> Ts,
        IReadOnlySet<string> TsOpaque,
        IReadOnlySet<string> Commands,
        IReadOnlySet<string> TypedCommands);

    private static string TsNameOf(Type type) => NameMap.GetValueOrDefault(type.Name, type.Name);

    private static Analysis Analyse()
    {
        var (commands, typed) = DispatcherResultTypes();
        var shapes = WalkJsonShapes(typed.Values.SelectMany(t => t).Distinct().ToList());
        var (ts, opaque) = TsParse(File.ReadAllText(Path.Combine(RepoRoot, "src", "Ui", "src", "api", "types.ts")));
        return new Analysis(shapes, ts, opaque, commands.ToHashSet(), typed.Keys.ToHashSet());
    }

    // ---- C# side ----

    [GeneratedRegex("""^"([\w.]+)"\s*=>\s*(.*)$""")]
    private static partial Regex SwitchArm();

    [GeneratedRegex(@"^(?:\(object\?\))?app\.((?:\w+\.)*)(\w+)\(")]
    private static partial Regex AppCall();

    [GeneratedRegex(@"^(\w+)\(")]
    private static partial Regex HelperCall();

    /// <summary>Every command in the Execute switch, and for each typed one the return types of the method that produces its result.</summary>
    private static (List<string> Commands, Dictionary<string, List<Type>> Typed) DispatcherResultTypes()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "AppService", "CommandDispatcher.cs"));
        var start = source.IndexOf("private object? Execute(", StringComparison.Ordinal);
        var end = source.IndexOf("_ => throw new UnknownCommandException", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not find the Execute switch in CommandDispatcher.cs.");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var commands = new List<string>();
        var typed = new Dictionary<string, List<Type>>();
        foreach (var raw in source[start..end].Split('\n'))
        {
            var arm = SwitchArm().Match(raw.Trim());
            if (!arm.Success) continue;
            var command = arm.Groups[1].Value;
            commands.Add(command);
            var expression = arm.Groups[2].Value;
            var types = new List<Type>();
            var app = AppCall().Match(expression);
            if (app.Success)
            {
                var owner = typeof(TomeStackApp);
                foreach (var member in app.Groups[1].Value.Split('.', StringSplitOptions.RemoveEmptyEntries))
                    owner = owner.GetProperty(member, all)?.PropertyType ?? owner.GetField(member, all)?.FieldType
                        ?? throw new InvalidOperationException($"{command}: cannot resolve '{member}' on {owner.Name}.");
                types.AddRange(owner.GetMethods(all).Where(m => m.Name == app.Groups[2].Value).Select(m => m.ReturnType));
            }
            else if (HelperCall().Match(expression) is { Success: true } helper)
            {
                types.AddRange(typeof(CommandDispatcher).GetMethods(all).Where(m => m.Name == helper.Groups[1].Value).Select(m => m.ReturnType));
            }
            types.RemoveAll(t => t == typeof(object) || t == typeof(void));
            if (types.Count > 0) typed[command] = types;
        }
        return (commands, typed);
    }

    private static Type Unwrap(Type type)
    {
        while (true)
        {
            if (Nullable.GetUnderlyingType(type) is { } inner) { type = inner; continue; }
            if (type.IsArray) { type = type.GetElementType()!; continue; }
            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                if (args.Length >= 1 && (type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    || type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))))
                {
                    type = args[^1];
                    continue;
                }
            }
            return type;
        }
    }

    private static bool IsWalkable(Type type) =>
        !type.IsPrimitive && !type.IsEnum && !Leaf.Contains(type) && type.Namespace is { } ns && ns.StartsWith("TomeStack", StringComparison.Ordinal);

    // Exempt by rule: get-only computed properties (no setter or init accessor) and [JsonExtensionData] members.
    // Positional record parameters have init accessors, so they stay checked.
    private static bool IsChecked(JsonPropertyInfo property) => property.Set is not null && !property.IsExtensionData;

    private static IReadOnlyDictionary<Type, IReadOnlyList<string>> WalkJsonShapes(IEnumerable<Type> roots)
    {
        var shapes = new Dictionary<Type, IReadOnlyList<string>>();
        var queue = new Queue<Type>(roots);
        while (queue.Count > 0)
        {
            var type = Unwrap(queue.Dequeue());
            if (!IsWalkable(type) || shapes.ContainsKey(type)) continue;
            var info = RulesJson.Compact.GetTypeInfo(type);
            if (info.Kind != JsonTypeInfoKind.Object) continue;
            shapes[type] = info.Properties.Where(IsChecked).Select(p => p.Name).ToList();
            foreach (var property in info.Properties) queue.Enqueue(property.PropertyType);
            if (info.PolymorphismOptions is { } polymorphism)
                foreach (var derived in polymorphism.DerivedTypes) queue.Enqueue(derived.DerivedType);
        }
        return shapes;
    }

    // ---- TS side ----

    [GeneratedRegex(@"export\s+interface\s+(\w+)\s*(?:<[^{]*?>)?\s*(?:extends\s+([^{]+?))?\s*\{")]
    private static partial Regex InterfaceHead();

    [GeneratedRegex(@"export\s+type\s+(\w+)\s*(?:<[^=]*?>)?\s*=\s*")]
    private static partial Regex AliasHead();

    [GeneratedRegex("""^\s*(?:readonly\s+)?["']?([A-Za-z_$][\w$]*)["']?\??\s*:""")]
    private static partial Regex FieldStart();

    [GeneratedRegex(@"^\w+$")]
    private static partial Regex PlainName();

    [GeneratedRegex(@"\n(?=\s*(?:export|declare|const|function|import|type|interface)\b)")]
    private static partial Regex NextDeclaration();

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> TsShapes(string typesTs) => TsParse(typesTs).Shapes;

    private static (IReadOnlyDictionary<string, IReadOnlySet<string>> Shapes, IReadOnlySet<string> Opaque) TsParse(string typesTs)
    {
        var text = StripComments(typesTs);
        var own = new Dictionary<string, HashSet<string>>();
        var parents = new Dictionary<string, List<string>>();
        var aliases = new Dictionary<string, List<string>>(); // alias -> parts: "{...}" bodies are stored as own fields, names as parents
        var opaque = new HashSet<string>();

        foreach (Match head in InterfaceHead().Matches(text))
        {
            var name = head.Groups[1].Value;
            var open = head.Index + head.Length - 1;
            var close = MatchClose(text, open);
            own[name] = FieldsOfBody(text[(open + 1)..close]);
            parents[name] = head.Groups[2].Success
                ? SplitTop(head.Groups[2].Value, [',']).Select(p => p.Trim()).Select(p => p.Split('<')[0].Trim()).Where(p => p.Length > 0).ToList()
                : [];
        }

        foreach (Match head in AliasHead().Matches(text))
        {
            var name = head.Groups[1].Value;
            var rhsStart = head.Index + head.Length;
            var rhs = AliasBody(text, rhsStart);
            var parts = SplitTop(rhs, ['|', '&']).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            var fields = new HashSet<string>();
            var names = new List<string>();
            var ok = parts.Count > 0;
            foreach (var part in parts)
            {
                if (part.StartsWith('{') && part.EndsWith('}')) fields.UnionWith(FieldsOfBody(part[1..^1]));
                else if (PlainName().IsMatch(part)) names.Add(part);
                else ok = false;
            }
            if (!ok) { opaque.Add(name); continue; }
            own[name] = fields;
            parents[name] = names;
            aliases[name] = names;
        }

        var result = new Dictionary<string, IReadOnlySet<string>>();
        foreach (var name in own.Keys)
        {
            var resolved = Resolve(name, [], out var isOpaque);
            if (isOpaque) opaque.Add(name);
            else result[name] = resolved;
        }
        return (result, opaque);

        HashSet<string> Resolve(string name, HashSet<string> seen, out bool isOpaque)
        {
            isOpaque = false;
            var fields = new HashSet<string>();
            if (!own.TryGetValue(name, out var mine) || !seen.Add(name))
            {
                // an unknown parent (for example a utility type) or a cycle: opaque for aliases, ignored for interface parents
                isOpaque = !own.ContainsKey(name);
                return fields;
            }
            fields.UnionWith(mine);
            foreach (var parent in parents[name])
            {
                var inherited = Resolve(parent, seen, out var parentOpaque);
                if (parentOpaque && aliases.ContainsKey(name)) { isOpaque = true; return fields; }
                fields.UnionWith(inherited);
            }
            return fields;
        }
    }

    private static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'' or '`')
            {
                var quote = c;
                sb.Append(c);
                for (i++; i < text.Length && text[i] != quote; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) sb.Append(text[i++]);
                    sb.Append(text[i]);
                }
                if (i < text.Length) sb.Append(quote);
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                sb.Append('\n');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i++;
                sb.Append(' ');
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static int MatchClose(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i;
            else if (text[i] is '"' or '\'' or '`') i = SkipString(text, i);
        }
        throw new InvalidOperationException("Unbalanced braces in types.ts.");
    }

    private static int SkipString(string text, int start)
    {
        var i = start + 1;
        while (i < text.Length && text[i] != text[start]) i += text[i] == '\\' ? 2 : 1;
        return i;
    }

    /// <summary>The text of a type alias, up to its terminating semicolon or the line before the next declaration.</summary>
    private static string AliasBody(string text, int start)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'' or '`') { i = SkipString(text, i); continue; }
            if (c is '{' or '(' or '[') depth++;
            else if (c is '}' or ')' or ']') depth--;
            else if (c == ';' && depth == 0) return text[start..i];
            else if (c == '\n' && depth == 0 && NextDeclaration().Match(text, i) is { Success: true, Index: var at } && at == i)
                return text[start..i];
        }
        return text[start..];
    }

    /// <summary>Splits at the separators that sit outside any bracket pair (angle brackets included, <c>=&gt;</c> excluded).</summary>
    private static List<string> SplitTop(string text, char[] separators)
    {
        var parts = new List<string>();
        var depth = 0;
        var from = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'' or '`') { i = SkipString(text, i); continue; }
            if (c is '{' or '(' or '[' or '<') depth++;
            else if (c is '}' or ')' or ']' || (c == '>' && !(i > 0 && text[i - 1] == '='))) depth--;
            else if (depth == 0 && separators.Contains(c))
            {
                parts.Add(text[from..i]);
                from = i + 1;
            }
        }
        parts.Add(text[from..]);
        return parts;
    }

    private static HashSet<string> FieldsOfBody(string body)
    {
        var fields = new HashSet<string>();
        foreach (var segment in SplitTop(body, [';', ',', '\n']))
            if (FieldStart().Match(segment) is { Success: true } m) fields.Add(m.Groups[1].Value);
        return fields;
    }
}
