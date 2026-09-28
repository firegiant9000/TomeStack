using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 exit (docs/features/m2-acceptance.md), MVP "Sheet" ("play through a short scripted encounter and rest") and
/// definition of done 2: one SRD 5.1 and one SRD 5.2.1 character, built only from the bundled SRD content, play a short
/// encounter (damage, a spell with a slot, an attack roll, a weapon attack), take a short rest with hit dice and a long
/// rest, and round-trip through a backup to a clean data folder with the same sheet.
/// </summary>
public class M2AcceptanceTests
{
    /// <summary>The newest revision (the last in the pack), as the pickers offer it.</summary>
    private static ContentRevision Named(ContentPack pack, string name, ContentKind kind) => pack.Revisions.Last(r => r.Name == name && r.Kind == kind);

    public static TheoryData<string, string, string, string> Families() => new()
    {
        // family, pack suffix, weapon name, cantrip with an attack
        { RulesFamilies.Srd51, "5.1", "Quarterstaff", "Fire Bolt" },
        { RulesFamilies.Srd521, "5.2.1", "Quarterstaff", "Fire Bolt" },
    };

    [Theory]
    [MemberData(nameof(Families))]
    public void An_SRD_wizard_plays_an_encounter_rests_and_survives_a_backup(string family, string version, string weapon, string cantrip)
    {
        var classes = TomeStackApp.LoadBundledPack($"TomeStack.Content.srd-{version}-classes.json");
        var spells = TomeStackApp.LoadBundledPack($"TomeStack.Content.srd-{version}-spells.json");
        var equipment = TomeStackApp.LoadBundledPack($"TomeStack.Content.srd-{version}-equipment.json");
        var caster = classes.Revisions.Where(r => r.Effects.OfType<SpellcastingEffect>().Any(s => s.SpellList == "wizard")).Select(r => r.ContentId).Distinct().Single();
        using var temp = new TempApp();

        // Build: a level 3 Wizard with a cantrip, two levelled spells and a quarterstaff (both SRDs make it a wizard weapon).
        var created = temp.App.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = $"Test Acceptance Wizard {version}", RulesFamily = family, Level = 3,
            Classes = [new(Named(classes, "Wizard", ContentKind.Class).Reference, 3)],
            BaseAbilities = new(10, 14, 14, 16, 12, 8),
            Spells =
            [
                new(caster, Named(spells, cantrip, ContentKind.Spell).Reference),
                new(caster, Named(spells, "Magic Missile", ContentKind.Spell).Reference),
                new(caster, Named(spells, "Shield", ContentKind.Spell).Reference),
            ],
            Equipment = [new(Named(equipment, weapon, ContentKind.Item).Reference, Equipped: true)],
        });
        var id = created.Character.Id;
        var sheet = created.Sheet;
        var maximum = sheet.HitPoints!.Maximum; // d6: 6 + 4 + 4, Con +2 x 3 = 20
        Assert.Equal(20, maximum);
        Assert.Equal((5, 13), (sheet.Field(FieldIds.SpellAttack).Value, sheet.Field(FieldIds.SpellSaveDc).Value)); // PB 2 + Int 3
        Assert.Equal([(1, 4), (2, 2)], sheet.SpellSlots!.Select(s => (s.Level, s.Maximum))); // both SRD Wizard tables at level 3
        Assert.Empty(sheet.Spellcasting!.Single().Warnings);
        var staff = Assert.Single(sheet.Attacks!);
        Assert.True(staff.Proficient);

        // Encounter: roll the cantrip's attack and the staff, take 12 damage, cast Magic Missile with a slot.
        Assert.Equal("1d20", temp.App.Roll(new(id, Spell: Named(spells, cantrip, ContentKind.Spell).Reference, SpellAttack: true)).Formula);
        Assert.Equal("1d20", temp.App.Roll(new(id, Weapon: staff.Item)).Formula);
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 12));
        var hurt = temp.App.Play(new(id, PlayActionKind.SpendSlot, Confirm: true, Amount: 1));
        Assert.Equal((8, 3), (hurt.Sheet.HitPoints!.Current, hurt.Sheet.SpellSlots!.Single(s => s.Level == 1).Remaining));

        // Short rest: spend one d6 hit die showing 4 (+2 Con). Arcane Recovery stays a manual step (text) in both.
        var shortRest = temp.App.PreviewRest(id, RestPeriod.ShortRest, [new(6, 4)]);
        var rested = temp.App.Rest(new(id, RestPeriod.ShortRest, Confirm: true, Basis: shortRest.Basis, HitDice: [new(6, 4)]));
        Assert.Equal((14, 2), (rested.Sheet.HitPoints!.Current, rested.Sheet.HitDice!.Single().Remaining));

        // Long rest: everything back, per the family's rules.
        var longRest = temp.App.PreviewRest(id, RestPeriod.LongRest);
        Assert.Contains(longRest.Changes, c => c.Kind == RestChangeKind.SpellSlots);
        var morning = temp.App.Rest(new(id, Confirm: true, Basis: longRest.Basis));
        Assert.Equal(maximum, morning.Sheet.HitPoints!.Current);
        Assert.Equal(4, morning.Sheet.SpellSlots!.Single(s => s.Level == 1).Remaining);
        Assert.Equal(3, morning.Sheet.HitDice!.Single().Remaining); // 2014: half of 3 = 1 back (1 was spent); 2024: all

        // Backup to a clean data folder: the same character and sheet, with the SRD attribution notice.
        var backup = temp.App.ExportCharacters([id]);
        Assert.Contains(backup.Manifest.Notices, n => n.Attribution!.Contains(version == "5.1" ? "5.1" : "5.2.1", StringComparison.Ordinal));
        using var clean = new TempApp();
        clean.App.ApplyImport(backup.Content);
        var restored = clean.App.GetCharacter(id);
        Assert.Equal(TempApp.Json(morning.Character), TempApp.Json(restored.Character));
        Assert.Equal(TempApp.Json(morning.Sheet), TempApp.Json(restored.Sheet));
    }
}
