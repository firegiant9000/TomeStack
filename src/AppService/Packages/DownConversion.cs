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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "UUIDv5 is defined over SHA-1 (RFC 4122 §4.3); the hash only names a copy and protects nothing.")]
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
        var gearConverted = new HashSet<Guid>();
        var gearBlocked = new HashSet<Guid>();
        var afterGear = new List<Character>(plan.Characters.Count);
        foreach (var character in plan.Characters)
        {
            var gear = GearAsCustomItems(character, plan, target, isBundledSource, catalog, gearConverted, gearBlocked);
            afterGear.Add(gear.Character);
            var below = DownConvert.Below(gear.Character, target.CharacterSchema);
            characters.Add(below.Character);
            var entry = $"characters/{character.Id:D}.json";
            foreach (var change in gear.Changes.Concat(below.Changes))
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

        // A converted item no longer needs to exist on the target. It stays blocking when anything else still holds it: an entry that
        // could not be converted, another pin of the character, or a revision that grants or offers it.
        gearConverted.ExceptWith(gearBlocked);
        if (gearConverted.Count > 0)
        {
            // Each character after its conversion: only the converted entries are gone, so the kept entries and their mods still count.
            var held = afterGear.SelectMany(HeldReferences).Select(r => r.RevisionId).ToHashSet();
            gearConverted.ExceptWith(held);
            gearConverted.ExceptWith(plan.Revisions.SelectMany(RefsOf));
        }
        var content = LowerContent(plan, target, isBundledSource, sourceTitle, catalog, mayLeaveOut, changes, gearConverted);
        // One map, one pass per character (moves never chain: a copy id is new).
        var moves = new Dictionary<ContentReference, ContentReference>();
        foreach (var (from, to) in content.Moves)
            moves[from] = to;
        if (moves.Count > 0)
        {
            for (var i = 0; i < characters.Count; i++)
            {
                foreach (var from in HeldReferences(characters[i]).Where(moves.ContainsKey).ToList())
                    characters[i] = characters[i].ReplaceReference(from, moves[from]);
            }
        }

        var lowered = plan with { Characters = characters, Revisions = content.Revisions, FileName = WithTarget(plan.FileName, target) };
        return new LoweredPlan(lowered, changes, HashChanges(changes), changes.Any(c => c.Action == "blocks"), target);
    }

    /// <summary>
    /// The pack rule for <c>mayLeaveOut</c>: a published revision that is not the newest published one of its content in
    /// <paramref name="revisions"/> (<see cref="Newest"/>: the last stored, but never a copy while an unmarked revision exists).
    /// </summary>
    internal static Func<ContentRevision, bool> Superseded(IEnumerable<ContentRevision> revisions)
    {
        var newest = revisions.Where(r => r.Status == RevisionStatus.Published).GroupBy(r => r.ContentId).ToDictionary(g => g.Key, g => Newest(g)!.RevisionId);
        return r => r.Status == RevisionStatus.Published && newest.TryGetValue(r.ContentId, out var id) && id != r.RevisionId;
    }

    /// <summary>Whether <paramref name="revision"/> is a stored copy: its marker verifies against its own bytes (<see cref="VerifiedRoot"/>). A forged or malformed marker makes it an ordinary revision.</summary>
    internal static bool IsCopy(ContentRevision revision) => VerifiedRoot(revision).RevisionId != revision.RevisionId;

    /// <summary>
    /// "Newest" (SPEC I-06, owner answer Q14): the last stored revision of one content, but a copy never counts while an unmarked
    /// revision exists. <paramref name="revisions"/> are the content's revisions in stored order; null when there are none. With no copies
    /// this is exactly the last one.
    /// </summary>
    internal static ContentRevision? Newest(IEnumerable<ContentRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        var list = revisions as IReadOnlyList<ContentRevision> ?? [.. revisions];
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!IsCopy(list[i]))
                return list[i];
        }
        return list.Count == 0 ? null : list[^1];
    }

    /// <summary>The revision without the <see cref="MarkerKey"/> extension (an edit is no longer the copy); every other key is kept, and an emptied extension map becomes null.</summary>
    internal static ContentRevision WithoutMarker(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.Extensions?.ContainsKey(MarkerKey) != true)
            return revision;
        var rest = revision.Extensions.Where(p => p.Key != MarkerKey).ToDictionary(p => p.Key, p => p.Value);
        return revision with { Extensions = rest.Count == 0 ? null : rest };
    }

    /// <summary>
    /// The warning for a lowered character that would replace a fuller one on this machine (spec section 4, D85 part 2): when
    /// <paramref name="incoming"/> has a lower schema than <paramref name="local"/> and lowering <paramref name="local"/> to it loses
    /// anything but silent details, lists what the file has less of. Null otherwise. <paramref name="revisions"/> is read only to name a
    /// toggle by its label. Writes nothing.
    /// </summary>
    internal static Diagnostic? CharacterLoss(Character local, Character incoming, string? madeFor, Func<IEnumerable<ContentRevision>> revisions)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(revisions);
        if (incoming.SchemaVersion >= local.SchemaVersion || incoming.SchemaVersion < Character.PreviousSchemaVersion)
            return null;
        var lines = new List<string>();
        foreach (var change in DownConvert.Below(local, incoming.SchemaVersion).Changes.Where(c => c.Action != BelowAction.Silent))
        {
            if (change.Code == BelowCodes.EncounterRunning)
            {
                lines.Add("the running encounter");
                continue;
            }
            var message = change.Code == BelowCodes.RoundsLeft ? ToggleLabelled(change, local, revisions()) : change.Message;
            var prefix = $"'{local.Name}': ";
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                message = message[prefix.Length..];
            lines.Add(message.TrimEnd('.'));
        }
        if (lines.Count == 0)
            return null;
        var made = PackageManifest.IsValidMadeFor(madeFor) ? $"TomeStack {madeFor}" : "an older TomeStack";
        return new("package.down-converted-character",
            $"'{incoming.Name}' in this file was made for {made} and has less than your copy: {string.Join("; ", lines)}. Importing replaces your copy; a backup is taken first.");
    }

    /// <summary>The fixed prefix of the id a converted gear entry's custom item gets (with <see cref="Namespace"/>), so preview and export agree.</summary>
    private const string GearIdPrefix = "gear:";

    /// <summary>
    /// Plain gear the target release does not ship (Q3c, D98): an equipment entry whose item is a bundled revision outside
    /// <paramref name="target"/>'s set becomes a custom item, listed as <see cref="BelowCodes.GearAsCustomItem"/> (dropped). "Plain gear" means an
    /// <c>item</c> revision whose effects are empty or only one <c>item</c> effect with no charges and no attunement. A pack (an <c>item</c> effect with
    /// <c>contains</c>) is still plain gear: it becomes one custom item named after the pack and its contents are not expanded. Weapons, armour, mods and
    /// anything else keep today's behaviour (the bundled-outside-target block). The custom item has the entry's name and quantity, no note, and is not equipped.
    /// Its id is a UUIDv5 of the entry id, so converting twice gives the same item. When the character would hold more than
    /// <see cref="Character.MaxCustomItems"/> custom items, or a quantity or name cannot be represented, nothing is converted and one blocking line says why.
    /// Converted revision ids go to <paramref name="converted"/>, ids still held by an unconverted entry to <paramref name="unconverted"/>.
    /// </summary>
    private static (Character Character, List<BelowChange> Changes) GearAsCustomItems(Character character, PackageService.ExportPlan plan, LadderRow target,
        Func<Guid, bool> isBundledSource, IContentCatalog catalog, HashSet<Guid> converted, HashSet<Guid> unconverted)
    {
        var changes = new List<BelowChange>();
        // Below a v9 target a custom item has no home: the old behaviour (block) stays.
        if (target.CharacterSchema < Character.DetailsSchemaVersion)
            return (character, changes);
        var release = target.Release.ToString(3);
        var byId = plan.Revisions.GroupBy(r => r.RevisionId).ToDictionary(g => g.Key, g => g.First());
        ContentRevision? Find(ContentReference reference) => byId.TryGetValue(reference.RevisionId, out var r) ? r : catalog.FindRevision(reference);

        var plain = new List<(EquipmentEntry Entry, ContentRevision Item)>();
        foreach (var entry in character.Equipment)
        {
            if (entry.Mods is not null || entry.Attuned is true || entry.ChargesSpent is not null || target.BundledRevisionIds.Contains(entry.Item.RevisionId))
                continue;
            if (Find(entry.Item) is { Kind: ContentKind.Item } item && isBundledSource(item.Provenance.SourceId) && IsPlainGear(item))
                plain.Add((entry, item));
        }
        // Whatever stays an equipment entry keeps its revision blocking, even when another character converts the same item.
        foreach (var entry in character.Equipment.Where(e => plain.All(p => !ReferenceEquals(p.Entry, e))))
            unconverted.Add(entry.Item.RevisionId);
        if (plain.Count == 0)
            return (character, changes);

        var name = character.Name;
        var existing = character.CustomItems ?? [];
        var problem = existing.Count + plain.Count > Character.MaxCustomItems
            ? $"'{name}': {existing.Count + plain.Count} custom items would be needed for gear TomeStack {release} does not have, and a character can keep {Character.MaxCustomItems}. Remove some custom items or gear, then export."
            : plain.FirstOrDefault(p => p.Entry.Quantity is < 1 or > EquipmentEntry.MaxQuantity) is { Item: { } big }
                ? $"'{name}': the quantity of '{big.Name}' cannot be kept as a custom item. Change it to between 1 and {EquipmentEntry.MaxQuantity}, then export."
                : plain.FirstOrDefault(p => string.IsNullOrWhiteSpace(p.Item.Name) || p.Item.Name.Length > Character.MaxName) is { Item: { } tooLong }
                    ? $"'{name}': the name '{tooLong.Name}' cannot be kept as a custom item (it must have 1 to {Character.MaxName} characters and not be blank), so it cannot be copied into the file."
                    : null;
        if (problem is not null)
        {
            changes.Add(new("equipment", BelowAction.Blocks, BelowCodes.GearAsCustomItem, problem));
            foreach (var (_, item) in plain)
                unconverted.Add(item.RevisionId);
            return (character, changes);
        }

        var used = existing.Select(i => i.Id).ToHashSet();
        var added = new List<CustomItem>();
        foreach (var (entry, item) in plain)
        {
            // An entry without an id is keyed by its place in the list, so two of the same item never depend on the :2 suffix order.
            var key = entry.Id?.ToString("D") ?? FormattableString.Invariant($"{item.RevisionId:D}@{character.Equipment.ToList().FindIndex(e => ReferenceEquals(e, entry))}");
            var id = Uuid5(Namespace, GearIdPrefix + key);
            for (var n = 2; !used.Add(id); n++)
                id = Uuid5(Namespace, FormattableString.Invariant($"{GearIdPrefix}{key}:{n}"));
            added.Add(new CustomItem(id, item.Name, entry.Quantity, null, false));
            converted.Add(item.RevisionId);
            changes.Add(new("equipment", BelowAction.Dropped, BelowCodes.GearAsCustomItem,
                $"'{name}': '{item.Name}' is not in TomeStack {release}, so it becomes a custom item (not equipped, no longer linked to its content)."));
        }
        var kept = character.Equipment.Where(e => plain.All(p => !ReferenceEquals(p.Entry, e))).ToList();
        return (character with { Equipment = kept, CustomItems = [.. existing, .. added] }, changes);
    }

    /// <summary>Plain gear: no effects, or only <c>item</c> effects with no charges and no attunement (a pack's <c>contains</c> is allowed).</summary>
    private static bool IsPlainGear(ContentRevision item) =>
        item.Effects.All(e => e is ItemEffect { Charges: null, RequiresAttunement: null or false });

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

        public IReadOnlyList<ContentRevision> GuidesFor(Guid targetContentId) => inner.GuidesFor(targetContentId);

        public IEnumerable<ContentRevision> RevisionsOf(Guid contentId) => inner.RevisionsOf(contentId);

        public ContentRevision? NewestPublished(Guid contentId) => inner.NewestPublished(contentId);
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
        Func<Guid, string> sourceTitle, IContentCatalog catalog, Func<ContentRevision, bool>? mayLeaveOut, List<ExportChange> changes,
        IReadOnlySet<Guid> gearConverted)
    {
        var release = target.Release.ToString(3);
        var revisions = new List<ContentRevision>();
        foreach (var revision in plan.Revisions)
        {
            // Plain gear that became a custom item is neither written nor blocking.
            if (!gearConverted.Contains(revision.RevisionId) && revisions.All(r => r.RevisionId != revision.RevisionId))
                revisions.Add(revision);
        }
        var overlay = new OverlayCatalog(catalog, revisions);
        var lowered = new Dictionary<Guid, ContentRevision>();
        var own = new HashSet<Guid>();
        // Revisions lowering leaves out whole (a creation guide): not written, but their dropped line is kept. Nothing can pin one
        // (a character holds no guide), so no character is left holding a missing revision.
        var omitted = new HashSet<Guid>();

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

        // A bundled revision outside the target's set blocks. A written revision that grants or offers it blocks the same way
        // (spec Q9): the sentence names that revision, and one that is left out is never the one named while another is written.
        var bundledTitles = new Dictionary<Guid, string>();
        string BundledBlock(ContentRevision r, string title, HashSet<Guid> skipped)
        {
            var reference = new ContentReference(r.ContentId, r.RevisionId);
            var user = plan.Characters.FirstOrDefault(c => c.AllReferences().Contains(reference));
            const string tail = "Update the receiver's TomeStack; this content cannot be copied into the file.";
            var referrers = user is null
                ? revisions.Where(x => !isBundledSource(x.Provenance.SourceId) && RefsOf(x).Contains(r.RevisionId)).ToList()
                : [];
            var referrer = referrers.FirstOrDefault(x => !skipped.Contains(x.RevisionId)) ?? referrers.FirstOrDefault();
            var from = referrer is null ? "" : sourceTitle(referrer.Provenance.SourceId) is { Length: > 0 } rs ? $"'{referrer.Name}' ({rs})" : $"'{referrer.Name}'";
            return user is not null
                ? $"'{user.Name}' uses '{r.Name}' from the bundled {title} content, which TomeStack {release} does not have. {tail}"
                : referrer is not null
                    ? $"{from} uses '{r.Name}' from the bundled {title} content, which TomeStack {release} does not have. {tail}"
                    : $"'{r.Name}' from the bundled {title} content is not in TomeStack {release}. {tail}";
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
                var message = BundledBlock(r, title, []);
                Note(r, Line(r, "blocks", BlockedCode, message), message);
                bundledTitles[r.RevisionId] = title;
                continue;
            }

            var below = DownConvert.Below(r, target.ContentSchema, overlay);
            if (below.Omit)
                omitted.Add(r.RevisionId);
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
                // A bundled revision is never written, so it has no line to leave out (the revisions that reached it have theirs).
                if (bundledTitles.ContainsKey(r.RevisionId))
                    continue;
                var siblings = revisions.Where(x => x.ContentId == r.ContentId).Select(x => x.RevisionId).ToList();
                var title = sourceTitle(r.Provenance.SourceId);
                var where = title.Length == 0 ? "" : $" ({title})";
                changes.Add(Line(r, "leftOut", LeftOutCode, $"'{r.Name}', version {siblings.IndexOf(r.RevisionId) + 1} of {siblings.Count}{where}: left out; {reasons[r.RevisionId]}"));
            }
            else if (linesOf.TryGetValue(r.RevisionId, out var mine))
            {
                if (bundledTitles.TryGetValue(r.RevisionId, out var bundledTitle))
                    mine = [Line(r, "blocks", BlockedCode, BundledBlock(r, bundledTitle, leftOut))];
                changes.AddRange(mine);
            }
        }
        revisions.RemoveAll(r => leftOut.Contains(r.RevisionId) || omitted.Contains(r.RevisionId));
        foreach (var id in leftOut.Concat(omitted))
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

    /// <summary>What <see cref="Repin"/> made of a file's revisions and characters.</summary>
    internal sealed record RepinOutcome(IReadOnlyList<ContentRevision> Revisions, IReadOnlyList<Character> Characters, IReadOnlyList<Repinned> Repinned, IReadOnlyList<Diagnostic> Warnings);

    /// <summary>
    /// The way back (D85 part 2): a copy whose original is here and verifies is dropped from the file and every reference to it moves to
    /// the original. A copy is trusted only when its id is <see cref="CopyId"/> of its marker and bytes, and then only together with its
    /// whole closure (copies that grant or offer one another): every original must be a published revision of the same content and
    /// source, outside the bundled sources, and lowering the originals again for the file's release must give exactly the file's copy
    /// ids (an id embeds the SHA-256 of the bytes). Any failure leaves every member of the closure an ordinary revision with a warning.
    /// A marker that is unreadable or does not match its copy's content is ignored with a warning. Writes nothing.
    /// </summary>
    internal static RepinOutcome Repin(IReadOnlyList<ContentRevision> revisions, IReadOnlyList<Character> characters, PackageManifest manifest,
        IContentCatalog catalog, Func<Guid, bool> isBundledSource)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(isBundledSource);
        var warnings = new List<Diagnostic>();
        var markers = new Dictionary<Guid, DownConvertedFrom>();
        foreach (var revision in revisions.Where(r => r.Extensions?.ContainsKey(MarkerKey) == true))
        {
            var marker = ReadMarker(revision);
            if (marker is null)
                warnings.Add(new("package.marker-ignored", $"'{revision.Name}': the note saying which version it was copied from is not readable, so it was ignored.", revision.Reference));
            else if (marker.RevisionId == revision.RevisionId || VerifiedRoot(revision).RevisionId != marker.RevisionId)
                warnings.Add(new("package.marker-ignored", $"'{revision.Name}': the note saying which version it was copied from does not match its content, so it was ignored.", revision.Reference));
            else
                markers[revision.RevisionId] = marker;
        }
        if (markers.Count == 0)
            return new(revisions, characters, [], warnings);

        // Closures: copies joined by a grant or an option between them.
        var copies = revisions.Where(r => markers.ContainsKey(r.RevisionId)).ToList();
        var parent = copies.ToDictionary(r => r.RevisionId, r => r.RevisionId);
        Guid Find(Guid id)
        {
            while (parent[id] != id)
                id = parent[id] = parent[parent[id]];
            return id;
        }
        foreach (var copy in copies)
        {
            foreach (var reference in ReferencesOf(copy).Where(r => parent.ContainsKey(r.RevisionId)))
                parent[Find(copy.RevisionId)] = Find(reference.RevisionId);
        }

        var madeFor = manifest.MadeFor;
        var repinned = new List<Repinned>();
        var moved = new Dictionary<Guid, Guid>();
        // Revisions the file keeps (not copies) are never rewritten, so a closure one of them grants or offers stays as it is.
        var heldByKept = revisions.Where(r => !markers.ContainsKey(r.RevisionId)).SelectMany(ReferencesOf).Select(x => x.RevisionId).ToHashSet();
        var asTheyAre = new List<ContentRevision>();
        foreach (var closure in copies.GroupBy(c => Find(c.RevisionId)))
        {
            var members = closure.ToList();
            var originals = members.Any(m => heldByKept.Contains(m.RevisionId)) ? null : VerifiedOriginals(members, markers, manifest, catalog, isBundledSource);
            foreach (var member in members)
            {
                if (originals is null)
                {
                    asTheyAre.Add(member);
                    continue;
                }
                var original = originals[member.RevisionId];
                moved[member.RevisionId] = original.RevisionId;
                repinned.Add(new(member.RevisionId, original.RevisionId, member.Name));
                warnings.Add(new("package.down-converted-repinned",
                    $"'{member.Name}': switched back to your original version (the file held a copy made for {madeFor ?? "an older TomeStack"}).", original.Reference));
            }
        }
        // C2-Q5: one line for the file, not one per copy.
        if (asTheyAre.Count > 0)
        {
            var by = PackageManifest.IsValidMadeFor(madeFor) ? $"TomeStack {madeFor}" : "an older TomeStack";
            warnings.Add(asTheyAre.Count == 1
                ? new("package.down-converted", $"'{asTheyAre[0].Name}' in this file was made for {by} by {manifest.AppVersion} and imports as it is (no original here matches it); the original has more.", asTheyAre[0].Reference)
                : new("package.down-converted", $"{asTheyAre.Count} entries in this file were made for {by} by {manifest.AppVersion} and import as they are (no original here matches them); the originals have more."));
        }
        if (moved.Count == 0)
            return new(revisions, characters, [], warnings);

        // A kept revision is never edited (same id, same bytes everywhere), so only dropped copies disappear.
        var kept = revisions.Where(r => !moved.ContainsKey(r.RevisionId)).ToList();

        // One map for every closure, one pass per character: only the copies a character actually holds are swapped.
        var swaps = copies.Where(c => moved.ContainsKey(c.RevisionId)).ToDictionary(c => c.Reference, c => new ContentReference(c.ContentId, moved[c.RevisionId]));
        var rewritten = characters.Select(character =>
        {
            var held = HeldReferences(character).Where(swaps.ContainsKey).ToList();
            if (held.Count == 0)
                return character;
            foreach (var from in held)
                character = character.ReplaceReference(from, swaps[from]);
            return Deduplicated(character);
        }).ToList();
        return new(kept, rewritten, repinned, warnings);
    }

    /// <summary>The original of each member of a closure by the member's revision id, or null when any member does not verify.</summary>
    private static Dictionary<Guid, ContentRevision>? VerifiedOriginals(List<ContentRevision> members, Dictionary<Guid, DownConvertedFrom> markers,
        PackageManifest manifest, IContentCatalog catalog, Func<Guid, bool> isBundledSource)
    {
        var found = new Dictionary<Guid, ContentRevision>();
        foreach (var member in members)
        {
            var marker = markers[member.RevisionId];
            var original = catalog.FindRevision(new ContentReference(member.ContentId, marker.RevisionId));
            if (original is null || original.Status != RevisionStatus.Published || isBundledSource(original.Provenance.SourceId)
                || original.SchemaVersion != marker.SchemaVersion
                || original.ContentId != member.ContentId || original.Provenance.SourceId != member.Provenance.SourceId)
                return null;
            found[member.RevisionId] = original;
        }

        // R-C11: the row the manifest names; else a stand-in at the highest schema among the copies.
        var target = manifest.MadeFor is { } release ? ReleaseLadder.Rows.FirstOrDefault(r => r.Release.ToString(3) == release) : null;
        target ??= new LadderRow(new Version(0, 0, 0), 0, members.Max(m => m.SchemaVersion), 0, 0, 0, new HashSet<Guid>(), false);
        var distinct = found.Values.DistinctBy(o => o.RevisionId).ToList();
        var lowered = Lower(new PackageService.ExportPlan([], distinct, [], [], "x.tomestack.zip"), target, isBundledSource, _ => "", catalog);
        if (lowered.Blocked || lowered.Plan.Revisions.Count != distinct.Count)
            return null;
        var again = distinct.Select((o, i) => (o.RevisionId, Copy: lowered.Plan.Revisions[i])).ToDictionary(x => x.RevisionId, x => x.Copy.RevisionId);
        return members.All(m => again[found[m.RevisionId].RevisionId] == m.RevisionId) ? found : null;
    }

    /// <summary>Every revision a character refers to, wherever <see cref="Character.ReplaceReference"/> looks (AllReferences omits choice sources and exceptions).</summary>
    private static IEnumerable<ContentReference> HeldReferences(Character character) =>
        character.AllReferences()
            .Concat(character.Choices.Select(c => c.Source))
            .Concat(character.CrossFamilyExceptions.Select(e => e.Content))
            .Concat(character.CampaignExceptions.Select(e => e.Content))
            .Concat(character.Play.Concentration is { } c ? [c.Spell] : [])
            .Distinct();

    /// <summary>After a re-pin a character that held both a copy and its original holds the original once (pins, classes, choices, exceptions).</summary>
    private static Character Deduplicated(Character character) => character with
    {
        Pins = [.. character.Pins.Distinct()],
        Classes = [.. character.Classes.Distinct()],
        Choices = [.. character.Choices
            .Select(c => c with { Selected = [.. c.Selected.Distinct()] })
            .GroupBy(c => (c.Source, c.ChoiceId))
            .Select(g => g.Count() == 1 ? g.First() : g.First() with { Selected = [.. g.SelectMany(c => c.Selected).Distinct()] })],
        CrossFamilyExceptions = [.. character.CrossFamilyExceptions.DistinctBy(e => e.Content)],
        CampaignExceptions = [.. character.CampaignExceptions.DistinctBy(e => e.Content)],
    };

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
