namespace TomeStack.RulesCore.Tests;

/// <summary>
/// MECH-X item 4 (R60, R65): companion cards a <c>companion</c> effect declares. The attacks and pools a card lists stay in the sheet's
/// own lists, tagged with the companion id. Original fixtures: <c>fixture-pack-companions.json</c> (the three golden revisions and the
/// class "Fixture Keeper Path") and <c>characters/pm-handler-l3.json</c> (a level 3 character with the Warden Bond pinned and a stored
/// content row of 4 hit points), put in the Keeper Path class here.
/// </summary>
public class CompanionCalculationTests
{
    private static readonly ContentReference Keeper = new(Guid.Parse("6a7e00f4-0000-4000-8000-000000000010"), Guid.Parse("6a7e00f4-0000-4000-8000-000000000110"));
    private static readonly ContentReference WardenBond = new(Guid.Parse("6a7e00f4-0000-4000-8000-000000000001"), Guid.Parse("6a7e00f4-0000-4000-8000-000000000101"));
    private static readonly ContentReference HoundKeeper = new(Guid.Parse("6a7e00f4-0000-4000-8000-000000000002"), Guid.Parse("6a7e00f4-0000-4000-8000-000000000102"));
    private static readonly ContentReference SentinelBond = new(Guid.Parse("6a7e00f4-0000-4000-8000-000000000003"), Guid.Parse("6a7e00f4-0000-4000-8000-000000000103"));
    private static readonly Guid WardenId = Guid.Parse("6a7e0c00-0000-4000-8000-000000000001");
    private static readonly Guid HoundId = Guid.Parse("6a7e0c00-0000-4000-8000-000000000002");
    private static readonly Guid SentinelId = Guid.Parse("6a7e0c00-0000-4000-8000-000000000003");

    private static InMemoryContentCatalog Catalog(params ContentRevision[] extra)
    {
        var pack = Fixtures.CompanionsPack();
        return new(pack.Sources, [.. pack.Revisions, .. extra]);
    }

    private static Character Handler(int level = 3) =>
        Fixtures.Load("pm-handler-l3.json") with { Level = level, Classes = [new(Keeper, level)] };

    private static CharacterSheet Sheet(Character character, params ContentRevision[] extra) => CharacterCalculator.Calculate(character, Catalog(extra));

    private static int Count(CharacterSheet sheet, string code) => sheet.Diagnostics.Count(d => d.Code == code);

    private static ContentRevision Feature(string name, int n, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"6a7e00f4-0000-4000-8000-{0x200 + n:X12}"), RevisionId = Guid.Parse($"6a7e00f4-0000-4000-8000-{0x300 + n:X12}"),
        SchemaVersion = 12, Kind = ContentKind.Feature, Name = name, RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("6a7e0000-0000-4000-8000-000000000001")), Status = RevisionStatus.Published, Effects = effects,
    };

    private static CompanionEffect Companion(string id, int n, string armorClass = "12", string hitPoints = "5", AutomationStatus automation = AutomationStatus.Automatic,
        string[]? attacks = null, string[]? pools = null) => new()
    {
        Id = id, CompanionId = Guid.Parse($"6a7e0c00-0000-4000-8000-{0x100 + n:X12}"), Label = $"Fixture {id}", ArmorClass = armorClass, HitPointMaximum = hitPoints,
        Automation = automation, Attacks = attacks, ResourceIds = pools,
    };

    private static Character With(Character character, params ContentRevision[] features) =>
        character with { Pins = [.. character.Pins, .. features.Select(f => f.Reference)] };

    [Fact]
    public void A_companion_card_is_calculated()
    {
        var sheet = Sheet(Handler());

        var card = Assert.Single(sheet.Companions!, c => c.CompanionId == WardenId);
        Assert.Equal(("Fixture Warden", WardenBond, "warden", "Fixture Warden Bond"), (card.Label, card.Content, card.EffectId, card.ContentName));
        Assert.Equal((15, 15, 4, 0, "40 ft."), (card.ArmorClass, card.HitPointMaximum, card.Current, card.Temporary, card.Speed));
        Assert.Equal(["warden-bite"], card.AttackEffectIds);
        Assert.Equal(["warden-fury"], card.ResourceIds);
        Assert.NotEmpty(card.ArmorClassTrace);
        Assert.NotEmpty(card.HitPointTrace);
        Assert.Equal(15, card.ArmorClassTrace[^1].Result); // 13 + PB 2
        Assert.Equal(AutomationStatus.Automatic, card.Automation);
        Assert.Equal(WardenBond, card.Origin.Content);
        Assert.Equal("warden", card.Origin.EffectId);
        Assert.Empty(card.Warnings);
    }

    [Fact]
    public void A_character_without_companion_content_has_no_cards()
    {
        var sheet = Sheet(Fixtures.Load("pm-handler-l3.json") with { Pins = [], Classes = [], Level = 3 });

        Assert.Null(sheet.Companions);
    }

    [Fact]
    public void Its_attack_and_pool_are_tagged_not_moved()
    {
        var sheet = Sheet(Handler());

        var bite = Assert.Single(sheet.Attacks!, a => a.EffectId == "warden-bite");
        Assert.Equal(WardenId, bite.CompanionId);
        Assert.Equal("1d8+1d4+2", bite.Damage); // the level 3 Warden Growth rider is applied to the tagged row
        var rider = Assert.Single(bite.Riders!);
        Assert.Equal("warden-growth-rider", rider.EffectId);
        var pool = Assert.Single(sheet.Resources!, r => r.ResourceId == "warden-fury");
        Assert.Equal(WardenId, pool.CompanionId);
        Assert.Equal(2, pool.Maximum);
        var recovery = Assert.Single(pool.Recoveries);
        Assert.Equal((RestPeriod.LongRest, true), (recovery.On, recovery.All));
        Assert.All(sheet.Attacks!.Where(a => a.EffectId != "warden-bite"), a => Assert.Null(a.CompanionId));
        Assert.All(sheet.Resources!.Where(r => r.ResourceId != "warden-fury"), r => Assert.Null(r.CompanionId));
    }

    [Fact]
    public void Current_follows_the_maximum()
    {
        var noRow = Handler() with { Play = Handler().Play with { Companions = null } };
        Assert.Equal(15, Sheet(noRow).Companions!.Single().Current);

        var high = Handler() with { Play = Handler().Play with { Companions = [new(WardenId, 20, 0, WardenBond.ContentId)] } };
        Assert.Equal(15, Sheet(high).Companions!.Single().Current);

        var temporary = Handler() with { Play = Handler().Play with { Companions = [new(WardenId, 4, 3, WardenBond.ContentId)] } };
        Assert.Equal(3, Sheet(temporary).Companions!.Single().Temporary);

        var grown = Handler(5) with { Play = Handler().Play with { Companions = [new(WardenId, 4, 0, WardenBond.ContentId)] } };
        var card = Sheet(grown).Companions!.Single();
        Assert.Equal((25, 4, 16), (card.HitPointMaximum, card.Current, card.ArmorClass)); // PB 3 at level 5
    }

    [Fact]
    public void A_formula_that_fails_shows_no_number()
    {
        var featOnly = Fixtures.Load("pm-handler-l3.json") with
        {
            Classes = [], Level = 3, Pins = [SentinelBond],
            Play = Fixtures.Load("pm-handler-l3.json").Play with { Companions = [new(SentinelId, 5, 0, SentinelBond.ContentId)] },
        };

        var sheet = Sheet(featOnly);

        var card = Assert.Single(sheet.Companions!);
        Assert.Null(card.HitPointMaximum);
        Assert.Empty(card.HitPointTrace);
        Assert.Equal(0, card.Current);
        Assert.Equal(14, card.ArmorClass);
        Assert.Equal(AutomationStatus.Assisted, card.Automation);
        Assert.Equal(1, Count(sheet, "effect.invalid-formula"));
        Assert.Single(card.Warnings, w => w.Code == "effect.invalid-formula");
    }

    [Fact]
    public void Out_of_range_clamps_with_a_warning()
    {
        var wild = Feature("Fixture Wild Bond", 1, Companion("wild", 1, armorClass: "99", hitPoints: "-1"));

        var sheet = Sheet(With(Handler(), wild), wild);

        var card = Assert.Single(sheet.Companions!, c => c.EffectId == "wild");
        Assert.Equal((50, 0, 0), (card.ArmorClass, card.HitPointMaximum, card.Current));
        Assert.Equal(2, Count(sheet, "companion.out-of-range"));
        Assert.Contains(sheet.Diagnostics, d => d.Code == "companion.out-of-range" && d.Message.Contains("armor class"));
        Assert.Contains(sheet.Diagnostics, d => d.Code == "companion.out-of-range" && d.Message.Contains("hit point maximum"));
    }

    [Fact]
    public void A_missing_part_is_reported_once()
    {
        var bite = new AttackEffect
        {
            Id = "ref-bite", AttackId = "refBite", Label = "Reference bite", Kind = WeaponAttack.Melee, Abilities = [Ability.Str], Damage = new("1", "6"),
            DamageType = "piercing", Automation = AutomationStatus.Reference,
        };
        var odd = Feature("Fixture Odd Bond", 2, bite, Companion("odd", 2, attacks: ["ref-bite"]));

        var sheet = Sheet(With(Handler(), odd), odd);

        Assert.Equal(1, Count(sheet, "companion.missing-part"));
        Assert.DoesNotContain(sheet.Attacks!, a => a.EffectId == "ref-bite");
        Assert.NotNull(sheet.Companions!.Single(c => c.EffectId == "odd"));
    }

    [Fact]
    public void An_untracked_pool_is_a_missing_part_too()
    {
        var odd = Feature("Fixture Odd Bond", 2, Companion("odd", 2, pools: ["nowhere"]));

        var sheet = Sheet(With(Handler(), odd), odd);

        Assert.Equal(1, Count(sheet, "companion.missing-part"));
    }

    [Fact]
    public void Two_companions_in_one_revision_make_two_cards()
    {
        var pair = Feature("Fixture Pair Bond", 3, Companion("one", 1), Companion("two", 2));

        var sheet = Sheet(With(Handler(), pair), pair);

        Assert.Equal(2, sheet.Companions!.Count(c => c.Content == pair.Reference));
    }

    [Fact]
    public void A_reference_automation_companion_is_not_calculated()
    {
        var ref1 = Feature("Fixture Reference Bond", 4, Companion("by-hand", 1, automation: AutomationStatus.Reference));

        var sheet = Sheet(With(Handler(), ref1), ref1);

        Assert.DoesNotContain(sheet.Companions!, c => c.EffectId == "by-hand");
    }

    [Fact]
    public void Eleven_companions_cap_at_ten_with_a_warning()
    {
        var many = Feature("Fixture Many Bond", 5, [.. Enumerable.Range(1, 8).Select(i => (Effect)Companion($"m{i}", i))]); // plus the Warden and the Hound
        var more = Feature("Fixture More Bond", 6, Companion("m9", 9), Companion("m10", 10));

        var sheet = Sheet(With(Handler(), many, more) with { Pins = [WardenBond, HoundKeeper, many.Reference, more.Reference] }, many, more); // Warden, Hound, then twelve more

        Assert.Equal(10, sheet.Companions!.Count);
        Assert.Equal(1, Count(sheet, "companion.too-many"));
    }

    [Fact]
    public void A_revision_active_twice_makes_one_card()
    {
        var handler = Handler();
        Assert.Contains(WardenBond, handler.Pins); // pinned, and the Keeper Path grants it at level 1

        var sheet = Sheet(handler);

        Assert.Single(sheet.Companions!, c => c.CompanionId == WardenId);
        var bite = Assert.Single(sheet.Attacks!, a => a.EffectId == "warden-bite");
        Assert.Equal(WardenId, bite.CompanionId);
        Assert.Single(sheet.Resources!, r => r.ResourceId == "warden-fury");
    }

    [Fact]
    public void A_response_still_finds_a_tagged_pool()
    {
        var shield = Feature("Fixture Shield Bond", 7, new ResponseEffect
        {
            Id = "ward-off", ResponseId = "wardOff", Label = "Ward off", Phase = ResponsePhase.DamageReduction, Amount = "3",
            ResourceId = "warden-fury", ResourceContent = WardenBond.ContentId, Cost = "1",
        });

        var sheet = Sheet(With(Handler(), shield), shield);

        var response = Assert.Single(sheet.Responses!);
        Assert.True(response.Available);
        Assert.Equal(WardenId, sheet.Resources!.Single(r => r.ResourceId == "warden-fury").CompanionId);
    }

    [Fact]
    public void A_hand_companion_row_with_the_same_id_is_not_the_content_row()
    {
        var handler = Handler();
        var hand = handler with { Play = handler.Play with { Companions = [new(WardenId, 1, 0), new(WardenId, 9, 0, WardenBond.ContentId)] } };

        Assert.Equal(9, Sheet(hand).Companions!.Single(c => c.CompanionId == WardenId).Current);
    }
}
