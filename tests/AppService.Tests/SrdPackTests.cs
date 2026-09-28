using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M1 item 1: SRD 5.1 and SRD 5.2.1 ship as separate source packs (separate sources, content IDs and revisions) under
/// CC-BY-4.0, each carrying the approved attribution verbatim (docs/licensing/srd-attribution-draft.md) and a
/// modification notice. Every revision validates, and a fresh data folder has SRD content but no test fixtures.
/// </summary>
public class SrdPackTests
{
    // The M1 base packs, and everything bundled per family (base pack plus the M2 spell pack, sharing one source record).
    private static readonly ContentPack Base51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1.json");
    private static readonly ContentPack Base521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1.json");
    private static readonly ContentPack Spells51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-spells.json");
    private static readonly ContentPack Spells521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-spells.json");
    private static readonly ContentPack Equipment51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-equipment.json");
    private static readonly ContentPack Equipment521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-equipment.json");
    private static readonly ContentPack Classes51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-classes.json");
    private static readonly ContentPack Classes521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-classes.json");
    private static readonly ContentPack Srd51 = Merge(Base51, Spells51, Equipment51, Classes51);
    private static readonly ContentPack Srd521 = Merge(Base521, Spells521, Equipment521, Classes521);

    private static ContentPack Merge(params ContentPack[] packs) => new()
    {
        PackId = packs[0].PackId,
        FormatVersion = packs[0].FormatVersion,
        Sources = [.. packs.SelectMany(p => p.Sources).DistinctBy(s => s.Id)],
        Revisions = [.. packs.SelectMany(p => p.Revisions)],
    };

    public static TheoryData<string, string, string> Packs() => new()
    {
        { "srd-5.1", RulesFamilies.Srd51, "2504d2a0abb0a4d491a939be4f17910a2dde0312570ab8d208080225ccf0a1f0" },
        { "srd-5.2.1", RulesFamilies.Srd521, "8974902d109d6e63672d7c490bde9ccf052410503d9cfa768237154fbc5e3d87" },
    };

    private static ContentPack Pack(string id) => id == "srd-5.1" ? Srd51 : Srd521;

    [Fact]
    public void A_familys_packs_carry_the_identical_source_record()
    {
        foreach (var pack in new[] { Spells51, Equipment51, Classes51 })
            Assert.Equal(TempApp.Json(Base51.Sources), TempApp.Json(pack.Sources));
        foreach (var pack in new[] { Spells521, Equipment521, Classes521 })
            Assert.Equal(TempApp.Json(Base521.Sources), TempApp.Json(pack.Sources));
    }

    [Fact]
    public void The_weapon_tables_are_items_with_weapons_and_differ_by_family_as_content()
    {
        Assert.Equal(37, Equipment51.Revisions.Count); // SRD 5.1 p. 66
        Assert.Equal(38, Equipment521.Revisions.Count); // SRD 5.2.1 p. 91: adds the musket and the pistol, no net
        Assert.All(Equipment51.Revisions.Concat(Equipment521.Revisions), r => Assert.Equal(ContentKind.Item, r.Kind));
        // Text only: the net has no damage, and the blowgun's flat 1 damage is not a dice roll.
        Assert.Equal(["Blowgun", "Net"], Equipment51.Revisions.Where(r => !r.Effects.OfType<WeaponEffect>().Any()).Select(r => r.Name));
        Assert.Equal(["Blowgun"], Equipment521.Revisions.Where(r => !r.Effects.OfType<WeaponEffect>().Any()).Select(r => r.Name));
        WeaponEffect Weapon(ContentPack pack, string name) => pack.Revisions.Single(r => r.Name == name).Effects.OfType<WeaponEffect>().Single();

        // The trident is 1d6 (versatile 1d8) in 2014 and 1d8 (versatile 1d10) in 2024; only 2024 has mastery properties.
        Assert.Equal(("1d6", "1d8", null), (Weapon(Equipment51, "Trident").Damage, Weapon(Equipment51, "Trident").Versatile, Weapon(Equipment51, "Trident").Mastery));
        Assert.Equal(("1d8", "1d10", "Topple"), (Weapon(Equipment521, "Trident").Damage, Weapon(Equipment521, "Trident").Versatile, Weapon(Equipment521, "Trident").Mastery));
        Assert.Equal(("light-crossbow", "light-crossbow"), (Weapon(Equipment51, "Crossbow, light").WeaponKey, Weapon(Equipment521, "Light Crossbow").WeaponKey));
        Assert.Equal(["finesse", "light", "thrown"], Weapon(Equipment51, "Dagger").Properties);
        Assert.Equal("20/60", Weapon(Equipment51, "Dagger").Range);
    }

    [Fact]
    public void The_Barbarian_as_a_later_class_gains_simple_weapons_only_under_2014_rules_side_by_side()
    {
        // SRD 5.1 p. 57: shields, simple and martial weapons. SRD 5.2.1 p. 28: Martial weapons and Shields only.
        using var temp = new TempApp();
        Character Later(ContentPack classes, ContentPack equipment, string family, string club)
        {
            var barbarian = classes.Revisions.Single(r => r.Name == "Barbarian");
            var fixtureCaster = new ContentReference(Guid.Parse("5f5dc000-0000-4000-8000-000000000001"), Guid.Parse("5f5de000-0000-4000-8000-000000000001"));
            return new Character
            {
                Id = Guid.NewGuid(), Name = "Test Later Barbarian", RulesFamily = family, Level = 2,
                Classes = [new(fixtureCaster, 1), new(barbarian.Reference, 1)],
                BaseAbilities = new(14, 12, 12, 13, 10, 10),
                Equipment = [new(equipment.Revisions.Single(r => r.Name == club).Reference, Equipped: true), new(equipment.Revisions.Single(r => r.Name == "Greataxe").Reference, Equipped: true)],
            };
        }

        var old = temp.App.SaveCharacter(Later(Classes51, Equipment51, RulesFamilies.Srd51, "Club")).Sheet;
        var current = temp.App.SaveCharacter(Later(Classes521, Equipment521, RulesFamilies.Srd521, "Club")).Sheet;

        Assert.Equal([true, true], old.Attacks!.Select(a => a.Proficient));
        Assert.Equal([false, true], current.Attacks!.Select(a => a.Proficient)); // club (simple) no, greataxe (martial) yes
        // Saving throw proficiencies come only with the starting class: Str save = the modifier +2, not +2 + PB.
        Assert.Equal((2, 2), (old.Field(FieldIds.Save(Ability.Str)).Value, current.Field(FieldIds.Save(Ability.Str)).Value));
    }

    [Fact]
    public void Content_list_offers_the_newest_revision_and_marks_the_superseded_Barbarian()
    {
        using var temp = new TempApp();

        var barbarians = temp.App.ListContent(RulesFamilies.Srd521).Where(o => o.Name == "Barbarian" && o.Compatible).ToList();

        Assert.Equal(2, barbarians.Count); // the M1 revision (still pinned by saved characters) and the content v5 revision
        Assert.Equal(Classes521.Revisions.Single(r => r.Name == "Barbarian").Reference, Assert.Single(barbarians, o => !o.Superseded).Reference);
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void Each_pack_has_one_CC_BY_source_with_the_approved_attribution_verbatim(string packId, string family, string pdfHash)
    {
        var source = Assert.Single(Pack(packId).Sources);
        var approved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "licensing", "srd-attribution-draft.md"));

        Assert.Equal(("CC-BY-4.0", true, pdfHash), (source.License, source.Redistributable, source.Sha256));
        Assert.Equal([family], source.RulesFamilies);
        Assert.Contains($"> {source.Attribution}", approved, StringComparison.Ordinal); // verbatim, as approved
        Assert.StartsWith("Modified: ", source.ModificationNotice, StringComparison.Ordinal);
        Assert.Contains(source.ModificationNotice!, approved, StringComparison.Ordinal); // the recorded §3 wording
        // "Do not include any other attribution to Wizards": only the attribution statement names them.
        Assert.DoesNotContain("Wizards", source.Publisher + source.Title + source.ModificationNotice, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void Every_revision_belongs_to_its_own_family_and_source_and_cites_a_page(string packId, string family, string _)
    {
        var pack = Pack(packId);
        var sourceId = Assert.Single(pack.Sources).Id;

        Assert.All(pack.Revisions, r =>
        {
            Assert.Equal([family], r.RulesFamilies);
            Assert.Equal(sourceId, r.Provenance.SourceId);
            Assert.NotNull(r.Provenance.Page);
            Assert.Equal(RevisionStatus.Published, r.Status);
        });
    }

    [Fact]
    public void The_two_packs_share_no_source_or_content_ids_even_where_names_match()
    {
        Assert.Empty(Srd51.Sources.Select(s => s.Id).Intersect(Srd521.Sources.Select(s => s.Id)));
        Assert.Empty(Srd51.Revisions.Select(r => r.ContentId).Intersect(Srd521.Revisions.Select(r => r.ContentId)));
        Assert.Empty(Srd51.Revisions.Select(r => r.RevisionId).Intersect(Srd521.Revisions.Select(r => r.RevisionId)));
        // "Barbarian", "Rage" and others exist in both: same name, different content (never merged by name, SPEC S-03).
        Assert.Contains("Barbarian", Srd51.Revisions.Select(r => r.Name).Intersect(Srd521.Revisions.Select(r => r.Name)));
    }

    [Fact]
    public void Every_revision_validates_without_errors()
    {
        var catalog = new InMemoryContentCatalog([.. Srd51.Sources, .. Srd521.Sources], [.. Srd51.Revisions, .. Srd521.Revisions]);

        foreach (var revision in Srd51.Revisions.Concat(Srd521.Revisions))
        {
            var report = ContentValidator.Validate(revision, catalog);
            Assert.True(report.CanPublish, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => e.Message))}");
        }
    }

    [Fact]
    public void The_spell_packs_have_every_SRD_spell_once_with_its_data()
    {
        string[] classes = ["bard", "cleric", "druid", "paladin", "ranger", "sorcerer", "warlock", "wizard"];
        foreach (var (pack, count) in new[] { (Spells51, 319), (Spells521, 339) })
        {
            Assert.Equal(count, pack.Revisions.Count);
            Assert.All(pack.Revisions, r => Assert.Equal(ContentKind.Spell, r.Kind));
            Assert.Equal(pack.Revisions.Count, pack.Revisions.Select(r => r.Name).Distinct().Count());
            var data = pack.Revisions.Select(r => Assert.Single(r.Effects.OfType<SpellEffect>())).ToList();
            Assert.All(data, s =>
            {
                Assert.InRange(s.Level, 0, 9);
                Assert.NotEmpty(s.Lists);
                Assert.All(s.Lists, l => Assert.Contains(l, classes));
                Assert.False(string.IsNullOrWhiteSpace(s.Text));
            });
        }
    }

    [Fact]
    public void The_same_spell_differs_by_family_as_content_side_by_side()
    {
        // Cure Wounds heals 1d8 under 2014 rules (SRD 5.1 p. 132) and 2d8 under 2024 rules (SRD 5.2.1 p. 121): two revisions.
        SpellEffect Cure(ContentPack pack) => pack.Revisions.Single(r => r.Name == "Cure Wounds").Effects.OfType<SpellEffect>().Single();

        Assert.Equal(("1d8", 1), (Cure(Spells51).Dice, Cure(Spells51).Level));
        Assert.Equal(("2d8", 1), (Cure(Spells521).Dice, Cure(Spells521).Level));
        Assert.NotEqual(Spells51.Revisions.Single(r => r.Name == "Cure Wounds").ContentId, Spells521.Revisions.Single(r => r.Name == "Cure Wounds").ContentId);
    }

    [Fact]
    public void The_slice_is_one_species_one_background_one_class_with_its_subclass_and_feats()
    {
        foreach (var pack in new[] { Base51, Base521 })
        {
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Species);
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Background);
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Class);
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Subclass);
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Feat);
        }
    }

    [Fact]
    public void A_fresh_data_folder_gets_the_SRD_packs_and_no_test_fixtures()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: []);
        try
        {
            var sources = app.Store.ListSources();
            Assert.Equal(["System Reference Document 5.1", "System Reference Document 5.2.1"], sources.Select(s => s.Title).Order());
            Assert.Equal(Srd51.Revisions.Count + Srd521.Revisions.Count, app.Store.ListRevisions().Count);
            Assert.DoesNotContain(app.ListContent(RulesFamilies.Srd51), o => o.Name.StartsWith("Fixture", StringComparison.Ordinal));
        }
        finally
        {
            app.Dispose();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void Package_notices_carry_the_attribution_and_the_modification_notice()
    {
        using var temp = new TempApp();
        var halfOrc = Srd51.Revisions.Single(r => r.Name == "Half-Orc");
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json") with { Pins = [halfOrc.Reference] };
        var saved = temp.App.SaveCharacter(character);

        var notice = Assert.Single(temp.App.ExportCharacters([saved.Character.Id], Packages.ExportPurpose.Share).Manifest.Notices);

        Assert.Equal(Srd51.Sources[0].Attribution, notice.Attribution);
        Assert.Equal(Srd51.Sources[0].ModificationNotice, notice.ModificationNotice);
    }
}
