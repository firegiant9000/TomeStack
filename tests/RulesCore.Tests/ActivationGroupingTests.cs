using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>Wave 3 item 22: a toggle's and a resource's <c>activation</c> reaches the sheet from v10 revisions only.</summary>
public class ActivationGroupingTests
{
    private static readonly IReadOnlyList<Effect> Extras =
    [
        new ResourceEffect { Id = "pool", ResourceId = "pool", Label = "Testy pool", Maximum = "2", Activation = Activation.BonusAction },
        new ResourceEffect { Id = "plain-pool", ResourceId = "plain-pool", Label = "Testy plain pool", Maximum = "3" },
    ];

    private static ContentRevision Revision(int schema, Activation? activation)
    {
        var revision = ToggleAndCostTests.AutoRevision(RulesFamilies.Srd521, schema: schema, auto: false, more: Extras);
        return revision with { Effects = [.. revision.Effects.Select(e => e is ToggleEffect t ? t with { Activation = activation } : e)] };
    }

    private static CharacterSheet Sheet(ContentRevision revision) =>
        CharacterCalculator.Calculate(ToggleAndCostTests.Hero(revision, null), ToggleAndCostTests.CatalogWith(revision));

    [Fact]
    public void A_v10_toggle_and_resource_carry_their_activation_to_the_sheet_and_none_is_written_when_absent()
    {
        var sheet = Sheet(Revision(10, Activation.Reaction));

        Assert.Equal(Activation.Reaction, sheet.Toggles!.Single().Activation);
        Assert.Equal(Activation.BonusAction, sheet.Resources!.Single(r => r.ResourceId == "pool").Activation);
        Assert.Null(sheet.Resources!.Single(r => r.ResourceId == "plain-pool").Activation);

        var plain = Sheet(Revision(10, null));
        Assert.Null(plain.Toggles!.Single().Activation);
        var json = JsonSerializer.Serialize(plain.Toggles, RulesJson.Compact);
        Assert.DoesNotContain("activation", json, StringComparison.Ordinal);
        Assert.Contains("\"activation\":\"bonusAction\"", JsonSerializer.Serialize(sheet.Resources, RulesJson.Compact), StringComparison.Ordinal);
    }

    [Fact]
    public void In_a_v9_revision_activation_is_extension_data_and_never_reaches_the_sheet()
    {
        // "Stored by an older build": the v10 revision's JSON re-read as v9, so activation stays extension data on both effects.
        var json = JsonSerializer.Serialize(Revision(10, Activation.Reaction), RulesJson.Options).Replace("\"schemaVersion\": 10", "\"schemaVersion\": 9", StringComparison.Ordinal);
        Assert.Contains("\"activation\": \"reaction\"", json, StringComparison.Ordinal);
        var v9 = JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Options)!;
        Assert.Null(v9.Effects.OfType<ToggleEffect>().Single().Activation);
        Assert.All(v9.Effects.OfType<ResourceEffect>(), r => Assert.Null(r.Activation));

        var sheet = Sheet(v9);
        Assert.Null(sheet.Toggles!.Single().Activation);
        Assert.All(sheet.Resources!, r => Assert.Null(r.Activation));
        var without = Sheet(ToggleAndCostTests.AutoRevision(RulesFamilies.Srd521, schema: 9, auto: false, more: [
            new ResourceEffect { Id = "pool", ResourceId = "pool", Label = "Testy pool", Maximum = "2" },
            new ResourceEffect { Id = "plain-pool", ResourceId = "plain-pool", Label = "Testy plain pool", Maximum = "3" }]));
        Assert.Equal(JsonSerializer.Serialize(without, RulesJson.Options), JsonSerializer.Serialize(sheet, RulesJson.Options)); // identical to a revision without the key
    }
}
