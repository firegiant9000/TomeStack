using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Content v12 (G6, Task 6): how the hit came (<c>from</c>, <c>save</c>) through <c>character.damagePreview</c> and the damage command,
/// on the original responses-v12 pack and the Testy Sidestepper (level 6, Dexterity 16: modifier 3; class level 6).
/// </summary>
public class DamageResponseV12ServiceTests
{
    private static readonly Guid Source = Guid.Parse("6f0d5000-0000-4000-8000-000000001201");
    private static readonly Guid Duck = Guid.Parse("6f0dc000-0000-4000-8000-000000001202");
    private static readonly Guid Turn = Guid.Parse("6f0dc000-0000-4000-8000-000000001203");
    private static readonly Guid Landing = Guid.Parse("6f0dc000-0000-4000-8000-000000001204");
    private static readonly Guid Catch = Guid.Parse("6f0dc000-0000-4000-8000-000000001205");
    private static readonly Guid CatchAny = Guid.Parse("6f0dc000-0000-4000-8000-000000001206");
    private static readonly Guid PlusClass = Guid.Parse("6f0dc000-0000-4000-8000-000000001207");
    private static readonly Guid PlusRevision = Guid.Parse("6f0de000-0000-4000-8000-000000001207");

    private static DamageSave Dex(bool succeeded) => new("dex", succeeded);

    private static (TempApp Temp, Guid Id) Sidestepper(bool plus = false)
    {
        var temp = new TempApp(time: new TickingTime(TempApp.Now));
        temp.AddPack("fixture-pack-responses-v12.json");
        var character = TempApp.LoadFixture<Character>("characters/v12-sidestepper-l6.json");
        if (plus)
            character = character with
            {
                Classes = [character.Classes[0] with { Class = new ContentReference(PlusClass, PlusRevision) }],
                Pins = [.. character.Pins.Where(p => p.ContentId != Guid.Parse("6f0dc000-0000-4000-8000-000000001201")), new(PlusClass, PlusRevision), new(CatchAny, Guid.Parse("6f0de000-0000-4000-8000-000000001206"))],
            };
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static CharacterView View(TempApp temp, Guid id) => temp.App.GetCharacter(id);

    private static int Max(TempApp temp, Guid id) => View(temp, id).Sheet.HitPoints!.Maximum;

    private static string Stored(TempApp temp, Guid id) => TempApp.Json(temp.App.Store.FindCharacter(id));

    private static AppValidationException Refused(Action act) => Assert.Throws<AppValidationException>(act);

    [Fact]
    public void Preview_with_a_save_success_takes_nothing_with_the_save_response()
    {
        var (temp, id) = Sidestepper();
        using var _ = temp;
        var max = Max(temp, id);

        var preview = temp.App.PreviewDamage(new(id, 20, "fire", [new(Duck, "duck-success")], ResponseSource.Save, Dex(true)));

        Assert.Empty(preview.Notes);
        Assert.Equal(10, preview.Reduction); // the save halved 20 to 10 (not counted), the response took the rest
        Assert.Equal(0, preview.Outcome.Adjusted);
        Assert.True(preview.Outcome.SaveHalved);
        Assert.Equal(max, preview.After.Current);
        // The failure twin is not eligible after a success.
        Assert.DoesNotContain(preview.Offers, o => o.Response.ResponseId == "duck-failure");
        Assert.Contains(preview.Offers, o => o.Response.ResponseId == "duck-success" && o.Offered);
    }

    [Fact]
    public void Confirm_with_a_save_failure_halves()
    {
        var (temp, id) = Sidestepper();
        using var _ = temp;
        var max = Max(temp, id);
        var stamp = View(temp, id).Character.UpdatedAt;

        var view = temp.App.Play(new(
            id, PlayActionKind.Damage, Confirm: true, Amount: 20, DamageType: "fire", ExpectedUpdatedAt: stamp,
            Responses: [new(Duck, "duck-failure")], DamageFrom: ResponseSource.Save, DamageSave: Dex(false)));

        Assert.Equal(10, view.Damage!.Adjusted);
        Assert.Equal(max - 10, view.Sheet.HitPoints!.Current);
    }

    [Fact]
    public void Plain_damage_applies_the_save_half()
    {
        var (temp, id) = Sidestepper();
        using var _ = temp;
        var max = Max(temp, id);

        var view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 21, DamageFrom: ResponseSource.Save, DamageSave: Dex(true)));

        Assert.Equal(10, view.Damage!.Adjusted);
        Assert.True(view.Damage.SaveHalved);
        Assert.Equal(max - 10, view.Sheet.HitPoints!.Current);
    }

    [Fact]
    public void Fall_response_is_not_offered_for_an_attack()
    {
        var (temp, id) = Sidestepper();
        using var _ = temp;

        var attack = temp.App.PreviewDamage(new(id, 40, null, [new(Landing, "landing")], ResponseSource.Attack));
        var fall = temp.App.PreviewDamage(new(id, 40, null, [new(Landing, "landing")], ResponseSource.Fall));

        Assert.DoesNotContain(attack.Offers, o => o.Response.ResponseId == "landing");
        Assert.Contains(attack.Notes, n => n.Code == "damage.response-unavailable");
        Assert.Empty(fall.Notes);
        Assert.Equal(30, fall.Reduction); // 5 * level 6
        Assert.Equal(10, fall.Outcome.Adjusted);
    }

    [Fact]
    public void Dice_response_confirm_needs_the_roll()
    {
        var (temp, id) = Sidestepper();
        using var _ = temp;
        var stamp = View(temp, id).Character.UpdatedAt;
        var before = Stored(temp, id);

        var refused = Refused(() => temp.App.Play(new(
            id, PlayActionKind.Damage, Confirm: true, Amount: 20, DamageType: "slashing", ExpectedUpdatedAt: stamp,
            Responses: [new(Catch, "catch-narrow")], DamageFrom: ResponseSource.Attack)));

        Assert.Equal("damage.response-roll", refused.Code);
        Assert.Equal(before, Stored(temp, id));

        var max = Max(temp, id);
        var view = temp.App.Play(new(
            id, PlayActionKind.Damage, Confirm: true, Amount: 20, DamageType: "slashing", ExpectedUpdatedAt: stamp,
            Responses: [new(Catch, "catch-narrow", Rolled: 6)], DamageFrom: ResponseSource.Attack));

        Assert.Equal(11, view.Damage!.Adjusted); // 20 less the roll 6 and the Dexterity modifier 3
        Assert.Equal(max - 11, view.Sheet.HitPoints!.Current);
    }

    [Fact]
    public void Superseding_feature_hides_the_narrow_response()
    {
        var (narrow, narrowId) = Sidestepper();
        var (plus, plusId) = Sidestepper(plus: true);
        using var _ = narrow;
        using var __ = plus;

        var before = narrow.App.PreviewDamage(new(narrowId, 20, "slashing", null, ResponseSource.Attack));
        var after = plus.App.PreviewDamage(new(plusId, 20, "slashing", null, ResponseSource.Attack));

        Assert.Contains(before.Offers, o => o.Response.ResponseId == "catch-narrow");
        Assert.DoesNotContain(before.Offers, o => o.Response.ResponseId == "catch-any");
        Assert.DoesNotContain(after.Offers, o => o.Response.ResponseId == "catch-narrow");
        Assert.Contains(after.Offers, o => o.Response.ContentId == CatchAny && o.Response.ResponseId == "catch-any");
        // The wider response also covers a type the narrow one does not.
        var fire = plus.App.PreviewDamage(new(plusId, 20, "fire", null, ResponseSource.Attack));
        Assert.Contains(fire.Offers, o => o.Response.ResponseId == "catch-any");
    }

    [Fact]
    public void Save_without_save_source_is_refused()
    {
        var (temp, id) = Sidestepper();
        using var _ = temp;
        var before = Stored(temp, id);

        var noSource = Refused(() => temp.App.PreviewDamage(new(id, 20, null, null, null, Dex(true))));
        var wrongSource = Refused(() => temp.App.PreviewDamage(new(id, 20, null, null, ResponseSource.Attack, Dex(true))));
        var unknownAbility = Refused(() => temp.App.PreviewDamage(new(id, 20, null, null, ResponseSource.Save, new("luck", true))));
        var played = Refused(() => temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 20, DamageSave: Dex(true))));
        var playedPlain = Refused(() => temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 20, DamageFrom: ResponseSource.Save, DamageSave: new("DEX!", false))));

        Assert.All([noSource, wrongSource, unknownAbility, played, playedPlain], e => Assert.Equal("damage.invalid-source", e.Code));
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void Skipped_notice_lists_only_matching_responses()
    {
        var (temp, id) = Sidestepper();
        using var _ = temp;

        var view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 20, DamageType: "slashing", DamageFrom: ResponseSource.Attack));

        var skipped = view.Notices!.Where(n => n.Code == "damage.responses-skipped").Select(n => n.EffectId).Order().ToList();
        Assert.Equal(["catch-narrow", "turn"], skipped); // the fall and the save responses are not for an attack
    }

    [Fact]
    public void Fixture_features_calculate_with_a_trace_and_are_Automatic()
    {
        var (temp, id) = Sidestepper(plus: true);
        using var _ = temp;
        var sheet = View(temp, id).Sheet;

        var fixtures = sheet.Features!.Where(f => f.Name.StartsWith("Fixture: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, fixtures.Count);
        Assert.Empty(fixtures.SelectMany(f => f.Diagnostics));
        Assert.All(fixtures, f => Assert.Equal(AutomationStatus.Automatic, f.Automation));
        Assert.Empty(sheet.Diagnostics ?? []);
        // Every surviving response is listed with its evaluated amount (the narrow catch is superseded).
        var responses = sheet.Responses!.ToDictionary(r => r.ResponseId);
        Assert.Equal(["catch-any", "duck-failure", "duck-success", "landing", "turn"], responses.Keys.Order());
        Assert.Equal(30, responses["landing"].Amount);
        Assert.Equal(3, responses["catch-any"].Amount);
        Assert.Equal("1d10", responses["catch-any"].Dice);
        Assert.True(responses["duck-success"].Automatic);

        var report = temp.App.Coverage(new(Source));
        var source = Assert.Single(report.Sources);
        Assert.Equal(new CoverageCounts(Automatic: 8, Assisted: 0, Reference: 0, Unsupported: 0), source.Totals); // two hit dice, six responses
    }
}
