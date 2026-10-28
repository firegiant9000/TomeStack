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

    /// <summary>Content at this schema (v12: a weapon's <c>bonus</c>, a cost <c>payment</c>, <c>castsSpell</c>, spell <c>tags</c>) gets the version-agnostic text: the release that ships it is not decided (R22).</summary>
    public const int ContentV12SchemaFrom = ContentRevision.WeaponBonusSchemaVersion;

    public static readonly Diagnostic Warning = new(Code, $"Opening this needs TomeStack {Version} or later: it holds content or a character that older versions refuse.");

    public static readonly Diagnostic ContentV12Warning = new(Code, "Opening this needs a TomeStack version that reads content schema 12 or later: it holds content that older versions refuse.");

    /// <summary>The warning when anything in the package that would be written needs <see cref="Version"/> or content schema 12; otherwise none. At most one; the v12 text wins.</summary>
    public static IReadOnlyList<Diagnostic> For(IEnumerable<ContentRevision> revisions, IEnumerable<Character> characters)
    {
        var revisionList = revisions as IReadOnlyCollection<ContentRevision> ?? revisions.ToList();
        if (revisionList.Any(r => r.SchemaVersion >= ContentV12SchemaFrom))
            return [ContentV12Warning];
        return revisionList.Any(r => r.SchemaVersion >= ContentSchemaFrom) || characters.Any(c => c.SchemaVersion >= CharacterSchemaFrom)
            ? [Warning]
            : [];
    }
}
