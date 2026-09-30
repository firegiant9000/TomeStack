using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// M6 slice 2 (ROADMAP "M6 plan", B13): campaign packs. A campaign pack (<see cref="PackageScope.Campaign"/>, format v8,
/// provisional until the slice merges) shares one campaign profile: its name, rules family, allowed sources and house
/// rules, plus the published content of the allowed sources that pass the source-pack guard (<see cref="ShareProblem"/>).
/// Bundled SRD sources are referenced by id and never copied; every other allowed source is left out and listed in
/// <c>omitted[]</c>, and the receiver records it as pending until it is installed. A pack never carries characters, gap
/// notes, drafts, PDFs, paths or machine-local ids. Documented in docs/features/package-format.md ("Campaign packs").
/// </summary>
public sealed partial class PackageService
{
    public const string CampaignAttachmentPolicy = "A campaign pack never includes characters, gap notes, drafts, PDFs or text read from PDFs.";

    /// <summary>M6 slice 2: set by the app, which can calculate characters (the preview of "use the imported one").</summary>
    private Func<Campaign, Campaign, IReadOnlyList<CampaignImpact>>? _campaignImpact;

    internal void SetCampaignImpact(Func<Campaign, Campaign, IReadOnlyList<CampaignImpact>> impact) => _campaignImpact = impact;

    private sealed record CampaignPackPlan(
        Campaign Campaign, List<SourceRecord> Included, List<SourceRecord> Referenced, List<LeftOutSource> LeftOut,
        List<ContentRevision> Revisions, List<Diagnostic> Warnings, string FileName);

    /// <summary><c>package.campaignPackPreview</c>: what a campaign pack of <paramref name="campaignId"/> would hold. Writes nothing.</summary>
    public CampaignPackPreview PreviewCampaignPack(Guid campaignId)
    {
        var plan = PlanCampaignPack(campaignId);
        return new(plan.FileName, plan.Campaign.Id, plan.Campaign.Name, plan.Campaign.RulesFamily,
            [.. plan.Included.Select(Notice)], [.. plan.Referenced.Select(Notice)], plan.LeftOut, plan.Revisions.Count, plan.Warnings);
    }

    /// <summary><c>package.campaignPackExport</c> / <c>package.campaignPackSaveAs</c>: the pack's bytes.</summary>
    public ExportResult ExportCampaignPack(Guid campaignId)
    {
        var plan = PlanCampaignPack(campaignId);
        var files = new SortedDictionary<string, (string Kind, byte[] Bytes)>(StringComparer.Ordinal)
        {
            [$"campaigns/{plan.Campaign.Id:D}.json"] = ("campaign", Json(ForCampaignPack(plan.Campaign))),
        };
        foreach (var source in plan.Included)
            files[$"sources/{source.Id:D}.json"] = ("source", Json(ForSourcePack(source)));
        foreach (var revision in plan.Revisions)
            files[$"content/{revision.RevisionId:D}.json"] = ("contentRevision", Json(revision));
        CheckPackLimits(files, "Allow fewer homebrew sources in the campaign, or share them as source packs.");

        var manifest = new PackageManifest
        {
            FormatVersion = PackageManifest.CampaignFormatVersion,
            Scope = PackageScope.Campaign,
            Purpose = ExportPurpose.Share,
            CreatedAt = time.GetUtcNow(),
            AppVersion = typeof(PackageService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Entries = [.. files.Select(f => new PackageEntry(f.Key, f.Value.Kind, Hash(f.Value.Bytes), f.Value.Bytes.LongLength))],
            Notices = [.. plan.Included.Select(Notice)],
            // No characters go with a campaign pack, so no revision is listed: only what to get.
            Omitted = [.. plan.LeftOut.Select(l => new OmittedSource(l.SourceId, l.Title, l.Publisher, l.License, []))],
            RevisionOrder = [.. plan.Revisions.Select(r => r.RevisionId)],
            Attestations = [.. plan.Included.Select(s => new SourceAttestation(s.Id, TomeStackApp.OwnWorkStatement, s.ShareConfirmedAt!.Value))],
            AttachmentPolicy = CampaignAttachmentPolicy,
        };
        return new ExportResult(plan.FileName, Zip(manifest, files), manifest);
    }

    /// <summary>
    /// Sorts the campaign's allowed sources: bundled ones are referenced; ones that pass the guard (D14 item 6, which runs
    /// at every campaign-pack export) and have published content are carried; every other one is left out, with why.
    /// </summary>
    private CampaignPackPlan PlanCampaignPack(Guid campaignId)
    {
        var campaign = store.FindCampaign(campaignId)
            ?? throw new PackageException([new("pack.campaign-missing", $"Campaign {campaignId} does not exist.")]);
        var pending = (campaign.PendingSources ?? []).ToDictionary(p => p.SourceId);
        var all = store.ListRevisionsInOrder();
        var withContent = all.Where(r => r.Status == RevisionStatus.Published).Select(r => r.Provenance.SourceId).ToHashSet();
        // A content whose revisions sit in two sources cannot leave with one of them (as for a source pack). Here that
        // source is left out, not the whole pack refused (review fix): the profile is what is being shared.
        var spanning = all.GroupBy(r => r.ContentId).Where(g => g.Select(r => r.Provenance.SourceId).Distinct().Count() > 1)
            .SelectMany(g => g.Select(r => (Source: r.Provenance.SourceId, Content: g.Last())))
            .DistinctBy(s => s.Source).ToDictionary(s => s.Source, s => s.Content);
        List<SourceRecord> included = [], referenced = [];
        List<LeftOutSource> leftOut = [];
        // Titles and publishers have no length limit here, but a pending entry does: the pack names each one as the receiver
        // will store it (review fix).
        LeftOutSource Left(Guid id, string? title, string? publisher, string? license, Diagnostic reason)
        {
            var named = PendingSource.Clamped(id, title, publisher, license);
            return new(id, named.Title, named.Publisher, named.License, reason);
        }
        foreach (var id in campaign.AllowedSources.Distinct())
        {
            var source = store.FindSource(id);
            if (source is not null && _bundledSources.Contains(id))
                referenced.Add(source);
            else if (source is null)
            {
                // Not installed here: pending from an earlier campaign pack, so its name is known, or unknown.
                var known = pending.GetValueOrDefault(id);
                leftOut.Add(Left(id, known?.Title, known?.Publisher, known?.License,
                    new("pack.source-missing", $"'{known?.Title ?? id.ToString()}' is not installed here, so the pack only names it.")));
            }
            else if (ShareProblem(id, source) is { } problem)
                leftOut.Add(Left(id, source.Title, source.Publisher, source.License, problem));
            else if (!withContent.Contains(id))
                leftOut.Add(Left(id, source.Title, source.Publisher, source.License, new("pack.source-empty", $"'{source.Title}' has no published content to share yet.")));
            else if (spanning.TryGetValue(id, out var split))
                leftOut.Add(Left(id, source.Title, source.Publisher, source.License, new("pack.content-spans-sources", $"'{split.Name}' has revisions in '{source.Title}' and in another source, so '{source.Title}' cannot be shared from here.", split.Reference)));
            else
                included.Add(source);
        }

        var errors = new List<Diagnostic>();
        var (revisions, _, warnings) = PackContent(included, errors);
        if (errors.Count > 0)
            throw new PackageException(errors);
        return new CampaignPackPlan(campaign, included, referenced, leftOut, revisions, warnings, $"{SafeFileName(campaign.Name)}-campaign-pack.tomestack.zip");
    }

    /// <summary>
    /// A campaign as a campaign pack writes it: no pending list (the manifest's <c>omitted[]</c> names what to get) and no
    /// unknown properties, whose content this build cannot vouch for (they could hold a path).
    /// </summary>
    private static Campaign ForCampaignPack(Campaign campaign) => campaign with { PendingSources = null, Extensions = null };

    /// <summary>
    /// A campaign pack's own rules, on top of a source pack's (<see cref="CheckSourcePack"/>, which runs on its sources and
    /// revisions too): exactly one campaign; no source it does not allow; every allowed source carried, installed here, or
    /// named in <c>omitted[]</c>, and a named one that is not installed becomes pending with a warning.
    /// </summary>
    private void CheckCampaignPack(ParsedPackage parsed, List<Diagnostic> errors, List<Diagnostic> warnings)
    {
        if (parsed.Campaigns.Count != 1)
        {
            errors.Add(new("pack.campaign-count", $"A campaign pack carries exactly one campaign; this one has {parsed.Campaigns.Count}."));
            return;
        }
        var campaign = parsed.Campaigns[0];
        var carried = parsed.Sources.Select(s => s.Id).ToHashSet();
        var allowed = campaign.AllowedSources.ToHashSet();
        foreach (var stray in parsed.Sources.Where(s => !allowed.Contains(s.Id)))
            errors.Add(new("pack.source-not-allowed", $"The pack carries '{stray.Title}', which its campaign does not allow."));
        var named = new Dictionary<Guid, OmittedSource>();
        foreach (var omitted in parsed.Manifest.Omitted)
            named.TryAdd(omitted.SourceId, omitted); // a repeated entry in the untrusted manifest is harmless
        foreach (var id in campaign.AllowedSources)
        {
            if (carried.Contains(id) || store.FindSource(id) is not null)
                continue;
            if (named.TryGetValue(id, out var omitted))
                warnings.Add(new("campaign.source-pending", $"'{campaign.Name}' allows '{omitted.Title}' ({omitted.Publisher}, {omitted.License}), which the sender left out because it was not theirs to share. The campaign records it as waiting until you install it yourself (unless you keep a version of the campaign that does not allow it)."));
            else
                errors.Add(new("pack.campaign-source-unlisted", $"'{campaign.Name}' allows source {id}, which the pack neither carries nor names."));
        }
    }

    /// <summary>
    /// The campaign a campaign pack stores here: the sender's profile, with a pending entry for each allowed source that is
    /// still not installed (named by the pack's <c>omitted[]</c>), and no unknown properties. Called inside the import
    /// transaction, after the pack's sources are written.
    /// </summary>
    private Campaign ForReceiver(Campaign imported, PackageManifest manifest)
    {
        var stored = imported with { PendingSources = NamedPending(imported.AllowedSources, manifest, []), Extensions = null };
        // The manifest's text is clamped (PendingSource.Clamped), so this holds; checked anyway, because a campaign that
        // fails its own validation would make every later backup of the library refuse to restore (review fix).
        if (stored.Validate() is { Count: > 0 } problems)
            throw new PackageException([.. problems.Select(p => p with { Message = $"Campaign '{imported.Name}': {p.Message}" })]);
        return stored;
    }

    /// <summary>
    /// Pending entries for <paramref name="allowed"/> sources that are not installed here, named by the pack's
    /// <c>omitted[]</c> (clamped), after the ones in <paramref name="already"/>. Null when there is none.
    /// </summary>
    private List<PendingSource>? NamedPending(IEnumerable<Guid> allowed, PackageManifest manifest, IReadOnlyList<PendingSource> already)
    {
        // Sets and a dictionary, not list scans: both lists come from an untrusted pack (M6 stack review).
        List<PendingSource> pending = [.. already];
        var listed = pending.Select(p => p.SourceId).ToHashSet();
        var named = new Dictionary<Guid, OmittedSource>();
        foreach (var omitted in manifest.Omitted)
            named.TryAdd(omitted.SourceId, omitted);
        foreach (var id in allowed.Where(id => !listed.Contains(id) && store.FindSource(id) is null))
        {
            if (named.TryGetValue(id, out var source) && listed.Add(id))
                pending.Add(PendingSource.Clamped(id, source.Title, source.Publisher, source.License));
        }
        return pending.Count == 0 ? null : pending;
    }

    /// <summary>
    /// "Keep mine", or a pack whose profile is the same as the local one: the local profile stays, but a source the pack
    /// names and this machine does not have is recorded as pending if the local profile allows it and has no entry for it
    /// yet, as the preview says (review fix). Returns null when nothing is added.
    /// </summary>
    private Campaign? WithPackPending(Campaign local, PackageManifest manifest)
    {
        var current = local.PendingSources ?? [];
        var merged = NamedPending(local.AllowedSources, manifest, current);
        return merged is null || merged.Count == current.Count ? null : local with { PendingSources = merged };
    }

    /// <summary>
    /// What differs between a local campaign and a campaign pack's copy, by what the profile means: name, rules family,
    /// allowed sources (as a set, by title where known) and house rules. The save time and pending entries are not a
    /// difference.
    /// </summary>
    private List<FieldChange> CampaignChanges(Campaign local, Campaign imported, IReadOnlyDictionary<Guid, SourceRecord> packSources)
    {
        string Titles(IEnumerable<Guid> ids) => string.Join(", ", ids
            .Select(id => packSources.GetValueOrDefault(id)?.Title ?? store.FindSource(id)?.Title ?? id.ToString())
            .Order(StringComparer.Ordinal));
        var changes = new List<FieldChange>();
        if (local.Name != imported.Name)
            changes.Add(new("name", local.Name, imported.Name));
        if (local.RulesFamily != imported.RulesFamily)
            changes.Add(new("rulesFamily", local.RulesFamily, imported.RulesFamily));
        if (!local.AllowedSources.ToHashSet().SetEquals(imported.AllowedSources))
            changes.Add(new("allowedSources", Titles(local.AllowedSources), Titles(imported.AllowedSources)));
        if ((local.HouseRules ?? "") != (imported.HouseRules ?? ""))
            changes.Add(new("houseRules", local.HouseRules, imported.HouseRules));
        return changes;
    }
}
