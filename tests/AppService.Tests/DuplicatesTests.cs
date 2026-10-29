using System.Text.Json;
using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.ImportWorker;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Import Lab (L6, design 9.2): <c>content.duplicates</c> lists entries that look alike and never merges them. Same kind, same rules
/// family, compared by name and by the hash of their rules. All names and content are invented.
/// </summary>
public class DuplicatesTests
{
    private static ModifierEffect Bonus(string id, int value, string target = FieldIds.Initiative) =>
        new() { Id = id, Operation = ModifierOperation.Bonus, Target = target, Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private static ContentReference Publish(TomeStackApp app, SourceRecord source, string name, string family, params Effect[] effects) =>
        app.Publish(app.SaveDraft(Revision(source, name, family, RevisionStatus.Draft, effects))).Published;

    private static ContentRevision Revision(SourceRecord source, string name, string family, RevisionStatus status, Effect[] effects, ContentKind kind = ContentKind.Feature) => new()
    {
        ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = kind, Name = name, RulesFamilies = [family], Provenance = new(source.Id), Status = status, Effects = effects,
    };

    private static SourceRecord Source(TomeStackApp app, string title, string family) => app.CreateHomebrewSource(new(title, [family]));

    private static IReadOnlyList<DuplicateGroup> Fixture(IReadOnlyList<DuplicateGroup> groups) =>
        [.. groups.Where(g => g.Members.Any(m => m.Name.StartsWith("Fixture", StringComparison.Ordinal)))];

    private static string Snapshot(TomeStackApp app) =>
        JsonSerializer.Serialize(new object[] { app.Store.ListRevisionsInOrder(), app.Store.ListSources() }, RulesJson.Compact);

    private static Guid NewJob(TomeStackApp app, SourceRecord source)
    {
        var job = new ImportJobRecord { Id = Guid.NewGuid(), SourceId = source.Id, Sha256 = new string('a', 64) };
        app.Store.SaveImportJob(job);
        return job.Id;
    }

    private static Guid Candidate(TomeStackApp app, Guid job, SourceRecord source, string name, string family, CandidateStatus status = CandidateStatus.Pending, params Effect[] effects)
    {
        var id = Guid.NewGuid();
        var draft = new DraftCandidate(id, source.Id, new PageRef(1), "Fixture excerpt.", ContentKind.Feature, name, [family], effects, 0.9, []);
        app.Store.SaveImportCandidate(new StoredCandidate { Id = id, JobId = job, Candidate = draft, Status = status, UpdatedAt = TempApp.Now });
        return id;
    }

    [Fact]
    public void Same_name_two_sources_different_rules_stay_distinct()
    {
        using var temp = new TempApp();
        var first = Source(temp.App, "Fixture Book One", RulesFamilies.Srd521);
        var second = Source(temp.App, "Fixture Book Two", RulesFamilies.Srd521);
        Publish(temp.App, first, "Fixture Twin", RulesFamilies.Srd521, Bonus("a", 1));
        Publish(temp.App, second, "Fixture Twin", RulesFamilies.Srd521, Bonus("a", 2));
        var before = Snapshot(temp.App);

        var group = Assert.Single(Fixture(temp.App.ContentDuplicates(new())));

        Assert.Equal((DuplicateClass.SameNameDifferentRules, RulesFamilies.Srd521), (group.Class, group.Family));
        Assert.Equal(["Fixture Book One", "Fixture Book Two"], group.Members.Select(m => m.SourceName).Order(StringComparer.Ordinal));
        Assert.Equal(before, Snapshot(temp.App)); // listed, never merged: both revisions are still there, unchanged
        Assert.Equal(2, temp.App.Store.ListRevisionsInOrder().Count(r => r.Name == "Fixture Twin" && r.Status == RevisionStatus.Published));
    }

    [Fact]
    public void Exact_duplicates_flagged()
    {
        using var temp = new TempApp();
        var first = Source(temp.App, "Fixture Book One", RulesFamilies.Srd521);
        var second = Source(temp.App, "Fixture Book Two", RulesFamilies.Srd521);
        Publish(temp.App, first, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        Publish(temp.App, second, "  fixture   EMBER ", RulesFamilies.Srd521, Bonus("other-id", 1));

        var group = Assert.Single(Fixture(temp.App.ContentDuplicates(new())));

        Assert.Equal(DuplicateClass.Exact, group.Class);
        Assert.Equal(2, group.Members.Count);
        Assert.All(group.Members, m => Assert.NotNull(m.RevisionId));
    }

    [Fact]
    public void Same_rules_under_another_name_is_listed_as_possible()
    {
        using var temp = new TempApp();
        var source = Source(temp.App, "Fixture Book One", RulesFamilies.Srd521);
        Publish(temp.App, source, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 4));
        Publish(temp.App, source, "Fixture Cinder", RulesFamilies.Srd521, Bonus("b", 4));
        // Text-only entries have no rules to share, so they are not "the same rules".
        Publish(temp.App, source, "Fixture Words One", RulesFamilies.Srd521);
        Publish(temp.App, source, "Fixture Words Two", RulesFamilies.Srd521);

        var group = Assert.Single(Fixture(temp.App.ContentDuplicates(new())));

        Assert.Equal(DuplicateClass.SameRulesDifferentName, group.Class);
        Assert.Equal(["Fixture Cinder", "Fixture Ember"], group.Members.Select(m => m.Name));
    }

    [Fact]
    public void Never_across_families()
    {
        using var temp = new TempApp();
        var older = Source(temp.App, "Fixture Old Book", RulesFamilies.Srd51);
        var newer = Source(temp.App, "Fixture New Book", RulesFamilies.Srd521);
        Publish(temp.App, older, "Fixture Ember", RulesFamilies.Srd51, Bonus("a", 1));
        Publish(temp.App, newer, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        Publish(temp.App, older, "Fixture Frost", RulesFamilies.Srd51, Bonus("a", 2));
        Publish(temp.App, newer, "Fixture Frost", RulesFamilies.Srd521, Bonus("a", 3));
        var job = NewJob(temp.App, newer);
        Candidate(temp.App, job, newer, "Fixture Ember", RulesFamilies.Srd521, effects: Bonus("a", 1));

        Assert.Empty(Fixture(temp.App.ContentDuplicates(new())));
        // The candidate (5.2.1) matches the 5.2.1 entry only, never the 5.1 one.
        var group = Assert.Single(Fixture(temp.App.ContentDuplicates(new(JobId: job))));
        Assert.Equal(RulesFamilies.Srd521, group.Family);
        Assert.DoesNotContain(group.Members, m => m.SourceName == "Fixture Old Book");
    }

    [Fact]
    public void A_revision_for_both_families_is_compared_inside_each()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Both Book", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        var both = Revision(source, "Fixture Ember", RulesFamilies.Srd51, RevisionStatus.Draft, [Bonus("a", 1)]) with { RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521] };
        temp.App.Publish(temp.App.SaveDraft(both));
        Publish(temp.App, source, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));

        var group = Assert.Single(Fixture(temp.App.ContentDuplicates(new())));

        Assert.Equal((DuplicateClass.Exact, RulesFamilies.Srd521), (group.Class, group.Family));
    }

    [Fact]
    public void A_draft_is_not_a_library_entry()
    {
        using var temp = new TempApp();
        var source = Source(temp.App, "Fixture Book One", RulesFamilies.Srd521);
        Publish(temp.App, source, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        temp.App.SaveDraft(Revision(source, "Fixture Ember", RulesFamilies.Srd521, RevisionStatus.Draft, [Bonus("a", 1)]));
        var job = NewJob(temp.App, source);
        Candidate(temp.App, job, source, "Fixture Ember", RulesFamilies.Srd521, effects: Bonus("a", 1));

        Assert.Empty(Fixture(temp.App.ContentDuplicates(new())));
        var group = Assert.Single(temp.App.ContentDuplicates(new(JobId: job)));
        Assert.Equal(2, group.Members.Count); // the candidate and the one published entry, not the draft
    }

    [Fact]
    public void A_job_lists_its_pending_candidates_against_the_library()
    {
        using var temp = new TempApp();
        var library = Source(temp.App, "Fixture Library Book", RulesFamilies.Srd521);
        var imported = Source(temp.App, "Fixture Imported Book", RulesFamilies.Srd521);
        Publish(temp.App, library, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        Publish(temp.App, library, "Fixture Frost", RulesFamilies.Srd521, Bonus("a", 2));
        Publish(temp.App, library, "Fixture Gale", RulesFamilies.Srd521, Bonus("a", 3));
        var job = NewJob(temp.App, imported);
        var exact = Candidate(temp.App, job, imported, "Fixture Ember", RulesFamilies.Srd521, effects: Bonus("x", 1));
        var different = Candidate(temp.App, job, imported, "Fixture Frost", RulesFamilies.Srd521, effects: Bonus("x", 9));
        Candidate(temp.App, job, imported, "Fixture Gale", RulesFamilies.Srd521, CandidateStatus.Ignored, Bonus("x", 3));
        Candidate(temp.App, job, imported, "Fixture Unique", RulesFamilies.Srd521, effects: Bonus("x", 7));

        var groups = temp.App.ContentDuplicates(new(JobId: job));

        Assert.Equal([DuplicateClass.Exact, DuplicateClass.SameNameDifferentRules], groups.Select(g => g.Class));
        Assert.Contains(groups[0].Members, m => m.CandidateId == exact);
        Assert.Contains(groups[1].Members, m => m.CandidateId == different);
        Assert.All(groups, g => Assert.Contains(g.Members, m => m.ContentId is not null && m.SourceName == "Fixture Library Book"));
        Assert.Equal(4, temp.App.Store.QueryImportCandidates(job, new(), 0, null).Total); // nothing was accepted or removed
    }

    [Fact]
    public void A_source_lists_the_groups_it_is_in()
    {
        using var temp = new TempApp();
        var one = Source(temp.App, "Fixture Book One", RulesFamilies.Srd521);
        var two = Source(temp.App, "Fixture Book Two", RulesFamilies.Srd521);
        var three = Source(temp.App, "Fixture Book Three", RulesFamilies.Srd521);
        Publish(temp.App, one, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        Publish(temp.App, two, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        Publish(temp.App, three, "Fixture Frost", RulesFamilies.Srd521, Bonus("a", 2));
        Publish(temp.App, two, "Fixture Frost", RulesFamilies.Srd521, Bonus("a", 2));

        Assert.Equal(2, temp.App.ContentDuplicates(new(SourceId: two.Id)).Count);
        var forOne = Assert.Single(temp.App.ContentDuplicates(new(SourceId: one.Id)));
        Assert.Contains(forOne.Members, m => m.SourceId == two.Id); // the other side is shown, whichever source it is in
        Assert.Throws<AppValidationException>(() => temp.App.ContentDuplicates(new(SourceId: Guid.NewGuid())));
        Assert.Throws<AppValidationException>(() => temp.App.ContentDuplicates(new(JobId: Guid.NewGuid())));
    }

    [Fact]
    public void Finds_without_writing()
    {
        using var temp = new TempApp();
        var one = Source(temp.App, "Fixture Book One", RulesFamilies.Srd521);
        var two = Source(temp.App, "Fixture Book Two", RulesFamilies.Srd521);
        Publish(temp.App, one, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        Publish(temp.App, two, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        var job = NewJob(temp.App, two);
        var candidate = Candidate(temp.App, job, two, "Fixture Ember", RulesFamilies.Srd521, effects: Bonus("a", 1));
        var before = Snapshot(temp.App);
        var stored = temp.App.Store.FindImportCandidate(candidate)!.Etag;

        temp.App.ContentDuplicates(new());
        temp.App.ContentDuplicates(new(SourceId: one.Id));
        temp.App.ContentDuplicates(new(JobId: job));

        Assert.Equal(before, Snapshot(temp.App));
        Assert.Equal(stored, temp.App.Store.FindImportCandidate(candidate)!.Etag);
        Assert.Equal(CandidateStatus.Pending, temp.App.Store.FindImportCandidate(candidate)!.Status);
    }

    [Fact]
    public void The_command_answers_with_groups_in_camel_case()
    {
        using var temp = new TempApp();
        var one = Source(temp.App, "Fixture Book One", RulesFamilies.Srd521);
        var two = Source(temp.App, "Fixture Book Two", RulesFamilies.Srd521);
        Publish(temp.App, one, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        Publish(temp.App, two, "Fixture Ember", RulesFamilies.Srd521, Bonus("a", 1));
        var dispatcher = new CommandDispatcher(temp.App);

        var response = JsonDocument.Parse(dispatcher.Dispatch($$$"""{"id":"1","command":"content.duplicates","payload":{"sourceId":"{{{one.Id}}}"}}""")).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var group = Assert.Single(response.GetProperty("result").EnumerateArray());
        Assert.Equal("exact", group.GetProperty("class").GetString());
        Assert.Equal(RulesFamilies.Srd521, group.GetProperty("family").GetString());
        Assert.Equal("Fixture Book Two", group.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("sourceName").GetString()).Order(StringComparer.Ordinal).Last());
        Assert.Contains("content.duplicates", CommandDispatcher.Commands);
    }

    // ---- The fingerprint ----

    private static readonly string[] Targets = [FieldIds.Initiative, "save.wis", "skill.stealth", "ac"];

    public sealed record EffectSpec(int Target, int Value, int Seed)
    {
        public override string ToString() => $"{Target}:{Value}/{Seed}";
    }

    private static Gen<EffectSpec[]> Specs { get; } =
        from list in (from t in Gen.Choose(0, Targets.Length - 1) from v in Gen.Choose(1, 5) from s in Gen.Choose(0, 1000) select new EffectSpec(t, v, s)).ArrayOf()
        select list.Take(6).ToArray();

    /// <summary>The same effects with other ids, and each written with its keys in another order (read back through JSON).</summary>
    private static List<Effect> Rewritten(EffectSpec[] specs) =>
        [.. specs.Select((s, i) =>
        {
            var node = JsonSerializer.SerializeToNode<Effect>(Bonus($"e{i}", s.Value, Targets[s.Target]), RulesJson.Compact)!.AsObject();
            node["id"] = $"renamed-{s.Seed}-{i}";
            var keys = node.Select(p => p.Key).OrderBy(k => (k.GetHashCode() ^ s.Seed) & 0xffff).ThenBy(k => k, StringComparer.Ordinal).ToList();
            var shuffled = new JsonObject();
            foreach (var key in keys)
            {
                var value = node[key];
                node.Remove(key);
                shuffled[key] = value;
            }
            return JsonSerializer.Deserialize<Effect>(shuffled.ToJsonString(), RulesJson.Compact)!;
        })];

    [Property(MaxTest = 200)]
    public Property Fingerprint_ignores_effect_ids_and_key_order() =>
        Prop.ForAll(Specs.ToArbitrary(), specs =>
        {
            Effect[] original = [.. specs.Select((s, i) => Bonus($"id-{i}", s.Value, Targets[s.Target]))];
            return Fingerprint.Of(original) == Fingerprint.Of(Rewritten(specs));
        });

    // ---- The canonical form, on JSON with nesting (the typed property above cannot see key order: typing re-serialises it) ----

    private static readonly string[] KeyPool = ["a", "b", "c", "d", "e", "Z", "id", "m_1"];

    private static JsonNode Leaf(Random rng) => rng.Next(3) switch
    {
        0 => JsonValue.Create(rng.Next(10))!,
        1 => JsonValue.Create("v" + rng.Next(10))!,
        _ => JsonValue.Create(rng.Next(2) == 0)!,
    };

    private static JsonObject Obj(Random rng, int depth)
    {
        var node = new JsonObject();
        foreach (var key in KeyPool.OrderBy(_ => rng.Next()).Take(1 + rng.Next(4)))
        {
            node[key] = depth > 0
                ? rng.Next(4) switch { 0 => Obj(rng, depth - 1), 1 => new JsonArray([.. Enumerable.Range(0, 1 + rng.Next(3)).Select(_ => (JsonNode)Obj(rng, depth - 1))]), _ => Leaf(rng) }
                : Leaf(rng);
        }
        return node;
    }

    /// <summary>The same JSON with the keys of every object, at every level, in another order.</summary>
    private static JsonNode Permuted(JsonNode node, Random rng) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(_ => rng.Next()).Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : Permuted(p.Value, rng)))),
        JsonArray a => new JsonArray([.. a.Select(x => x is null ? null : Permuted(x, rng))]),
        _ => node.DeepClone(),
    };

    [Property(MaxTest = 300)]
    public Property Canonical_form_ignores_key_order_at_every_level_and_top_level_ids() =>
        Prop.ForAll(Gen.Choose(0, 1_000_000).ToArbitrary(), seed =>
        {
            var rng = new Random(seed);
            var original = new JsonArray([.. Enumerable.Range(0, rng.Next(5)).Select(i =>
            {
                var effect = Obj(rng, 3);
                effect["id"] = $"first-{i}";
                return (JsonNode)effect;
            })]);
            var other = (JsonArray)Permuted(original, new Random(seed + 1));
            foreach (var (effect, i) in other.OfType<JsonObject>().Select((e, i) => (e, i)))
                effect["id"] = $"second-{seed}-{i}";
            return Fingerprint.OfJson(original) == Fingerprint.OfJson(other);
        });

    [Fact]
    public void Canonical_form_tells_a_nested_change_apart_and_keeps_array_order()
    {
        JsonArray Parse(string json) => (JsonArray)JsonNode.Parse(json)!;
        Assert.NotEqual(Fingerprint.OfJson(Parse("""[{"id":"x","a":{"b":[{"c":1}]}}]""")), Fingerprint.OfJson(Parse("""[{"id":"x","a":{"b":[{"c":2}]}}]""")));
        Assert.NotEqual(Fingerprint.OfJson(Parse("""[{"a":1},{"a":2}]""")), Fingerprint.OfJson(Parse("""[{"a":2},{"a":1}]""")));
        Assert.Equal("""{"a":{"x":1,"y":2},"b":[{"m":1,"n":2}]}""", Fingerprint.Canonical(JsonNode.Parse("""{"b":[{"n":2,"m":1}],"a":{"y":2,"x":1}}""")!));
    }

    [Fact]
    public void A_default_written_out_and_a_default_left_out_give_the_same_fingerprint()
    {
        var written = JsonSerializer.SerializeToNode<Effect>(Bonus("a", 1), RulesJson.Compact)!.AsObject();
        Assert.True(written.ContainsKey("automation")); // the default is written out here
        var left = (JsonObject)written.DeepClone();
        left.Remove("automation");
        Assert.False(left.ContainsKey("automation"));

        var explicitEffect = JsonSerializer.Deserialize<Effect>(written.ToJsonString(), RulesJson.Compact)!;
        var omittedEffect = JsonSerializer.Deserialize<Effect>(left.ToJsonString(), RulesJson.Compact)!;

        Assert.Equal(Fingerprint.Of([explicitEffect]), Fingerprint.Of([omittedEffect]));
    }

    [Fact]
    public void An_entry_with_no_name_is_skipped_and_unnamed_entries_do_not_form_a_group()
    {
        using var temp = new TempApp();
        var source = Source(temp.App, "Fixture Book One", RulesFamilies.Srd521);
        var job = NewJob(temp.App, source);
        Candidate(temp.App, job, source, "", RulesFamilies.Srd521, effects: Bonus("a", 1));
        Candidate(temp.App, job, source, "   ", RulesFamilies.Srd521, effects: Bonus("a", 1));

        Assert.Empty(temp.App.ContentDuplicates(new(JobId: job)));
    }

    [Fact]
    public void Fingerprint_tells_different_rules_apart()
    {
        Assert.NotEqual(Fingerprint.Of([Bonus("a", 1)]), Fingerprint.Of([Bonus("a", 2)]));
        Assert.NotEqual(Fingerprint.Of([Bonus("a", 1)]), Fingerprint.Of([Bonus("a", 1, "save.wis")]));
        Assert.NotEqual(Fingerprint.Of([Bonus("a", 1)]), Fingerprint.Of([]));
        Assert.Equal(64, Fingerprint.Of([]).Length);
    }
}
