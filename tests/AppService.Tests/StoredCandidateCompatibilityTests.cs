using System.Text.Json;
using System.Text.Json.Serialization;
using TomeStack.ImportWorker;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D64 (Import Lab L4, approval A1): spans and <c>attachmentSha256</c> are additive, optional fields of the stored candidate.
/// <see cref="StoredCandidate.CurrentSchemaVersion"/> stays 1; a row stored without them reads and writes byte-for-byte as it
/// did, and accepting a candidate never reads them. Invented text only.
/// </summary>
public class StoredCandidateCompatibilityTests
{
    /// <summary>A row as the previous build wrote it: no span keys, no attachment hash (property order is the serializer's).</summary>
    private const string PreviousBuildRow =
        """{"id":"0b5d8a10-0000-4000-8000-000000000001","schemaVersion":1,"jobId":"0b5d8a10-0000-4000-8000-000000000002","candidate":{"id":"0b5d8a10-0000-4000-8000-000000000001","sourceId":"0b5d8a10-0000-4000-8000-000000000003","page":{"start":2},"excerpt":"Placeholder Feat\nOrigin Feat\nYou gain a bonus of 1 to initiative.","proposedKind":"feat","proposedName":"Placeholder Feat","rulesFamilies":["srd-5.2.1"],"proposedEffects":[{"type":"modifier","id":"bonus-1","operation":"bonus","target":"initiative","value":"1","stacking":"stack","automation":"automatic","timing":"always"}],"confidence":0.85,"uncertainties":[],"unresolvedReferences":[],"fields":{"category":"Origin"},"lowConfidenceFields":[],"summary":"You gain a bonus of 1 to initiative."},"status":"pending","updatedAt":"2026-10-01T00:00:00+00:00"}""";

    private static readonly CandidateSpan Span = new(2, null, 4, 5, null, null, "feat.bonus", 0.85);

    [Fact]
    public void Candidate_without_spans_round_trips()
    {
        var read = JsonSerializer.Deserialize<StoredCandidate>(PreviousBuildRow, RulesJson.Compact)!;

        Assert.Equal(1, StoredCandidate.CurrentSchemaVersion); // additive: no version change
        Assert.Null(read.Candidate.EffectSpans);
        Assert.Null(read.Candidate.NameSpan);
        Assert.Null(read.AttachmentSha256);
        Assert.Equal(PreviousBuildRow, JsonSerializer.Serialize(read, RulesJson.Compact)); // read and saved: the same bytes
        Assert.Equal(PreviousBuildRow, JsonSerializer.Serialize(JsonSerializer.Deserialize<StoredCandidate>(JsonSerializer.Serialize(read, RulesJson.Compact), RulesJson.Compact), RulesJson.Compact));
    }

    [Fact]
    public void Spans_are_stored_as_plain_additive_keys_and_read_back()
    {
        var read = JsonSerializer.Deserialize<StoredCandidate>(PreviousBuildRow, RulesJson.Compact)!;
        var withSpans = read with
        {
            AttachmentSha256 = new string('a', 64),
            Candidate = read.Candidate with { EffectSpans = new Dictionary<string, CandidateSpan> { ["bonus-1"] = Span }, NameSpan = Span with { PatternId = "feat.header" } },
        };

        var json = JsonSerializer.Serialize(withSpans, RulesJson.Compact);
        var again = JsonSerializer.Deserialize<StoredCandidate>(json, RulesJson.Compact)!;

        Assert.Contains("\"effectSpans\":{\"bonus-1\":{\"page\":2,\"blockStart\":4,\"blockEnd\":5,\"patternId\":\"feat.bonus\",\"confidence\":0.85}}", json, StringComparison.Ordinal);
        Assert.Contains("\"nameSpan\":{", json, StringComparison.Ordinal);
        Assert.Contains($"\"attachmentSha256\":\"{new string('a', 64)}\"", json, StringComparison.Ordinal);
        Assert.Equal(Span, again.Candidate.EffectSpans!["bonus-1"]);
        Assert.Equal("feat.header", again.Candidate.NameSpan!.PatternId);
        Assert.Equal(new string('a', 64), again.AttachmentSha256);
        Assert.Equal(json, JsonSerializer.Serialize(again, RulesJson.Compact));
        // Dropping the additive keys gives the previous build's row again.
        Assert.Equal(PreviousBuildRow, JsonSerializer.Serialize(again with { AttachmentSha256 = null, Candidate = again.Candidate with { EffectSpans = null, NameSpan = null } }, RulesJson.Compact));
    }

    /// <summary>The previous build's row shape, with the unknown-field catch-all every record of the row keeps.</summary>
    private sealed record OldCandidate(Guid Id, JsonElement Page, string Excerpt)
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extensions { get; init; }
    }

    private sealed record OldRow(Guid Id, int SchemaVersion, OldCandidate Candidate, string Status)
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extensions { get; init; }
    }

    [Fact]
    public void Spans_survive_an_older_reader()
    {
        var read = JsonSerializer.Deserialize<StoredCandidate>(PreviousBuildRow, RulesJson.Compact)!;
        var newer = JsonSerializer.Serialize(
            read with { AttachmentSha256 = new string('b', 64), Candidate = read.Candidate with { EffectSpans = new Dictionary<string, CandidateSpan> { ["bonus-1"] = Span } } },
            RulesJson.Compact);

        // A reader of the old shape that keeps what it does not know (extension data) writes the spans back unchanged.
        var old = JsonSerializer.Deserialize<OldRow>(newer, RulesJson.Compact)!;
        var rewritten = JsonSerializer.Deserialize<StoredCandidate>(JsonSerializer.Serialize(old, RulesJson.Compact), RulesJson.Compact)!;

        Assert.Equal(new string('b', 64), rewritten.AttachmentSha256);
        Assert.Equal(Span, rewritten.Candidate.EffectSpans!["bonus-1"]);
    }

    [Fact]
    public void Accept_ignores_spans()
    {
        var read = JsonSerializer.Deserialize<StoredCandidate>(PreviousBuildRow, RulesJson.Compact)!.Candidate;
        var withSpans = read with { EffectSpans = new Dictionary<string, CandidateSpan> { ["bonus-1"] = Span }, NameSpan = Span with { PatternId = "feat.header" } };

        // The draft has fresh ids each time; with those zeroed, the two drafts are the same.
        static string Draft(DraftCandidate candidate, params string[] confirmed) =>
            JsonSerializer.Serialize(CandidateQuarantine.ToDraftRevision(candidate, confirmed) with { ContentId = Guid.Empty, RevisionId = Guid.Empty }, RulesJson.Compact);

        Assert.Equal(Draft(read), Draft(withSpans));
        Assert.Equal(Draft(read, "bonus-1"), Draft(withSpans, "bonus-1"));
        Assert.DoesNotContain("Spans", Draft(withSpans), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("feat.bonus", Draft(withSpans), StringComparison.Ordinal);
    }

    [Fact]
    public void Detection_stores_spans_and_the_attachment_hash_and_an_edit_keeps_them()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Span Book", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = temp.App.StartImport(new(source.Id, WholeDocument: true));
        temp.WaitForImport(job.Id);

        var stored = temp.App.ListCandidates(new(job.Id));
        var hash = Assert.Single(stored.Select(c => c.AttachmentSha256).Distinct());
        Assert.Matches("^[0-9a-f]{64}$", hash);
        var weapon = Assert.Single(stored, c => c.Candidate.ProposedName == "Fixture Hookblade");
        Assert.Equal("weapon.row", weapon.Candidate.EffectSpans!["weapon"].PatternId);

        var edited = temp.App.EditCandidate(new(weapon.Id, Name: "Fixture Hookblade Two", Expected: weapon.Etag));

        Assert.Equal(weapon.Candidate.EffectSpans, edited.Edited!.EffectSpans);
        Assert.Equal(weapon.Candidate.NameSpan, edited.Edited.NameSpan);
        Assert.Equal(hash, edited.AttachmentSha256);
    }
}
