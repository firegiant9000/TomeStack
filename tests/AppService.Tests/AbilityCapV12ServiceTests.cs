using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Content v12 (G5): an ability-score increase may declare a maximum above 20; plain increases still stop at 20, the cap follows
/// its toggle in play, the trace names it, and saving for an older release keeps the bonus and lists the lost cap.
/// Original fixtures (<c>tests/RulesFixtures/fixture-pack-ability-cap.json</c>).
/// </summary>
public class AbilityCapV12ServiceTests
{
    private static ContentReference V12(int n) => new(Guid.Parse($"6f16c000-0000-4000-8000-{n:D12}"), Guid.Parse($"6f16e000-0000-4000-8000-{n:D12}"));

    private static readonly Guid PackSource = Guid.Parse("6f165000-0000-4000-8000-000000001701");
    private static readonly ContentReference Summit = V12(1701);
    private static readonly ContentReference Lantern = V12(1702);
    private static readonly ContentReference Plain = V12(1703);

    private static readonly ContentReference Longblade = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000001"), Guid.Parse("5f6de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Duelist = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000011"), Guid.Parse("5f6de000-0000-4000-8000-000000000011"));

    private static Character Hero(params ContentReference[] features) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Pip Testwright",
        RulesFamily = RulesFamilies.Srd521,
        Level = 1,
        Classes = [new(Duelist, 1)],
        BaseAbilities = new(20, 16, 17, 10, 20, 10),
        Equipment = [new(Longblade, Equipped: true)],
        Pins = [Duelist, .. features],
    };

    private static (TempApp Temp, Guid Id) Start(params ContentReference[] features)
    {
        var temp = new TempApp();
        temp.AddPack("fixture-pack-ability-cap.json");
        return (temp, temp.App.SaveCharacter(Hero(features)).Character.Id);
    }

    private static DerivedValue Field(CharacterView view, string field) => Assert.Single(view.Sheet.Fields, f => f.Field == field);

    private static CharacterView Focus(TempApp temp, Guid id, PlayActionKind kind) =>
        temp.App.Play(new(id, kind, Confirm: true, ContentId: Lantern.ContentId, ToggleId: "focus"));

    [Fact]
    public void Capped_increases_raise_scores_above_20()
    {
        var (temp, id) = Start(Summit);
        using var _ = temp;
        var view = temp.App.GetCharacter(id);
        Assert.Equal(24, Field(view, "ability.str.score").Value);
        Assert.Equal(21, Field(view, "ability.con.score").Value); // 17 + 4 crosses 20, stops below the cap 25

        // The plain +2 stops at 20 first, then the capped +4 raises it to 24.
        var (temp2, id2) = Start(Summit, Plain);
        using var __ = temp2;
        Assert.Equal(24, Field(temp2.App.GetCharacter(id2), "ability.str.score").Value);

        // Without the capped one, the plain increase leaves 20.
        var (temp3, id3) = Start(Plain);
        using var ___ = temp3;
        Assert.Equal(20, Field(temp3.App.GetCharacter(id3), "ability.str.score").Value);
    }

    [Fact]
    public void Toggled_cap_follows_the_toggle_in_play()
    {
        var (temp, id) = Start(Lantern);
        using var _ = temp;
        var wisdom = temp.App.GetCharacter(id);
        Assert.Equal(20, Field(wisdom, "ability.wis.score").Value);

        var on = Focus(temp, id, PlayActionKind.ToggleOn);
        Assert.Equal(22, Field(on, "ability.wis.score").Value);

        var off = Focus(temp, id, PlayActionKind.ToggleOff);
        Assert.Equal(20, Field(off, "ability.wis.score").Value);
    }

    [Fact]
    public void Trace_names_the_cap()
    {
        var (temp, id) = Start(Summit, Plain);
        using var _ = temp;
        var trace = Field(temp.App.GetCharacter(id), "ability.str.score").Trace.Select(t => t.Description).ToList();

        Assert.Contains(trace, d => d.StartsWith("Bonus from ", StringComparison.Ordinal) && d.Contains("Fixture: Plain Training", StringComparison.Ordinal)
            && d.EndsWith(", capped: increases cannot raise an ability score above 20 (+2 would give 22)", StringComparison.Ordinal));
        Assert.Contains(trace, d => d.StartsWith("Bonus from ", StringComparison.Ordinal) && d.Contains("Fixture: Summit Conditioning", StringComparison.Ordinal)
            && d.EndsWith(" (it can raise the score up to 25)", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixture_features_are_counted_automatic()
    {
        var (temp, id) = Start(Summit, Lantern, Plain);
        using var __ = temp;

        var sheet = temp.App.GetCharacter(id).Sheet;
        var fixtures = sheet.Features!.Where(f => f.Name.StartsWith("Fixture: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, fixtures.Count);
        Assert.All(fixtures, f => Assert.Equal(AutomationStatus.Automatic, f.Automation));

        var report = temp.App.Coverage(new(PackSource));

        var totals = Assert.Single(report.Sources).Totals;
        Assert.Equal(5, totals.Automatic); // one per effect: three bonuses, a toggle, and the capped Wisdom bonus
        Assert.Equal((0, 0, 0), (totals.Assisted, totals.Reference, totals.Unsupported));
    }

    [Fact]
    public void Publishing_the_pack_requires_12()
    {
        using var temp = new TempApp();
        var pack = temp.AddPack("fixture-pack-ability-cap.json");

        Assert.Equal(3, pack.Revisions.Count);
        foreach (var revision in pack.Revisions)
        {
            Assert.Equal(12, revision.SchemaVersion);
            var draft = revision with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft };
            var published = temp.App.Publish(temp.App.SaveDraft(draft));
            var stored = temp.App.Store.FindRevision(published.Published)!.SchemaVersion;
            if (revision.Name == "Fixture: Plain Training")
                Assert.Equal(3, published.Report.RequiredSchemaVersion); // no cap: nothing here needs v12
            else
                Assert.Equal((12, 12), (stored, published.Report.RequiredSchemaVersion));
        }
    }

    [Fact]
    public void Saving_for_11_keeps_the_bonus_and_lists_the_cap()
    {
        var (temp, id) = Start(Summit);
        using var _ = temp;
        var row = ReleaseLadder.Rows.Single(r => r.Release == new Version(0, 7, 0));

        var preview = temp.App.PreviewExport([id], ExportPurpose.Share, row);

        Assert.DoesNotContain(preview.Changes, c => c.Action == "blocks");
        var drops = preview.Changes.Where(c => c.Name == "Fixture: Summit Conditioning").ToList();
        Assert.Equal(2, drops.Count);
        Assert.All(drops, c => Assert.Equal(("dropped", "package.target-drops"), (c.Action, c.Code)));
        Assert.All(drops, c => Assert.Equal("'Fixture: Summit Conditioning' (TomeStack Fixtures: Ability Cap v12): that the increase can raise the score above 20 (up to 25); the older version stops it at 20.", c.Message));

        var export = temp.App.ExportCharacters([id], ExportPurpose.Share, row, preview.ChangesHash);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(export.Content));
        var lowered = zip.Entries.Where(e => e.FullName.StartsWith("content/", StringComparison.Ordinal)).Select(e =>
        {
            using var reader = new StreamReader(e.Open());
            return System.Text.Json.Nodes.JsonNode.Parse(reader.ReadToEnd())!;
        }).Single(n => n["name"]!.GetValue<string>() == "Fixture: Summit Conditioning");
        var effects = lowered["effects"]!.AsArray();
        Assert.Equal(
            [("bonus", "ability.str.score", "4"), ("bonus", "ability.con.score", "4")],
            effects.Select(e => (e!["operation"]!.GetValue<string>(), e["target"]!.GetValue<string>(), e["value"]!.GetValue<string>())));
        Assert.All(effects, e => Assert.Null(e!["cap"]));
    }
}
