using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// MECH-X item 5 (ADR-021): a "target mark" is a player-set toggle whose attack riders and roll modes apply only while it is on.
/// These characterization tests pin what the engine already does. Original fixtures: <c>fixture-pack-target-marks.json</c> and
/// <c>characters/pm-marker-l5.json</c> (an adept at level 5 with a longblade equipped). Variants are built in memory; fixture files are never edited.
/// </summary>
public class TargetMarkTests
{
    private static Character Marker(bool marked) =>
        Fixtures.Load("pm-marker-l5.json") with
        {
            Play = new PlayState { Toggles = marked ? [new ActiveToggle(Fixtures.SigilMark.ContentId, "marked")] : [] },
        };

    private static CharacterSheet Sheet(Character character) =>
        CharacterCalculator.Calculate(character, Fixtures.PlayMechanicsCatalog());

    private static AttackEntry Blade(CharacterSheet sheet) => sheet.Attacks!.Single(a => a.Source == AttackSource.Weapon);

    private static AttackEntry Strike(CharacterSheet sheet) => sheet.Attacks!.Single(a => a.EffectId == "strike");

    /// <summary>The damage string with +1d6 inserted after the weapon dice (the format the rider damage uses).</summary>
    private static string WithMarkDice(string damage)
    {
        var at = damage.IndexOf('+', StringComparison.Ordinal);
        return damage[..at] + "+1d6" + damage[at..];
    }

    [Fact]
    public void The_target_marks_pack_validates_without_errors_or_warnings_at_v11()
    {
        var catalog = Fixtures.PlayMechanicsCatalog();
        foreach (var reference in new[] { Fixtures.SigilMark, Fixtures.SigilFocus })
        {
            var revision = catalog.FindRevision(reference)!;
            var report = ContentValidator.Validate(revision, catalog);
            Assert.True(report.Errors.Count == 0, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => $"{e.Code} {e.Message}"))}");
            Assert.True(report.Warnings.Count == 0, $"{revision.Name}: {string.Join("; ", report.Warnings.Select(e => $"{e.Code} {e.Message}"))}");
            Assert.Equal(ContentRevision.PlayMechanicsSchemaVersion, report.RequiredSchemaVersion);
            Assert.DoesNotContain(revision.Effects, e => e is UnknownEffect);
        }
    }

    [Fact]
    public void Unmarked_no_mark_rider_applies()
    {
        var sheet = Sheet(Marker(marked: false));
        var blade = Blade(sheet);
        var strike = Strike(sheet);

        Assert.Null(blade.Riders);
        Assert.Null(strike.Riders);
        Assert.DoesNotContain(blade.Trace.Concat(strike.Trace), t => t.Description.Contains("Fixture Sigil", StringComparison.Ordinal));
        Assert.DoesNotContain("1d6", blade.Damage, StringComparison.Ordinal);
        Assert.DoesNotContain("1d6", blade.VersatileDamage!, StringComparison.Ordinal);
        Assert.DoesNotContain("1d6", strike.Damage, StringComparison.Ordinal);
    }

    [Fact]
    public void Marked_the_rider_adds_its_dice_to_weapons_and_feature_attacks()
    {
        var off = Sheet(Marker(marked: false));
        var on = Sheet(Marker(marked: true));

        Assert.Equal("1d8+2", Blade(off).Damage);
        Assert.Equal("1d10+2", Blade(off).VersatileDamage);
        Assert.Equal("1d8+3", Strike(off).Damage);

        Assert.Equal(WithMarkDice(Blade(off).Damage), Blade(on).Damage);
        Assert.Equal("1d8+1d6+2", Blade(on).Damage);
        Assert.Equal(WithMarkDice(Blade(off).VersatileDamage!), Blade(on).VersatileDamage);
        Assert.Equal("1d10+1d6+2", Blade(on).VersatileDamage);
        Assert.Equal(WithMarkDice(Strike(off).Damage), Strike(on).Damage);
        Assert.Equal("1d8+1d6+3", Strike(on).Damage);

        Assert.Contains(Blade(on).Trace, t => t.Description.Contains("Fixture Sigil Mark: +1d6 force damage (attack rider)", StringComparison.Ordinal));
        Assert.Contains(Strike(on).Trace, t => t.Description.Contains("Fixture Sigil Mark: +1d6 force damage (attack rider)", StringComparison.Ordinal));
        Assert.Contains(Blade(on).Riders!, r => r.Content == Fixtures.SigilMark && r.EffectId == "mark-dice" && r.Dice == "1d6");
    }

    [Fact]
    public void Marked_the_cross_entry_rider_adds_to_hit_on_weapons_only()
    {
        var off = Sheet(Marker(marked: false));
        var on = Sheet(Marker(marked: true));

        Assert.Equal(Blade(off).ToHit + 1, Blade(on).ToHit);
        Assert.Contains(Blade(on).Trace, t => t.Description.Contains("Fixture Sigil Focus: +1 to hit (attack rider)", StringComparison.Ordinal));
        Assert.Equal(Strike(off).ToHit, Strike(on).ToHit); // the tag weapon does not match a feature attack
        Assert.DoesNotContain(Strike(on).Trace, t => t.Description.Contains("Fixture Sigil Focus", StringComparison.Ordinal));
    }

    [Fact]
    public void Focus_without_the_mark_on_the_character_adds_nothing_and_reports_nothing()
    {
        var plain = Blade(Sheet(Marker(marked: false)));
        var noMark = Marker(marked: true) with
        {
            Pins = [.. Marker(marked: true).Pins.Where(p => p.ContentId != Fixtures.SigilMark.ContentId)],
        };
        Assert.Contains(noMark.Pins, p => p.ContentId == Fixtures.SigilFocus.ContentId);
        Assert.Single(noMark.Play.Toggles); // the toggle entry is still there

        var sheet = Sheet(noMark);

        Assert.Equal(plain.ToHit, Blade(sheet).ToHit);
        Assert.Null(Blade(sheet).Riders);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Content == Fixtures.SigilFocus);
    }

    [Fact]
    public void Marked_gives_advantage_on_survival_only_while_on()
    {
        var on = Sheet(Marker(marked: true)).Field("skill.survival").Mode;
        var off = Sheet(Marker(marked: false)).Field("skill.survival").Mode;

        Assert.Equal(RollMode.Advantage, on!.Automatic);
        Assert.True(off is null || off.Automatic == RollMode.Normal);
    }
}
