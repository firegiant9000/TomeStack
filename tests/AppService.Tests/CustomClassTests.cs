using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M5 slice 1 (ADR-010, content schema v9) through the app: the original Test Chronicler
/// (<c>tests/RulesFixtures/fixture-pack-m5-chronicler.json</c>) multiclasses with the bundled SRD classes in both rules
/// families, and <c>content.publish</c> writes it as v9 because it uses v9 features. No SRD revision is involved beyond
/// reading the bundled packs, which stay byte-identical (<see cref="SrdPackTests"/>).
/// </summary>
public class CustomClassTests
{
    private static readonly ContentReference Chronicler = new(Guid.Parse("5fc0c000-0000-4000-8000-000000000001"), Guid.Parse("5fc0e000-0000-4000-8000-000000000001"));
    private static readonly ContentReference ArchiveOfEchoes = new(Guid.Parse("5fc0c000-0000-4000-8000-000000000003"), Guid.Parse("5fc0e000-0000-4000-8000-000000000003"));

    private static readonly ContentPack Classes51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-classes.json");
    private static readonly ContentPack Classes521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-classes.json");
    private static readonly ContentPack Fighter51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-fighter.json");
    private static readonly ContentPack Fighter521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-fighter.json");

    private static ContentPack ChroniclerPack() =>
        JsonSerializer.Deserialize<ContentPack>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "fixture-pack-m5-chronicler.json")), RulesJson.Options)!;

    private static void Install(TempApp temp)
    {
        var pack = ChroniclerPack();
        foreach (var source in pack.Sources)
            temp.App.Store.UpsertSource(source);
        foreach (var revision in pack.Revisions)
            temp.App.Store.AddRevision(revision);
    }

    private static ContentReference Srd(ContentPack pack, string name) => pack.Revisions.Last(r => r.Name == name && r.Kind == ContentKind.Class).Reference;

    private static CharacterSheet Build(TempApp temp, string family, params ClassLevel[] classes) => temp.App.SaveCharacter(new Character
    {
        Id = Guid.NewGuid(), Name = "Test Chronicler multiclass", RulesFamily = family, Level = classes.Sum(c => c.Level), Classes = classes,
        BaseAbilities = new(13, 12, 14, 16, 14, 14),
        Choices = [new(Chronicler, "chronicler-archive", [ArchiveOfEchoes])],
    }).Sheet;

    private static int[] Slots(CharacterSheet sheet) => [.. Enumerable.Range(1, 9).Select(l => sheet.Field(FieldIds.SpellSlots(l)).Value)];

    [Fact]
    public void The_Chronicler_combines_with_the_SRD_Wizard_and_a_Paladin_by_each_familys_rules_side_by_side()
    {
        using var temp = new TempApp();
        Install(temp);

        foreach (var (family, classes) in new[] { (RulesFamilies.Srd51, Classes51), (RulesFamilies.Srd521, Classes521) })
        {
            // Chronicler 5 counts 3 (its table) + Wizard 3 (full): caster level 6 in both families.
            var wizard = Build(temp, family, new(Chronicler, 5), new(Srd(classes, "Wizard"), 3));
            Assert.Equal([4, 3, 3, 0, 0, 0, 0, 0, 0], Slots(wizard));
            Assert.Equal(AutomationStatus.Automatic, wizard.Field(FieldIds.SpellSlots(1)).Automation);
            Assert.Equal(2, wizard.Spellcasting!.Count); // each caster keeps its own spells and counts
            Assert.Equal(4, wizard.Resources!.Single(r => r.ResourceId == "ink").Maximum);
            Assert.DoesNotContain(wizard.Diagnostics, d => d.Code is "restriction.multiclass-unmet" or "content.schema-unsupported" or "effect.invalid-formula");
        }

        // Chronicler 3 counts 2 + an SRD Paladin 5 (half): 2014 rounds down (2 + 2), 2024 rounds up (2 + 3).
        Assert.Equal([4, 3, 0, 0, 0, 0, 0, 0, 0], Slots(Build(temp, RulesFamilies.Srd51, new(Chronicler, 3), new(Srd(Classes51, "Paladin"), 5))));
        Assert.Equal([4, 3, 2, 0, 0, 0, 0, 0, 0], Slots(Build(temp, RulesFamilies.Srd521, new(Chronicler, 3), new(Srd(Classes521, "Paladin"), 5))));
    }

    [Fact]
    public void The_Chronicler_multiclasses_with_the_SRD_Fighter_and_keeps_its_own_table_side_by_side()
    {
        using var temp = new TempApp();
        Install(temp);

        foreach (var (family, fighter) in new[] { (RulesFamilies.Srd51, Fighter51), (RulesFamilies.Srd521, Fighter521) })
        {
            var sheet = Build(temp, family, new(Srd(fighter, "Fighter"), 3), new(Chronicler, 5));
            Assert.Equal([3, 2, 0, 0, 0, 0, 0, 0, 0], Slots(sheet)); // the only slot caster: its own table at level 5
            Assert.Equal(3, sheet.Field(FieldIds.ProficiencyBonus).Value); // total level 8, a rules constant for every class
            Assert.Equal(3 + 2, sheet.Field("skill.history").Value); // the Lore column at Chronicler level 5
            Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "restriction.multiclass-unmet");
            Assert.Contains(sheet.Scales!, s => s.ScaleId == "echo" && s.Value == 1);
        }
    }

    [Fact]
    public void Publishing_a_Chronicler_draft_writes_v9_and_a_class_without_v9_features_stays_lower()
    {
        using var temp = new TempApp();
        Install(temp);
        var published = temp.App.Store.FindRevision(Chronicler)!;

        var draft = temp.App.SaveDraft(published with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Name = "Test Chronicler (edited)" });
        Assert.Equal(9, temp.App.Store.FindRevision(temp.App.Publish(draft).Published)!.SchemaVersion);

        var plain = temp.App.SaveDraft(published with
        {
            RevisionId = Guid.NewGuid(),
            Status = RevisionStatus.Draft,
            Effects = [.. published.Effects.Where(e => e is HitDieEffect or GrantEffect { Grant: GrantKind.Proficiency })],
        });
        Assert.Equal(5, temp.App.Store.FindRevision(temp.App.Publish(plain).Published)!.SchemaVersion); // onlyAs is v5
    }
}
