using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// One line of what "Save for an older TomeStack" (D85) changes in an export, shown in the preview and hashed into
/// <c>changesHash</c> so a save presents exactly the list the user confirmed.
/// </summary>
/// <param name="Entry">The package path the change concerns (<c>characters/&lt;id&gt;.json</c>, <c>content/&lt;id&gt;.json</c>).</param>
/// <param name="Kind"><c>character</c> or <c>content</c>.</param>
/// <param name="SourceTitle">For content, the title of the source it belongs to.</param>
/// <param name="Action"><c>dropped</c>, <c>blocks</c>, <c>copied</c> or <c>leftOut</c>.</param>
/// <param name="Code"><c>package.target-drops</c> for a dropped change, <c>package.target-blocked</c> for a blocking one.</param>
public sealed record ExportChange(string Entry, string Kind, Guid Id, string Name, string? SourceTitle,
    string Action, string Code, string Message);

/// <summary>An export plan lowered for an older release: the plan to write, what it loses, and the hash of that list.</summary>
internal sealed record LoweredPlan(PackageService.ExportPlan Plan, IReadOnlyList<ExportChange> Changes, string ChangesHash, bool Blocked, LadderRow Target);

/// <summary>
/// The marker a down-converted copy carries in its extensions (D85 part 2): the original revision it was lowered from and
/// the schema version it was lowered to. Import re-pins a copy to its original when the marker and the bytes verify.
/// </summary>
public sealed record DownConvertedFrom(Guid RevisionId, int SchemaVersion);

public static class DownConversion
{
    public const string DropsCode = "package.target-drops";
    public const string BlockedCode = "package.target-blocked";
    public const string ChangedCode = "package.target-changed";
    public const string CopiedCode = "package.target-copied";
    public const string LeftOutCode = "package.target-left-out";

    /// <summary>The extensions key of the <see cref="DownConvertedFrom"/> marker. Fixed: changing it breaks re-pinning.</summary>
    public const string MarkerKey = "downConvertedFrom";

    /// <summary>The fixed namespace for copy ids (D85 part 2). Never change it: every saved copy id depends on it.</summary>
    public static readonly Guid Namespace = new("5b2e7c4a-9d31-4f6e-8a07-2c1d3e4f5a6b");

    /// <summary>RFC 4122 §4.3 name-based (SHA-1) UUID, version 5.</summary>
    public static Guid Uuid5(Guid ns, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Span<byte> nsBytes = stackalloc byte[16];
        ns.TryWriteBytes(nsBytes, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[nsBytes.Length + nameBytes.Length];
        nsBytes.CopyTo(input);
        nameBytes.CopyTo(input, nsBytes.Length);
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(input, hash);
        var b = hash[..16];
        b[6] = (byte)((b[6] & 0x0F) | 0x50);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b, bigEndian: true);
    }

    /// <summary>
    /// The id of a lowered copy (D85 part 2): a UUIDv5 over the original, the target schema and the SHA-256 of the copy's
    /// <see cref="CanonicalBytes"/>, so the same lowering always gives the same id and different bytes never share one.
    /// </summary>
    public static Guid CopyId(Guid rootOriginal, int targetContentSchema, ReadOnlySpan<byte> loweredBytes)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(loweredBytes));
        return Uuid5(Namespace, FormattableString.Invariant($"{rootOriginal:D}:{targetContentSchema}:{digest}"));
    }

    /// <summary>
    /// The bytes a copy is identified by: the copy as the package writer serializes it, with the revision id zeroed and the
    /// <see cref="MarkerKey"/> removed, so a copy and the bytes it was lowered from hash the same way. The canonical form is
    /// LF (every <c>\r\n</c> becomes <c>\n</c>; written entries are unchanged). Extension keys keep their serialized order, so
    /// the form is order-sensitive by design: a reordered extension is a different copy.
    /// </summary>
    public static byte[] CanonicalBytes(ContentRevision copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        var extensions = copy.Extensions?.Where(p => p.Key != MarkerKey).ToDictionary(p => p.Key, p => p.Value);
        if (extensions is { Count: 0 })
            extensions = null;
        var written = PackageService.Json(copy with { RevisionId = Guid.Empty, Extensions = extensions });
        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(written).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>SHA-256 (lowercase hex) of the changes, one <c>action|entry|code|message</c> line each, so a save can prove it is the list that was previewed.</summary>
    public static string HashChanges(IReadOnlyList<ExportChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var text = string.Join("\n", changes.Select(c => $"{c.Action}|{c.Entry}|{c.Code}|{c.Message}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// Reads the <see cref="MarkerKey"/> marker of a copy (D85 part 2): exactly <c>revisionId</c> (a GUID) and <c>schemaVersion</c>
    /// (1 to <see cref="ContentRevision.CurrentSchemaVersion"/>). Null when there is none or the shape is anything else; never throws.
    /// </summary>
    internal static DownConvertedFrom? ReadMarker(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.Extensions is null || !revision.Extensions.TryGetValue(MarkerKey, out var marker) || marker.ValueKind != JsonValueKind.Object)
            return null;
        Guid? id = null;
        int? schema = null;
        var count = 0;
        foreach (var property in marker.EnumerateObject())
        {
            count++;
            if (property.NameEquals("revisionId") && property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetGuid(out var g))
                id = g;
            else if (property.NameEquals("schemaVersion") && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var n))
                schema = n;
            else
                return null;
        }
        return count == 2 && id is { } revisionId && schema is >= 1 and <= ContentRevision.CurrentSchemaVersion
            ? new DownConvertedFrom(revisionId, schema.Value)
            : null;
    }

    /// <summary>
    /// Lowers every character in <paramref name="plan"/> for <paramref name="target"/> (<see cref="DownConvert.Below"/>) and every
    /// content revision too (D85 part 2): bundled revisions are kept or block, the others become copies with new ids, the copies'
    /// references cascade, and the characters follow them. Lists what that loses. Writes nothing.
    /// <paramref name="mayLeaveOut"/> is the left-out rule (spec section 4): a revision it accepts that has a blocking change, that no
    /// character pins and that no revision still written grants or offers is left out of the file with one <c>leftOut</c> line instead
    /// of blocking it. Null (a character package) leaves nothing out: every blocker blocks.
    /// </summary>
    internal static LoweredPlan Lower(PackageService.ExportPlan plan, LadderRow target, Func<Guid, bool> isBundledSource, Func<Guid, string> sourceTitle, IContentCatalog catalog,
        Func<ContentRevision, bool>? mayLeaveOut = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isBundledSource);
        ArgumentNullException.ThrowIfNull(sourceTitle);
        ArgumentNullException.ThrowIfNull(catalog);

        var characters = new List<Character>(plan.Characters.Count);
        var changes = new List<ExportChange>();
        foreach (var character in plan.Characters)
        {
            var below = DownConvert.Below(character, target.CharacterSchema);
            characters.Add(below.Character);
            var entry = $"characters/{character.Id:D}.json";
            foreach (var change in below.Changes)
            {
                switch (change.Action)
                {
                    case BelowAction.Dropped:
                        changes.Add(new(entry, "character", character.Id, character.Name, null, "dropped", DropsCode, ToggleLabelled(change, character, plan.Revisions)));
                        break;
                    case BelowAction.Blocks:
                        changes.Add(new(entry, "character", character.Id, character.Name, null, "blocks", BlockedCode, change.Message));
                        break;
                }
            }
        }

        var content = LowerContent(plan, target, isBundledSource, sourceTitle, catalog, mayLeaveOut, changes);
        for (var i = 0; i < characters.Count; i++)
        {
            foreach (var (from, to) in content.Moves)
                characters[i] = characters[i].ReplaceReference(from, to);
        }

        var lowered = plan with { Characters = characters, Revisions = content.Revisions, FileName = WithTarget(plan.FileName, target) };
        return new LoweredPlan(lowered, changes, HashChanges(changes), changes.Any(c => c.Action == "blocks"), target);
    }

    /// <summary>
    /// The pack rule for <c>mayLeaveOut</c>: a published revision that is not the newest published one of its content in
    /// <paramref name="revisions"/> (newest is the last stored). The one place "newest" is decided, so a Q14-aware helper can replace it.
    /// </summary>
    internal static Func<ContentRevision, bool> Superseded(IEnumerable<ContentRevision> revisions)
    {
        var newest = revisions.Where(r => r.Status == RevisionStatus.Published).GroupBy(r => r.ContentId).ToDictionary(g => g.Key, g => g.Last().RevisionId);
        return r => r.Status == RevisionStatus.Published && newest.TryGetValue(r.ContentId, out var id) && id != r.RevisionId;
    }

    /// <summary>R-C4: a rounds-left line names the toggle by its label (the id when no revision in the plan has one).</summary>
    private static string ToggleLabelled(BelowChange change, Character character, IEnumerable<ContentRevision> revisions)
    {
        if (change.Code != BelowCodes.RoundsLeft)
            return change.Message;
        var toggles = character.Play.Toggles;
        for (var i = 0; i < toggles.Count; i++)
        {
            if (change.Path != $"play.toggles[{i}].roundsLeft")
                continue;
            var id = toggles[i].ToggleId;
            var contentId = toggles[i].ContentId;
            var label = (revisions.Where(r => r.ContentId == contentId).SelectMany(r => r.Effects).OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == id)
                ?? revisions.SelectMany(r => r.Effects).OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == id))?.Label;
            return label is null ? change.Message : change.Message.Replace($"'{id}'", $"'{label}'", StringComparison.Ordinal);
        }
        return change.Message;
    }

    private sealed record ContentOutcome(List<ContentRevision> Revisions, List<(ContentReference From, ContentReference To)> Moves);

    /// <summary>The plan's revisions over the catalog, so a lookup resolves a revision that is in the export but not yet stored.</summary>
    private sealed class OverlayCatalog(IContentCatalog inner, IEnumerable<ContentRevision> extra) : IContentCatalog
    {
        private readonly Dictionary<Guid, ContentRevision> _extra = extra.GroupBy(r => r.RevisionId).ToDictionary(g => g.Key, g => g.First());

        public ContentRevision? FindRevision(ContentReference reference) =>
            inner.FindRevision(reference) ?? (_extra.TryGetValue(reference.RevisionId, out var found) ? found : null);

        public SourceRecord? FindSource(Guid sourceId) => inner.FindSource(sourceId);

        public IEnumerable<ContentRevision> ChoiceExtensions(Guid contentId, string choiceId) => inner.ChoiceExtensions(contentId, choiceId);

        public IEnumerable<ContentRevision> RevisionsOf(Guid contentId) => inner.RevisionsOf(contentId);
    }

    /// <summary>The revision ids a revision refers to through <c>grant.content</c> and <c>choice.options</c>, the only two fields that hold them.</summary>
    private static IEnumerable<Guid> RefsOf(ContentRevision revision) => ReferencesOf(revision).Select(r => r.RevisionId);

    /// <summary>The revisions a revision grants or offers (<c>grant.content</c>, <c>choice.options</c>), as references.</summary>
    internal static IEnumerable<ContentReference> ReferencesOf(ContentRevision revision) =>
        revision.Effects.SelectMany(e => e switch
        {
            GrantEffect { Content: { } c } => [c],
            ChoiceEffect ch => ch.Options,
            _ => (IEnumerable<ContentReference>)[],
        });

    private static ContentOutcome LowerContent(PackageService.ExportPlan plan, LadderRow target, Func<Guid, bool> isBundledSource,
        Func<Guid, string> sourceTitle, IContentCatalog catalog, Func<ContentRevision, bool>? mayLeaveOut, List<ExportChange> changes)
    {
        var release = target.Release.ToString(3);
        var revisions = new List<ContentRevision>();
        foreach (var revision in plan.Revisions)
        {
            if (revisions.All(r => r.RevisionId != revision.RevisionId))
                revisions.Add(revision);
        }
        var overlay = new OverlayCatalog(catalog, revisions);
        var lowered = new Dictionary<Guid, ContentRevision>();
        var own = new HashSet<Guid>();

        // Each revision's own lines are kept apart until the left-out rule has decided, so a left-out revision can swap them for one.
        var linesOf = new Dictionary<Guid, List<ExportChange>>();
        var reasons = new Dictionary<Guid, string>();
        ExportChange Line(ContentRevision r, string action, string code, string message) =>
            new($"content/{r.RevisionId:D}.json", "content", r.RevisionId, r.Name, sourceTitle(r.Provenance.SourceId), action, code, message);
        string Rest(ContentRevision r, string message)
        {
            var lead = $"'{r.Name}': ";
            return message.StartsWith(lead, StringComparison.Ordinal) ? message[lead.Length..] : message;
        }
        string Prefixed(ContentRevision r, string message)
        {
            var title = sourceTitle(r.Provenance.SourceId);
            return title.Length == 0 ? $"'{r.Name}': {Rest(r, message)}" : $"'{r.Name}' ({title}): {Rest(r, message)}";
        }
        void Note(ContentRevision r, ExportChange line, string? blockingReason = null)
        {
            if (!linesOf.TryGetValue(r.RevisionId, out var list))
                linesOf[r.RevisionId] = list = [];
            list.Add(line);
            if (blockingReason is not null)
                reasons.TryAdd(r.RevisionId, blockingReason);
        }

        foreach (var r in revisions)
        {
            if (isBundledSource(r.Provenance.SourceId))
            {
                if (target.BundledRevisionIds.Contains(r.RevisionId))
                    continue;
                var title = sourceTitle(r.Provenance.SourceId).Replace("System Reference Document", "SRD", StringComparison.Ordinal);
                if (title.Length == 0)
                    title = "SRD";
                var reference = new ContentReference(r.ContentId, r.RevisionId);
                var user = plan.Characters.FirstOrDefault(c => c.AllReferences().Contains(reference));
                const string tail = "Update the receiver's TomeStack; this content cannot be copied into the file.";
                // A written revision that grants or offers it blocks the same way (spec Q9): the sentence names that revision.
                var referrer = user is null
                    ? revisions.FirstOrDefault(x => !isBundledSource(x.Provenance.SourceId) && RefsOf(x).Contains(r.RevisionId))
                    : null;
                var from = referrer is null ? "" : sourceTitle(referrer.Provenance.SourceId) is { Length: > 0 } rs ? $"'{referrer.Name}' ({rs})" : $"'{referrer.Name}'";
                var message = user is not null
                    ? $"'{user.Name}' uses '{r.Name}' from the bundled {title} pack, which TomeStack {release} does not have. {tail}"
                    : referrer is not null
                        ? $"{from} uses '{r.Name}' from the bundled {title} pack, which TomeStack {release} does not have. {tail}"
                        : $"'{r.Name}' from the bundled {title} pack is not in TomeStack {release}. {tail}";
                Note(r, Line(r, "blocks", BlockedCode, message), message);
                continue;
            }

            var below = DownConvert.Below(r, target.ContentSchema, overlay);
            lowered[r.RevisionId] = below.Revision;
            if (!ReferenceEquals(below.Revision, r))
                own.Add(r.RevisionId);
            foreach (var change in below.Changes)
            {
                switch (change.Action)
                {
                    case BelowAction.Dropped:
                        Note(r, Line(r, "dropped", DropsCode, Prefixed(r, change.Message)));
                        break;
                    case BelowAction.Blocks:
                        Note(r, Line(r, "blocks", BlockedCode, Prefixed(r, change.Message)), Rest(r, change.Message));
                        break;
                }
            }
        }

        // The left-out rule: a caller-chosen revision whose blocker no character pins and no written revision grants or offers is
        // dropped from the file with one line. Leaving one out can free another, so this runs to a fixed point.
        var leftOut = new HashSet<Guid>();
        if (mayLeaveOut is not null)
        {
            var pinned = plan.Characters.SelectMany(c => c.AllReferences()).Select(x => x.RevisionId).ToHashSet();
            foreach (var r in revisions.Where(r => reasons.ContainsKey(r.RevisionId) && !pinned.Contains(r.RevisionId) && mayLeaveOut(r)))
                leftOut.Add(r.RevisionId);
            for (var shrunk = true; shrunk;)
            {
                shrunk = false;
                foreach (var needed in revisions.Where(r => !leftOut.Contains(r.RevisionId)).SelectMany(RefsOf).Distinct().Where(leftOut.Contains).ToList())
                    shrunk |= leftOut.Remove(needed);
            }
        }
        foreach (var r in revisions)
        {
            if (leftOut.Contains(r.RevisionId))
            {
                var siblings = revisions.Where(x => x.ContentId == r.ContentId).Select(x => x.RevisionId).ToList();
                var title = sourceTitle(r.Provenance.SourceId);
                var where = title.Length == 0 ? "" : $" ({title})";
                changes.Add(Line(r, "leftOut", LeftOutCode, $"'{r.Name}', revision {siblings.IndexOf(r.RevisionId) + 1} of {siblings.Count}{where}: left out; {reasons[r.RevisionId]}"));
            }
            else if (linesOf.TryGetValue(r.RevisionId, out var mine))
            {
                changes.AddRange(mine);
            }
        }
        revisions.RemoveAll(r => leftOut.Contains(r.RevisionId));
        foreach (var id in leftOut)
        {
            lowered.Remove(id);
            own.Remove(id);
        }

        // A revision is changed when lowering changed it, or when something it grants or offers is changed.
        var movable = revisions.Where(r => lowered.ContainsKey(r.RevisionId)).ToList();
        var edges = movable.ToDictionary(r => r.RevisionId, r => RefsOf(r).Where(lowered.ContainsKey).Distinct().ToList());
        var changed = new HashSet<Guid>(own);
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var r in movable)
            {
                if (!changed.Contains(r.RevisionId) && edges[r.RevisionId].Any(changed.Contains))
                    grew |= changed.Add(r.RevisionId);
            }
        }

        // Leaves first: a copy can be made once every changed revision it refers to has its copy.
        var copies = new Dictionary<Guid, ContentRevision>();
        for (var progress = true; progress;)
        {
            progress = false;
            foreach (var r in movable.Where(r => changed.Contains(r.RevisionId) && !copies.ContainsKey(r.RevisionId)))
            {
                if (edges[r.RevisionId].Where(changed.Contains).All(copies.ContainsKey))
                {
                    copies[r.RevisionId] = MakeCopy(r, lowered[r.RevisionId], copies);
                    progress = true;
                }
            }
        }

        var stuck = movable.Where(r => changed.Contains(r.RevisionId) && !copies.ContainsKey(r.RevisionId)).ToList();
        foreach (var r in stuck.Where(r => ReachesItself(r.RevisionId, edges, copies)))
            changes.Add(Line(r, "blocks", BlockedCode, $"{Prefixed(r, $"'{r.Name}': ")}it refers to itself through other changed content, so a copy cannot be made."));
        foreach (var r in movable.Where(r => copies.ContainsKey(r.RevisionId) && !own.Contains(r.RevisionId)))
            changes.Add(Line(r, "copied", CopiedCode, $"{Prefixed(r, $"'{r.Name}': ")}copied because it grants a changed feature; nothing of its own is left out."));

        var moves = movable.Where(r => copies.ContainsKey(r.RevisionId))
            .Select(r => (new ContentReference(r.ContentId, r.RevisionId), new ContentReference(r.ContentId, copies[r.RevisionId].RevisionId))).ToList();
        var written = revisions.Select(r => copies.TryGetValue(r.RevisionId, out var c) ? c : r).ToList();
        return new ContentOutcome(written, moves);
    }

    private static bool ReachesItself(Guid start, Dictionary<Guid, List<Guid>> edges, Dictionary<Guid, ContentRevision> copies)
    {
        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>(edges[start]);
        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            if (next == start)
                return true;
            if (copies.ContainsKey(next) || !seen.Add(next))
                continue;
            foreach (var onward in edges[next])
                queue.Enqueue(onward);
        }
        return false;
    }

    /// <summary>
    /// The root a revision lowers from: the marker's original and schema when the revision verifies as that copy
    /// (<c>RevisionId == CopyId(marker, own schema, CanonicalBytes)</c>, and the marker names another revision), else the revision itself.
    /// A marker that does not verify is never trusted.
    /// </summary>
    internal static (Guid RevisionId, int SchemaVersion) VerifiedRoot(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return ReadMarker(revision) is { } marker && marker.RevisionId != revision.RevisionId
            && revision.RevisionId == CopyId(marker.RevisionId, revision.SchemaVersion, CanonicalBytes(revision))
            ? (marker.RevisionId, marker.SchemaVersion)
            : (revision.RevisionId, revision.SchemaVersion);
    }

    /// <summary>The copy of <paramref name="original"/>: its lowered body, references moved to the copies made so far, the marker, and the new id.</summary>
    private static ContentRevision MakeCopy(ContentRevision original, ContentRevision body, Dictionary<Guid, ContentRevision> copies)
    {
        var (root, rootSchema) = VerifiedRoot(original);
        ContentReference Move(ContentReference r) => copies.TryGetValue(r.RevisionId, out var c) ? new ContentReference(r.ContentId, c.RevisionId) : r;
        Effect Rewrite(Effect e) => e switch
        {
            GrantEffect { Content: { } c } g => g with { Content = Move(c) },
            ChoiceEffect ch => ch with { Options = [.. ch.Options.Select(Move)] },
            _ => e,
        };
        var extensions = body.Extensions is null ? new Dictionary<string, JsonElement>() : new Dictionary<string, JsonElement>(body.Extensions);
        extensions[MarkerKey] = JsonDocument.Parse(FormattableString.Invariant($$"""{"revisionId":"{{root:D}}","schemaVersion":{{rootSchema}}}""")).RootElement.Clone();
        var copy = body with { Effects = [.. body.Effects.Select(Rewrite)], Extensions = extensions };
        return copy with { RevisionId = CopyId(root, copy.SchemaVersion, CanonicalBytes(copy)) };
    }

    /// <summary>The file name with <c>-for-&lt;release&gt;</c> before its suffix: <c>Hero-for-0.6.0.tomestack.zip</c>, <c>Hero-for-0.6.0-personal-backup.tomestack.zip</c>.</summary>
    internal static string WithTarget(string fileName, LadderRow target)
    {
        var release = target.Release.ToString(3);
        const string backup = "-personal-backup.tomestack.zip";
        const string plain = ".tomestack.zip";
        if (fileName.EndsWith(backup, StringComparison.Ordinal))
            return $"{fileName[..^backup.Length]}-for-{release}{backup}";
        if (fileName.EndsWith(plain, StringComparison.Ordinal))
            return $"{fileName[..^plain.Length]}-for-{release}{plain}";
        return $"{fileName}-for-{release}";
    }
}
