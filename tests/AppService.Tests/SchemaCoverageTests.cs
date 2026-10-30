using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Keeps tests/RulesFixtures/downconvert/requires-vN.json (the features each schema version adds, D85) in step with
/// docs/schemas: every listed feature must exist in that version's schema, and every property a schema version adds
/// over the previous one must be listed, so a new field cannot reach an old build unlowered unnoticed.
/// </summary>
public sealed class SchemaCoverageTests
{
    private static readonly string Base = AppContext.BaseDirectory;

    /// <summary>Consecutive schema pairs whose added properties must be listed (previous, next).</summary>
    public static TheoryData<string, int, int> Pairs => new()
    {
        { "content", 9, 10 },
        { "content", 10, 11 },
        { "character", 8, 9 },
        { "character", 9, 10 },
    };

    /// <summary>One case per (kind, version) block of requires-vN.json.</summary>
    public static TheoryData<string, int> Blocks()
    {
        var data = new TheoryData<string, int>();
        var requires = LoadRequires();
        foreach (var (kind, versions) in requires)
        {
            foreach (var (version, _) in versions!.AsObject())
            {
                data.Add(kind, int.Parse(version));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void Every_requires_vN_feature_is_in_the_schema_files(string kind, int version)
    {
        var features = Features(LoadRequires(), kind, version);
        var schema = SchemaPath(kind, version);
        if (!File.Exists(schema))
        {
            // Content v12 (MECH-X) is announced in the file before its schema is published; nothing is checked until it exists.
            Assert.True(
                version > LatestSchemaVersion(kind),
                $"{kind} v{version} has no schema file {Path.GetFileName(schema)} but is not above the latest one.");
            return;
        }

        var missing = MissingFromSchema(Collect(schema), features);
        Assert.True(missing.Count == 0, $"{kind} v{version}: requires-vN.json lists features the schema does not declare: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Every_schema_property_added_at_vN_is_listed(string kind, int previous, int next)
    {
        var features = Features(LoadRequires(), kind, next);
        var unlisted = UnlistedAdditions(Collect(SchemaPath(kind, previous)), Collect(SchemaPath(kind, next)), features);
        Assert.True(
            unlisted.Count == 0,
            $"{kind} v{previous} -> v{next}: schema paths added but missing from requires-vN.json (add them there once DownConvert.Below handles them, else list them as coverage gaps): {string.Join(", ", unlisted)}");
    }

    [Fact]
    public void A_listed_feature_missing_from_the_schema_is_reported()
    {
        var schema = Collect(SchemaPath("content", 11));
        Assert.Empty(MissingFromSchema(schema, ["toggle.cost", "roll.dice:none", "modifier.target:speed.fly"]));
        Assert.Equal(["toggle.noSuchField", "modifier.target:warp"], MissingFromSchema(schema, ["toggle.cost", "toggle.noSuchField", "modifier.target:warp"]));
    }

    [Fact]
    public void A_schema_addition_that_is_not_listed_is_reported()
    {
        var v10 = Collect(SchemaPath("content", 10));
        var v11 = Collect(SchemaPath("content", 11));
        var all = Features(LoadRequires(), "content", 11);
        Assert.Empty(UnlistedAdditions(v10, v11, all));
        var without = all.Where(f => f != "toggle.cost" && f != "attackRider").ToList();
        var unlisted = UnlistedAdditions(v10, v11, without);
        Assert.Contains("toggle.cost", unlisted);
        Assert.Contains("attackRider.dice", unlisted);
    }

    private static JsonObject LoadRequires() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Base, "RulesFixtures", "downconvert", "requires-vN.json")))!.AsObject();

    private static List<string> Features(JsonObject requires, string kind, int version) =>
        requires[kind]![version.ToString()]!.AsArray().Select(e => e!["feature"]!.GetValue<string>()).ToList();

    private static string SchemaPath(string kind, int version) =>
        Path.Combine(Base, "schemas", kind == "content" ? $"content-revision.v{version}.schema.json" : $"character.v{version}.schema.json");

    private static int LatestSchemaVersion(string kind) =>
        Directory.GetFiles(Path.Combine(Base, "schemas"), kind == "content" ? "content-revision.v*.schema.json" : "character.v*.schema.json")
            .Select(f => int.Parse(Regex.Match(Path.GetFileName(f), @"\.v(\d+)\.").Groups[1].Value))
            .Max();

    private static List<string> MissingFromSchema(SchemaPaths schema, IEnumerable<string> features) =>
        features.Where(f => !schema.Has(f)).ToList();

    private static List<string> UnlistedAdditions(SchemaPaths previous, SchemaPaths next, IReadOnlyList<string> features) =>
        next.Paths.Where(p => !previous.Paths.Contains(p) && !IsVersionMarker(p) && !Covered(p, features)).OrderBy(p => p, StringComparer.Ordinal).ToList();

    private static bool IsVersionMarker(string path) => path.StartsWith("schemaVersion", StringComparison.Ordinal);

    /// <summary>
    /// A path is covered when a listed feature names it, names an ancestor of it, or (for a grant kind such as
    /// grant.damageResistance) names the enum member "grant.grant:damageResistance".
    /// </summary>
    private static bool Covered(string path, IReadOnlyList<string> features)
    {
        foreach (var feature in features)
        {
            var scope = feature.Split(':')[0];
            if (path == feature
                || path.StartsWith(feature + ":", StringComparison.Ordinal)
                || path.StartsWith(feature + ".", StringComparison.Ordinal)
                || path.StartsWith(feature + "[]", StringComparison.Ordinal)
                || feature.Contains(':') && (path == scope || path.StartsWith(scope + ".", StringComparison.Ordinal))
                || Alias(feature) == path)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>"grant.damageResistance" is spelled in the schema as the member damageResistance of grant.grant.</summary>
    private static string? Alias(string feature)
    {
        var dot = feature.IndexOf('.');
        return dot > 0 && !feature.Contains(':') ? $"{feature[..dot]}.{feature[..dot]}:{feature[(dot + 1)..]}" : null;
    }

    private static SchemaPaths Collect(string file)
    {
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        var paths = new SchemaPaths();
        Walk(root, "", root, paths, []);
        return paths;
    }

    /// <summary>
    /// Collects dotted property paths from a schema: "$ref" is followed (a cycle is cut), allOf/anyOf/oneOf/if/then/else are
    /// merged into the same path, an array's items add "[]", a oneOf branch that fixes properties.type to a const is
    /// rooted at that effect type (so "toggle.cost", not "effects[].toggle.cost"), enum and const members are added
    /// as "path:member" and the top-level alternatives of a pattern as "path:alternative" (plus "path:prefix.*").
    /// </summary>
    private static void Walk(JsonNode? node, string path, JsonObject root, SchemaPaths paths, HashSet<string> refs)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            var name = reference[(reference.LastIndexOf('/') + 1)..];
            if (refs.Add(name))
            {
                Walk(root["$defs"]?[name], path, root, paths, refs);
                refs.Remove(name);
            }
        }

        if (schema["description"]?.GetValue<string>() is { } description)
        {
            paths.Descriptions.Add(description);
        }

        foreach (var member in schema["enum"]?.AsArray() ?? [])
        {
            paths.Paths.Add($"{path}:{member}");
        }

        if (schema["const"] is { } constant)
        {
            paths.Paths.Add($"{path}:{constant}");
        }

        if (schema["pattern"]?.GetValue<string>() is { } pattern)
        {
            AddPattern(path, pattern, paths);
        }

        var required = schema["required"]?.AsArray().Select(r => r!.GetValue<string>()).ToHashSet() ?? [];
        foreach (var (name, child) in schema["properties"]?.AsObject() ?? [])
        {
            var childPath = path.Length == 0 ? name : $"{path}.{name}";
            paths.Paths.Add(childPath);
            if (!required.Contains(name))
            {
                paths.Optional.Add(childPath);
            }

            Walk(child, childPath, root, paths, refs);
        }

        Walk(schema["items"], path + "[]", root, paths, refs);
        foreach (var keyword in new[] { "allOf", "anyOf", "oneOf" })
        {
            foreach (var branch in schema[keyword]?.AsArray() ?? [])
            {
                var effectType = branch?["properties"]?["type"]?["const"]?.GetValue<string>();
                if (keyword == "oneOf" && effectType is not null)
                {
                    paths.Paths.Add(effectType);
                    Walk(branch, effectType, root, paths, refs);
                }
                else
                {
                    Walk(branch, path, root, paths, refs);
                }
            }
        }

        foreach (var keyword in new[] { "if", "then", "else" })
        {
            Walk(schema[keyword], path, root, paths, refs);
        }
    }

    private static void AddPattern(string path, string pattern, SchemaPaths paths)
    {
        var body = pattern.TrimStart('^').TrimEnd('$');
        if (body.StartsWith('(') && body.EndsWith(')'))
        {
            body = body[1..^1];
        }

        var depth = 0;
        var start = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length)
            {
                if (body[i] == '(' || body[i] == '[')
                {
                    depth++;
                }
                else if (body[i] == ')' || body[i] == ']')
                {
                    depth--;
                }
            }

            if (i == body.Length || body[i] == '|' && depth == 0)
            {
                var alternative = body[start..i].Replace("\\.", ".");
                var head = Regex.Match(alternative, "^[A-Za-z]+").Value;
                if (alternative.IndexOfAny(['[', '(', '{']) < 0)
                {
                    paths.Paths.Add($"{path}:{alternative}");
                }
                else if (head.Length > 0 && alternative.Length > head.Length)
                {
                    // A family of ids ("passive.(...)", "resource.[^\s.]+") is named by its head: "passive.*".
                    paths.Paths.Add($"{path}:{head}.*");
                }

                start = i + 1;
            }
        }
    }

    /// <summary>The property paths (and enum, const and pattern members) of one schema file.</summary>
    private sealed class SchemaPaths
    {
        public HashSet<string> Paths { get; } = [];

        public HashSet<string> Optional { get; } = [];

        public List<string> Descriptions { get; } = [];

        /// <summary>
        /// A feature is declared when it is a collected path, when "x:none" names an optional property x (the property
        /// may be left out), or when "formula:NAME" is a formula identifier the schema documents as "NAME.".
        /// </summary>
        public bool Has(string feature)
        {
            if (Paths.Contains(feature))
            {
                return true;
            }

            if (feature.EndsWith(":none", StringComparison.Ordinal))
            {
                var property = feature[..^5];
                return Paths.Contains(property) && Optional.Contains(property);
            }

            if (feature.StartsWith("formula:", StringComparison.Ordinal))
            {
                var identifier = feature["formula:".Length..] + ".";
                return Descriptions.Any(d => d.Contains(identifier, StringComparison.Ordinal));
            }

            var alias = Alias(feature);
            return alias is not null && Paths.Contains(alias);
        }
    }
}
