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
    /// The character's slot whatever its stamp, without removing it (null when there is none). The undo looks here first, writes,
    /// and only then <see cref="Remove"/>s the slot, so a failed write keeps it.
    /// </summary>
    public PlayUndoSlot? Find(Guid characterId)
    {
        lock (_lock)
            return _slots.GetValueOrDefault(characterId);
    }

    public void Remove(Guid characterId)
    {
        lock (_lock)
            _slots.Remove(characterId);
    }

    /// <summary>
    /// Forgets every slot. A library restore or a package import can store a character with an older stamp again, which would make
    /// a slot written for that stamp match a play state it no longer belongs to.
    /// </summary>
    public void ClearAll()
    {
        lock (_lock)
            _slots.Clear();
    }
}
