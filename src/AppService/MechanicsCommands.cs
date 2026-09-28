using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <param name="SourceIds">The sources to list; by default every source made in TomeStack (the user's own homebrew).</param>
public sealed record MechanicsRequest(Guid CharacterId, IReadOnlyList<Guid>? SourceIds = null);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// <c>character.mechanics</c> (M3 B1): every mechanic the character gets from the given sources, each marked automatic,
    /// assisted or reference, with its manual step. Writes nothing. This is how the Stardust Guardian acceptance and the
    /// session gap notes (M3 B3) see which mechanics TomeStack handles.
    /// </summary>
    public MechanicsReport Mechanics(MechanicsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var view = GetCharacter(request.CharacterId);
        var sources = request.SourceIds is { Count: > 0 } ids
            ? ids.ToHashSet()
            : _store.ListSources().Where(s => s.EditionVersion == "homebrew").Select(s => s.Id).ToHashSet();
        return MechanicsInventory.Of(view.Sheet, sources);
    }
}
