using System.Text;
using TomeStack.AppService.Exports;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// MECH-X item 4 (R60 to R65): a content companion's hit points, pool and attack through the play, roll, rest, save, snapshot and
/// export paths. (Named apart from <c>CompanionPlayTests</c>, which covers the hand companions of Wave 2.) Fixtures:
/// <c>fixture-pack-companions.json</c> (installed per test) and <c>characters/pm-handler-l3.json</c>, put in the pack's "Fixture Keeper
/// Path" class (a level 3 character whose Warden has 15 hit points; the stored row says 4).
/// </summary>
public class ContentCompanionPlayTests
{
    private static readonly Guid WardenId = Guid.Parse("6a7e0c00-0000-4000-8000-000000000001");
    private static readonly Guid MuleId = Guid.Parse("6a7e0cb0-0000-4000-8000-0000000000c1");
    private static readonly ContentReference Keeper = new(Guid.Parse("6a7e00f4-0000-4000-8000-000000000010"), Guid.Parse("6a7e00f4-0000-4000-8000-000000000110"));
    private static readonly ContentReference WardenBond = new(Guid.Parse("6a7e00f4-0000-4000-8000-000000000001"), Guid.Parse("6a7e00f4-0000-4000-8000-000000000101"));
    private static readonly ContentReference HoundKeeper = new(Guid.Parse("6a7e00f4-0000-4000-8000-000000000002"), Guid.Parse("6a7e00f4-0000-4000-8000-000000000102"));
    private static readonly ContentReference SentinelBond = new(Guid.Parse("6a7e00f4-0000-4000-8000-000000000003"), Guid.Parse("6a7e00f4-0000-4000-8000-000000000103"));

    private sealed record Setup(TempApp Temp, Guid Id);

    private static Character Handler(int level = 3) =>
        TempApp.LoadFixture<Character>("characters/pm-handler-l3.json") with { Level = level, Classes = [new(Keeper, level)] };

    /// <summary>Stores the handler directly (the stored row is exactly the fixture's), with the pack installed.</summary>
    private static Setup Start(Func<Character, Character>? adjust = null)
    {
        var temp = new TempApp();
        temp.AddPack("fixture-pack-companions.json");
        var character = adjust?.Invoke(Handler()) ?? Handler();
        temp.App.Store.InTransaction(() => temp.App.Store.SaveCharacter(character));
        return new(temp, character.Id);
    }

    private static PlayCommand Content(Setup s, PlayActionKind action, int amount = 1, int? temporary = null, Guid? contentId = null) =>
        new(s.Id, action, Confirm: true, Amount: amount, ContentId: contentId ?? WardenBond.ContentId, CompanionId: WardenId, Temporary: temporary);

    private static string CodeOf(Action act) => Assert.Throws<AppValidationException>(act).Problems[0].Code;

    private static CompanionPlay Row(CharacterView view) => Assert.Single(view.Character.Play.Companions!, p => p.ContentId == WardenBond.ContentId);

    [Fact]
    public void Damage_heal_and_set_work_on_a_content_companion()
    {
        var s = Start();
        using var _ = s.Temp;

        var padded = s.Temp.App.Play(Content(s, PlayActionKind.CompanionSetTemporaryHitPoints, amount: 2));
        Assert.Equal((4, 2), (Row(padded).Current, Row(padded).Temporary));

        var hit = s.Temp.App.Play(Content(s, PlayActionKind.CompanionDamage, amount: 3));
        Assert.Equal((3, 0, WardenBond.ContentId), (Row(hit).Current, Row(hit).Temporary, Row(hit).ContentId)); // 2 absorbed, 1 taken
        Assert.Equal(3, hit.Sheet.Companions!.Single().Current);

        var healed = s.Temp.App.Play(Content(s, PlayActionKind.CompanionHeal, amount: 100));
        Assert.DoesNotContain(healed.Character.Play.Companions!, p => p.ContentId is not null); // full and no temporary points: no row
        Assert.Equal(15, healed.Sheet.Companions!.Single().Current);

        Assert.Equal("play.companion-hit-points-above-maximum", CodeOf(() => s.Temp.App.Play(Content(s, PlayActionKind.CompanionSetHitPoints, amount: 16))));
        var set = s.Temp.App.Play(Content(s, PlayActionKind.CompanionSetHitPoints, amount: 7, temporary: 1));
        Assert.Equal((7, 1, WardenBond.ContentId), (Row(set).Current, Row(set).Temporary, Row(set).ContentId));
        Assert.Contains(set.Character.Play.Companions!, p => p.ContentId is null && p.Id == MuleId); // the hand companion's row is untouched
    }

    [Fact]
    public void A_content_companion_needs_its_content_id()
    {
        var s = Start();
        using var _ = s.Temp;

        var withoutContent = new PlayCommand(s.Id, PlayActionKind.CompanionDamage, Confirm: true, Amount: 1, CompanionId: WardenId);
        Assert.Equal("play.companion-unknown", CodeOf(() => s.Temp.App.Play(withoutContent)));
        Assert.Equal("play.companion-unknown", CodeOf(() => s.Temp.App.Play(Content(s, PlayActionKind.CompanionDamage, contentId: HoundKeeper.ContentId))));
        Assert.Equal("play.companion-unknown", CodeOf(() => s.Temp.App.Play(Content(s, PlayActionKind.CompanionDamage) with { CompanionId = Guid.NewGuid() })));
    }

    [Fact]
    public void A_companion_whose_hit_points_cannot_be_calculated_is_unknown_to_play()
    {
        var s = Start(c => c with { Classes = [], Level = 3, Pins = [SentinelBond] });
        using var _ = s.Temp;

        var command = Content(s, PlayActionKind.CompanionDamage, contentId: SentinelBond.ContentId) with
        {
            CompanionId = Guid.Parse("6a7e0c00-0000-4000-8000-000000000003"),
        };
        Assert.Equal("play.companion-unknown", CodeOf(() => s.Temp.App.Play(command)));
    }

    [Fact]
    public void Its_pool_spends_rests_and_regains_like_any_pool()
    {
        var s = Start();
        using var _ = s.Temp;
        var spend = new PlayCommand(s.Id, PlayActionKind.Spend, Confirm: true, Amount: 1, ContentId: WardenBond.ContentId, ResourceId: "warden-fury");

        var spent = s.Temp.App.Play(spend);
        var pool = spent.Sheet.Resources!.Single(r => r.ResourceId == "warden-fury");
        Assert.Equal((1, 1, WardenId), (pool.Spent, pool.Current, pool.CompanionId));

        var restored = s.Temp.App.Play(spend with { Action = PlayActionKind.Regain });
        Assert.Equal(0, restored.Sheet.Resources!.Single(r => r.ResourceId == "warden-fury").Spent);

        s.Temp.App.Play(spend with { Amount = 2 });
        var asking = s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest);
        var rested = s.Temp.App.Rest(new(s.Id, RestPeriod.LongRest, Confirm: true, Basis: asking.Basis));
        Assert.Equal(0, rested.Sheet.Resources!.Single(r => r.ResourceId == "warden-fury").Spent);
    }

    [Fact]
    public void Its_attack_rolls_by_content_and_effect_id()
    {
        var s = Start();
        using var _ = s.Temp;

        var attack = s.Temp.App.Roll(new(s.Id, Content: WardenBond, EffectId: "warden-bite"));
        var damage = s.Temp.App.Roll(new(s.Id, Content: WardenBond, EffectId: "warden-bite", Damage: true));

        Assert.StartsWith("1d20", attack.Formula, StringComparison.Ordinal);
        Assert.Contains("1d8", damage.Formula, StringComparison.Ordinal);
        Assert.Contains("1d4", damage.Formula, StringComparison.Ordinal); // the level 3 Warden Growth rider
    }

    [Fact]
    public void A_snapshot_restore_keeps_the_content_row()
    {
        var s = Start();
        using var _ = s.Temp;
        var snapshot = s.Temp.App.Snapshot(new(s.Id));
        s.Temp.App.Play(Content(s, PlayActionKind.CompanionDamage, amount: 2));

        var preview = s.Temp.App.PreviewRestore(new(s.Id, snapshot.Id));
        var restored = s.Temp.App.RestoreSnapshot(new(preview.Token, Confirm: true));

        Assert.Equal(4, Row(restored.View).Current);
        Assert.Equal(4, restored.View.Sheet.Companions!.Single().Current);
    }

    [Fact]
    public void A_level_up_grows_the_maximum_and_keeps_current()
    {
        var s = Start();
        using var _ = s.Temp;
        var stored = s.Temp.App.GetCharacter(s.Id).Character;

        var saved = s.Temp.App.SaveCurrent(stored with { Level = 5, Classes = [new(Keeper, 5)] });

        var card = saved.Sheet.Companions!.Single();
        Assert.Equal((25, 4), (card.HitPointMaximum, card.Current));
        Assert.Equal(4, s.Temp.App.GetCharacter(s.Id).Sheet.Companions!.Single().Current);
    }

    [Fact]
    public void A_hand_and_a_content_companion_with_one_id_keep_two_rows()
    {
        var s = Start(c => c with
        {
            Companions = [new(WardenId, "Fixture Mule", null, null, 11, null, null, null)],
            Play = c.Play with { Companions = [new(WardenId, 6, 0), new(WardenId, 4, 0, WardenBond.ContentId)] },
        });
        using var _ = s.Temp;

        var handHit = s.Temp.App.Play(new(s.Id, PlayActionKind.CompanionDamage, Confirm: true, Amount: 1, CompanionId: WardenId));
        Assert.Equal(5, Assert.Single(handHit.Character.Play.Companions!, p => p.ContentId is null).Current);
        Assert.Equal(4, Row(handHit).Current);

        var contentHit = s.Temp.App.Play(Content(s, PlayActionKind.CompanionDamage));
        Assert.Equal(5, Assert.Single(contentHit.Character.Play.Companions!, p => p.ContentId is null).Current);
        Assert.Equal(3, Row(contentHit).Current);
        Assert.Equal(2, contentHit.Character.Play.Companions!.Count);
    }

    [Fact]
    public void An_ordinary_save_keeps_the_row_of_an_active_companion()
    {
        var s = Start();
        using var _ = s.Temp;
        var stored = s.Temp.App.GetCharacter(s.Id).Character;

        var saved = s.Temp.App.SaveCurrent(stored with { Name = "Testy Handler Renamed" });

        Assert.Equal(4, Row(saved).Current);
        Assert.Equal(4, Row(s.Temp.App.GetCharacter(s.Id)).Current);

        var dropped = s.Temp.App.SaveCurrent(saved.Character with { Pins = [], Classes = [], Level = 3 });
        Assert.DoesNotContain(dropped.Character.Play.Companions!, p => p.ContentId is not null); // the content is no longer active
    }

    [Fact]
    public void Exports_leave_companion_rows_out()
    {
        var s = Start();
        using var _ = s.Temp;

        foreach (var target in new[] { FoundryDnd5e.Target, SheetJson.Target })
        {
            var preview = s.Temp.App.PreviewVttExport(s.Id, target, SheetPurpose.Share);
            var text = Encoding.UTF8.GetString(s.Temp.App.VttExportOutput(preview.Token).Bytes);
            Assert.DoesNotContain("Warden bite", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Warden fury", text, StringComparison.Ordinal);
            Assert.DoesNotContain("warden-bite", text, StringComparison.Ordinal);
            Assert.DoesNotContain("warden-fury", text, StringComparison.Ordinal);
        }
    }
}
