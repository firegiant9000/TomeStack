using System.Globalization;
using TomeStack.AppService.CharacterImport;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Character-sheet import S3 acceptance (<c>features/ddb-pdf-import.md</c> "Slices and acceptance"): a character built from
/// installed content is written out as a sheet from its own calculated values, read back through the 2014 layout, and
/// previewed. Classes, pins, choices, spells, equipment and base scores come back equal, with zero number differences.
/// Skill options are compared as a set: when two choices could each take a skill, the importer fills them in skill order,
/// which may differ from the original's split but gives the same proficiencies. The originals equip nothing: the 2014
/// layout has no equipped mark (S0), so an import never equips an item.
/// </summary>
public class DdbRoundTripTests
{
    private static ContentOption Option(TempApp temp, string family, ContentKind kind, string name) =>
        temp.App.ListContent(family).Single(o => o.Kind == kind && o.Name == name && o.Compatible && !o.Superseded);

    private static string Name(TempApp temp, ContentReference reference) => temp.App.Store.FindRevision(reference)!.Name;

    /// <summary>Writes the character as a sheet, the way a sheet shows it: names, final scores, marks and numbers.</summary>
    private static SheetBuilder Write(TempApp temp, Character character, CharacterSheet sheet)
    {
        var classes = character.Classes.Select(c =>
        {
            var subclass = character.Choices.Where(s => s.Source.ContentId == c.Class.ContentId).SelectMany(s => s.Selected)
                .Select(r => temp.App.Store.FindRevision(r)!).FirstOrDefault(r => r.Kind == ContentKind.Subclass);
            return $"{Name(temp, c.Class)} {c.Level}{(subclass is null ? "" : $" ({subclass.Name})")}";
        });
        var builder = new SheetBuilder(string.Join(" / ", classes), character.Name);
        foreach (var pin in character.Pins.Select(p => temp.App.Store.FindRevision(p)!))
        {
            if (pin.Kind is ContentKind.Species)
                builder.Text("species", pin.Name);
            if (pin.Kind is ContentKind.Background)
                builder.Text("background", pin.Name);
        }
        builder.Text("feats", string.Join("\n", sheet.Features!.Where(f => f.Kind == ContentKind.Feat).Select(f => f.Name)));
        builder.Text("features", string.Join("\n", sheet.Features!.Where(f => f.Kind == ContentKind.Feature).Select(f => f.Name)));
        foreach (var ability in Enum.GetValues<Ability>())
            builder.Score(ability, sheet.Field(FieldIds.Score(ability)).Value);
        foreach (var (key, _, _) in CharacterCalculator.Skills)
        {
            var field = FieldIds.Skill(key);
            if (sheet.Field(field).Trace.Any(t => t.Operation == "add" && t.Field == field))
                builder.Skill(key);
        }
        string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        foreach (var field in new[] { FieldIds.HitPoints, FieldIds.ProficiencyBonus, FieldIds.ArmorClass, FieldIds.Initiative })
            builder.Text(field, Number(sheet.Field(field).Value));
        foreach (var ability in Enum.GetValues<Ability>())
            builder.Text($"saves.{FieldIds.Key(ability)}.bonus", Number(sheet.Field(FieldIds.Save(ability)).Value));
        foreach (var (key, _, _) in CharacterCalculator.Skills)
            builder.Text($"skills.{key}.bonus", Number(sheet.Field(FieldIds.Skill(key)).Value));
        foreach (var spell in character.Spells)
            builder.Spell(Name(temp, spell.Spell), spell.Prepared);
        foreach (var item in character.Equipment)
            builder.Item(Name(temp, item.Item), item.Quantity, item.Equipped);
        return builder;
    }

    private static HashSet<Guid> SkillOptions(TempApp temp, Character character) =>
    [
        .. character.Choices.SelectMany(c => c.Selected)
            .Where(r => temp.App.Store.FindRevision(r)!.Effects.OfType<GrantEffect>().Any(g => g.Grant == GrantKind.Proficiency && g.Target?.StartsWith("skill.", StringComparison.Ordinal) == true))
            .Select(r => r.ContentId),
    ];

    private static IEnumerable<string> OtherChoices(TempApp temp, Character character) =>
        character.Choices
            .Select(c => (c.Source.ContentId, c.ChoiceId, Selected: c.Selected.Where(r => !SkillOptions(temp, character).Contains(r.ContentId)).Select(r => r.ContentId).Order().ToList()))
            .Where(c => c.Selected.Count > 0)
            .Select(c => $"{c.ContentId}/{c.ChoiceId}: {string.Join(",", c.Selected)}")
            .Order(StringComparer.Ordinal);

    /// <summary>Imports <paramref name="original"/> back and checks everything the spec names.</summary>
    private static void RoundTrip(DdbHarness h, Character original, IReadOnlyList<Resolution>? resolutions = null, IReadOnlyList<ChoiceSelection>? answers = null)
    {
        var saved = h.Temp.App.SaveCharacter(original);
        var sheet = Write(h.Temp, saved.Character, saved.Sheet);

        var token = h.Read(sheet);
        var preview = h.Temp.App.PreviewDdbImport(new(token, original.RulesFamily, null, resolutions, null, false, answers));
        var imported = preview.Character;

        Assert.True(preview.CanApply, string.Join("; ", preview.Diagnostics.Select(d => d.Code)) + " | " + string.Join("; ", preview.Matches.Where(m => m.Status != MatchStatus.Matched).Select(m => $"{m.RowId} {m.Status} {m.Note}")));
        Assert.Equal(saved.Character.Classes.Select(c => (c.Class.ContentId, c.Level)), imported.Classes.Select(c => (c.Class.ContentId, c.Level)));
        Assert.Equal(saved.Character.Pins.Select(p => p.ContentId).Order(), imported.Pins.Select(p => p.ContentId).Order());
        Assert.Equal(OtherChoices(h.Temp, saved.Character), OtherChoices(h.Temp, imported));
        Assert.Equal(SkillOptions(h.Temp, saved.Character).Order(), SkillOptions(h.Temp, imported).Order());
        Assert.Equal(saved.Character.Spells.Select(s => (s.Caster, s.Spell.ContentId, s.Prepared)).Order(), imported.Spells.Select(s => (s.Caster, s.Spell.ContentId, s.Prepared)).Order());
        Assert.Equal(saved.Character.Equipment.Select(e => (e.Item.ContentId, e.Quantity, e.Equipped)).Order(), imported.Equipment.Select(e => (e.Item.ContentId, e.Quantity, e.Equipped)).Order());
        Assert.Equal(saved.Character.BaseAbilities, imported.BaseAbilities);
        Assert.NotEmpty(preview.Comparison);
        Assert.DoesNotContain(preview.Comparison, n => n.Differs);
        Assert.NotEqual(saved.Character.Id, imported.Id);
    }

    private static List<ContentReference> Items(TempApp temp, string family) =>
        [.. temp.App.ListContent(family).Where(o => o.Kind == ContentKind.Item && o.Compatible && !o.Superseded && o.Standalone && o.SourceTitle.Contains("SRD", StringComparison.Ordinal)).Take(2).Select(o => o.Reference)];

    /// <summary>
    /// The choices a draft offers now, answered with their first options not chosen elsewhere, skipping a skill the draft
    /// already has (as a player would: a sheet cannot show a skill taken twice).
    /// </summary>
    private static Character AnswerAll(TempApp temp, Character character, Func<ChoiceStatus, bool> which)
    {
        for (var round = 0; round < 5; round++)
        {
            var draft = temp.App.Preview(character).Sheet;
            var open = draft.Choices!.FirstOrDefault(c => !c.Resolved && which(c));
            if (open is null)
                return character;
            var taken = character.Choices.SelectMany(c => c.Selected).ToHashSet();
            bool Redundant(ContentReference option) => temp.App.Store.FindRevision(option)!.Effects.OfType<GrantEffect>()
                .Any(g => g.Grant == GrantKind.Proficiency && g.Target is { } target && target.StartsWith("skill.", StringComparison.Ordinal)
                    && draft.Field(target).Trace.Any(t => t.Operation == "add" && t.Field == target));
            var picks = open.Options.Where(o => !taken.Contains(o) && !Redundant(o)).Take(open.Count - open.Selected.Count).ToList();
            character = temp.App.PreviewChoice(new(character, open.Source, open.ChoiceId, [.. open.Selected, .. picks])).Character;
        }
        return character;
    }

    [Fact]
    public void An_SRD_5_1_character_written_as_a_sheet_imports_back_with_equal_classes_pins_choices_spells_equipment_and_base_scores_and_zero_differences()
    {
        using var h = new DdbHarness();
        const string family = RulesFamilies.Srd51;
        var barbarian = Option(h.Temp, family, ContentKind.Class, "Barbarian").Reference;
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Testy McFixture", RulesFamily = family, BaseAbilities = new(15, 13, 14, 8, 12, 10),
            Pins = [Option(h.Temp, family, ContentKind.Species, "Half-Orc").Reference, Option(h.Temp, family, ContentKind.Background, "Acolyte").Reference,
                Option(h.Temp, family, ContentKind.Feat, "Grappler").Reference],
            Classes = [new(barbarian, 3)],
            Equipment = [.. Items(h.Temp, family).Select((r, i) => new EquipmentEntry(r, false, i + 1))],
        };
        character = AnswerAll(h.Temp, character, _ => true);

        RoundTrip(h, character);
    }

    [Fact]
    public void An_SRD_5_2_1_character_written_as_a_sheet_imports_back_the_same_way()
    {
        using var h = new DdbHarness();
        const string family = RulesFamilies.Srd521;
        var barbarian = Option(h.Temp, family, ContentKind.Class, "Barbarian").Reference;
        var soldier = Option(h.Temp, family, ContentKind.Background, "Soldier").Reference;
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Testy McFixture", RulesFamily = family, BaseAbilities = new(15, 13, 14, 8, 12, 10),
            Pins = [Option(h.Temp, family, ContentKind.Species, "Dwarf").Reference, soldier],
            Classes = [new(barbarian, 3)],
            Equipment = [.. Items(h.Temp, family).Select((r, i) => new EquipmentEntry(r, false, i + 1))],
        };
        character = AnswerAll(h.Temp, character, _ => true);

        // The background's ability-score choice is not on a sheet: the user answers it in step 3.
        RoundTrip(h, character, answers: [.. character.Choices.Where(c => c.Source.ContentId == soldier.ContentId)]);
    }

    [Fact]
    public void The_round_trip_of_a_multiclass_caster_keeps_each_spell_on_its_caster()
    {
        using var h = new DdbHarness();
        const string family = RulesFamilies.Srd521;
        var arcanist = Option(h.Temp, family, ContentKind.Class, "Fixture Arcanist").Reference;
        var chanter = Option(h.Temp, family, ContentKind.Class, "Fixture Chanter").Reference;
        KnownSpell On(ContentReference caster, string spell, bool prepared = true) => new(caster.ContentId, Option(h.Temp, family, ContentKind.Spell, spell).Reference, prepared);
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Testy McFixture", RulesFamily = family, BaseAbilities = new(10, 12, 13, 15, 10, 14),
            Classes = [new(arcanist, 3), new(chanter, 2)],
            Spells =
            [
                On(arcanist, "Fixture Spark"), On(arcanist, "Fixture Frost Ring"), On(arcanist, "Fixture Ember Wave", prepared: false),
                On(chanter, "Fixture Mending Word"), On(chanter, "Fixture Veil"),
            ],
        };
        character = AnswerAll(h.Temp, character, _ => true);

        // "Fixture Veil" is on both casters' lists, so the user picks its caster (row 4, in the sheet's spell order).
        RoundTrip(h, character, resolutions: [new("spell:4", Option(h.Temp, family, ContentKind.Spell, "Fixture Veil").Reference, false, chanter.ContentId)]);
    }
}
