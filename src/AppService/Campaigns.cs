using System.Text.Json;
using System.Text.Json.Serialization;
using TomeStack.AppService.Packages;
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

    /// <summary>
    /// M6 slice 2: allowed sources that are not installed here yet, because a campaign pack left them out (they were not
    /// the sender's to share). Each is still in <see cref="AllowedSources"/>, so the campaign allows it as soon as it is
    /// installed, in this build and in older ones (which keep this field as an unknown property). This only says what to
    /// get: the title, publisher and license the sender's pack named. Absent when there is none, so a campaign without
    /// one is written exactly as before.
    /// </summary>
    public IReadOnlyList<PendingSource>? PendingSources { get; init; }

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
        if (PendingSources is { } pending)
        {
            if (pending.Any(p => p is null || p.Title is null || p.Publisher is null || p.License is null)
                || pending.Select(p => p.SourceId).Distinct().Count() != pending.Count
                || (AllowedSources is not null && pending.Any(p => !AllowedSources.Contains(p.SourceId))))
            {
                problems.Add(new("campaign.pending-invalid", "Each source waiting to be installed is listed once, is one of the allowed sources, and has a title, publisher and license."));
            }
            else if (pending.Any(p => p.Title.Length is 0 or > MaxNameLength || p.Publisher.Length > MaxNameLength || p.License.Length > MaxNameLength))
            {
                problems.Add(new("campaign.pending-invalid", $"A source waiting to be installed has a title of 1 to {MaxNameLength} characters, and a publisher and license of at most {MaxNameLength}."));
            }
        }
        return problems;
    }
}

/// <summary>
/// M6 slice 2: an allowed source a campaign pack left out, and that is not installed here: what the sender's pack said it
/// is, so you know what to get. It stops being pending once a source with that id is installed.
/// </summary>
public sealed record PendingSource(Guid SourceId, string Title, string Publisher, string License);

/// <summary>How a character stands with its campaign: shown with the sheet, never part of the calculation.</summary>
public sealed record CampaignStatus(Guid CampaignId, string Name, string RulesFamily, IReadOnlyList<Diagnostic> Warnings);

public sealed partial class TomeStackApp
{
    public IReadOnlyList<Campaign> ListCampaigns() => _store.ListCampaigns();

    /// <summary>
    /// <c>campaign.save</c>: creates (id empty) or updates a campaign. Every allowed source must be installed
    /// (<c>campaign.source-missing</c>), except one the stored campaign already has as pending (M6 slice 2: a campaign
    /// pack left it out). Pending entries come only from the stored campaign, never from the request, and one whose source
    /// is now installed, or that is no longer allowed, is dropped. Characters in it are not changed; their warnings follow
    /// the new profile.
    /// </summary>
    public Campaign SaveCampaign(Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var id = campaign.Id == Guid.Empty ? Guid.NewGuid() : campaign.Id;
        var allowed = campaign.AllowedSources ?? [];
        List<PendingSource> pending =
        [
            .. (_store.FindCampaign(id)?.PendingSources ?? [])
                .Where(p => allowed.Contains(p.SourceId) && _store.FindSource(p.SourceId) is null),
        ];
        var saved = campaign with
        {
            Id = id,
            Name = campaign.Name?.Trim() ?? "",
            HouseRules = string.IsNullOrWhiteSpace(campaign.HouseRules) ? null : campaign.HouseRules.Trim(),
            PendingSources = pending.Count == 0 ? null : pending,
            UpdatedAt = _time.GetUtcNow(),
        };
        var problems = saved.Validate().ToList();
        if (problems.Count == 0)
        {
            foreach (var missing in allowed.Where(s => _store.FindSource(s) is null && !pending.Any(p => p.SourceId == s)))
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
        foreach (var (reference, revision) in OutsideCampaign(character, sheet, campaign, catalog))
        {
            var source = catalog.FindSource(revision.Provenance.SourceId)?.Title ?? "an unknown source";
            warnings.Add(character.CampaignExceptions.LastOrDefault(e => e.Content == reference) is { } exception
                ? new("campaign.exception", $"'{revision.Name}' is from {source}, which '{campaign.Name}' does not allow; used by exception: {exception.Reason}", reference)
                : new("campaign.source-not-allowed", $"'{revision.Name}' is from {source}, which '{campaign.Name}' does not allow. Remove it or record an exception with a reason.", reference));
        }
        return new(campaign.Id, campaign.Name, campaign.RulesFamily, warnings);
    }

    /// <summary>The character's own active references (pins, classes, chosen options, equipment) from sources <paramref name="campaign"/> does not allow.</summary>
    private static IEnumerable<(ContentReference Reference, ContentRevision Revision)> OutsideCampaign(
        Character character, CharacterSheet sheet, Campaign campaign, IContentCatalog catalog)
    {
        var allowed = campaign.AllowedSources.ToHashSet();
        var active = (sheet.Active ?? []).ToHashSet();
        foreach (var reference in character.AllReferences().Where(active.Contains))
        {
            if (catalog.FindRevision(reference) is { } revision && !allowed.Contains(revision.Provenance.SourceId))
                yield return (reference, revision);
        }
    }

    /// <summary>
    /// M6 slice 2: before a campaign pack replaces <paramref name="local"/> with <paramref name="imported"/> ("use the
    /// imported one"), the characters in it whose content the imported profile would no longer allow. Content they already
    /// use by a recorded exception, or that the local profile does not allow either, is not counted: only what would newly
    /// become "not allowed". Nothing is written.
    /// </summary>
    private IReadOnlyList<CampaignImpact> CampaignImpactOf(Campaign local, Campaign imported)
    {
        var impact = new List<CampaignImpact>();
        foreach (var character in _store.ListCharacters().Where(c => c.CampaignId == local.Id).OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var sheet = CharacterCalculator.Calculate(character, _store);
            var before = OutsideCampaign(character, sheet, local, _store).Select(o => o.Reference).ToHashSet();
            var excepted = character.CampaignExceptions.Select(e => e.Content).ToHashSet();
            List<string> newly =
            [
                .. OutsideCampaign(character, sheet, imported, _store)
                    .Where(o => !before.Contains(o.Reference) && !excepted.Contains(o.Reference))
                    .Select(o => o.Revision.Name).Distinct().Order(StringComparer.Ordinal),
            ];
            var familyChanges = imported.RulesFamily != local.RulesFamily && character.RulesFamily == local.RulesFamily;
            if (newly.Count > 0 || familyChanges)
                impact.Add(new(local.Id, character.Id, character.Name, newly, familyChanges ? imported.RulesFamily : null));
        }
        return impact;
    }
}
