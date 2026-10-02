using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// T3 property 4 (ARCHITECTURE: the sheet is derived from stored choices; ADR-003: calculation over a declarative AST, with
/// no code; SPEC C-04 roll records): the same seed rolls the same dice, and the same
/// character and pins calculate the same sheet, every time and from a fresh catalog. Sheets are compared as JSON:
/// records with list members compare by reference.
/// </summary>
public class DeterminismProperties
{
    private static readonly ContentPack[] Packs = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack()];

    private static readonly ContentRevision[] Published = [.. Packs.SelectMany(p => p.Revisions).Where(r => r.Status == RevisionStatus.Published)];

    private static readonly string[] CharacterFiles =
        ["m1-acceptance-srd51-korga.json", "m1-acceptance-srd521-brenna.json", "srd51-ash-m1.json", "srd51-quickfoot.json", "srd521-ash-m1.json", "srd521-courier.json", "srd521-rook-exception.json"];

    private static readonly Character[] Characters = [.. CharacterFiles.Select(Fixtures.Load)];

    private static InMemoryContentCatalog Catalog() => new([.. Packs.SelectMany(p => p.Sources)], [.. Packs.SelectMany(p => p.Revisions)]);

    private static readonly InMemoryContentCatalog Shared = Catalog();

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, RulesJson.Options);

    /// <summary>A fixture character with generated ability scores, overrides and extra pins of its own rules family only.</summary>
    private static Gen<Character> Variants { get; } =
        from character in Gen.Elements(Characters)
        from scores in Gen.Choose(1, 30).ArrayOf(6)
        from extra in Gen.Elements(Published.Where(r => r.RulesFamilies.Contains(character.RulesFamily)).Select(r => r.Reference).ToArray())
            .ListOf().Select(p => p.Take(5))
        from overrideValue in Gen.Choose(-5, 40)
        from withOverride in Gen.Elements(true, false)
        select character with
        {
            BaseAbilities = new(scores[0], scores[1], scores[2], scores[3], scores[4], scores[5]),
            Pins = [.. character.Pins.Concat(extra).Distinct()],
            Overrides = withOverride ? [new FieldOverride(FieldIds.Initiative, overrideValue, "Property test")] : character.Overrides,
        };

    /// <summary>Within one process: a difference that only shows between two launches is outside what this checks.</summary>
    [Property(MaxTest = 300)]
    public Property A_sheet_computed_twice_from_the_same_pins_is_equal() =>
        Prop.ForAll(Variants.ToArbitrary(), character =>
        {
            var first = Json(CharacterCalculator.Calculate(character, Shared));
            var second = Json(CharacterCalculator.Calculate(character, Shared));
            var fresh = Json(CharacterCalculator.Calculate(character, Catalog()));
            return (first == second && first == fresh).Label(character.Name);
        });

    private static Gen<string> DiceFormulas { get; } =
        from terms in (from count in Gen.Choose(1, 6)
                       from sides in Gen.Elements(4, 6, 8, 10, 12, 20, 100)
                       select $"{count}d{sides}").ArrayOf().Select(t => t.Take(3).ToArray())
        from constant in Gen.Choose(-5, 10)
        select (terms.Length == 0 ? "1d20" : string.Join("+", terms)) + (constant == 0 ? "" : constant > 0 ? $"+{constant}" : $"{constant}");

    /// <summary>A valid roll: any dice for a normal roll (critical or not), a d20 test for advantage and disadvantage.</summary>
    private static Gen<(RollMode Mode, string Formula, bool Critical)> Rolls { get; } =
        Gen.OneOf(
            from formula in DiceFormulas
            from critical in Gen.Elements(true, false)
            select (RollMode.Normal, formula, critical),
            from mode in Gen.Elements(RollMode.Advantage, RollMode.Disadvantage)
            from constant in Gen.Choose(-5, 10)
            select (mode, "1d20" + (constant == 0 ? "" : constant > 0 ? $"+{constant}" : $"{constant}"), false));

    [Property(MaxTest = 1000)]
    public Property The_same_seed_rolls_the_same_dice() =>
        Prop.ForAll(
            Gen.Choose(int.MinValue, int.MaxValue).Two().Select(s => ((ulong)(uint)s.Item1 << 32) | (uint)s.Item2).ToArbitrary(),
            Rolls.ToArbitrary(),
            (seed, roll) =>
            {
                var request = new RollRequest(roll.Formula, roll.Mode, roll.Critical);
                var a = DiceRoller.TryRoll(request, new SeededRandomSource(seed), out var first, out var errorA);
                var b = DiceRoller.TryRoll(request, new SeededRandomSource(seed), out var second, out var errorB);
                // Every generated roll is valid, so both must succeed; advantage and disadvantage keep one of two d20s.
                var dropped = roll.Mode == RollMode.Normal ? 0 : 1;
                return (a && b && Json(first) == Json(second)
                        && first!.Dice.All(d => d.Value >= 1 && d.Value <= d.Sides)
                        && first.Dice.Count(d => !d.Kept) == dropped)
                    .Label($"{seed} {roll} → {errorA?.Code ?? errorB?.Code}");
            });
}
