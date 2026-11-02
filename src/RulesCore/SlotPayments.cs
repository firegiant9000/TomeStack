namespace TomeStack.RulesCore;

/// <summary>MECH-X item 1 (R35): the spell slot a payment would take, by its spell level and whether it is a pact slot.</summary>
public sealed record SlotChoice(int Level, bool Pact);

/// <summary>MECH-X item 1: how a slot payment (<see cref="PaymentKind.Slot"/>) picks the slot it spends.</summary>
public static class SlotPayments
{
    /// <summary>
    /// R35: the lowest slot level at or above <paramref name="minLevel"/> (a value below 1 counts as 1) with a slot remaining, over the
    /// ordinary slots and the pact slots together; on a tie the pact slot, which refills on a short rest. Null when none is left.
    /// </summary>
    public static SlotChoice? Pick(IReadOnlyList<SlotValue>? slots, SlotValue? pact, int minLevel)
    {
        var floor = Math.Max(minLevel, 1);
        SlotChoice? best = null;
        foreach (var slot in slots ?? [])
        {
            if (slot.Remaining > 0 && slot.Level >= floor && (best is null || slot.Level < best.Level))
                best = new SlotChoice(slot.Level, false);
        }
        if (pact is { Remaining: > 0 } && pact.Level >= floor && (best is null || pact.Level <= best.Level))
            best = new SlotChoice(pact.Level, true);
        return best;
    }
}
