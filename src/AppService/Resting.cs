using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <param name="Basis">
/// Identifies the exact proposal: a hash of the stored character and the plan. <c>character.rest</c> applies only the
/// same proposal, so a confirmation never applies changes the player did not see.
/// </param>
public sealed record RestPreview(RestPeriod Kind, IReadOnlyList<RestChange> Changes, IReadOnlyList<Diagnostic> Manual, string Basis);

/// <param name="Skip">Ids of proposed changes the player unticked; they are not applied.</param>
/// <param name="HitDice">Short rest only: the hit dice the player spends, with what each die shows. The same as in the preview.</param>
public sealed record RestRequest(
    Guid CharacterId,
    RestPeriod Kind = RestPeriod.LongRest,
    bool Confirm = false,
    string? Basis = null,
    IReadOnlyList<string>? Skip = null,
    IReadOnlyList<HitDieRoll>? HitDice = null);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// <c>character.restPreview</c> (SPEC C-05, D01): the changes a rest proposes and what the player must do by hand.
    /// Writes nothing. A short rest proposes the hit dice in <paramref name="hitDice"/> (rolled in TomeStack or at the
    /// table) and the short-rest recoveries; a long rest proposes hit points, hit dice, recoveries and exhaustion.
    /// </summary>
    public RestPreview PreviewRest(Guid characterId, RestPeriod kind, IReadOnlyList<HitDieRoll>? hitDice = null)
    {
        var (character, plan) = Plan(characterId, kind, hitDice ?? []);
        return new(plan.Kind, plan.Changes, plan.Manual, Basis(character, plan));
    }

    /// <summary>
    /// <c>character.rest</c>: applies a previewed rest, minus the unticked changes, in one transaction. Refused without
    /// <c>confirm</c> (<c>rest.confirmation-required</c>), and when the character or the plan changed since the preview
    /// (<c>rest.preview-stale</c>), which includes different hit dice. Nothing changes when it is refused.
    /// </summary>
    public CharacterView Rest(RestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("rest.confirmation-required", "A rest changes hit points and resources; confirm the previewed changes to apply them.")]);
        var (character, plan) = Plan(request.CharacterId, request.Kind, request.HitDice ?? []);
        if (request.Basis != Basis(character, plan))
            throw new AppValidationException([new("rest.preview-stale", "The character changed since this rest was previewed. Preview the rest again.")]);
        var skip = (request.Skip ?? []).Where(s => s is not null).ToHashSet(StringComparer.Ordinal);
        var maximum = CharacterCalculator.Calculate(character, _store).HitPoints?.Maximum ?? 0;
        return SaveWithPlay(character with { Play = RestPlanner.Apply(character.Play, plan, skip, maximum) });
    }

    private (Character Character, RestPlan Plan) Plan(Guid characterId, RestPeriod kind, IReadOnlyList<HitDieRoll> hitDice)
    {
        var character = _store.FindCharacter(characterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
        var sheet = CharacterCalculator.Calculate(character, _store);
        if (RestPlanner.CannotRest(character, sheet, kind) is { } cannot)
            throw new AppValidationException([cannot]);
        if (kind == RestPeriod.LongRest)
        {
            if (hitDice.Count > 0)
                throw new AppValidationException([new("rest.hit-dice-long-rest", "Hit dice are spent on a short rest; a long rest restores all hit points.")]);
            return (character, RestPlanner.LongRest(character, sheet));
        }
        if (RestPlanner.CheckHitDice(sheet, hitDice) is { Count: > 0 } problems)
            throw new AppValidationException(problems);
        return (character, RestPlanner.ShortRest(character, sheet, hitDice));
    }

    private static string Basis(Character character, RestPlan plan)
    {
        var text = JsonSerializer.Serialize(character, RulesJson.Compact) + "\n" + JsonSerializer.Serialize(plan, RulesJson.Compact);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
