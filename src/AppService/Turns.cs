using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <param name="Confirm">Must be <c>true</c> for <c>character.turn</c>; <c>character.turnPreview</c> ignores it.</param>
/// <param name="ExpectedUpdatedAt">The <see cref="TurnPreview.UpdatedAt"/> the player previewed. <c>character.turn</c> applies only on the same stored stamp.</param>
/// <param name="Skip">Ids of proposed changes the player unticked; the plan is recomputed without them.</param>
/// <param name="Choices">What the player chose for each question the plan asks (<see cref="TurnChoicePrompt.Id"/>).</param>
public sealed record TurnRequest(
    Guid CharacterId,
    TurnMoment At = TurnMoment.TurnStart,
    bool Confirm = false,
    DateTimeOffset? ExpectedUpdatedAt = null,
    IReadOnlyList<string>? Skip = null,
    IReadOnlyDictionary<string, TurnChoiceOption>? Choices = null);

/// <param name="UpdatedAt">The stored stamp the preview was computed on; <c>character.turn</c> must send it back.</param>
public sealed record TurnPreview(
    TurnMoment At,
    IReadOnlyList<TurnChange> Changes,
    IReadOnlyList<TurnChoicePrompt> Choices,
    IReadOnlyList<Diagnostic> Manual,
    IReadOnlyList<TurnPrompt> Prompts,
    DateTimeOffset UpdatedAt);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// <c>character.turnPreview</c> (D43 slice 3): what the start or end of the character's turn proposes, for the selection in the
    /// request. Ignores <c>confirm</c> and the stamp; writes nothing. Refused <c>turn.not-in-encounter</c> outside an encounter.
    /// </summary>
    public TurnPreview PreviewTurn(TurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (character, plan) = PlanTurn(request);
        return new(plan.At, plan.Changes, plan.Choices, plan.Manual, plan.Prompts, character.UpdatedAt);
    }

    /// <summary>
    /// <c>character.turn</c>: applies a previewed turn, minus the unticked changes, in one write. Refused, writing nothing, in this
    /// order: no <c>confirm</c> (<c>turn.confirmation-required</c>); unknown character (<c>character.not-found</c>); no stamp
    /// (<c>play.basis-required</c>); a stamp that is not the stored one (<c>turn.preview-stale</c>, which also stops a double send);
    /// not in an encounter (<c>turn.not-in-encounter</c>); a choice for a question the recomputed plan does not ask
    /// (<c>turn.choice-unknown</c>); a question with no choice (<c>turn.choice-required</c>). The turn is one undo step ("turn start" / "turn end"); a turn whose plan has no changes writes nothing (no new stamp, the undo slot is kept) and replies with the current view and the plan's notices.
    /// </summary>
    public CharacterView Turn(TurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("turn.confirmation-required", "A turn changes resources and effects; confirm the previewed changes to apply them.")], "turn.confirmation-required");
        var character = FindForTurn(request.CharacterId);
        if (request.ExpectedUpdatedAt is not { } expected)
            throw new AppValidationException([new("play.basis-required", "This change needs the character's current state. Reload it and try again.")], "play.basis-required");
        if (expected != character.UpdatedAt)
            throw new AppValidationException([new("turn.preview-stale", "The character changed since this turn was previewed. Preview the turn again.")], "turn.preview-stale");
        var (_, plan) = PlanTurn(request, character);
        var chosen = request.Choices ?? new Dictionary<string, TurnChoiceOption>();
        if (chosen.Keys.FirstOrDefault(id => plan.Choices.All(p => p.Id != id)) is not null)
            throw new AppValidationException([new("turn.choice-unknown", "A choice was sent for a question this turn does not ask. Preview the turn again.")], "turn.choice-unknown");
        if (plan.Choices.Any(p => !chosen.ContainsKey(p.Id)))
            throw new AppValidationException([new("turn.choice-required", "Choose what to do about each resource that cannot pay before the turn is applied.")], "turn.choice-required");

        // Content-authored text only (the plan's manual lines and a pool's resourceFull text); no character data.
        var notices = (IReadOnlyList<Diagnostic>)[.. plan.Manual, .. plan.Prompts.Select(p => new Diagnostic("turn.resource-full", p.Text))];
        // A turn with no changes writes nothing: no new stamp, and the undo slot keeps the step before it.
        if (plan.Changes.Count == 0)
            return View(character, stored: true) with { Notices = notices };

        var view = SaveWithPlay(
            character with { Play = TurnPlanner.Apply(character.Play, plan) },
            (character.Play, request.At == TurnMoment.TurnStart ? "turn start" : "turn end"));
        return view with { Notices = notices };
    }

    private Character FindForTurn(Guid characterId) =>
        _store.FindCharacter(characterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);

    private (Character Character, TurnPlan Plan) PlanTurn(TurnRequest request, Character? loaded = null)
    {
        var character = loaded ?? FindForTurn(request.CharacterId);
        if (TurnPlanner.CannotTakeTurn(character) is { } cannot)
            throw new AppValidationException([cannot], cannot.Code);
        var sheet = CharacterCalculator.Calculate(character, _store);
        var selection = new TurnSelection(
            (request.Skip ?? []).Where(s => s is not null).ToHashSet(StringComparer.Ordinal),
            request.Choices ?? new Dictionary<string, TurnChoiceOption>());
        return (character, TurnPlanner.Plan(character, sheet, request.At, selection));
    }
}
