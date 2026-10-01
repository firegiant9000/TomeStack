using BenchmarkDotNet.Attributes;
using TomeStack.RulesCore;

namespace TomeStack.Benchmarks;

/// <summary>
/// T4 case 1: a level-20, three-class character (Wizard 8, Cleric 6, Fighter 6) with every spell on both casters' lists
/// and a set of SRD weapons and armor, calculated from the bundled SRD packs, once per rules family. Pure
/// <see cref="CharacterCalculator.Calculate"/> over an in-memory catalog: no database, no UI.
/// </summary>
public class CalculationBenchmarks
{
    private Character _character = null!;
    private InMemoryContentCatalog _catalog = null!;

    [Params(RulesFamilies.Srd51, RulesFamilies.Srd521)]
    public string Family { get; set; } = RulesFamilies.Srd51;

    /// <summary>The benchmark character, for case 4.</summary>
    internal Character Character => _character;

    [GlobalSetup]
    public void Setup()
    {
        _catalog = Srd.Catalog();
        var wizard = Srd.Find(Family, ContentKind.Class, "Wizard");
        var cleric = Srd.Find(Family, ContentKind.Class, "Cleric");
        var fighter = Srd.Find(Family, ContentKind.Class, "Fighter");
        // The SRD classes grant a "Spellcasting" feature that holds the spellcasting effect; that feature is the caster.
        var spells = new[] { wizard, cleric }
            .Select(c => c.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => _catalog.FindRevision(g.Content!))
                .First(r => r?.Effects.OfType<SpellcastingEffect>().Any() == true)!)
            .SelectMany(caster => Srd.SpellsOn(Family, caster.Effects.OfType<SpellcastingEffect>().First().SpellList)
                .Select(spell => new KnownSpell(caster.ContentId, spell.Reference)))
            .DistinctBy(s => (s.Caster, s.Spell.ContentId))
            .ToList();
        var weapons = Srd.Items<WeaponEffect>(Family).Take(6).Select(w => new EquipmentEntry(w.Reference, Equipped: true));
        var armor = Srd.Items<ArmorEffect>(Family).Where(a => a.Effects.OfType<ArmorEffect>().Any(e => e.Category is ArmorCategory.Heavy or ArmorCategory.Shield))
            .GroupBy(a => a.Effects.OfType<ArmorEffect>().First().Category == ArmorCategory.Shield).Select(g => g.First())
            .Select(a => new EquipmentEntry(a.Reference, Equipped: true));
        _character = new Character
        {
            Id = Guid.Parse("7be0c400-0000-4000-8000-000000000001"),
            Name = "Benchmark Triclass",
            RulesFamily = Family,
            Level = 20,
            Classes = [new(wizard.Reference, 8), new(cleric.Reference, 6), new(fighter.Reference, 6)],
            BaseAbilities = new(14, 12, 14, 16, 15, 8),
            Spells = spells,
            Equipment = [.. weapons, .. armor],
        };
        if (spells.Count < 100 || _character.Equipment.Count < 5)
            throw new InvalidOperationException($"The SRD packs gave {spells.Count} spells and {_character.Equipment.Count} items; expected a full list.");
        Console.WriteLine($"// {Family}: {spells.Count} spells, {_character.Equipment.Count} items, {CharacterCalculator.Calculate(_character, _catalog).Fields.Count} fields");
    }

    [Benchmark]
    public CharacterSheet Calculate() => CharacterCalculator.Calculate(_character, _catalog);
}
