using System.Text.Json;
using TomeStack.AppService.CharacterImport;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Character-sheet import S4 (<c>features/ddb-pdf-import.md</c> "Commands"): <c>ddb.apply</c> needs <c>confirm</c> and a
/// live token, rebuilds the previewed character under a new id, and saves it with its overrides and gap notes in one
/// transaction, then spends the token. It never replaces a character.
/// </summary>
public class DdbApplyTests
{
    private static ContentReference Srd(int n) => new(Guid.Parse($"52c00000-0000-4000-8000-{n:D12}"), Guid.Parse($"52e00000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Barbarian = Srd(11);
    private static readonly ContentReference SavageAttacker = Srd(10);

    private static DdbApplyRequest Apply(Guid token, IReadOnlyList<NumberChoice>? numbers = null, bool play = false, bool confirm = true, IReadOnlyList<Resolution>? resolutions = null) =>
        new(token, RulesFamilies.Srd521, null, resolutions ?? [], numbers ?? [], play, Confirm: confirm);

    private static string Notes(TempApp temp) => TempApp.Json(temp.App.ListAllGapNotes());

    [Fact]
    public void Apply_without_confirm_is_refused_and_writes_nothing()
    {
        using var h = new DdbHarness();
        var token = h.Read(new SheetBuilder($"{h.Name(Barbarian)} 1"));

        var refused = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(token, confirm: false)));

        Assert.Equal("ddb.confirmation-required", refused.Code);
        Assert.Empty(h.Temp.App.ListCharacters());
        Assert.NotNull(h.Temp.App.DdbSessions.Peek(token));
    }

    [Fact]
    public void Apply_with_an_unknown_used_or_expired_token_is_refused_with_ddb_token_invalid()
    {
        var clock = new MutableTime(TempApp.Now);
        var fields = new List<ImportWorker.Forms.FormField>();
        using var temp = new TempApp(formReader: new FakeFormReader(() => fields), time: clock);
        var barbarian = temp.App.ListContent(RulesFamilies.Srd521).Single(o => o.Reference == Barbarian).Name;
        fields = new SheetBuilder($"{barbarian} 1").Build();
        string Code(Guid token) => Assert.Throws<AppValidationException>(() => temp.App.ApplyDdbImport(Apply(token))).Code;

        Assert.Equal("ddb.token-invalid", Code(Guid.NewGuid()));

        var used = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;
        temp.App.ApplyDdbImport(Apply(used));
        Assert.Equal("ddb.token-invalid", Code(used));

        var expired = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;
        clock.Now = TempApp.Now.AddMinutes(31);
        Assert.Equal("ddb.token-invalid", Code(expired));
        Assert.Single(temp.App.ListCharacters());
    }

    [Fact]
    public void Apply_saves_the_character_its_overrides_and_its_gap_notes_in_one_transaction()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Text("armorClass", "19").Text("feats", "Fixture Unknown Feat");
        var token = h.Read(sheet);

        // The last write, the note on a field the sheet does not have, is refused, so nothing before it stays.
        var refused = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(token,
            [new(FieldIds.ArmorClass, NumberAction.KeepSheet), new("fixture-no-such-field", NumberAction.Note)])));

        Assert.Equal("gap.target-not-found", refused.Problems[0].Code);
        Assert.Empty(h.Temp.App.ListCharacters());
        Assert.Equal("[]", Notes(h.Temp));
        Assert.NotNull(h.Temp.App.DdbSessions.Peek(token)); // a refused apply does not spend the token

        var applied = h.Temp.App.ApplyDdbImport(Apply(token, [new(FieldIds.ArmorClass, NumberAction.KeepSheet)]));
        Assert.Equal((1, 1), (applied.Overrides, applied.GapNotes));
        Assert.Single(h.Temp.App.ListCharacters());
    }

    [Fact]
    public void Apply_creates_a_new_id_every_time_and_never_touches_an_existing_character()
    {
        using var h = new DdbHarness();
        var existing = h.Temp.App.CreateCharacter(new("Testy McFixture", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null)).Character;
        var before = TempApp.Json(h.Temp.App.GetCharacter(existing.Id).Character);
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1");

        var first = h.Temp.App.ApplyDdbImport(Apply(h.Read(sheet)));
        var second = h.Temp.App.ApplyDdbImport(Apply(h.Read(sheet)));

        Assert.Equal(3, h.Temp.App.ListCharacters().Count);
        Assert.Equal(3, new[] { existing.Id, first.CharacterId, second.CharacterId }.Distinct().Count());
        Assert.Equal(before, TempApp.Json(h.Temp.App.GetCharacter(existing.Id).Character));
        Assert.True(first.Report.SameNameExists);
    }

    [Fact]
    public void Apply_with_play_state_ticked_keeps_the_sheets_hit_points_and_spent_slots_and_without_it_the_character_is_rested()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder("Fixture Arcanist 3").Text("currentHitPoints", "7").Text("spellSlotsSpent.1", "1");

        var played = h.Temp.App.GetCharacter(h.Temp.App.ApplyDdbImport(Apply(h.Read(sheet), play: true)).CharacterId).Character.Play;
        var rested = h.Temp.App.GetCharacter(h.Temp.App.ApplyDdbImport(Apply(h.Read(sheet))).CharacterId).Character.Play;

        Assert.Equal((7, 1), (played.CurrentHitPoints, played.SlotsSpentOf(1)));
        Assert.Equal((null, 0), (rested.CurrentHitPoints, rested.SlotsSpentOf(1)));
    }

    [Fact]
    public void A_kept_sheet_number_is_a_FieldOverride_with_the_import_reason_and_the_computed_value_beneath_it()
    {
        using var h = new DdbHarness();
        var token = h.Read(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("armorClass", "19"));

        var view = h.Temp.App.GetCharacter(h.Temp.App.ApplyDdbImport(Apply(token, [new(FieldIds.ArmorClass, NumberAction.KeepSheet)])).CharacterId);

        Assert.Equal([new FieldOverride(FieldIds.ArmorClass, 19, TomeStackApp.ImportedOverrideReason)], view.Character.Overrides);
        var armorClass = view.Sheet.Field(FieldIds.ArmorClass);
        Assert.Equal(19, armorClass.Value);
        Assert.NotEqual(19, armorClass.ComputedValue);
    }

    [Fact]
    public void A_noted_difference_becomes_a_field_gap_note_and_each_unmatched_item_an_import_gap_note()
    {
        using var h = new DdbHarness();
        var token = h.Read(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("armorClass", "19").Text("feats", $"Fixture Unknown Feat\n{h.Name(SavageAttacker)}"));

        var applied = h.Temp.App.ApplyDdbImport(Apply(token, [new(FieldIds.ArmorClass, NumberAction.Note)]));

        var notes = h.Temp.App.ListGapNotes(applied.CharacterId);
        Assert.Equal(3, applied.GapNotes);
        Assert.Single(notes, n => n.Target.Kind == GapTargetKind.Field && n.Target.FieldId == FieldIds.ArmorClass);
        Assert.Equal(["Fixture Unknown Feat", h.Name(SavageAttacker)], notes.Where(n => n.Target.Kind == GapTargetKind.Import).Select(n => n.Target.Label!).Order());
        Assert.Empty(h.Temp.App.GetCharacter(applied.CharacterId).Character.Overrides);
    }

    [Fact]
    public void Unmatched_items_past_the_note_limit_are_counted_not_stored()
    {
        using var h = new DdbHarness();
        var features = string.Join("\n", Enumerable.Range(0, GapNote.MaxNotesPerCharacter + 5).Select(i => $"Fixture Missing Feature {i:D3}"));

        var applied = h.Temp.App.ApplyDdbImport(Apply(h.Read(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("features", features))));

        Assert.Equal((GapNote.MaxNotesPerCharacter, 5), (applied.GapNotes, applied.GapNotesNotStored));
        Assert.Equal(GapNote.MaxNotesPerCharacter, h.Temp.App.ListGapNotes(applied.CharacterId).Count);
    }

    [Fact]
    public void Apply_refuses_unreadable_classes_with_the_characters_own_level_diagnostics()
    {
        using var h = new DdbHarness();
        var token = h.Read(new SheetBuilder($"{h.Name(Barbarian)} 15 / Fixture Arcanist 6"));

        var refused = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(token)));

        Assert.Contains(refused.Problems, p => p.Code == "character.level-out-of-range");
        Assert.Empty(h.Temp.App.ListCharacters());
    }

    [Fact]
    public void Apply_refuses_a_row_that_still_needs_a_choice()
    {
        using var h = new DdbHarness();
        DdbTestContent.Publish(h.Temp, DdbTestContent.Source(h.Temp, "Fixture First Notes", RulesFamilies.Srd521), ContentKind.Feat, "Fixture Twin Feat", [RulesFamilies.Srd521]);
        DdbTestContent.Publish(h.Temp, DdbTestContent.Source(h.Temp, "Fixture Second Notes", RulesFamilies.Srd521), ContentKind.Feat, "Fixture Twin Feat", [RulesFamilies.Srd521]);
        var token = h.Read(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("feats", "Fixture Twin Feat"));

        var refused = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(token)));

        Assert.Contains(refused.Problems, p => p.Code == "ddb.choice-required");
        Assert.Empty(h.Temp.App.ListCharacters());
        h.Temp.App.ApplyDdbImport(Apply(token, resolutions: [new("feat:0", null, LeaveOut: true)]));
        Assert.Single(h.Temp.App.ListCharacters());
    }

    [Fact]
    public void The_committed_fixture_sheet_reads_through_the_worker_and_creates_a_character_once_its_choice_is_resolved()
    {
        // The real worker (no fake reader), the committed PDF, the seeded fixture casters: the path the e2e flow takes.
        using var temp = new TempApp();
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf"));
        var read = temp.App.ReadDdbSheetData("fixture-ddb-sheet.pdf", bytes);
        Assert.Equal(("ddb-2014", RulesFamilies.Srd51), (read.Layout, read.SuggestedFamily));

        var open = temp.App.PreviewDdbImport(new(read.Token, RulesFamilies.Srd51, null, null, null, false));
        var veil = open.Matches.Single(m => m.Kind == MatchKind.Spell && m.Status == MatchStatus.Choose);
        Assert.False(open.CanApply);
        Assert.Contains(open.Matches, m => m.RowId == "species" && m.Status == MatchStatus.Matched);
        Assert.Contains(open.Comparison, n => n.Field == FieldIds.ArmorClass && n.Differs);

        var chanter = veil.Candidates.Single(c => c.Placement.Caster == open.Character.Classes[1].Class.ContentId);
        var applied = temp.App.ApplyDdbImport(new(read.Token, RulesFamilies.Srd51, null, [new(veil.RowId, chanter.Reference, false, chanter.Placement.Caster)],
            [new(FieldIds.ArmorClass, NumberAction.KeepSheet)], false, Confirm: true));

        var view = temp.App.GetCharacter(applied.CharacterId);
        Assert.Equal(("Testy McFixture", 2, 1), (view.Character.Name, view.Character.Classes.Count, applied.Overrides));
        Assert.Equal(3, view.Character.Spells.Count);
        Assert.True(applied.GapNotes > 0);
    }

    [Fact]
    public void ddb_apply_works_through_the_dispatcher()
    {
        using var h = new DdbHarness();
        var token = h.Read(new SheetBuilder($"{h.Name(Barbarian)} 1"));
        var dispatcher = new CommandDispatcher(h.Temp.App);
        JsonElement Send(object payload) => JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command = "ddb.apply", payload }, RulesJson.Compact))).RootElement;

        Assert.Equal("ddb.confirmation-required", Send(new { token, rulesFamily = RulesFamilies.Srd521 }).GetProperty("error").GetProperty("code").GetString());
        var applied = Send(new { token, rulesFamily = RulesFamilies.Srd521, confirm = true });
        Assert.True(applied.GetProperty("ok").GetBoolean(), applied.ToString());
        Assert.Equal(h.Temp.App.ListCharacters().Single().Id, applied.GetProperty("result").GetProperty("characterId").GetGuid());
    }
}
