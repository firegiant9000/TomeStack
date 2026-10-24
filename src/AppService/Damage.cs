using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// <c>character.damagePreview</c> (D43 slice 7): one hit, with the responses the player has chosen so far. <paramref name="Type"/> is any
/// case of one of the thirteen damage types; absent means untyped.
/// </summary>
public sealed record DamagePreviewRequest(Guid CharacterId, int Amount, string? Type, IReadOnlyList<ResponseChoice>? Responses = null);

/// <summary>
/// What a hit would do with the chosen responses, from a plan and an apply on an in-memory copy (nothing is written). <paramref name="Type"/>
/// is the normalised type. <paramref name="Reduction"/> is the damage the chosen reductions take off; <paramref name="Outcome"/> the hit
/// after defences; <paramref name="After"/> the hit point state the confirm would write (unchanged when <paramref name="Notes"/> says the
/// choices cannot be applied); <paramref name="Offers"/> every response eligible for this hit; <paramref name="Rolls"/> the rolls the
/// confirm would offer; <paramref name="Notes"/> the errors that make the choices unusable (reported, never thrown). The confirm
/// (<c>character.play</c>, <c>damage</c> with <c>responses</c>) sends <paramref name="UpdatedAt"/> back as <c>expectedUpdatedAt</c>.
/// </summary>
public sealed record DamagePreview(
    Guid CharacterId, int Amount, string? Type, int Reduction, DamageOutcome Outcome,
    HitPointState After, int? ConcentrationDc, bool ConcentrationEnds,
    IReadOnlyList<ResponseOffer> Offers, IReadOnlyList<OfferedRoll> Rolls,
    IReadOnlyList<Diagnostic> Notes, DateTimeOffset UpdatedAt);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// <c>character.damagePreview</c>: plans and applies one hit with the chosen responses on a copy, and writes nothing. Refused only for
    /// <c>character.not-found</c>, <c>play.amount-out-of-range</c>, <c>play.damage-type-unknown</c> and a choice list longer than the sheet's
    /// bound (<c>damage.response-unavailable</c>); anything wrong with the choices themselves comes back in <see cref="DamagePreview.Notes"/>.
    /// </summary>
    public DamagePreview PreviewDamage(DamagePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var character = _store.FindCharacter(request.CharacterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {request.CharacterId} does not exist.")]);
        if (request.Amount is < 0 or > PlayState.MaxHitPoints)
            throw new AppValidationException([new("play.amount-out-of-range", $"The amount must be between 0 and {PlayState.MaxHitPoints}.")]);
        var sheet = CharacterCalculator.Calculate(character, _store);
        var (plan, type) = PlanDamage(character, sheet, request.Amount, request.Type, request.Responses ?? []);
        var (play, rolls, errors) = DamagePipeline.Apply(character.Play, plan, sheet);
        var notes = (IReadOnlyList<Diagnostic>)[.. plan.Notes, .. errors.Where(e => !plan.Notes.Contains(e))];
        // The state the confirm would write, as the sheet will show it (a toggle outcome can change the maximum).
        var hitPoints = errors.Count > 0 ? sheet.HitPoints : CharacterCalculator.Calculate(character with { Play = play }, _store).HitPoints;
        return new(
            character.Id, request.Amount, type, plan.Reduction, plan.Result.Outcome, hitPoints ?? plan.Final,
            plan.Result.ConcentrationDc, plan.Result.ConcentrationEnds, plan.Offers, rolls, notes, character.UpdatedAt);
    }

    /// <summary>The damage type in its canonical spelling, or <c>play.damage-type-unknown</c>. Absent means untyped.</summary>
    internal static string? NormalizedDamageType(string? written)
    {
        if (written is null)
            return null;
        return DamageTypes.TryNormalize(written, out var canonical)
            ? canonical
            : throw new AppValidationException([new("play.damage-type-unknown", $"Damage type must be one of: {string.Join(", ", DamageTypes.All)}.")]);
    }

    /// <summary>The one planning step of the preview and the confirm: the type is normalised and the choice list bounded before the plan runs.</summary>
    private static (DamagePlan Plan, string? Type) PlanDamage(Character character, CharacterSheet sheet, int amount, string? written, IReadOnlyList<ResponseChoice> chosen)
    {
        var type = NormalizedDamageType(written);
        if (chosen.Count > CharacterCalculator.MaxResponses)
            throw new AppValidationException(
                [new("damage.response-unavailable", $"At most {CharacterCalculator.MaxResponses} responses can be chosen for one hit.")], "damage.response-unavailable");
        var plan = DamagePipeline.Plan(new(amount, type), character, sheet, chosen, RulesFamilies.Get(character.RulesFamily));
        return (plan, type);
    }

    /// <summary>
    /// A confirmed damage with responses (inside <see cref="Play"/>): re-plans with the same choices, refuses the whole change with the
    /// plan's or the apply's first code on top when anything is wrong, and otherwise returns the new play state. The offered rolls come
    /// back as <c>damage.roll-offered</c> notices that carry the roll's content and effect id, so the page can roll them; the messages name ids only.
    /// </summary>
    private static PlayState DamageWithResponses(
        Character character, PlayState play, CharacterSheet sheet, PlayCommand command, out DamageOutcome outcome, out IReadOnlyList<Diagnostic>? notices)
    {
        var (plan, _) = PlanDamage(character, sheet, command.Amount, command.DamageType, command.Responses!);
        var (applied, rolls, errors) = DamagePipeline.Apply(play, plan, sheet); // returns the plan's notes when it has any
        if (errors.Count > 0)
            throw new AppValidationException(errors, errors[0].Code);
        outcome = plan.Result.Outcome;
        notices = rolls.Count == 0
            ? null
            : [.. rolls.Select(r => new Diagnostic("damage.roll-offered", $"Roll {r.RollId} of content {r.Content.ContentId} is offered.", r.Content, r.EffectId))];
        return applied;
    }

    /// <summary>
    /// Plain damage (no responses) applies as it always did; this names the responses the sheet would have offered for the same hit
    /// (R12), one <c>damage.responses-skipped</c> notice each with its content and response id, so the player knows what they passed
    /// over. Planned on the sheet from before the damage. Null when there is none.
    /// </summary>
    private static IReadOnlyList<Diagnostic>? SkippedResponses(Character character, CharacterSheet sheet, PlayCommand command)
    {
        if (sheet.Responses is not { Count: > 0 })
            return null;
        var plan = DamagePipeline.Plan(new(command.Amount, NormalizedDamageType(command.DamageType)), character, sheet, [], RulesFamilies.Get(character.RulesFamily));
        var skipped = plan.Offers.Where(o => o.Offered).Select(o => o.Response).ToList();
        if (skipped.Count == 0)
            return null;
        return
        [
            .. skipped.Select(r => new Diagnostic(
                "damage.responses-skipped",
                $"Response {r.ResponseId} of content {r.ContentId} was available for this hit and was not chosen.",
                sheet.Features?.FirstOrDefault(f => f.Content.ContentId == r.ContentId)?.Content,
                r.ResponseId)),
        ];
    }

    /// <summary>The undo label of a damage with responses: <c>damage {amount} {type} ({taken} taken)</c>, the type left out when untyped.</summary>
    private static string DamageLabel(PlayCommand command, DamageOutcome outcome) =>
        $"damage {command.Amount}{(outcome.Type is null ? "" : $" {outcome.Type}")} ({outcome.Adjusted} taken)";
}
