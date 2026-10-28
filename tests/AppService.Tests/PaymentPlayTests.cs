using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// MECH-X item 1 (content v12), Task 3: paying hit points or a spell slot through <c>useAction</c> and toggles (one compound change,
/// undone by the service), the roll record naming the payment, and a roll that casts a spell without a slot. Original fixture:
/// "Fixture Payment Rite" and two sparks, on the invented Arcanist (level 5) and Oathbinder (pact magic) classes.
/// </summary>
public class PaymentPlayTests
{
    private const string RiteId = "6a7e0700-0000-4000-8000-000000000001";
    private const string RiteRevision = "6a7e0700-0000-4000-8000-000000000101";
    private const string ClashId = "6a7e0700-0000-4000-8000-000000000002";
    private const string ClashRevision = "6a7e0700-0000-4000-8000-000000000102";
    private const string SparkId = "6a7e0700-0000-4000-8000-0000000000a1";
    private const string SparkRevision = "6a7e0700-0000-4000-8000-0000000001a1";
    private const string QuietId = "6a7e0700-0000-4000-8000-0000000000a2";
    private const string QuietRevision = "6a7e0700-0000-4000-8000-0000000001a2";
    private const string SpellSource = "5f5d5000-0000-4000-8000-000000000001";

    private static readonly Guid Rite = Guid.Parse(RiteId);
    private static readonly ContentReference RiteRef = new(Rite, Guid.Parse(RiteRevision));
    private static readonly ContentReference ClashRef = new(Guid.Parse(ClashId), Guid.Parse(ClashRevision));
    private static readonly ContentReference SparkRef = new(Guid.Parse(SparkId), Guid.Parse(SparkRevision));

    private static ContentReference Ref(int n) => new(Guid.Parse($"5f5dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f5de000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Arcanist = Ref(1);
    private static readonly ContentReference Oathbinder = Ref(3);

    private const string RiteEffects =
        "{\"type\":\"resource\",\"id\":\"pool\",\"resourceId\":\"pool\",\"label\":\"Pool\",\"maximum\":\"2\"},"
        + "{\"type\":\"roll\",\"id\":\"hp\",\"rollId\":\"hp\",\"label\":\"Blood Rite\",\"dice\":\"1d6\",\"cost\":\"3\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"roll\",\"id\":\"hpz\",\"rollId\":\"hpz\",\"label\":\"Last Breath\",\"dice\":\"1d6\",\"cost\":\"3\",\"payment\":{\"kind\":\"hitPoints\",\"mayDropToZero\":true}},"
        + "{\"type\":\"roll\",\"id\":\"hpu\",\"rollId\":\"hpu\",\"label\":\"Surge\",\"dice\":\"1d4\",\"cost\":\"1\",\"perUnit\":{\"dice\":\"1d4\",\"maxUnits\":4},\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"roll\",\"id\":\"hp0\",\"rollId\":\"hp0\",\"label\":\"Free Rite\",\"dice\":\"1d6\",\"cost\":\"0\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"roll\",\"id\":\"slotr\",\"rollId\":\"slotr\",\"label\":\"Slot Rite\",\"dice\":\"1d6\",\"payment\":{\"kind\":\"slot\"}},"
        + "{\"type\":\"roll\",\"id\":\"slot2\",\"rollId\":\"slot2\",\"label\":\"Higher Rite\",\"dice\":\"1d6\",\"payment\":{\"kind\":\"slot\",\"minSlotLevel\":2}},"
        + "{\"type\":\"roll\",\"id\":\"plain\",\"rollId\":\"plain\",\"label\":\"Plain\",\"dice\":\"1d6\",\"resourceId\":\"pool\",\"cost\":\"2\"},"
        + "{\"type\":\"roll\",\"id\":\"cast\",\"rollId\":\"cast\",\"label\":\"Cast Spark\",\"castsSpell\":\"" + SparkId + "\"},"
        + "{\"type\":\"roll\",\"id\":\"castq\",\"rollId\":\"castq\",\"label\":\"Cast Quiet\",\"castsSpell\":\"" + QuietId + "\"},"
        + "{\"type\":\"toggle\",\"id\":\"pain\",\"toggleId\":\"pain\",\"label\":\"Pained\",\"cost\":\"3\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"toggle\",\"id\":\"free\",\"toggleId\":\"free\",\"label\":\"Freed\",\"cost\":\"0\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"toggle\",\"id\":\"ward\",\"toggleId\":\"ward\",\"label\":\"Warded\",\"payment\":{\"kind\":\"slot\",\"minSlotLevel\":2}},"
        + "{\"type\":\"toggleGroup\",\"id\":\"forms-group\",\"groupId\":\"forms\",\"label\":\"Forms\"},"
        + "{\"type\":\"toggle\",\"id\":\"formA\",\"toggleId\":\"formA\",\"label\":\"Form A\",\"group\":\"forms\",\"groupContent\":\"" + RiteId + "\"},"
        + "{\"type\":\"toggle\",\"id\":\"formB\",\"toggleId\":\"formB\",\"label\":\"Form B\",\"group\":\"forms\",\"groupContent\":\"" + RiteId + "\"},"
        + "{\"type\":\"toggle\",\"id\":\"brace\",\"toggleId\":\"brace\",\"label\":\"Brace\",\"group\":\"forms\",\"groupContent\":\"" + RiteId + "\",\"layer\":\"overlay\",\"allowedInBase\":true,"
        + "\"allowedWith\":[{\"contentId\":\"" + RiteId + "\",\"toggleId\":\"formA\"},{\"contentId\":\"" + RiteId + "\",\"toggleId\":\"formB\"}],\"cost\":\"3\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"attack\",\"id\":\"bolt\",\"attackId\":\"bolt\",\"label\":\"Bolt\",\"kind\":\"ranged\",\"abilities\":[\"int\"],\"damage\":{\"count\":\"1\",\"die\":\"6\"},\"damageType\":\"fire\",\"cost\":\"2\",\"payment\":{\"kind\":\"hitPoints\"}}";

    // A roll named like an attack of the same revision (ambiguous), and an attack that pays nothing (not usable through useAction).
    private const string ClashEffects =
        "{\"type\":\"roll\",\"id\":\"r1\",\"rollId\":\"bolt\",\"label\":\"Clash Roll\",\"dice\":\"1d6\",\"cost\":\"2\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"attack\",\"id\":\"bolt\",\"attackId\":\"bolt\",\"label\":\"Clash Bolt\",\"kind\":\"ranged\",\"abilities\":[\"int\"],\"damage\":{\"count\":\"1\",\"die\":\"6\"},\"damageType\":\"fire\",\"cost\":\"2\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"attack\",\"id\":\"plainbolt\",\"attackId\":\"plainbolt\",\"label\":\"Plain Bolt\",\"kind\":\"ranged\",\"abilities\":[\"int\"],\"damage\":{\"count\":\"1\",\"die\":\"6\"},\"damageType\":\"fire\"},"
        + "{\"type\":\"roll\",\"id\":\"r2\",\"rollId\":\"shadow\",\"label\":\"Shadow Roll\",\"dice\":\"1d6\",\"cost\":\"1\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"attack\",\"id\":\"shadow\",\"attackId\":\"shadow\",\"label\":\"Shadow Bolt\",\"kind\":\"ranged\",\"abilities\":[\"int\"],\"damage\":{\"count\":\"1\",\"die\":\"6\"},\"damageType\":\"fire\"}";

    private static ContentRevision Revision(string contentId, string revisionId, string kind, string name, string effects) =>
        JsonSerializer.Deserialize<ContentRevision>(
            "{\"contentId\":\"" + contentId + "\",\"revisionId\":\"" + revisionId + "\",\"schemaVersion\":12,\"kind\":\"" + kind + "\",\"name\":\"" + name
            + "\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"" + SpellSource + "\",\"page\":{\"start\":1}},\"status\":\"published\",\"effects\":[" + effects + "]}",
            RulesJson.Compact)!;

    private static string SparkEffect(string dice) =>
        "{\"type\":\"spell\",\"id\":\"s\",\"level\":2,\"lists\":[\"fixture-arcane\"],\"attack\":\"none\",\"save\":\"dex\"" + (dice.Length == 0 ? "" : ",\"dice\":\"" + dice + "\"") + "}";

    /// <summary>The Rite, a clashing content and the sparks, installed; a level 5 Arcanist (plus Oathbinder levels) with the given play state. Stamps move on every write.</summary>
    private static (TempApp Temp, Guid Id) Caster(PlayState? play = null, int oathbinderLevels = 0, bool clash = true)
    {
        var temp = new TempApp(time: new TickingTime(TempApp.Now));
        temp.App.Store.AddRevision(Revision(RiteId, RiteRevision, "feature", "Fixture Payment Rite", RiteEffects));
        temp.App.Store.AddRevision(Revision(ClashId, ClashRevision, "feature", "Fixture Rite Clash", ClashEffects));
        temp.App.Store.AddRevision(Revision(SparkId, SparkRevision, "spell", "Fixture Rite Spark", SparkEffect("2d6")));
        temp.App.Store.AddRevision(Revision(QuietId, QuietRevision, "spell", "Fixture Quiet Spark", SparkEffect("")));
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Test Rite Caster",
            RulesFamily = RulesFamilies.Srd521,
            Level = 5 + oathbinderLevels,
            Classes = oathbinderLevels == 0 ? [new(Arcanist, 5)] : [new(Arcanist, 5), new(Oathbinder, oathbinderLevels)],
            BaseAbilities = new(10, 12, 14, 16, 10, 14),
            Pins = clash ? [RiteRef, ClashRef] : [RiteRef], // the clashing content is invalid on purpose: a package of it is refused
            Play = play ?? new PlayState(),
        };
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static DateTimeOffset Stamp(TempApp temp, Guid id) => temp.App.GetCharacter(id).Character.UpdatedAt;

    private static string Stored(TempApp temp, Guid id) => TempApp.Json(temp.App.Store.FindCharacter(id));

    private static CharacterView Use(TempApp temp, Guid id, string rollId, int amount = 1, Guid? content = null) =>
        temp.App.Play(new(id, PlayActionKind.UseAction, Confirm: true, Amount: amount, ContentId: content ?? Rite, RollId: rollId, ExpectedUpdatedAt: Stamp(temp, id)));

    private static CharacterView Toggle(TempApp temp, Guid id, string toggleId, bool replace = false, DateTimeOffset? stamp = null) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Rite, ToggleId: toggleId, Replace: replace, ExpectedUpdatedAt: stamp));

    private static CharacterView Undo(TempApp temp, Guid id) => temp.App.UndoPlay(new(id, Stamp(temp, id), Confirm: true));

    private static AppValidationException Refused(Action act) => Assert.Throws<AppValidationException>(act);

    [Fact]
    public void Paying_hit_points_through_use()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12, TemporaryHitPoints = 4 });
        using var _ = temp;

        var view = Use(temp, id, "hp");

        Assert.Equal((9, 4), (view.Sheet.HitPoints!.Current, view.Sheet.HitPoints.Temporary));
        Assert.Equal("use Blood Rite", view.UndoLabel);
        var undone = Undo(temp, id);
        Assert.Equal((12, 4), (undone.Sheet.HitPoints!.Current, undone.Sheet.HitPoints.Temporary));
    }

    [Theory]
    [InlineData(4, "hp", null, false)]
    [InlineData(3, "hp", "pay.hit-points-too-low", false)]
    [InlineData(2, "hp", "pay.hit-points-too-low", false)]
    [InlineData(3, "hpz", null, true)]
    [InlineData(2, "hpz", "pay.hit-points-too-low", false)]
    public void Hit_point_edges(int current, string rollId, string? code, bool endsConcentration)
    {
        var concentration = new Concentration(SparkRef, "Fixture Rite Spark");
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = current, TemporaryHitPoints = 5, Concentration = concentration });
        using var _ = temp;
        var before = Stored(temp, id);

        if (code is not null)
        {
            var refused = Refused(() => Use(temp, id, rollId));
            Assert.Equal(code, refused.Code);
            Assert.Equal(code, refused.Problems[0].Code);
            Assert.Equal(before, Stored(temp, id));
            Assert.Null(temp.App.GetCharacter(id).UndoLabel);
            return;
        }

        var view = Use(temp, id, rollId);

        Assert.Equal(current - 3, view.Sheet.HitPoints!.Current);
        Assert.Equal(5, view.Sheet.HitPoints.Temporary); // temporary hit points are never paid from
        Assert.Equal(endsConcentration, temp.App.Store.FindCharacter(id)!.Play.Concentration is null);
    }

    [Fact]
    public void A_hit_point_cost_of_zero_shows_payable_but_the_use_is_refused()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12 });
        using var _ = temp;
        var row = temp.App.GetCharacter(id).Sheet.Features!.SelectMany(f => f.Effects).Single(e => e.Id == "hp0");
        var before = Stored(temp, id);

        Assert.True(row.Payment!.Payable);
        Assert.Equal("use.unavailable", Refused(() => Use(temp, id, "hp0")).Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void Per_unit_hit_points()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12 });
        using var _ = temp;

        Assert.Equal("roll.amount-out-of-range", Refused(() => Use(temp, id, "hpu", amount: 0)).Code); // below the base cost
        Assert.Equal("roll.amount-out-of-range", Refused(() => Use(temp, id, "hpu", amount: 6)).Code); // base 1 + at most 4 units
        var view = Use(temp, id, "hpu", amount: 3);

        Assert.Equal(9, view.Sheet.HitPoints!.Current);
        Assert.Equal("use Surge", view.UndoLabel);
    }

    [Fact]
    public void Slot_payment_takes_the_picked_slot()
    {
        // Arcanist 5 and Oathbinder 2 (two pact slots of level 1). Spend every ordinary level-1 slot, leaving higher ordinary slots and the pact slots.
        var (temp, id) = Caster(oathbinderLevels: 2);
        using var _ = temp;
        var fresh = temp.App.GetCharacter(id);
        Assert.Equal(1, fresh.Sheet.PactSlots!.Level);
        Assert.Contains(fresh.Sheet.SpellSlots!, s => s.Level == 2 && s.Remaining > 0);
        // SaveCharacter keeps the stored play state, so the test writes the drained one to the store directly.
        temp.App.Store.SaveCharacter(fresh.Character with { Play = fresh.Character.Play.WithSlotsSpent(1, fresh.Sheet.SpellSlots!.Single(s => s.Level == 1).Maximum) });

        // minSlotLevel 1: no ordinary level-1 slot is left, so the pact slot (level 1) is the lowest.
        var viaPact = Use(temp, id, "slotr");
        Assert.Equal(1, temp.App.Store.FindCharacter(id)!.Play.PactSlotsSpent);
        Assert.Equal(0, temp.App.Store.FindCharacter(id)!.Play.SlotsSpentOf(2));
        Assert.Equal("use Slot Rite", viaPact.UndoLabel);
        Undo(temp, id);
        Assert.Equal(0, temp.App.Store.FindCharacter(id)!.Play.PactSlotsSpent);

        // minSlotLevel 2: the pact slots are level 1, so the ordinary level-2 slot pays.
        Use(temp, id, "slot2");
        var play = temp.App.Store.FindCharacter(id)!.Play;
        Assert.Equal((0, 1), (play.PactSlotsSpent, play.SlotsSpentOf(2)));
        Undo(temp, id);
        Assert.Equal(0, temp.App.Store.FindCharacter(id)!.Play.SlotsSpentOf(2));

        // Nothing at level 2 or above left: refused, the row is not payable, nothing is written.
        var all = temp.App.GetCharacter(id);
        var spentPlay = all.Sheet.SpellSlots!.Where(s => s.Level >= 2).Aggregate(all.Character.Play, (p, slot) => p.WithSlotsSpent(slot.Level, slot.Maximum));
        temp.App.Store.SaveCharacter(all.Character with { Play = spentPlay });
        var row = temp.App.GetCharacter(id).Sheet.Features!.SelectMany(f => f.Effects).Single(e => e.Id == "slot2");
        var before = Stored(temp, id);
        Assert.False(row.Payment!.Payable);
        Assert.Equal("slots.none-left", Refused(() => Use(temp, id, "slot2")).Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void A_paying_feature_attack_is_used_by_its_effect_id()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12 });
        using var _ = temp;

        var view = Use(temp, id, "bolt");

        Assert.Equal(10, view.Sheet.HitPoints!.Current);
        Assert.Equal("use Bolt", view.UndoLabel);
        Assert.Equal(12, Undo(temp, id).Sheet.HitPoints!.Current);
    }

    [Fact]
    public void A_revision_with_a_roll_and_an_attack_of_the_same_name_is_ambiguous_and_an_attack_without_payment_is_not_usable()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12 });
        using var _ = temp;
        var before = Stored(temp, id);

        Assert.Equal("use.not-found", Refused(() => Use(temp, id, "bolt", content: ClashRef.ContentId)).Code);
        Assert.Equal("use.unavailable", Refused(() => Use(temp, id, "plainbolt", content: ClashRef.ContentId)).Code);
        Assert.Equal("use.not-found", Refused(() => Use(temp, id, "nothing")).Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void An_attack_without_payment_never_shadows_a_roll_of_the_same_id()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12 });
        using var _ = temp;

        var view = Use(temp, id, "shadow", content: ClashRef.ContentId);

        Assert.Equal(11, view.Sheet.HitPoints!.Current);
        Assert.Equal("use Shadow Roll", view.UndoLabel);
    }

    [Fact]
    public void A_paying_toggle_is_compound()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12 });
        using var _ = temp;
        var old = Stamp(temp, id);
        var before = Stored(temp, id);

        Assert.Equal("play.basis-required", Refused(() => Toggle(temp, id, "pain")).Code);
        Assert.Equal(before, Stored(temp, id));

        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1)); // moves the stamp
        var moved = Stored(temp, id);
        Assert.Equal("character.stale", Refused(() => Toggle(temp, id, "pain", stamp: old)).Code);
        Assert.Equal(moved, Stored(temp, id));

        var view = Toggle(temp, id, "pain", stamp: Stamp(temp, id));
        Assert.Equal(8, view.Sheet.HitPoints!.Current); // 12 - 1 damage - 3
        Assert.True(view.Sheet.Toggles!.Single(t => t.ToggleId == "pain").On);
        Assert.Equal("switch on Pained", view.UndoLabel);

        var undone = Undo(temp, id);
        Assert.Equal(11, undone.Sheet.HitPoints!.Current);
        Assert.False(undone.Sheet.Toggles!.Single(t => t.ToggleId == "pain").On);
    }

    [Fact]
    public void A_paying_toggle_is_refused_when_it_cannot_be_paid_and_writes_nothing()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 3 });
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Toggle(temp, id, "pain", stamp: Stamp(temp, id)));

        Assert.Equal("pay.hit-points-too-low", refused.Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void Roll_records_name_the_payment()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12 });
        using var _ = temp;
        temp.App.Random = new SeededRandomSource(7);

        var hp = temp.App.Roll(new RollCommand(id, RiteRef, "hp"));
        Assert.Equal(new RollPayment(Rite, "hp", PaymentKind.HitPoints, 3, null, false), hp.Payment);
        Assert.Null(hp.Spend);
        Assert.Null(hp.Provenance!.LinkedResourceId);

        Assert.Equal(new RollPayment(Rite, "hpz", PaymentKind.HitPoints, 3, null, true), temp.App.Roll(new RollCommand(id, RiteRef, "hpz")).Payment);
        Assert.Equal(new RollPayment(Rite, "hpu", PaymentKind.HitPoints, 3, null, false), temp.App.Roll(new RollCommand(id, RiteRef, "hpu", Amount: 3)).Payment);
        Assert.Equal(new RollPayment(Rite, "slot2", PaymentKind.Slot, null, 2, false), temp.App.Roll(new RollCommand(id, RiteRef, "slot2")).Payment);

        // A paying attack names its payment on the to-hit record only (B4), and links no pool.
        var toHit = temp.App.Roll(new RollCommand(id, RiteRef, "bolt"));
        Assert.Equal(new RollPayment(Rite, "bolt", PaymentKind.HitPoints, 2, null, false), toHit.Payment);
        Assert.Null(toHit.Spend);
        Assert.Null(toHit.Provenance!.LinkedResourceId);
        Assert.Null(temp.App.Roll(new RollCommand(id, RiteRef, "bolt", Damage: true)).Payment);

        // A pool roll is unchanged: it names its spend, not a payment.
        var plain = temp.App.Roll(new RollCommand(id, RiteRef, "plain"));
        Assert.Null(plain.Payment);
        Assert.Equal(new RollSpend(Rite, "pool", 2), plain.Spend);
    }

    [Fact]
    public void An_on_paying_toggle_survives_a_form_switch_at_low_hit_points()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 2, Toggles = [new ActiveToggle(Rite, "formA"), new ActiveToggle(Rite, "brace")] });
        using var _ = temp;
        var row = temp.App.GetCharacter(id).Sheet.Toggles!.Single(t => t.ToggleId == "brace");
        Assert.True(row is { On: true, Available: true });
        Assert.False(row.Payment!.Payable);

        var view = Toggle(temp, id, "formB", replace: true, stamp: Stamp(temp, id));

        var on = view.Sheet.Toggles!.Where(t => t.On).Select(t => t.ToggleId).Order().ToArray();
        Assert.Equal(["brace", "formB"], on);
        Assert.Equal(2, view.Sheet.HitPoints!.Current);
    }

    [Fact]
    public void A_paying_toggle_with_cost_zero_turns_on_free()
    {
        var (temp, id) = Caster(new PlayState { CurrentHitPoints = 12 });
        using var _ = temp;

        var view = Toggle(temp, id, "free", stamp: Stamp(temp, id));

        Assert.True(view.Sheet.Toggles!.Single(t => t.ToggleId == "free").On);
        Assert.Equal(12, view.Sheet.HitPoints!.Current);
        Assert.Equal("switch on Freed", view.UndoLabel);
    }

    [Fact]
    public void A_slot_toggle_pays_the_picked_slot_and_undo_gives_it_back()
    {
        var (temp, id) = Caster();
        using var _ = temp;

        var view = Toggle(temp, id, "ward", stamp: Stamp(temp, id));

        Assert.Equal(1, temp.App.Store.FindCharacter(id)!.Play.SlotsSpentOf(2));
        Assert.True(view.Sheet.Toggles!.Single(t => t.ToggleId == "ward").On);
        Undo(temp, id);
        Assert.Equal(0, temp.App.Store.FindCharacter(id)!.Play.SlotsSpentOf(2));
    }

    [Fact]
    public void A_cast_spell_roll_rolls_the_spell_dice()
    {
        var (temp, id) = Caster();
        using var _ = temp;
        temp.App.Random = new SeededRandomSource(11);

        var record = temp.App.Roll(new RollCommand(id, RiteRef, "cast"));

        Assert.Equal("2d6", record.Formula);
        Assert.Equal(2, record.Dice.Count);
        Assert.Contains("Fixture Rite Spark", record.Provenance!.Label, StringComparison.Ordinal);
        Assert.Equal(("cast", RiteRef), (record.Provenance.RollId, record.Provenance.Content));
        Assert.Null(record.Spend);
        Assert.Null(record.Payment); // no slot is spent and the roll pays nothing of its own

        // A spell with no dice leaves today's refusal.
        Assert.Equal("roll.no-dice", Refused(() => temp.App.Roll(new RollCommand(id, RiteRef, "castq"))).Code);
    }

    [Fact]
    public void Casts_spell_is_a_reference()
    {
        var (temp, id) = Caster(clash: false);
        using var _ = temp;
        // A character package carries the spell revision, though the character never pins it (S8).
        var export = temp.App.ExportCharacters([id]);
        Assert.Contains(export.Manifest.Entries, e => e.Path == $"content/{SparkRef.RevisionId:D}.json");
        using var destination = new TempApp();
        destination.App.ApplyImport(export.Content);
        Assert.NotNull(destination.App.Store.FindRevision(SparkRef));
    }
}
