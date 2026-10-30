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

public static class DownConversion
{
    public const string DropsCode = "package.target-drops";
    public const string BlockedCode = "package.target-blocked";
    public const string ChangedCode = "package.target-changed";

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
