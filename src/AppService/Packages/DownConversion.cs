using System.Security.Cryptography;
using System.Text;
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
    /// <see cref="MarkerKey"/> removed, so a copy and the bytes it was lowered from hash the same way.
    /// </summary>
    public static byte[] CanonicalBytes(ContentRevision copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        var extensions = copy.Extensions?.Where(p => p.Key != MarkerKey).ToDictionary(p => p.Key, p => p.Value);
        if (extensions is { Count: 0 })
            extensions = null;
        return PackageService.Json(copy with { RevisionId = Guid.Empty, Extensions = extensions });
    }

    /// <summary>SHA-256 (lowercase hex) of the changes, one <c>action|entry|code|message</c> line each, so a save can prove it is the list that was previewed.</summary>
    public static string HashChanges(IReadOnlyList<ExportChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var text = string.Join("\n", changes.Select(c => $"{c.Action}|{c.Entry}|{c.Code}|{c.Message}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// Lowers every character in <paramref name="plan"/> for <paramref name="target"/> (<see cref="DownConvert.Below"/>) and lists what that
    /// loses. Content revisions pass through unchanged in C1 (C2 lowers them), so the two lookups are not used yet.
    /// </summary>
    internal static LoweredPlan Lower(PackageService.ExportPlan plan, LadderRow target, Func<Guid, bool> isBundledSource, Func<Guid, string> sourceTitle)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isBundledSource);
        ArgumentNullException.ThrowIfNull(sourceTitle);

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
                        changes.Add(new(entry, "character", character.Id, character.Name, null, "dropped", DropsCode, change.Message));
                        break;
                    case BelowAction.Blocks:
                        changes.Add(new(entry, "character", character.Id, character.Name, null, "blocks", BlockedCode, change.Message));
                        break;
                }
            }
        }

        var lowered = plan with { Characters = characters, FileName = WithTarget(plan.FileName, target) };
        return new LoweredPlan(lowered, changes, HashChanges(changes), changes.Any(c => c.Action == "blocks"), target);
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
