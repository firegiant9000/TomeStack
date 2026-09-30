using System.Text.Json;
using System.Text.Json.Serialization;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// SPEC P-01, BACKLOG B12 (M2 item 7): a local campaign profile. It groups characters and says which rules family and
/// which sources are allowed. Its rules never change calculation: content from a source it does not allow is
/// flagged on the character (unless the player recorded an exception with a reason) and hidden or disabled in pickers.
/// </summary>
public sealed record Campaign
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxNameLength = 200;
    public const int MaxHouseRulesLength = 10_000;

    public required Guid Id { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Name { get; init; }
    public required string RulesFamily { get; init; }

    /// <summary>Sources whose content characters in this campaign may use.</summary>
    public IReadOnlyList<Guid> AllowedSources { get; init; } = [];

    /// <summary>Free text: house rules and notes. Not interpreted.</summary>
    public string? HouseRules { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    public IReadOnlyList<Diagnostic> Validate()
    {
        var problems = new List<Diagnostic>();
        if (SchemaVersion is < 1 or > CurrentSchemaVersion)
            problems.Add(new("campaign.schema-unsupported", $"Campaign data uses schema v{SchemaVersion}; this version supports v1 to v{CurrentSchemaVersion}. Update TomeStack to open it."));
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > MaxNameLength)
            problems.Add(new("campaign.name-required", $"A campaign needs a name of 1 to {MaxNameLength} characters."));
        if (!RulesFamilies.IsKnown(RulesFamily))
            problems.Add(new("campaign.rules-family-unknown", $"Rules family '{RulesFamily}' is not supported."));
        if (AllowedSources is null || AllowedSources.Distinct().Count() != AllowedSources.Count)
            problems.Add(new("campaign.sources-invalid", "Each allowed source is listed once."));
        if (HouseRules is { Length: > MaxHouseRulesLength })
            problems.Add(new("campaign.house-rules-too-long", $"House rules are at most {MaxHouseRulesLength} characters."));
        return problems;
    }
}

/// <summary>How a character stands with its campaign: shown with the sheet, never part of the calculation.</summary>
public sealed record CampaignStatus(Guid CampaignId, string Name, string RulesFamily, IReadOnlyList<Diagnostic> Warnings);

public sealed partial class TomeStackApp
{
    public IReadOnlyList<Campaign> ListCampaigns() => _store.ListCampaigns();

    /// <summary>
    /// <c>campaign.save</c>: creates (id empty) or updates a campaign. Every allowed source must be installed
    /// (<c>campaign.source-missing</c>). Characters in it are not changed; their warnings follow the new profile.
    /// </summary>
    public Campaign SaveCampaign(Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var saved = campaign with
        {
            Id = campaign.Id == Guid.Empty ? Guid.NewGuid() : campaign.Id,
            Name = campaign.Name?.Trim() ?? "",
            HouseRules = string.IsNullOrWhiteSpace(campaign.HouseRules) ? null : campaign.HouseRules.Trim(),
            UpdatedAt = _time.GetUtcNow(),
        };
        var problems = saved.Validate().ToList();
        if (problems.Count == 0)
        {
            foreach (var missing in saved.AllowedSources.Where(s => _store.FindSource(s) is null))
                problems.Add(new("campaign.source-missing", $"Source {missing} is not installed."));
        }
        if (problems.Count > 0)
            throw new AppValidationException(problems);
        _store.InTransaction(() => _store.SaveCampaign(saved));
        return saved;
    }

    /// <summary><c>campaign.delete</c>: refused while characters are in it (<c>campaign.in-use</c>), so none is left pointing nowhere.</summary>
    public void DeleteCampaign(Guid campaignId)
    {
        var members = _store.ListCharacters().Where(c => c.CampaignId == campaignId).Select(c => c.Name).ToList();
        if (members.Count > 0)
            throw new AppValidationException([new("campaign.in-use", $"The campaign still has characters: {string.Join(", ", members)}. Move them out first.")]);
        _store.InTransaction(() => _store.DeleteCampaign(campaignId));
    }

    /// <summary>
    /// SPEC P-01: the campaign warnings for a character. Its own references (pins, classes, chosen options, equipment)
    /// must come from allowed sources; granted content follows what grants it. A recorded exception turns the warning
    /// into a note with the reason. A rules family other than the campaign's is also flagged.
    /// </summary>
    /// <param name="catalog">Where revisions are looked up: the store, or the sandbox overlay (M5 slice 3), so an unsaved draft is checked too.</param>
    private CampaignStatus? CampaignOf(Character character, CharacterSheet sheet, IContentCatalog? catalog = null)
    {
        catalog ??= _store;
        if (character.CampaignId is not { } id)
            return null;
        if (_store.FindCampaign(id) is not { } campaign)
            return new(id, "(missing campaign)", character.RulesFamily, [new("campaign.missing", "This character's campaign is not on this machine. Its source rules are not checked.")]);
        var warnings = new List<Diagnostic>();
        if (campaign.RulesFamily != character.RulesFamily)
            warnings.Add(new("campaign.rules-family-mismatch", $"'{campaign.Name}' plays {campaign.RulesFamily}; this character uses {character.RulesFamily}."));
        var allowed = campaign.AllowedSources.ToHashSet();
        var active = (sheet.Active ?? []).ToHashSet();
        foreach (var reference in character.AllReferences().Where(active.Contains))
        {
            if (catalog.FindRevision(reference) is not { } revision || allowed.Contains(revision.Provenance.SourceId))
                continue;
            var source = catalog.FindSource(revision.Provenance.SourceId)?.Title ?? "an unknown source";
            warnings.Add(character.CampaignExceptions.LastOrDefault(e => e.Content == reference) is { } exception
                ? new("campaign.exception", $"'{revision.Name}' is from {source}, which '{campaign.Name}' does not allow; used by exception: {exception.Reason}", reference)
                : new("campaign.source-not-allowed", $"'{revision.Name}' is from {source}, which '{campaign.Name}' does not allow. Remove it or record an exception with a reason.", reference));
        }
        return new(campaign.Id, campaign.Name, campaign.RulesFamily, warnings);
    }
}
