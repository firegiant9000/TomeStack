namespace TomeStack.RulesCore.Tests;

/// <summary>MECH-X item 1 (R35): which spell slot a slot payment takes.</summary>
public class SlotPaymentsTests
{
    /// <summary>"1:0,2:1" is a level-1 row with nothing left and a level-2 row with one left.</summary>
    private static List<SlotValue> Ordinary(string rows) =>
        [.. rows.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(row =>
        {
            var parts = row.Split(':');
            var level = int.Parse(parts[0]);
            var remaining = int.Parse(parts[1]);
            return new SlotValue(level, 4, 4 - remaining, remaining, $"spell.slots.{level}");
        })];

    private static SlotValue? Pact(int level, int remaining) =>
        level == 0 ? null : new SlotValue(level, 2, 2 - remaining, remaining, "spell.pact.slots");

    [Theory]
    [InlineData("1:2,2:1", 0, 0, 1, 1, false)] // the lowest ordinary slot
    [InlineData("1:2,2:1", 0, 0, 2, 2, false)] // minLevel skips level 1
    [InlineData("1:0,2:1", 1, 2, 1, 1, true)] // level-1 ordinary gone: the pact slot (Review Focus 5)
    [InlineData("1:0,2:1", 1, 2, 2, 2, false)] // minSlotLevel 2 with pact level 1: the level-2 ordinary slot
    [InlineData("1:1", 1, 2, 1, 1, true)] // a tie goes to the pact slot (it refills on a short rest)
    [InlineData("2:1", 3, 1, 1, 2, false)] // a higher pact slot loses to a lower ordinary one
    [InlineData("1:0,2:0", 0, 0, 1, -1, false)] // none left
    [InlineData("1:0,2:1", 1, 0, 1, 2, false)] // an empty pact pool is not a candidate
    [InlineData("1:3,2:3", 2, 2, 3, -1, false)] // none at or above minLevel
    [InlineData("1:3,9:1", 0, 0, 9, 9, false)] // the top level
    [InlineData("1:3,2:3", 0, 0, 9, -1, false)] // minLevel 9, nothing that high
    [InlineData("2:1", 0, 0, 0, 2, false)] // a minLevel below 1 counts as 1
    public void Picks(string ordinary, int pactLevel, int pactRemaining, int minLevel, int expectedLevel, bool expectedPact)
    {
        var pick = SlotPayments.Pick(Ordinary(ordinary), Pact(pactLevel, pactRemaining), minLevel);

        Assert.Equal(expectedLevel == -1 ? null : new SlotChoice(expectedLevel, expectedPact), pick);
    }

    [Fact]
    public void No_slots_at_all_gives_none()
    {
        Assert.Null(SlotPayments.Pick(null, null, 1));
        Assert.Null(SlotPayments.Pick([], null, 1));
    }
}
