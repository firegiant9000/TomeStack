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
public sealed record RestRequest(Guid CharacterId, RestPeriod Kind = RestPeriod.LongRest, bool Confirm = false, string? Basis = null, IReadOnlyList<string>? Skip = null);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// <c>character.restPreview</c> (M2 item 3, SPEC C-05, D01): the changes a rest proposes and what the player must do by
    /// hand. Writes nothing. M2 has the long rest only (owner decision D01, 2026-09-27); a short rest is refused.
    /// </summary>
    public RestPreview PreviewRest(Guid characterId, RestPeriod kind)
    {
        var (character, plan) = Plan(characterId, kind);
        return new(plan.Kind, plan.Changes, plan.Manual, Basis(character, plan));
    }

    /// <summary>
    /// <c>character.rest</c>: applies a previewed rest, minus the unticked changes, in one transaction. Refused without
    /// <c>confirm</c> (<c>rest.confirmation-required</c>), and when the character or the plan changed since the preview
    /// (<c>rest.preview-stale</c>). Nothing changes when it is refused.
    /// </summary>
    public CharacterView Rest(RestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("rest.confirmation-required", "A rest changes hit points and resources; confirm the previewed changes to apply them.")]);
        var (character, plan) = Plan(request.CharacterId, request.Kind);
        if (request.Basis != Basis(character, plan))
            throw new AppValidationException([new("rest.preview-stale", "The character changed since this rest was previewed. Preview the rest again.")]);
        var skip = (request.Skip ?? []).Where(s => s is not null).ToHashSet(StringComparer.Ordinal);
        return SaveCharacter(character with { Play = RestPlanner.Apply(character.Play, plan, skip) });
    }

    private (Character Character, RestPlan Plan) Plan(Guid characterId, RestPeriod kind)
    {
        if (kind != RestPeriod.LongRest)
            throw new AppValidationException([new("rest.short-rest-not-supported", "Short rests are not supported yet (planned after M2). Adjust resources by hand for now.")]);
        var character = _store.FindCharacter(characterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
        return (character, RestPlanner.LongRest(character, CharacterCalculator.Calculate(character, _store)));
    }

    private static string Basis(Character character, RestPlan plan)
    {
        var text = JsonSerializer.Serialize(character, RulesJson.Compact) + "\n" + JsonSerializer.Serialize(plan, RulesJson.Compact);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
