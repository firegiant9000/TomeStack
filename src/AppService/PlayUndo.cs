using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// D43 S2 (D44): the one thing a compound play change can be undone to. <paramref name="Before"/> is the play state the change
/// started from, <paramref name="UpdatedAfter"/> the stamp the change wrote, and <paramref name="Label"/> what the button says
/// (for example "switch to Tide form"). It lives in memory only; it is never stored, so closing the app forgets it.
/// </summary>
internal sealed record PlayUndoSlot(PlayState Before, DateTimeOffset UpdatedAfter, string Label);

/// <summary>
/// One undo slot per character, in memory. A slot is good only while the character's stored <c>UpdatedAt</c> is still the stamp
/// the change wrote: any other write moves the stamp on, so the slot can never be used again. The class guards itself with a lock;
/// the dispatcher serializes commands, but nothing here depends on that.
/// </summary>
internal sealed class PlayUndoSlots
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, PlayUndoSlot> _slots = [];

    /// <summary>Replaces the character's slot (one level: an earlier slot is lost).</summary>
    public void Set(Guid characterId, PlayUndoSlot slot)
    {
        lock (_lock)
            _slots[characterId] = slot;
    }

    /// <summary>The character's slot, only while the character's stamp is the one the change wrote; otherwise null.</summary>
    public PlayUndoSlot? Matching(Guid characterId, DateTimeOffset updatedAt)
    {
        lock (_lock)
            return _slots.TryGetValue(characterId, out var slot) && slot.UpdatedAfter == updatedAt ? slot : null;
    }

    /// <summary>
    /// Takes the character's slot out if the stamp still matches. A slot that does not match (another write happened since) is
    /// dropped as well, since the stamp only moves forward and it can never match again; the result is then false.
    /// </summary>
    public bool TryTake(Guid characterId, DateTimeOffset updatedAt, out PlayUndoSlot? slot)
    {
        lock (_lock)
        {
            if (!_slots.Remove(characterId, out var found))
            {
                slot = null;
                return false;
            }
            var matches = found.UpdatedAfter == updatedAt;
            slot = matches ? found : null;
            return matches;
        }
    }

    /// <summary>Whether the character has a slot at all (matching or not); tells "nothing to undo" from "changed since".</summary>
    public bool Has(Guid characterId)
    {
        lock (_lock)
            return _slots.ContainsKey(characterId);
    }

    public void Clear(Guid characterId)
    {
        lock (_lock)
            _slots.Remove(characterId);
    }
}
