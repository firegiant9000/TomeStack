using BenchmarkDotNet.Attributes;
using TomeStack.AppService;
using TomeStack.RulesCore;

namespace TomeStack.Benchmarks;

/// <summary>
/// D43 risk G2 and slice 7: the level-20 three-class SRD 5.2.1 character of <see cref="CalculationBenchmarks"/> with one original
/// feature pinned that carries nine forms in one exclusive group, sixteen techniques (rolls offered while a form is on), a rider and an
/// upkeep for every form, and damage responses of all three phases. The pack is built here in code, so no fixture file is committed.
/// <see cref="Calculate_with_nine_forms_and_sixteen_techniques"/> is held to the same run's <see cref="CalculationBenchmarks.Calculate"/>
/// for SRD 5.2.1; the two previews are recorded only.
/// </summary>
public class PlayMechanicsBenchmarks
{
    private const int Forms = 9;
    private const int Techniques = 16;

    private static readonly Guid ContentId = Guid.Parse("7be0e000-0000-4000-8000-000000000001");

    private TomeStackApp _app = null!;
    private InMemoryContentCatalog _catalog = null!;
    private Character _character = null!;
    private TurnRequest _turn = null!;
    private DamagePreviewRequest _damage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _app = BenchmarkLibrary.Open(BenchmarkLibrary.NewFolder());
        var source = _app.CreateHomebrewSource(new("Fixture Benchmark Forms", [RulesFamilies.Srd521]));
        var reference = BenchmarkLibrary.Publish(_app, Pack(source.Id));
        var pack = _app.ListRevisions(ContentId).Single(r => r.Reference == reference);
        _catalog = new InMemoryContentCatalog([.. Srd.Packs.SelectMany(p => p.Sources).DistinctBy(s => s.Id), source], [.. Srd.Revisions, pack]);

        var calculation = new CalculationBenchmarks { Family = RulesFamilies.Srd521 };
        calculation.Setup();
        // Form 1 on, an encounter at round 0 (so a turn proposes upkeep), and 10 hit points left (so a big hit drops to 0).
        var play = new PlayState { CurrentHitPoints = 10, Encounter = new EncounterState(0), Toggles = [new ActiveToggle(ContentId, "form-1")] };
        var view = _app.SaveCharacter(calculation.Character with { Id = Guid.NewGuid(), Name = "Benchmark Forms", Pins = [reference], Play = play });
        _character = view.Character;
        _turn = new(_character.Id, TurnMoment.TurnStart);
        _damage = new(_character.Id, 60, "slashing",
        [
            new(ContentId, "brace"), new(ContentId, "stand"), new(ContentId, "half"), new(ContentId, "after"),
        ]);

        var sheet = CharacterCalculator.Calculate(_character, _catalog);
        var turn = _app.PreviewTurn(_turn);
        var preview = _app.PreviewDamage(_damage);
        if (sheet.Responses?.Count != 5 || turn.Changes.Count < 2 || preview.Notes.Count != 0 || preview.Reduction != 3 || preview.Rolls.Count != 2 || preview.Offers.Count != 5)
            throw new InvalidOperationException(
                $"The benchmark character did not set up: {sheet.Responses?.Count} responses, {turn.Changes.Count} turn changes, {preview.Notes.Count} notes, reduction {preview.Reduction}, {preview.Rolls.Count} rolls, {preview.Offers.Count} offers.");
        Console.WriteLine($"// forms: {sheet.Fields.Count} fields, {sheet.Responses.Count} responses, {turn.Changes.Count} turn changes, {preview.Offers.Count} offers");
    }

    [GlobalCleanup]
    public void Cleanup() => BenchmarkLibrary.Delete(_app);

    [Benchmark]
    public CharacterSheet Calculate_with_nine_forms_and_sixteen_techniques() => CharacterCalculator.Calculate(_character, _catalog);

    [Benchmark]
    public TurnPreview Turn_preview() => _app.PreviewTurn(_turn);

    [Benchmark]
    public DamagePreview Damage_preview_with_responses() => _app.PreviewDamage(_damage);

    /// <summary>Generic, original content: nothing here is SRD or third-party text.</summary>
    private static ContentRevision Pack(Guid source)
    {
        var effects = new List<Effect>
        {
            new ToggleGroupEffect { Id = "forms-group", GroupId = "form", Label = "Forms" },
            new ResourceEffect { Id = "focus", ResourceId = "focus", Label = "Focus", Maximum = "20" },
            new RecoveryEffect { Id = "focus-long", ResourceId = "focus", On = RestPeriod.LongRest, Amount = "all", Timing = EffectTiming.OnLongRest },
            new ResourceEffect { Id = "last-pool", ResourceId = "last", Label = "Last stand", Maximum = "1" },
            new RecoveryEffect { Id = "last-long", ResourceId = "last", On = RestPeriod.LongRest, Amount = "all", Timing = EffectTiming.OnLongRest },
            new AttackEffect
            {
                Id = "strike", AttackId = "strike", Label = "Benchmark strike", Kind = WeaponAttack.Melee, Abilities = [Ability.Str, Ability.Dex],
                Damage = new("1", "6"), DamageType = "bludgeoning", Tags = ["bench"],
            },
        };
        for (var form = 1; form <= Forms; form++)
        {
            var id = $"form-{form}";
            effects.Add(new ToggleEffect { Id = id, ToggleId = id, Label = $"Benchmark form {form}", Group = "form" });
            effects.Add(new ModifierEffect { Id = $"bonus-{form}", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = $"{form}", Timing = EffectTiming.WhileActive, Toggle = id });
            effects.Add(new AttackRiderEffect { Id = $"rider-{form}", Tags = ["bench"], Dice = "1d6", DamageBonus = $"{form}", Toggle = id });
            effects.Add(new UpkeepEffect { Id = $"upkeep-{form}", Toggle = id, At = TurnMoment.TurnStart, ResourceId = "focus", Change = UpkeepChange.Spend, Amount = "1", IfShort = UpkeepShortfall.End });
        }
        for (var technique = 1; technique <= Techniques; technique++)
        {
            var id = $"technique-{technique:D2}";
            effects.Add(new RollEffect
            {
                Id = id, RollId = id, Label = $"Benchmark technique {technique}", Dice = $"1d8+{technique}",
                WhileToggle = new(null, $"form-{(technique - 1) % Forms + 1}"),
            });
        }
        effects.Add(new ResponseEffect { Id = "brace", ResponseId = "brace", Label = "Brace", Phase = ResponsePhase.DamageReduction, Amount = "3", ResourceId = "focus", Cost = "1" });
        effects.Add(new ResponseEffect { Id = "stand", ResponseId = "stand", Label = "Stand", Phase = ResponsePhase.HitPointOutcome, Amount = "1", ResourceId = "last", Cost = "1" });
        effects.Add(new ResponseEffect
        {
            Id = "half", ResponseId = "half", Label = "Half", Phase = ResponsePhase.Threshold, When = new("floor(HP.MAX / 2)", null), ResourceId = "focus", Cost = "1",
            Outcomes =
            [
                new(ResponseOutcomeKind.ToggleOn, null, null, null, new(null, "form-2"), null, null),
                new(ResponseOutcomeKind.OfferRoll, null, null, null, null, "technique-02", null),
            ],
        });
        effects.Add(new ResponseEffect
        {
            Id = "after", ResponseId = "after", Label = "After", Phase = ResponsePhase.Threshold, When = new(null, true),
            Outcomes = [new(ResponseOutcomeKind.OfferRoll, null, null, null, null, "technique-01", null)],
        });
        effects.Add(new ResponseEffect
        {
            Id = "mend", ResponseId = "mend", Label = "Mend", Phase = ResponsePhase.Threshold, When = new("floor(HP.MAX / 2)", null),
            Outcomes = [new(ResponseOutcomeKind.RegainHitPoints, null, null, "5", null, null, null)],
        });
        return new()
        {
            ContentId = ContentId,
            RevisionId = Guid.Empty,
            Kind = ContentKind.Feature,
            Name = "Fixture Benchmark Forms",
            RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(source),
            Status = RevisionStatus.Draft,
            Summary = "Original benchmark content: nine forms, sixteen techniques, riders, upkeep and damage responses.",
            Effects = effects,
        };
    }
}
