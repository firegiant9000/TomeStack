using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Exports;
using TomeStack.AppService.Extensions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Exports
{
    /// <summary>
    /// <c>export.preview</c> (ADR-012): what an adapter would write for a character, before anything is written.
    /// </summary>
    /// <param name="TargetVersion">The target release the adapter was verified against (for Foundry, the pinned pair).</param>
    /// <param name="Differences">Values Foundry calculates itself that may not match TomeStack's (Foundry only).</param>
    public sealed record VttExportPreview(
        Guid Token, string Target, string FileName, long Bytes, string AdapterVersion, string? TargetVersion, SheetPurpose Purpose,
        IReadOnlyList<SheetDropped> Dropped, IReadOnlyList<SheetNotice> Notices, IReadOnlyList<string> Differences, IReadOnlyList<Diagnostic> Warnings);

    /// <summary>The neutral sheet-export JSON (ADR-012): the sheet export model v1 itself, keys sorted, written indented.</summary>
    public static class SheetJson
    {
        public const string Target = "sheet-json";
        public const string AdapterVersion = "1.0.0";
        public const string FileSuffix = ".tomestack-sheet.json";

        public static JsonNode Map(SheetExport sheet) => FoundryDnd5e.Sorted(JsonSerializer.SerializeToNode(sheet, RulesJson.Compact))!;

        /// <summary>The adapter's validator: the model's format and version, and every list the schema requires. Empty when valid.</summary>
        public static IReadOnlyList<string> Validate(JsonNode node)
        {
            try
            {
                return Check(node);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                return ["a value has the wrong type"];
            }
        }

        private static List<string> Check(JsonNode node)
        {
            var problems = new List<string>();
            if (node is not JsonObject obj)
                return ["the file is an object"];
            if (obj["format"]?.GetValue<string>() != SheetExport.FormatName || obj["formatVersion"]?.GetValue<int>() != SheetExport.CurrentFormatVersion)
                problems.Add("format and formatVersion name the sheet export model v1");
            foreach (var list in new[] { "abilities", "skills", "fields", "hitDice", "slots", "spellcasting", "resources", "attacks", "features", "toggles", "scales", "dropped", "notices" })
            {
                if (obj[list] is not JsonArray)
                    problems.Add($"{list} is a list");
            }
            if (obj["character"]?["name"] is not JsonValue)
                problems.Add("character.name is present");
            return problems;
        }
    }
}

namespace TomeStack.AppService
{
    /// <summary>
    /// M6 slice 4 (ADR-012, accepted 2026-09-29): export adapters. Each maps the sheet export model v1 (ADR-011), filtered by
    /// purpose (ADR-007 item 11), to a file the user saves with the native Save dialog and imports into another tool by
    /// hand. Nothing is uploaded, and the shipped app opens no socket (ADR-001, ADR-006). Outputs are validated and scanned
    /// for paths and the user name before anything is written. Documented in docs/features/export-adapters.md.
    /// </summary>
    public sealed partial class TomeStackApp
    {
        public static IReadOnlyList<string> ExportTargets { get; } = [FoundryDnd5e.Target, SheetJson.Target];

        private readonly ConcurrentDictionary<Guid, (string FileName, byte[] Bytes)> _pendingExports = new();

        /// <summary><c>export.preview { characterId, target, purpose }</c>: runs the adapter and shows the result; writes nothing.</summary>
        public VttExportPreview PreviewVttExport(Guid characterId, string target, SheetPurpose purpose)
        {
            if (!ExportTargets.Contains(target))
                throw new AppValidationException([new("export.target-unknown", $"'{(target?.Length > 40 ? target[..40] : target)}' is not an export target this TomeStack has ({string.Join(", ", ExportTargets)}).")]);
            var sheet = SheetExportFor(characterId, purpose);
            string text;
            string fileName;
            string adapterVersion;
            string? targetVersion;
            IReadOnlyList<string> differences = [];
            var baseName = SafeName(sheet.Character.Name);
            if (target == FoundryDnd5e.Target)
            {
                var actor = FoundryDnd5e.Map(sheet);
                if (FoundryDnd5e.Validate(actor) is { Count: > 0 } problems)
                    throw new AppValidationException([new("export.invalid-output", $"The Foundry file would not match what dnd5e {FoundryDnd5e.SystemVersion} reads ({problems[0]}), so nothing was written.")]);
                text = actor.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                fileName = baseName + FoundryDnd5e.FileSuffix;
                (adapterVersion, targetVersion) = (FoundryDnd5e.AdapterVersion, $"Foundry VTT {FoundryDnd5e.CoreVersion} with dnd5e {FoundryDnd5e.SystemVersion}");
                differences = FoundryDifferences(sheet);
            }
            else
            {
                var node = SheetJson.Map(sheet);
                if (SheetJson.Validate(node) is { Count: > 0 } problems)
                    throw new AppValidationException([new("export.invalid-output", $"The sheet file would not be a valid sheet export model ({problems[0]}), so nothing was written.")]);
                text = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                fileName = baseName + SheetJson.FileSuffix;
                (adapterVersion, targetVersion) = (SheetJson.AdapterVersion, $"sheet export model v{SheetExport.CurrentFormatVersion}");
            }
            // The model is an allowlist with no path field; the scan is the second line (ADR-012 "Privacy").
            if (OutputScan.Leaks(text, SensitiveStrings()))
                throw new AppValidationException([new("export.output-refused", "The file would contain a local path or your Windows user name, so nothing was written.")]);
            var bytes = Encoding.UTF8.GetBytes(text);
            KeepFew(_pendingExports);
            var token = Guid.NewGuid();
            _pendingExports[token] = (fileName, bytes);
            var warnings = new List<Diagnostic>();
            if (purpose == SheetPurpose.Personal)
                warnings.Add(new("export.personal", "Personal copy: includes your own homebrew. Do not share it."));
            if (target == FoundryDnd5e.Target)
                warnings.Add(new("export.experimental", "Experimental: the Foundry file is checked against the dnd5e data model, but no import into a real Foundry world has been recorded yet."));
            return new(token, target, fileName, bytes.LongLength, adapterVersion, targetVersion, purpose, sheet.Dropped, sheet.Notices, differences, warnings);
        }

        /// <summary><c>export.saveAs</c> / <c>export.download</c>: the previewed file, once.</summary>
        public (string FileName, byte[] Bytes) VttExportOutput(Guid token) =>
            _pendingExports.TryRemove(token, out var output)
                ? output
                : throw new AppValidationException([new("export.expired", "Show the export preview again.")]);

        /// <summary>
        /// Foundry recalculates skills, saves, initiative and spellcasting numbers from abilities, proficiency levels and its
        /// own bonuses. Where TomeStack's value is not what that gives, the preview says so (ADR-012).
        /// </summary>
        private static IReadOnlyList<string> FoundryDifferences(SheetExport sheet)
        {
            var differences = new List<string>();
            var pb = sheet.Fields.FirstOrDefault(f => f.Id == FieldIds.ProficiencyBonus)?.Value ?? 2;
            var mods = sheet.Abilities.ToDictionary(a => a.Ability, a => a.Modifier);
            int Expected(string ability, SheetProficiency proficiency) =>
                mods.GetValueOrDefault(ability) + (proficiency switch { SheetProficiency.Expertise => 2 * pb, SheetProficiency.Proficient => pb, _ => 0 });
            foreach (var skill in sheet.Skills.Where(s => s.Total != Expected(s.Ability, s.Proficiency)))
                differences.Add($"{skill.Label}: TomeStack {Signed(skill.Total)}; Foundry works out {Signed(Expected(skill.Ability, skill.Proficiency))} before its own bonuses.");
            foreach (var ability in sheet.Abilities.Where(a => a.Save != Expected(a.Ability, a.SaveProficiency)))
                differences.Add($"{ability.Ability.ToUpperInvariant()} save: TomeStack {Signed(ability.Save)}; Foundry works out {Signed(Expected(ability.Ability, ability.SaveProficiency))}.");
            if (sheet.Fields.FirstOrDefault(f => f.Id == FieldIds.Initiative) is { } initiative && initiative.Value != mods.GetValueOrDefault("dex"))
                differences.Add($"Initiative: TomeStack {Signed(initiative.Value)}; Foundry starts from the Dexterity modifier ({Signed(mods.GetValueOrDefault("dex"))}).");
            foreach (var caster in sheet.Spellcasting)
            {
                if (caster.AttackBonus != mods.GetValueOrDefault(caster.Ability) + pb || caster.SaveDc != 8 + mods.GetValueOrDefault(caster.Ability) + pb)
                    differences.Add($"{caster.Name} spellcasting: TomeStack {Signed(caster.AttackBonus)} to hit, DC {caster.SaveDc}; Foundry works these out from the ability and proficiency bonus.");
            }
            if (sheet.Dropped.Count > 0)
                differences.Add("Content left out for sharing is not in the file; the totals above still include it.");
            return differences;
        }

        private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
