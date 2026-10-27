using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// G6: the one place that says which package contents the previous release (0.6.0) refuses: a content revision at schema v11
/// or later (<see cref="ContentRevision.PlayMechanicsSchemaVersion"/>) or a character at schema v10 or later
/// (<see cref="Character.EncounterSchemaVersion"/>, written only while an encounter runs or a toggle has rounds left).
/// A sender's preview warns with <see cref="Code"/> so the receiver is not surprised by <c>package.schema-unsupported</c>.
/// </summary>
internal static class NewerBuild
{
    public const string Code = "package.needs-newer-build";

    /// <summary>The first release that opens a package holding such content or such a character.</summary>
    public const string Version = "0.7.0";

    public const int ContentSchemaFrom = ContentRevision.PlayMechanicsSchemaVersion;
    public const int CharacterSchemaFrom = Character.EncounterSchemaVersion;

    public static readonly Diagnostic Warning = new(Code, $"Opening this needs TomeStack {Version} or later: it holds content or a character that older versions refuse.");

    /// <summary>The warning when anything in the package that would be written needs <see cref="Version"/>; otherwise none. At most one.</summary>
    public static IReadOnlyList<Diagnostic> For(IEnumerable<ContentRevision> revisions, IEnumerable<Character> characters) =>
        revisions.Any(r => r.SchemaVersion >= ContentSchemaFrom) || characters.Any(c => c.SchemaVersion >= CharacterSchemaFrom)
            ? [Warning]
            : [];

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
    /// picks missing for good. The user updates and restores again.
    /// </summary>
    public static Diagnostic Restore(System.Version backupVersion, int pins, int characters)
    {
        var normal = Normal(backupVersion);
        var version = normal.ToString(normal.Revision > 0 ? 4 : 3);
        var thisBuild = typeof(NewerBuild).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new(RestoreCode,
            $"This backup was made with TomeStack {version}, newer than this one ({thisBuild}). {pins} content pick{(pins == 1 ? "" : "s")} of {characters} character{(characters == 1 ? "" : "s")} use content this version does not have, so restoring now would leave them missing. Update TomeStack to {version} or later, then restore the backup. Nothing has been changed.");
    }

    /// <summary>Missing parts count as 0, so 0.8 and 0.8.0 compare equal.</summary>
    private static System.Version Normal(System.Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
