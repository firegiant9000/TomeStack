using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content v12 G4: a modifier may apply only while no armor, heavy armor or shield is worn. "Worn" is what the calculator already
/// counts as armor and shield (automatic, always-on armor effects of active content). Every check compares the value with the gear
/// worn and not worn.
/// </summary>
public class NotWearingV12SheetTests
{
    // The JSON values of notWearing (the constants on ModifierEffect are internal).
    private const string Armor = "armor", HeavyArmor = "heavyArmor", Shield = "shield";

    private static readonly string[] Both =[RulesFamilies.Srd51, RulesFamilies.Srd521];

    private static ContentReference Ref(int n) => new(Guid.Parse($"5fadd000-0000-4000-8000-{n:D12}"), Guid.Parse($"5fadf000-0000-4000-8000-{n:D12}"));

    private static ContentRevision Revision(int n, ContentKind kind, string name, int schema, params Effect[] effects) => new()
    {
        ContentId = Ref(n).ContentId,
        RevisionId = Ref(n).RevisionId,
        SchemaVersion = schema,
        Kind = kind,
        Name = name,
        RulesFamilies = Both,
        Provenance = new(Fixtures.SourceShared, new(1)),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static GrantEffect Training(string key) => new() { Id = $"armor-{key}", Grant = GrantKind.Proficiency, Target = $"armor.{key}" };

    private static readonly int V8 = ContentRevision.CombatDetailsSchemaVersion;
    private static readonly int V12 = ContentRevision.AbilityCapSchemaVersion;

    private static readonly ContentRevision Trained = Revision(1, ContentKind.Class, "Test Gate Trained", V8,
        new HitDieEffect { Id = "hit-die", Die = 8 }, Training("light"), Training("medium"), Training("heavy"), Training("shield"));

    private static readonly ContentRevision LightOnly = Revision(2, ContentKind.Class, "Test Gate Light Only", V8,
        new HitDieEffect { Id = "hit-die", Die = 8 }, Training("light"));

    private static readonly ContentRevision Hauberk = Revision(31, ContentKind.Item, "Test Gate Hauberk", V8,
        new ArmorEffect { Id = "armor", Category = ArmorCategory.Heavy, ArmorClass = 16 });

    private static readonly ContentRevision Brigandine = Revision(32, ContentKind.Item, "Test Gate Brigandine", V8,
        new ArmorEffect { Id = "armor", Category = ArmorCategory.Medium, ArmorClass = 13 });

    private static readonly ContentRevision Coat = Revision(33, ContentKind.Item, "Test Gate Coat", V8,
        new ArmorEffect { Id = "armor", Category = ArmorCategory.Light, ArmorClass = 11 });

    private static readonly ContentRevision Buckler = Revision(34, ContentKind.Item, "Test Gate Buckler", V8,
        new ArmorEffect { Id = "shield", Category = ArmorCategory.Shield, ArmorClass = 2 });

    private static ModifierEffect Speed(string[]? notWearing, bool? whileArmored = null) =>
        new() { Id = "swift", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10", NotWearing = notWearing, WhileArmored = whileArmored };

    private static ContentRevision Gate(int schema, params Effect[] effects) => Revision(50, ContentKind.Feat, "Test Gate Feat", schema, effects);

    private static EquipmentEntry Worn(ContentRevision item) => new(item.Reference, Equipped: true);

    // Dex 14 (+2), Wis 16 (+3).
    private static CharacterSheet Calc(string family, ContentRevision? gate, ContentRevision[] gear, ContentRevision? cls = null, PlayState? play = null)
    {
        var m1 = Fixtures.M1Pack();
        ContentRevision[] extra = [Trained, LightOnly, Hauberk, Brigandine, Coat, Buckler, .. (gate is null ? [] : new[] { gate })];
        var catalog = new InMemoryContentCatalog(
            [.. Fixtures.Pack().Sources, .. m1.Sources],
            [.. Fixtures.Pack().Revisions, .. m1.Revisions, .. extra]);
        var character = Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with
        {
            Pins = gate is null ? [] : [gate.Reference],
            Classes = [new ClassLevel((cls ?? Trained).Reference, 1)],
            Level = 1,
            BaseAbilities = new(12, 14, 14, 10, 16, 10),
            Equipment = [.. gear.Select(Worn)],
            Spells = [],
            Play = play ?? Fixtures.Load("srd521-ash-m1.json").Play,
        };
        return CharacterCalculator.Calculate(character, catalog);
    }

    private static int SpeedGain(ContentRevision gate, ContentRevision[] gear, string family = RulesFamilies.Srd521) =>
        Calc(family, gate, gear).Field(FieldIds.Speed).Value - Calc(family, null, gear).Field(FieldIds.Speed).Value;

    [Fact]
    public void Shield_gate_turns_off_an_ac_replacement_while_a_shield_is_used()
    {
        var gate = Gate(V12, new ModifierEffect { Id = "bare", Operation = ModifierOperation.Replace, Target = FieldIds.ArmorClass, Value = "10 + DEX.MOD + WIS.MOD", NotWearing = [Shield] });

        var none = Calc(RulesFamilies.Srd521, gate, []).Field(FieldIds.ArmorClass);
        var plain = Calc(RulesFamilies.Srd521, null, []).Field(FieldIds.ArmorClass);
        var shielded = Calc(RulesFamilies.Srd521, gate, [Buckler]).Field(FieldIds.ArmorClass);
        var shieldedPlain = Calc(RulesFamilies.Srd521, null, [Buckler]).Field(FieldIds.ArmorClass);

        Assert.Equal(plain.Value + 3, none.Value); // 10 + Dex + Wis applies with nothing in hand
        Assert.Equal(shieldedPlain.Value, shielded.Value); // base 10 + Dex + the shield's 2
        Assert.Equal(plain.Value + 2, shielded.Value);
        Assert.Contains(shielded.Trace, t => t.Operation == "ignored" && t.Description.Contains("it applies only while no shield is used, and ", StringComparison.Ordinal) && t.Description.Contains("Test Gate Buckler", StringComparison.Ordinal));
        Assert.DoesNotContain(none.Trace, t => t.Operation == "ignored");
    }

    [Fact]
    public void Armor_and_shield_gate_turns_off_a_speed_bonus()
    {
        var gate = Gate(V12, Speed([Armor, Shield]));

        Assert.Equal(10, SpeedGain(gate, []));
        Assert.Equal(0, SpeedGain(gate, [Coat]));
        Assert.Equal(0, SpeedGain(gate, [Buckler]));
        var withArmor = Calc(RulesFamilies.Srd521, gate, [Coat]).Field(FieldIds.Speed);
        var withShield = Calc(RulesFamilies.Srd521, gate, [Buckler]).Field(FieldIds.Speed);
        Assert.Contains(withArmor.Trace, t => t.Operation == "ignored" && t.Description.Contains("it applies only while no armor is worn, and ", StringComparison.Ordinal) && t.Description.Contains("Test Gate Coat", StringComparison.Ordinal));
        Assert.Contains(withShield.Trace, t => t.Operation == "ignored" && t.Description.Contains("it applies only while no shield is used, and ", StringComparison.Ordinal) && t.Description.Contains("Test Gate Buckler", StringComparison.Ordinal));
    }

    [Fact]
    public void Heavy_armor_gate_allows_light_and_medium()
    {
        var gate = Gate(V12, Speed([HeavyArmor]));

        Assert.Equal(10, SpeedGain(gate, [Coat]));
        Assert.Equal(10, SpeedGain(gate, [Brigandine]));
        Assert.Equal(10, SpeedGain(gate, [Buckler]));
        Assert.Equal(0, SpeedGain(gate, [Hauberk]));
        Assert.Contains(Calc(RulesFamilies.Srd521, gate, [Hauberk]).Field(FieldIds.Speed).Trace,
            t => t.Operation == "ignored" && t.Description.Contains("it applies only while no heavy armor is worn, and ", StringComparison.Ordinal) && t.Description.Contains("Test Gate Hauberk", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Untrained_shield_still_counts_as_used(string family)
    {
        // The two families differ on whether an untrained shield adds Armor Class; neither makes it unused.
        var gate = Gate(V12, Speed([Shield]));

        Assert.Equal(10, Calc(family, gate, [Coat], LightOnly).Field(FieldIds.Speed).Value - Calc(family, null, [Coat], LightOnly).Field(FieldIds.Speed).Value);
        Assert.Equal(0, Calc(family, gate, [Coat, Buckler], LightOnly).Field(FieldIds.Speed).Value - Calc(family, null, [Coat, Buckler], LightOnly).Field(FieldIds.Speed).Value);
    }

    [Fact]
    public void Untrained_shield_adds_no_armor_class_under_one_family_yet_still_gates()
    {
        var gate = Gate(V12, new ModifierEffect { Id = "bare", Operation = ModifierOperation.Replace, Target = FieldIds.ArmorClass, Value = "10 + DEX.MOD + WIS.MOD", NotWearing = [Shield] });
        var untrained = Calc(RulesFamilies.Srd521, gate, [Buckler], LightOnly).Field(FieldIds.ArmorClass);
        var untrainedPlain = Calc(RulesFamilies.Srd521, null, [Buckler], LightOnly).Field(FieldIds.ArmorClass);
        var bare = Calc(RulesFamilies.Srd521, null, [], LightOnly).Field(FieldIds.ArmorClass);

        Assert.Equal(bare.Value, untrainedPlain.Value); // the shield adds nothing here
        Assert.Equal(untrainedPlain.Value, untrained.Value); // and the replacement still does not apply
        Assert.NotEqual(bare.Value + 3, untrained.Value);
    }

    [Fact]
    public void While_armored_and_heavy_armor_gate_applies_only_in_light_or_medium()
    {
        var gate = Gate(V12, Speed([HeavyArmor], whileArmored: true));

        Assert.Equal(0, SpeedGain(gate, [])); // not armored
        Assert.Equal(10, SpeedGain(gate, [Coat]));
        Assert.Equal(10, SpeedGain(gate, [Brigandine]));
        Assert.Equal(0, SpeedGain(gate, [Hauberk])); // armored but heavy
    }

    [Fact]
    public void Toggled_gated_bonus_applies_only_while_on_and_unarmed()
    {
        var gate = Gate(V12,
            new ToggleEffect { Id = "t", ToggleId = "dash", Label = "Dash" },
            Speed([Armor]) with { Toggle = "dash", Timing = EffectTiming.WhileActive });
        var on = new PlayState { Toggles = [new ActiveToggle(gate.ContentId, "dash")] };

        int Gain(ContentRevision[] gear, PlayState? play) =>
            Calc(RulesFamilies.Srd521, gate, gear, null, play).Field(FieldIds.Speed).Value - Calc(RulesFamilies.Srd521, null, gear, null, play).Field(FieldIds.Speed).Value;

        Assert.Equal(0, Gain([], null));
        Assert.Equal(10, Gain([], on));
        Assert.Equal(0, Gain([Coat], on));
    }

    [Fact]
    public void V11_revision_with_gate_applies_whatever_is_worn()
    {
        // Below schema 12 the gate is extension data the calculator must not honour.
        var gate = Gate(V12 - 1, Speed([Armor, Shield]));

        Assert.Equal(10, SpeedGain(gate, [Coat, Buckler]));
        Assert.Equal(10, SpeedGain(gate, [Hauberk]));
    }

    [Fact]
    public void Unknown_gear_value_is_ignored_in_an_unvalidated_draft()
    {
        var mixed = Gate(V12, Speed(["cloak", Shield]));
        var unknown = Gate(V12, Speed(["cloak"]));

        Assert.Equal(10, SpeedGain(mixed, []));
        Assert.Equal(0, SpeedGain(mixed, [Buckler]));
        Assert.Equal(10, SpeedGain(unknown, [Buckler, Hauberk]));
        Assert.Equal(10, SpeedGain(unknown, []));
    }

    [Fact]
    public void Item_mod_leaf_gate_is_ignored()
    {
        // An unvalidated item mod whose nested modifier carries a gate: the calculator ignores it (validation refuses it).
        var longblade = new ContentReference(Guid.Parse("6a7e00f3-0000-4000-8000-000000000011"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000111"));
        var mod = Revision(60, ContentKind.Item, "Fixture Gate Rune", V12,
            new ItemModEffect { Id = "im", AppliesTo = ModTarget.Weapon, Modifiers = [Speed([Shield])] });
        var packs = new[] { Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.ItemModsPack() };
        var catalog = new InMemoryContentCatalog([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions), mod, Buckler]);
        var tinker = Fixtures.Load("pm-tinker-l3.json");

        CharacterSheet Sheet(params EquipmentEntry[] extra) => CharacterCalculator.Calculate(tinker with
        {
            Equipment = [new EquipmentEntry(longblade, Equipped: true, Id: Guid.NewGuid(), Mods: [mod.Reference]), .. extra],
        }, catalog);

        var plain = Sheet().Field(FieldIds.Speed);
        var shielded = Sheet(Worn(Buckler)).Field(FieldIds.Speed);

        Assert.Equal(plain.Value, shielded.Value);
        Assert.DoesNotContain(shielded.Trace, t => t.Operation == "ignored" && t.Description.Contains("no shield is used", StringComparison.Ordinal));
        Assert.Contains(shielded.Trace, t => t.Origin.Content == mod.Reference && t.Amount == 10);
    }

    [Fact]
    public void Ungated_content_keeps_todays_values_and_trace()
    {
        var gate = Gate(V12, Speed(null));
        var empty = Gate(V12, Speed([]));

        foreach (var content in new[] { gate, empty })
            foreach (var gear in new[] { new ContentRevision[] { }, [Hauberk], [Coat, Buckler] })
            {
                var sheet = Calc(RulesFamilies.Srd521, content, gear).Field(FieldIds.Speed);
                Assert.Equal(10, SpeedGain(content, gear));
                Assert.DoesNotContain(sheet.Trace, t => t.Operation == "ignored" && t.Description.Contains("applies only while no", StringComparison.Ordinal));
            }
    }
}
