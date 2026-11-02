using TomeStack.RulesCore;

namespace TomeStack.AppService;

public sealed partial class TomeStackApp
{
    /// <summary>
    /// MECH-X item 1: makes one hit-point or slot payment on <paramref name="play"/>, checking affordability again on the
    /// <paramref name="sheet"/> it was decided on (the sheet's <c>Payable</c> is only a hint for the page; this is the backstop).
    /// Hit points (R34): paid from current hit points only, temporary ones untouched; allowed when current is above
    /// <paramref name="amount"/>, or at least it with <c>mayDropToZero</c>; reaching 0 ends concentration, as <c>setHitPoints 0</c>
    /// does. An amount of 0 changes nothing. Slot (R35): the slot <see cref="SlotPayments.Pick"/> chooses, one per use; the amount is ignored.
    /// Throws <see cref="AppValidationException"/> with the code as the wire code (<c>pay.hit-points-too-low</c>, <c>slots.none-left</c>);
    /// nothing is returned half-applied.
    /// </summary>
    internal static PlayState Pay(PlayState play, CharacterSheet sheet, PaymentValue payment, int amount, ContentReference content, string effectId)
    {
        switch (payment.Kind)
        {
            case PaymentKind.HitPoints:
                if (amount <= 0)
                    return play;
                var hp = sheet.HitPoints!;
                var enough = payment.MayDropToZero ? hp.Current >= amount : hp.Current > amount;
                if (!enough)
                {
                    var needs = payment.MayDropToZero ? "at least" : "more than";
                    var message = $"Paying {amount} hit points needs {needs} {amount} current hit points.";
                    throw new AppValidationException([new("pay.hit-points-too-low", message, content, effectId)], "pay.hit-points-too-low");
                }
                var paid = play.WithCurrentHitPoints(hp.Current - amount, hp.Maximum);
                return hp.Current - amount == 0 ? paid with { Concentration = null } : paid; // concentration ends at 0 hit points
            case PaymentKind.Slot:
                var level = payment.MinSlotLevel ?? 1;
                var slot = SlotPayments.Pick(sheet.SpellSlots, sheet.PactSlots, level)
                    ?? throw new AppValidationException([new("slots.none-left", $"No spell slot of level {level} or higher is left.", content, effectId)], "slots.none-left");
                return slot.Pact
                    ? play with { PactSlotsSpent = play.PactSlotsSpent + 1 }
                    : play.WithSlotsSpent(slot.Level, play.SlotsSpentOf(slot.Level) + 1);
            default:
                return play; // a pool payment is made through the pool, not here
        }
    }
}
