using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// G6, from the release ladder (D83): says which release first opens what a package would hold, and, when that is newer than the
/// previous minor release (<see cref="ReleaseLadder.PreviousMinor"/>) or no released row at all, names each entry that keeps the
/// previous minor out and carries the one <see cref="Code"/> warning, so the receiver is not surprised by
/// <c>package.schema-unsupported</c>.
/// </summary>
internal static class NewerBuild
{
    public const string Code = "package.needs-newer-build";

    /// <param name="bundledRevisionIds">The bundled SRD revisions the package carries (checked against each row's bundled set).</param>
    /// <param name="bundledCarried">False when the file does not hold the bundled revisions (a library backup): their causes point at the pinning character's entry.</param>
    /// <param name="bundledPackTitle">The title of the bundled pack a revision id comes from, for the cause's message.</param>
    public static OpensInResult Evaluate(IEnumerable<ContentRevision> revisions, IEnumerable<Character> characters,
        int gapNoteSchema, IEnumerable<Guid> bundledRevisionIds, Func<Guid, string> bundledPackTitle, bool bundledCarried = true)
    {
        var revisionList = revisions.ToList();
        var characterList = characters.ToList();
        var bundled = bundledRevisionIds.Distinct().ToList();
        var row = ReleaseLadder.OpensIn(revisionList, characterList, gapNoteSchema, bundled);
        var opensIn = row?.Release.ToString(3);
        var previous = ReleaseLadder.PreviousMinorFor(AppVersionStamp.CurrentVersion);
        if (previous is null || (row is not null && row.Release <= previous.Release))
            return new(opensIn, [], []);

        var causes = new List<OpensInCause>();
        foreach (var character in characterList.Where(c => c.SchemaVersion > previous.CharacterSchema))
            causes.Add(new($"characters/{character.Id:D}.json", "character", character.Id, character.Name, character.SchemaVersion, null,
                $"'{character.Name}' is a character at schema {character.SchemaVersion}"));
        foreach (var revision in revisionList.Where(r => r.SchemaVersion > previous.ContentSchema))
            causes.Add(new($"content/{revision.RevisionId:D}.json", "content", revision.RevisionId, revision.Name, revision.SchemaVersion, null,
                $"'{revision.Name}' is content at schema {revision.SchemaVersion}"));
        if (gapNoteSchema > previous.GapNoteSchema)
            causes.Add(new("gaps/", "gapNote", Guid.Empty, "gap notes", gapNoteSchema, null, $"the gap notes are at schema {gapNoteSchema}"));
        var causedByContent = causes.Where(c => c.Kind == "content").Select(c => c.Id).ToHashSet();
        foreach (var id in bundled.Where(id => !previous.BundledRevisionIds.Contains(id) && !causedByContent.Contains(id)))
        {
            var name = revisionList.FirstOrDefault(r => r.RevisionId == id)?.Name ?? id.ToString("D");
            var owner = characterList.FirstOrDefault(c => c.AllReferences().Any(r => r.RevisionId == id));
            var pack = bundledPackTitle(id);
            var message = owner is null
                ? $"'{name}' is from the bundled {pack} pack, which TomeStack {previous.Release.ToString(3)} does not have"
                : $"'{owner.Name}' uses '{name}' from the bundled {pack} pack, which TomeStack {previous.Release.ToString(3)} does not have";
            var entry = !bundledCarried && owner is not null ? $"characters/{owner.Id:D}.json" : $"content/{id:D}.json";
            causes.Add(new(entry, "content", id, name, null, pack, message));
        }

        return new(opensIn, causes, [new Diagnostic(Code, WarningText(row, causes, revisionList, characterList, gapNoteSchema))]);
    }

    /// <summary>At most the first three distinct messages, in cause order; three or fewer join with ", and ", more end with ", and N more".</summary>
    private static string CauseList(List<OpensInCause> causes)
    {
        var messages = causes.Select(c => c.Message).Distinct().ToList();
        return messages.Count <= 3
            ? string.Join(", and ", messages)
            : $"{string.Join(", ", messages.Take(3))}, and {messages.Count - 3} more";
    }

    private static string WarningText(LadderRow? row, List<OpensInCause> causes, List<ContentRevision> revisions, List<Character> characters, int gapNoteSchema)
    {
        if (row is not null)
            return $"Opening this needs TomeStack {row.Release.ToString(3)} or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): {CauseList(causes)}. Older versions refuse the file.";

        var last = ReleaseLadder.Rows[^1];
        var content = revisions.Where(r => r.SchemaVersion > last.ContentSchema).Select(r => r.SchemaVersion).DefaultIfEmpty(0).Max();
        if (content > 0)
            return $"Opening this needs a TomeStack version that reads content schema {content} or later: it holds content that older versions refuse.";
        var character = characters.Where(c => c.SchemaVersion > last.CharacterSchema).Select(c => c.SchemaVersion).DefaultIfEmpty(0).Max();
        if (character > 0)
            return $"Opening this needs a TomeStack version that reads character schema {character} or later: it holds a character that older versions refuse.";
        if (gapNoteSchema > last.GapNoteSchema)
            return $"Opening this needs a TomeStack version that reads gap note schema {gapNoteSchema} or later: it holds gap notes that older versions refuse.";
        return $"Opening this needs a TomeStack version newer than {last.Release.ToString(3)}: it holds bundled SRD content that released versions do not have.";
    }

    /// <summary>S2: a library backup from a newer build whose characters pin content this build's bundled packs do not hold.</summary>
    public const string RestoreCode = "restore.needs-newer-build";

    /// <summary>
    /// The backup's <c>appVersion</c> when it is a newer release than this build; otherwise null (the same or an older build,
    /// or a version that does not parse). The manifest is untrusted, so only the parsed version is ever shown.
    /// </summary>
    public static System.Version? NewerThanThisBuild(string? appVersion) =>
        System.Version.TryParse(appVersion, out var theirs) && typeof(NewerBuild).Assembly.GetName().Version is { } ours
        && Normal(theirs) > Normal(ours)
            ? theirs
            : null;

    /// <summary>
    /// S2 (Review focus 3): one refusal in place of a <c>package.pin-missing</c> per pin. A library backup leaves out the
    /// bundled packs, so a newer build's backup can pin bundled content this build never seeds; restoring would leave those
    /// picks missing for good. The user updates and restores again. A pin names no source, so a pin lost to damage cannot
    /// be told apart: the message says "most likely" and names the characters (review fix).
    /// </summary>
    public static Diagnostic Restore(System.Version backupVersion, int pins, IReadOnlyList<string> characters)
    {
        var normal = Normal(backupVersion);
        var version = normal.ToString(normal.Revision > 0 ? 4 : 3);
        var thisBuild = typeof(NewerBuild).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        // At most five names, each clipped: a backup's name is untrusted and no limit is checked on it.
        var names = string.Join(", ", characters.Take(5).Select(n => $"'{(n.Length > Character.MaxName ? n[..Character.MaxName] + "…" : n)}'"))
            + (characters.Count > 5 ? $" and {characters.Count - 5} more" : "");
        return new(RestoreCode,
            $"This backup was made with TomeStack {version}, newer than this one ({thisBuild}). {pins} content pick{(pins == 1 ? "" : "s")} of {characters.Count} character{(characters.Count == 1 ? "" : "s")} ({names}) {(pins == 1 ? "is" : "are")} neither in the backup nor installed here: most likely content this version does not have. Update TomeStack to {version} or later, then restore the backup. Nothing has been changed.");
    }

    /// <summary>Missing parts count as 0, so 0.8 and 0.8.0 compare equal.</summary>
    private static System.Version Normal(System.Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
