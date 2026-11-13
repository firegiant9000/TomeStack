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
/// Exempt by rule: <c>[JsonExtensionData]</c> members and computed properties, meaning no setter of any visibility (init counts as one) and no
/// constructor parameter binding them; positional record parameters and <c>{ get; private set; }</c> stay checked. A C# type whose TS name is an opaque
/// alias (Record, a union of literals) counts as matched with no field check.
/// A type behind a custom converter has no object contract to read: an abstract one (<c>Effect</c>) has its concrete subtypes walked, each checked against the TS union
/// member with the same discriminator (<c>TypeName</c> against <c>type: 'x'</c>), with the discriminator the converter writes added to its names.
/// A typed command whose result root (after unwrapping Task, collections and nullables) is not a TomeStack type, primitive, enum or leaf fails instead of being skipped.
/// An "inline in" UnmatchedTypes entry goes stale when types.ts gains a named interface or opaque alias of that name (InlineNameClashes lists the reasoned exceptions).
/// Any other non-object contract must be listed in <c>OpaqueConverterTypes</c> with a reason, or the walk fails instead of skipping it.
/// </summary>
public sealed partial class ContractParityTests
{
    // C# type name -> TS name, where they differ (field-checked like any other pair).
    private static readonly Dictionary<string, string> NameMap = new()
    {
        ["Appearance"] = "CharacterAppearance",
        ["ChoiceRef"] = "ClassTableRef",
        ["CostPayment"] = "EffectPayment",
        ["CoverageScope"] = "CoverageRequest",
        ["DailyCopiesState"] = "DailyCopies",
        ["FeatureRef"] = "ClassTableRef",
        ["ImportJobRecord"] = "ImportJob",
        ["ItemChargeRecoveryInfo"] = "ItemChargeRecovery",
        ["Mechanic"] = "MechanicEntry",
        ["ResponseOutcome"] = "ResponseOutcomeDef",
        ["TextBlock"] = "StoredTextBlock",
    };

    // C# types the dispatcher returns that have no named TS interface. Triaged by field set, not by name (2026-11-12).
    // An "inline in" reason says TS declares the shape inside its parent (the parent's own fields are checked, the inline ones are not);
    // such an entry also stops a same-named TS interface from being compared (ItemChargeRecovery is a different type from TS's ItemChargeRecovery).
    private static readonly Dictionary<string, string> UnmatchedTypes = new()
    {
        ["ActiveToggle"] = "inline in PlayState.toggles",
        ["AlsoChange"] = "inline in Effect (alsoChange)",
        ["AttackDamage"] = "inline in Effect (attack damage)",
        ["AttachOutcome"] = "no TS shape with fields attached and attachment",
        ["CandidateGrant"] = "inline in StoredCandidate.grantsCandidates",
        ["ChoiceReplaceable"] = "inline in Effect (choice replaceable)",
        ["Concentration"] = "inline in PlayState.concentration",
        ["ContentDiff"] = "inline in UpdateReview.mechanics (TS declares properties and effects; from and to sit on UpdateReview)",
        ["DeathSaves"] = "inline in PlayState.deathSaves",
        ["EncounterState"] = "inline in PlayState.encounter",
        ["ExtensionDraft"] = "inline in ExtensionRunPreview.drafts",
        ["ExtensionImportResult"] = "no TS shape with sourceId, sourceTitle and drafts (ExtensionRunPreview has no sourceId)",
        ["ExtensionUpdate"] = "inline in ExtensionInstallPreview.update",
        ["FormulaError"] = "inline in FormulaPreviewResult.error",
        ["HitDiceUse"] = "inline in PlayState.hitDiceSpent",
        ["ImportAuditEntry"] = "no TS shape with at, event and detail",
        ["ItemChargeRecovery"] = "inline in Effect (item charges recovery)",
        ["ItemCharges"] = "inline in Effect (item charges)",
        ["PackageEntry"] = "inline in PackageManifest.entries",
        ["PermissionInfo"] = "inline in ExtensionInstallPreview.permissions",
        ["PropertyChange"] = "inline in UpdateReview.mechanics.properties",
        ["Provenance"] = "inline in ContentRevision.provenance",
        ["Repinned"] = "inline in PackagePreview.repinned",
        ["ResourceFullPrompt"] = "inline in Effect (resourceFull)",
        ["ResponseSupersedes"] = "inline in Effect (response supersedes)",
        ["ResponseTrigger"] = "inline in Effect (response trigger)",
        ["ResponseWhen"] = "inline in Effect (response when)",
        ["RollSave"] = "inline in Effect (roll save)",
        ["ScaledDice"] = "inline in Effect (scaledDice)",
        ["SessionNote"] = "inline in Character.notes",
        ["SourceAttestation"] = "inline in PackageManifest.attestations",
        ["SpellSlotUse"] = "inline in PlayState.spellSlotsSpent",
        ["SpellSummary"] = "inline in ContentOption.spell",
        ["TextLine"] = "inline in TextChange.lines (the ImportWorker's TextLine shares the name; its TS shape is StoredTextLine and is not compared)",
        ["ToggleRef"] = "inline in Effect (toggle references)",
    };

    // "inline in" entries whose TS name is already taken by a different, named TS type (exempt from the inline staleness check; C# type name -> why).
    private static readonly Dictionary<string, string> InlineNameClashes = new()
    {
        ["ItemChargeRecovery"] = "TS's ItemChargeRecovery is the ItemChargeRecoveryInfo shape (NameMap); the Effect's own recovery object is a different, inline type",
    };

    // Types behind a custom converter that the walk deliberately does not enter (C# type name -> why). A type with a custom
    // contract that is neither abstract (its subtypes are walked) nor listed here fails Walk_does_not_silently_skip_a_type_with_a_custom_contract.
    private static readonly Dictionary<string, string> OpaqueConverterTypes = new()
    {
        ["UnknownEffect"] = "EffectJsonConverter writes its Raw JSON verbatim: the shape is whatever the content author wrote",
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
        ["content.sandbox"] = "private helper returns an anonymous object; its View, Draft and Changes types are walked via character.get (CharacterView), content.publish (ContentReference) and content.compare (FieldDelta)",
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
        ["library.restoreChoose"] = "private helper returns an anonymous object; its PackagePreview is walked via package.preview",
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
            if (TsFieldsOf(walk, type) is not { } fields)
            {
                if (IsTsOpaque(walk, type)) continue; // declared in TS as an alias (Record, union): matched, no field check
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
            else if (types.Any(t => TsFieldsOf(walk, t) is not null || IsTsOpaque(walk, t))) stale.Add($"UnmatchedTypes {key}: TS now has it");
            else if (!InlineNameClashes.ContainsKey(key) && types.Any(t => IsInline(t) && (walk.Ts.ContainsKey(TsNameOf(t)) || walk.TsOpaque.Contains(TsNameOf(t)))))
                stale.Add($"UnmatchedTypes {key}: TS now declares a named type {key}, so \"inline in\" no longer holds");
        }
        foreach (var key in KnownGaps.Keys)
        {
            var dot = key.LastIndexOf('.');
            var typeName = key[..dot];
            var json = key[(dot + 1)..];
            var types = byName.GetValueOrDefault(typeName);
            if (types is null) { stale.Add($"KnownGaps {key}: type no longer walked"); continue; }
            var stillGap = types.Any(t => walk.Shapes[t].Contains(json)
                && TsFieldsOf(walk, t) is { } fields && !fields.Contains(json));
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
    public void Typed_command_roots_are_walkable()
    {
        var (_, typed) = DispatcherResultTypes();
        var bad = UnwalkableRoots(typed, UntypedCommands.Keys.ToHashSet());
        Assert.True(bad.Count == 0, "Typed commands whose result type the walk cannot enter (return a TomeStack DTO, or add an UntypedCommands entry): " + string.Join(", ", bad));
        // The check itself: a ValueTuple root is flagged, Task/ValueTask wrappers are looked through.
        var probe = new Dictionary<string, List<Type>>
        {
            ["a.tuple"] = [typeof((int, string))],
            ["a.task"] = [typeof(Task<(int, string)>)],
            ["a.ok"] = [typeof(ValueTask<List<CharacterView>>), typeof(Task)],
        };
        Assert.Equal(["a.task: " + typeof((int, string)), "a.tuple: " + typeof((int, string))], UnwalkableRoots(probe, new HashSet<string>()));
    }

    [Fact]
    public void Walk_does_not_silently_skip_a_type_with_a_custom_contract()
    {
        var walk = Analyse();
        Assert.True(walk.Walk.Unlisted.Count == 0,
            "The walk skipped types whose JSON contract is not a plain object (custom converter): walk their subtypes or add an OpaqueConverterTypes entry with a reason: "
            + string.Join(", ", walk.Walk.Unlisted.Select(t => t.Name).Order(StringComparer.Ordinal)));
        var stale = OpaqueConverterTypes.Keys.Where(k => !walk.Walk.ListedOpaque.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Stale OpaqueConverterTypes entries (never reached by the walk): " + string.Join(", ", stale));
    }

    [Fact]
    public void Walker_reaches_the_effect_hierarchy_behind_its_converter()
    {
        var walk = Analyse();
        var modifier = Assert.Single(walk.Walk.Shapes, s => s.Key == typeof(ModifierEffect));
        Assert.Contains("type", modifier.Value);
        Assert.Contains("id", modifier.Value);
        Assert.Contains("operation", modifier.Value);
        Assert.Equal((typeof(Effect), "modifier"), walk.Walk.Subtypes[typeof(ModifierEffect)]);
        Assert.NotNull(TsFieldsOf(walk, typeof(ModifierEffect)));
    }

    // ---- Abstract type behind a converter: what the converter really writes ----

    // Property-level converters on Effect subtypes: (declaring type, property) -> the element type each written item must match.
    // A new converter-backed property must be listed here (guard below), so its output is compared with a walked shape.
    private static readonly Dictionary<(Type Owner, string Property), Type> PropertyConverterElements = new()
    {
        [(typeof(ItemModEffect), nameof(ItemModEffect.Modifiers))] = typeof(ModifierEffect),
    };

    /// <summary>Real effects: every revision of the bundled SRD packs and of the fixture packs (the fixtures cover the v11 and v12 types).</summary>
    private static List<Effect> RealEffects()
    {
        var files = Directory.GetFiles(Path.Combine(RepoRoot, "src", "AppService", "Content"), "*.json")
            .Concat(Directory.GetFiles(Path.Combine(RepoRoot, "tests", "RulesFixtures"), "fixture-pack*.json"));
        var effects = new List<Effect>();
        foreach (var file in files)
        {
            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject { } pack || pack["revisions"] is not JsonArray revisions) continue;
            foreach (var revision in revisions)
                effects.AddRange(revision.Deserialize<ContentRevision>(RulesJson.Options)!.Effects);
        }
        // Types no shipped pack or fixture uses yet (v13 guides are authored by the app, not bundled): built directly.
        if (!effects.Any(e => e is PrimaryAbilitiesEffect))
            effects.Add(new PrimaryAbilitiesEffect { Id = "primary", Abilities = ["str", "dex"] });
        return effects;
    }

    /// <summary>Concrete subtypes of <paramref name="root"/> the walk did not record with a TypeName, or that no sample covers. Opaque types (UnknownEffect) are exempt.</summary>
    private static List<string> ConcreteSubtypeGaps(Type root, Walk walk, IReadOnlySet<Type> sampled) =>
        root.Assembly.GetTypes()
            .Where(t => t != root && !t.IsAbstract && root.IsAssignableFrom(t) && !OpaqueConverterTypes.ContainsKey(t.Name))
            .SelectMany(t => !walk.Subtypes.ContainsKey(t) ? [$"{t.Name}: no TypeName constant, so it was not matched to the TS union"]
                : !sampled.Contains(t) ? (IEnumerable<string>)[$"{t.Name}: no sample instance, so its converter output is unchecked"] : [])
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Samples written through <paramref name="root"/>'s own converter: each emitted name must be in the walked shape and, when there is one, the TS fields.</summary>
    private static List<string> ConverterOutputProblems(Type root, IEnumerable<object> samples, Walk walk, Func<Type, IReadOnlySet<string>?> tsFields)
    {
        var problems = new List<string>();
        foreach (var group in samples.GroupBy(s => s.GetType()).Where(g => !OpaqueConverterTypes.ContainsKey(g.Key.Name)))
        {
            var emitted = group.SelectMany(s => JsonSerializer.SerializeToElement(s, root, RulesJson.Compact).EnumerateObject().Select(p => p.Name)).ToHashSet();
            if (!walk.Shapes.TryGetValue(group.Key, out var shape)) { problems.Add($"{group.Key.Name}: sampled but never walked"); continue; }
            var notInShape = emitted.Except(shape).Order(StringComparer.Ordinal).ToList();
            if (notInShape.Count > 0) problems.Add($"{group.Key.Name}: the converter writes {string.Join(", ", notInShape)}, not in the walked shape");
            if (tsFields(group.Key) is { } ts && emitted.Except(ts).Order(StringComparer.Ordinal).ToList() is { Count: > 0 } notInTs)
                problems.Add($"{group.Key.Name}: the converter writes {string.Join(", ", notInTs)}, not in the TS type");
        }
        return problems;
    }

    /// <summary>Names written inside a property-level converter's items that are not in the walked <paramref name="shape"/> or, when there is one, the <paramref name="ts"/> fields.</summary>
    private static List<string> NestedOutputProblems(IEnumerable<object> samples, Type owner, string property, IEnumerable<string> shape, IReadOnlySet<string>? ts, JsonSerializerOptions options)
    {
        var jsonName = options.PropertyNamingPolicy?.ConvertName(property) ?? property;
        var written = samples.Where(s => s.GetType() == owner)
            .Select(s => JsonSerializer.SerializeToElement(s, owner, options))
            .Where(e => e.TryGetProperty(jsonName, out var v) && v.ValueKind == JsonValueKind.Array)
            .SelectMany(e => e.GetProperty(jsonName).EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).SelectMany(i => i.EnumerateObject().Select(p => p.Name)))
            .ToHashSet();
        var problems = written.Except(shape).Order(StringComparer.Ordinal).Select(n => $"{owner.Name}.{property}: items carry {n}, not in the walked shape").ToList();
        if (ts is not null)
            problems.AddRange(written.Except(ts).Order(StringComparer.Ordinal).Select(n => $"{owner.Name}.{property}: items carry {n}, not in the TS type"));
        return problems;
    }

    /// <summary>
    /// Limitation: only properties that some sample populates are compared, because RulesJson.Compact omits defaults;
    /// a converter-written name that no sample sets goes unchecked.
    /// </summary>
    [Fact]
    public void Converter_output_names_match_the_walked_shape_and_ts()
    {
        var walk = Analyse();
        var effects = RealEffects();
        Assert.True(effects.Count(e => e is ModifierEffect) > 1, "The real samples are too thin to compare anything.");
        var problems = ConverterOutputProblems(typeof(Effect), effects, walk.Walk, t => TsFieldsOf(walk, t));
        Assert.True(problems.Count == 0, "Effect converter output drifted from the walked shape or TS type:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Every_concrete_effect_subtype_is_sampled_and_has_a_type_name()
    {
        var walk = Analyse();
        var gaps = ConcreteSubtypeGaps(typeof(Effect), walk.Walk, RealEffects().Select(e => e.GetType()).ToHashSet());
        Assert.True(gaps.Count == 0, "Effect subtypes the converter parity test cannot cover (add a TypeName, or a fixture that uses it):\n" + string.Join("\n", gaps));
    }

    [Fact]
    public void Property_level_converters_on_effects_write_the_walked_item_shape()
    {
        var walk = Analyse();
        var effects = RealEffects();
        var unlisted = typeof(Effect).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Effect).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties().Where(p => p.GetCustomAttribute<JsonConverterAttribute>() is not null).Select(p => (Owner: t, Property: p.Name)))
            .Where(x => !PropertyConverterElements.ContainsKey(x))
            .Select(x => $"{x.Owner.Name}.{x.Property}")
            .ToList();
        Assert.True(unlisted.Count == 0, "A property-level [JsonConverter] on an effect is not in PropertyConverterElements: " + string.Join(", ", unlisted));
        foreach (var ((owner, property), element) in PropertyConverterElements)
        {
            // A sample must populate the property, or the check below is vacuous.
            Assert.Contains(effects, e => e.GetType() == owner
                && owner.GetProperty(property)!.GetValue(e) is System.Collections.ICollection { Count: > 0 });
            var problems = NestedOutputProblems(effects, owner, property, walk.Shapes[element], TsFieldsOf(walk, element), RulesJson.Compact);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }

    [Fact]
    public void Converter_probe_that_renames_a_property_is_reported()
    {
        var probeWalk = WalkAll([typeof(ConvBase)]);
        Assert.Contains("extra", probeWalk.Shapes[typeof(ConvDerived)]);
        var problems = ConverterOutputProblems(typeof(ConvBase), [new ConvDerived { Extra = 1 }], probeWalk, _ => null);
        Assert.Contains(problems, p => p.Contains("renamed", StringComparison.Ordinal));
    }

    [Fact]
    public void Converter_probe_reports_a_name_the_ts_type_lacks()
    {
        var probeWalk = WalkAll([typeof(ConvBase)]);
        // "renamed" is in a (pretend) walked shape but TS only has "type": only the TS half can report it.
        var shapeWithRenamed = new Walk(
            new Dictionary<Type, IReadOnlyList<string>> { [typeof(ConvDerived)] = ["kind", "renamed"] },
            probeWalk.Subtypes, probeWalk.ListedOpaque, probeWalk.Unlisted);
        var problems = ConverterOutputProblems(typeof(ConvBase), [new ConvDerived { Extra = 1 }], shapeWithRenamed,
            _ => new HashSet<string> { "type" });
        Assert.DoesNotContain(problems, p => p.Contains("not in the walked shape", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("renamed", StringComparison.Ordinal) && p.Contains("not in the TS type", StringComparison.Ordinal));
    }

    [Fact]
    public void Property_converter_probe_reports_a_nested_name_the_ts_type_lacks()
    {
        // The shape has "renamed", TS does not: only the TS comparison inside NestedOutputProblems can report it.
        var problems = NestedOutputProblems([new ConvHolder { Items = [new ConvItem { Kept = 1 }] }], typeof(ConvHolder), nameof(ConvHolder.Items),
            ["kept", "renamed"], new HashSet<string> { "kept" }, RulesJson.Compact);
        var single = Assert.Single(problems);
        Assert.Contains("renamed", single, StringComparison.Ordinal);
        Assert.Contains("not in the TS type", single, StringComparison.Ordinal);
    }

    [Fact]
    public void Converter_probe_reports_a_missing_type_name_and_a_missing_sample()
    {
        var probeWalk = WalkAll([typeof(ConvBase)]);
        var gaps = ConcreteSubtypeGaps(typeof(ConvBase), probeWalk, new HashSet<Type>());
        Assert.Contains(gaps, g => g.StartsWith(nameof(ConvNoName), StringComparison.Ordinal) && g.Contains("no TypeName", StringComparison.Ordinal));
        Assert.Contains(gaps, g => g.StartsWith(nameof(ConvDerived), StringComparison.Ordinal) && g.Contains("no sample", StringComparison.Ordinal));
        Assert.DoesNotContain(ConcreteSubtypeGaps(typeof(ConvBase), probeWalk, new HashSet<Type> { typeof(ConvDerived) }), g => g.StartsWith(nameof(ConvDerived), StringComparison.Ordinal));
    }

    [Fact]
    public void Property_converter_probe_that_renames_a_nested_property_is_reported()
    {
        var problems = NestedOutputProblems([new ConvHolder { Items = [new ConvItem { Kept = 1 }] }], typeof(ConvHolder), nameof(ConvHolder.Items), ["kept"], null, RulesJson.Compact);
        var single = Assert.Single(problems);
        Assert.Contains("renamed", single, StringComparison.Ordinal);
    }

    [JsonConverter(typeof(ConvBaseConverter))]
    internal abstract class ConvBase;

    internal sealed class ConvDerived : ConvBase
    {
        public const string TypeName = "d";
        public int Extra { get; init; }
    }

    internal sealed class ConvNoName : ConvBase
    {
        public int Other { get; init; }
    }

    // Writes {"kind":"d","renamed":1} although the reflection shape of ConvDerived is {extra}.
    internal sealed class ConvBaseConverter : JsonConverter<ConvBase>
    {
        public override ConvBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, ConvBase value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "d");
            writer.WriteNumber("renamed", 1);
            writer.WriteEndObject();
        }
    }

    internal sealed class ConvItem
    {
        public int Kept { get; init; }
    }

    internal sealed class ConvHolder
    {
        [JsonConverter(typeof(ConvItemListConverter))]
        public IReadOnlyList<ConvItem>? Items { get; init; }
    }

    internal sealed class ConvItemListConverter : JsonConverter<IReadOnlyList<ConvItem>>
    {
        public override IReadOnlyList<ConvItem> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<ConvItem> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value)
            {
                writer.WriteStartObject();
                writer.WriteNumber("kept", item.Kept);
                writer.WriteNumber("renamed", 2);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
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
        IReadOnlySet<string> TypedCommands,
        Walk Walk,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>> Variants);

    private static string TsNameOf(Type type) => NameMap.GetValueOrDefault(type.Name, type.Name);

    /// <summary>The TS fields to check a walked type against: its interface, or the member of the TS union that has its discriminator. Null when TS has none.</summary>
    private static IReadOnlySet<string>? TsFieldsOf(Analysis walk, Type type)
    {
        if (IsInline(type)) return null;
        if (walk.Walk.Subtypes.TryGetValue(type, out var sub))
            return walk.Variants.GetValueOrDefault(TsNameOf(sub.Parent))?.GetValueOrDefault(sub.Discriminator);
        return walk.Ts.GetValueOrDefault(TsNameOf(type));
    }

    // An "inline in ..." entry says the TS type is declared inside its parent, so a same-named TS interface (if any) is a different thing.
    private static bool IsInline(Type type) => UnmatchedTypes.TryGetValue(type.Name, out var reason) && reason.StartsWith("inline in", StringComparison.Ordinal);

    private static bool IsTsOpaque(Analysis walk, Type type) => !IsInline(type) && !walk.Walk.Subtypes.ContainsKey(type) && walk.TsOpaque.Contains(TsNameOf(type));

    // Extra walk roots: DTOs that only an untyped command embeds (UntypedCommands says which).
    private static readonly Type[] ExtraRoots = [];

    private static Analysis Analyse()
    {
        var (commands, typed) = DispatcherResultTypes();
        var walked = WalkAll(typed.Values.SelectMany(t => t).Concat(ExtraRoots).Distinct().ToList());
        var (ts, opaque, variants) = TsParse(File.ReadAllText(Path.Combine(RepoRoot, "src", "Ui", "src", "api", "types.ts")));
        return new Analysis(walked.Shapes, ts, opaque, commands.ToHashSet(), typed.Keys.ToHashSet(), walked, variants);
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
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            {
                type = type.GetGenericArguments()[0];
                continue;
            }
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

    /// <summary>The typed commands whose result root cannot be walked (not a TomeStack type, primitive, enum, string or other leaf), each with its unwrapped type.</summary>
    private static List<string> UnwalkableRoots(IReadOnlyDictionary<string, List<Type>> typed, IReadOnlySet<string> untyped) =>
        typed.Where(kv => !untyped.Contains(kv.Key))
            .SelectMany(kv => kv.Value.Select(t => (Command: kv.Key, Type: Unwrap(t))))
            .Where(x => x.Type != typeof(Task) && x.Type != typeof(ValueTask) && !IsWalkable(x.Type) && !x.Type.IsPrimitive && !x.Type.IsEnum && !Leaf.Contains(x.Type))
            .Select(x => $"{x.Command}: {x.Type}")
            .Order(StringComparer.Ordinal)
            .ToList();

    private static bool IsWalkable(Type type) =>
        !type.IsPrimitive && !type.IsEnum && !Leaf.Contains(type) && type.Namespace is { } ns && ns.StartsWith("TomeStack", StringComparison.Ordinal);

    // Exempt by rule: get-only computed properties (no setter or init accessor) and [JsonExtensionData] members.
    // Positional record parameters have init accessors, so they stay checked.
    private static bool IsChecked(JsonPropertyInfo property) =>
        !property.IsExtensionData
        && property.Get is not null // [JsonIgnore] members are never written
        && (property.Set is not null
            || property.AssociatedParameter is not null
            || (property.AttributeProvider as PropertyInfo)?.SetMethod is not null);

    /// <summary>What the walk found: the checked JSON names per type, the converter-backed subtypes and their parent, and every skipped type.</summary>
    private sealed record Walk(
        IReadOnlyDictionary<Type, IReadOnlyList<string>> Shapes,
        IReadOnlyDictionary<Type, (Type Parent, string Discriminator)> Subtypes,
        IReadOnlySet<string> ListedOpaque,
        IReadOnlyList<Type> Unlisted);

    private static IReadOnlyDictionary<Type, IReadOnlyList<string>> WalkJsonShapes(IEnumerable<Type> roots) => WalkAll(roots).Shapes;

    private static Walk WalkAll(IEnumerable<Type> roots)
    {
        var shapes = new Dictionary<Type, IReadOnlyList<string>>();
        var subtypes = new Dictionary<Type, (Type, string)>();
        var listed = new HashSet<string>();
        var unlisted = new List<Type>();
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(roots);
        while (queue.Count > 0)
        {
            var type = Unwrap(queue.Dequeue());
            if (!IsWalkable(type) || !seen.Add(type)) continue;
            if (OpaqueConverterTypes.ContainsKey(type.Name)) { listed.Add(type.Name); continue; }
            var info = RulesJson.Compact.GetTypeInfo(type);
            if (info.Kind != JsonTypeInfoKind.Object)
            {
                // A custom converter or an abstract base hides the contract from the walk: walk the concrete subtypes it can write instead.
                // A concrete type behind its own converter has no subtypes to walk, so it must be listed in OpaqueConverterTypes.
                if (type.IsAbstract)
                {
                    foreach (var sub in type.Assembly.GetTypes().Where(t => t != type && !t.IsAbstract && type.IsAssignableFrom(t)))
                    {
                        if (sub.GetField("TypeName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is string discriminator)
                            subtypes[sub] = (type, discriminator);
                        queue.Enqueue(sub);
                    }
                }
                else unlisted.Add(type);
                continue;
            }
            var names = info.Properties.Where(IsChecked).Select(p => p.Name).ToList();
            // EffectJsonConverter writes the discriminator first from the (get-only) Type property.
            if (subtypes.ContainsKey(type) && !names.Contains("type")) names.Add("type");
            shapes[type] = names;
            foreach (var property in info.Properties) queue.Enqueue(property.PropertyType);
            if (info.PolymorphismOptions is { } polymorphism)
                foreach (var derived in polymorphism.DerivedTypes) queue.Enqueue(derived.DerivedType);
        }
        return new Walk(shapes, subtypes, listed, unlisted);
    }

    // ---- TS side ----

    [GeneratedRegex(@"(?:export\s+)?interface\s+(\w+)\s*(?:<[^{]*?>)?\s*(?:extends\s+([^{]+?))?\s*\{")]
    private static partial Regex InterfaceHead();

    [GeneratedRegex(@"export\s+type\s+(\w+)\s*(?:<[^=]*?>)?\s*=\s*")]
    private static partial Regex AliasHead();

    [GeneratedRegex("""^\s*(?:readonly\s+)?["']?([A-Za-z_$][\w$]*)["']?\??\s*:""")]
    private static partial Regex FieldStart();

    [GeneratedRegex(@"^\w+$")]
    private static partial Regex PlainName();

    [GeneratedRegex(@"\n(?=\s*(?:export|declare|const|function|import|type|interface)\b)")]
    private static partial Regex NextDeclaration();

    [GeneratedRegex(@"^\{\s*type\s*:\s*'([^']+)'")]
    private static partial Regex DiscriminatorStart();

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> TsShapes(string typesTs) => TsParse(typesTs).Shapes;

    private static (IReadOnlyDictionary<string, IReadOnlySet<string>> Shapes, IReadOnlySet<string> Opaque,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>> Variants) TsParse(string typesTs)
    {
        var text = StripComments(typesTs);
        var own = new Dictionary<string, HashSet<string>>();
        var parents = new Dictionary<string, List<string>>();
        var aliases = new Dictionary<string, List<string>>(); // alias -> parts: "{...}" bodies are stored as own fields, names as parents
        var opaque = new HashSet<string>();
        var rawVariants = new Dictionary<string, List<(string Discriminator, HashSet<string> Fields, List<string> Parents)>>();

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
            // A discriminated union: every member is `(Base & { type: 'x'; ... })` or `{ type: 'x'; ... }`.
            var members = new List<(string Discriminator, HashSet<string> Fields, List<string> Parents)>();
            foreach (var part in parts)
            {
                var member = part.StartsWith('(') && part.EndsWith(')') ? part[1..^1].Trim() : part;
                var pieces = SplitTop(member, ['&']).Select(p => p.Trim()).ToList();
                var body = pieces.FirstOrDefault(p => p.StartsWith('{') && p.EndsWith('}'));
                if (body is null || DiscriminatorStart().Match(body) is not { Success: true } d) { members.Clear(); break; }
                members.Add((d.Groups[1].Value, FieldsOfBody(body[1..^1]), pieces.Where(p => PlainName().IsMatch(p)).ToList()));
            }
            if (parts.Count > 1 && members.Count == parts.Count) rawVariants[name] = members;
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
        var variants = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>>();
        foreach (var (alias, members) in rawVariants)
        {
            var byValue = new Dictionary<string, IReadOnlySet<string>>();
            foreach (var (discriminator, fields, parentNames) in members)
            {
                var all = new HashSet<string>(fields);
                foreach (var parent in parentNames) all.UnionWith(Resolve(parent, [], out _));
                byValue[discriminator] = all;
            }
            variants[alias] = byValue;
        }
        return (result, opaque, variants);

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
