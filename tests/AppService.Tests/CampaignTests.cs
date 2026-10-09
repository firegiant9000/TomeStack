using System.IO.Compression;
using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 item 7, SPEC P-01, BACKLOG B12, MVP "Campaign": local profiles with a rules family and allowed sources. Two
/// profiles show different allowed content; content outside a profile warns unless a reasoned exception is recorded;
/// campaigns travel in packages (DoD 5).
/// </summary>
public class CampaignTests
{
    private static readonly Guid Srd521Source = Guid.Parse("52500000-0000-4000-8000-000000000001");
    private static readonly Guid FixtureShared = Guid.Parse("5f0d5000-0000-4000-8000-000000000001");
    private static readonly Guid Fixture2024 = Guid.Parse("5f0d5210-0000-4000-8000-000000000001");
    private static readonly Guid EquipmentFixtures = Guid.Parse("5f4d5000-0000-4000-8000-000000000001");

    private static Campaign New(string name, params Guid[] sources) =>
        new() { Id = Guid.Empty, Name = name, RulesFamily = RulesFamilies.Srd521, AllowedSources = sources, HouseRules = "Test notes." };

    [Fact]
    public void Two_profiles_show_different_allowed_content()
    {
        using var temp = new TempApp();
        var strict = temp.App.SaveCampaign(New("SRD only", Srd521Source));
        var open = temp.App.SaveCampaign(New("SRD and fixtures", Srd521Source, Fixture2024, FixtureShared, EquipmentFixtures));

        var inStrict = temp.App.ListContent(RulesFamilies.Srd521, strict.Id);
        var inOpen = temp.App.ListContent(RulesFamilies.Srd521, open.Id);

        var courier = inStrict.Single(o => o.Name == "Fixture Courier");
        Assert.False(courier.AllowedInCampaign);
        Assert.True(inOpen.Single(o => o.Name == "Fixture Courier").AllowedInCampaign);
        Assert.True(inStrict.Single(o => o.Name == "Soldier").AllowedInCampaign);
        Assert.NotEqual(inStrict.Count(o => o.AllowedInCampaign == true), inOpen.Count(o => o.AllowedInCampaign == true));
        Assert.All(temp.App.ListContent(RulesFamilies.Srd521), o => Assert.Null(o.AllowedInCampaign)); // no campaign: no opinion
    }

    [Fact]
    public void Content_outside_the_campaign_warns_and_a_reasoned_exception_turns_it_into_a_note()
    {
        using var temp = new TempApp();
        var strict = temp.App.SaveCampaign(New("SRD only", Srd521Source));
        var courier = TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = strict.Id };

        var view = temp.App.SaveCharacter(courier);

        Assert.Equal("SRD only", view.Campaign!.Name);
        var warnings = view.Campaign.Warnings.Where(w => w.Code == "campaign.source-not-allowed").ToList();
        Assert.Equal(courier.AllReferences().Count(view.Sheet.Active!.Contains), warnings.Count); // every active pin is fixture content (inactive ones are not checked)
        Assert.All(warnings, w => Assert.Contains("TomeStack Fixtures", w.Message, StringComparison.Ordinal));
        Assert.Equal(TempApp.Json(CharacterCalculator.Calculate(courier with { CampaignId = null }, temp.App.Store).Fields), TempApp.Json(view.Sheet.Fields)); // never changes calculation

        var excepted = temp.App.SaveCharacter(view.Character with { CampaignExceptions = [new(warnings[0].Content!, "DM approved")] });
        Assert.Equal(warnings.Count - 1, excepted.Campaign!.Warnings.Count(w => w.Code == "campaign.source-not-allowed"));
        Assert.Contains(excepted.Campaign.Warnings, w => w.Code == "campaign.exception" && w.Content == warnings[0].Content && w.Message.Contains("DM approved", StringComparison.Ordinal));
        Assert.Contains(
            Assert.Throws<AppValidationException>(() => temp.App.SaveCharacter(view.Character with { CampaignExceptions = [new(warnings[0].Content!, " ")] })).Problems,
            p => p.Code == "character.exception-reason-required");
    }

    [Fact]
    public void A_recorded_spell_from_a_source_the_campaign_does_not_allow_warns_until_an_exception_is_recorded()
    {
        using var temp = new TempApp();
        var spellFixtures = Guid.Parse("5f5d5000-0000-4000-8000-000000000001");
        var campaign = temp.App.SaveCampaign(New("Fixture spells only", spellFixtures));
        var arcanist = new ContentReference(Guid.Parse("5f5dc000-0000-4000-8000-000000000001"), Guid.Parse("5f5de000-0000-4000-8000-000000000001"));
        var spark = new ContentReference(Guid.Parse("5f5dc000-0000-4000-8000-000000000011"), Guid.Parse("5f5de000-0000-4000-8000-000000000011"));
        var srdSpells = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-spells.json");
        var outside = srdSpells.Revisions.Last(r => r.Name == "Fire Bolt" && r.Kind == ContentKind.Spell).Reference;
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Test Spell Camper", RulesFamily = RulesFamilies.Srd521, Level = 1, CampaignId = campaign.Id,
            Classes = [new(arcanist, 1)], BaseAbilities = new(8, 14, 12, 16, 10, 10),
            Spells = [new(arcanist.ContentId, spark), new(arcanist.ContentId, outside)],
        };

        var view = temp.App.SaveCharacter(character);

        // Only the spell from the other source is flagged; the campaign's own spell and class are not.
        var flagged = Assert.Single(view.Campaign!.Warnings, w => w.Code == "campaign.source-not-allowed");
        Assert.Equal(outside, flagged.Content);

        var excepted = temp.App.SaveCharacter(view.Character with { CampaignExceptions = [new(outside, "DM approved")] });
        Assert.DoesNotContain(excepted.Campaign!.Warnings, w => w.Code == "campaign.source-not-allowed");
        Assert.Contains(excepted.Campaign.Warnings, w => w.Code == "campaign.exception" && w.Content == outside && w.Message.Contains("DM approved", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rules_family_other_than_the_campaigns_is_flagged()
    {
        using var temp = new TempApp();
        var campaign = temp.App.SaveCampaign(New("2024 table", Srd521Source));

        var view = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json") with { CampaignId = campaign.Id });

        Assert.Contains(view.Campaign!.Warnings, w => w.Code == "campaign.rules-family-mismatch");
    }

    [Fact]
    public void Campaigns_are_validated_and_cannot_be_deleted_while_in_use()
    {
        using var temp = new TempApp();

        Assert.Contains(Assert.Throws<AppValidationException>(() => temp.App.SaveCampaign(New(" "))).Problems, p => p.Code == "campaign.name-required");
        Assert.Contains(Assert.Throws<AppValidationException>(() => temp.App.SaveCampaign(New("X", Guid.NewGuid()))).Problems, p => p.Code == "campaign.source-missing");
        Assert.Contains(Assert.Throws<AppValidationException>(() => temp.App.SaveCampaign(New("X", Srd521Source, Srd521Source))).Problems, p => p.Code == "campaign.sources-invalid");

        var campaign = temp.App.SaveCampaign(New("Table", Srd521Source));
        temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = campaign.Id });
        Assert.Equal("campaign.in-use", Assert.Throws<AppValidationException>(() => temp.App.DeleteCampaign(campaign.Id)).Problems[0].Code);

        var updated = temp.App.SaveCampaign(campaign with { Name = "Renamed table" });
        Assert.Equal((campaign.Id, "Renamed table"), (updated.Id, Assert.Single(temp.App.ListCampaigns()).Name));
    }

    [Fact]
    public void The_campaign_travels_in_the_package_to_a_clean_machine()
    {
        using var origin = new TempApp();
        var campaign = origin.App.SaveCampaign(New("Travelling table", Srd521Source, Fixture2024, FixtureShared));
        var character = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = campaign.Id });
        var package = origin.App.ExportCharacters([character.Character.Id]);

        Assert.Contains(package.Manifest.Entries, e => e.Path == $"campaigns/{campaign.Id:D}.json" && e.Kind == "campaign");
        using (var zip = new ZipArchive(new MemoryStream(package.Content)))
        {
            using var document = JsonDocument.Parse(new StreamReader(zip.GetEntry($"campaigns/{campaign.Id:D}.json")!.Open()).ReadToEnd());
            Assert.Equal("", SchemaTests.Validate("campaign", document.RootElement));
        }

        using var clean = new TempApp();
        var preview = clean.App.PreviewImport(package.Content);
        Assert.Contains(preview.Items, i => i.Kind == "campaign" && i.Name == "Travelling table" && i.Action == Packages.PackageItemAction.Add);
        clean.App.ApplyImport(package.Content);

        var imported = Assert.Single(clean.App.ListCampaigns());
        Assert.Equal(TempApp.Json(campaign), TempApp.Json(imported));
        Assert.Equal(TempApp.Json(character.Campaign), TempApp.Json(clean.App.GetCharacter(character.Character.Id).Campaign));
    }
}
