using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

public class DownConvertCoverageTests
{
    private static readonly string[] Contract =
    [
        "below.encounter-running", "below.rounds-left", "below.details", "below.custom-items", "below.custom-attacks",
        "below.damage-defences", "below.vulnerability", "below.companions", "below.xp", "below.equipment-state",
        "below.effect-unsupported", "below.roll-mode-disadvantage", "below.value-formula", "below.upkeep", "below.toggle-group",
        "below.toggle-cost", "below.toggle-level", "below.toggle-duration", "below.toggle-auto-when", "below.resource-detail",
        "below.resource-counts-up", "below.roll-detail", "below.roll-also-change", "below.diceless-roll",
        "below.modifier-toggle-content", "below.unknown-target", "below.restriction-lifted", "below.defence-while-toggle",
        "below.grant-defence", "below.item-attunement", "below.item-charges", "below.activation", "below.validation-error",
    ];

    private sealed record Entry(string Group, string Version, string Feature, string? Rule);

    private static List<Entry> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "downconvert", "requires-vN.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entries = new List<Entry>();
        foreach (var group in doc.RootElement.EnumerateObject())
            foreach (var version in group.Value.EnumerateObject())
                foreach (var item in version.Value.EnumerateArray())
                    entries.Add(new Entry(group.Name, version.Name, item.GetProperty("feature").GetString()!,
                        item.GetProperty("rule").GetString()));
        return entries;
    }

    [Fact]
    public void Every_requires_vN_entry_has_a_below_rule()
    {
        var entries = Load();
        var known = DownConvert.KnownCodes;

        Assert.Equal(33, Contract.Length);
        Assert.True(known.SetEquals(Contract), "KnownCodes must equal the contract set.");

        foreach (var entry in entries.Where(e => e.Rule is not null))
            Assert.True(known.Contains(entry.Rule!), $"{entry.Group} v{entry.Version} '{entry.Feature}' names unknown code '{entry.Rule}'.");

        var used = entries.Where(e => e.Rule is not null).Select(e => e.Rule!).ToHashSet();
        foreach (var code in known.Where(c => c != "below.validation-error"))
            Assert.True(used.Contains(code), $"No requires-vN entry maps to '{code}'.");

        foreach (var entry in entries.Where(e => e.Rule is null))
            Assert.True(entry.Group == "content" && entry.Version is "9" or "12", $"{entry.Group} v{entry.Version} '{entry.Feature}' has no rule.");
    }
}
