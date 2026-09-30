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

    /// <summary>
    /// M5 slice 1b: a class written the way the studio writes it (HomebrewStudio.tsx, ClassBasicsEditor.tsx), with a
    /// subclass choice that declares no options of its own. It is authored as drafts in a homebrew source, published,
    /// joined by a homebrew subclass through extendsChoice, multiclassed with the SRD Fighter, and round-tripped
    /// through a package to a clean data folder.
    /// </summary>
    [Fact]
    public void A_class_authored_like_the_studio_publishes_takes_a_homebrew_subclass_multiclasses_and_round_trips()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Studio Classes", [RulesFamilies.Srd521]));
        ContentRevision Draft(ContentKind kind, string name, params Effect[] effects) => new()
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = kind, Name = name, RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = effects,
        };
        ContentReference Publish(ContentRevision draft) => temp.App.Publish(temp.App.SaveDraft(draft)).Published;

        // The skill choice's options, published first, as "Create skill choice" does.
        var history = Publish(Draft(ContentKind.Feature, "Test Quillmaster: History", new GrantEffect { Id = "skill", Grant = GrantKind.Proficiency, Target = "skill.history" }));
        var arcana = Publish(Draft(ContentKind.Feature, "Test Quillmaster: Arcana", new GrantEffect { Id = "skill", Grant = GrantKind.Proficiency, Target = "skill.arcana" }));
        var classDraft = Draft(
            ContentKind.Class,
            "Test Quillmaster",
            new HitDieEffect { Id = "hit-die", Die = 8 },
            new GrantEffect { Id = "save-int", Grant = GrantKind.Proficiency, Target = "save.int", OnlyAs = ClassEntry.StartingClass },
            new RestrictionEffect { Id = "multiclass-int", Field = "ability.int.score", Minimum = 13, Multiclass = true },
            new ChoiceEffect { Id = "skills", ChoiceId = "skills", Count = 1, Options = [history, arcana], OnlyAs = ClassEntry.StartingClass },
            new ChoiceEffect { Id = "subclass", ChoiceId = "subclass", Count = 1, Options = [], Level = 3, Text = "Choose a subclass" },
            new ScaleEffect { Id = "scale-1", ScaleId = "ink", Label = "Ink", Values = [2, 2, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9] },
            new ResourceEffect { Id = "resource-2", ResourceId = "resource-2", Label = "Ink", Maximum = "SCALE.ink" });
        var report = temp.App.Publish(temp.App.SaveDraft(classDraft));
        Assert.Contains(report.Report.Warnings, w => w.Code == "validate.choice-options-none");
        Assert.Equal(9, temp.App.Store.FindRevision(report.Published)!.SchemaVersion);
        var quillmaster = report.Published;

        var subclass = Publish(Draft(ContentKind.Subclass, "Test Order of the Margin", new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "SCALE.ink" }) with
        {
            ExtendsChoice = new(quillmaster.ContentId, "subclass"),
        });

        var fighter = Srd(Fighter521, "Fighter");
        var character = temp.App.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Quill Fighter", RulesFamily = RulesFamilies.Srd521, Level = 8,
            Classes = [new(quillmaster, 5), new(fighter, 3)],
            BaseAbilities = new(13, 12, 14, 16, 14, 14),
            Choices = [new(quillmaster, "skills", [arcana]), new(quillmaster, "subclass", [subclass])],
        });
        var sheet = character.Sheet;
        Assert.Contains(sheet.Choices!, c => c.ChoiceId == "subclass" && c.Resolved && c.Options.Contains(subclass));
        Assert.Equal(4, sheet.Resources!.Single(r => r.Label == "Ink").Maximum);
        Assert.Equal(1 + 4, sheet.Field(FieldIds.Initiative).Value); // Dex +1, the subclass reads the class's Ink column (4)
        Assert.Equal(3 + 3, sheet.Field("skill.arcana").Value); // Int +3, PB +3 (total level 8)
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code is "restriction.multiclass-unmet" or "effect.invalid-formula");
        Assert.All(sheet.Choices!.Where(c => c.Source == quillmaster), c => Assert.True(c.Resolved)); // the Fighter's own stay open here

        // A clean data folder calculates the same sheet from a package.
        var export = temp.App.ExportCharacters([character.Character.Id]);
        using var destination = new TempApp();
        destination.App.ApplyImport(export.Content);
        Assert.Equal(TempApp.Json(sheet), TempApp.Json(destination.App.GetCharacter(character.Character.Id).Sheet));
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
