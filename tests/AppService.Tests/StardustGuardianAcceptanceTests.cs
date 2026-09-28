using System.Text;
using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M3 B1 (MVP definition of done 3; the M3 gate "the owner plays the Stardust Guardian character without D&amp;D Beyond").
/// The owner's material is private homebrew and stays in the gitignored <c>tests/RulesFixtures/local/stardust-guardian/</c>:
/// <list type="bullet">
/// <item><c>character.tomestack.zip</c>: a "Personal backup" export from TomeStack of the character, with its homebrew.</item>
/// <item><c>expectations.json</c> (optional): the owner's classification of each mechanic (see <see cref="Expectations"/>).</item>
/// </list>
/// The real test skips when the package is absent. Its messages carry counts and positions only, never names or text,
/// because test output is a log (tests/RulesFixtures/README.md); the full list goes to <c>report.md</c> in the same local
/// folder. The synthetic test runs the same pipeline on original content, so the gate proves the checks themselves.
/// </summary>
public class StardustGuardianAcceptanceTests
{
    /// <summary>The owner's classification: <c>{ "character": "optional name", "mechanics": [{ "feature", "effect", "automation" }] }</c>.</summary>
    public sealed record Expectations(string? Character, IReadOnlyList<ExpectedMechanic> Mechanics);

    /// <param name="Effect">The effect id, or null for a feature that is text only.</param>
    public sealed record ExpectedMechanic(string Feature, string? Effect, AutomationStatus Automation);

    internal static string LocalFolder
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
                directory = directory.Parent;
            return directory is null ? "" : Path.Combine(directory.FullName, "tests", "RulesFixtures", "local", "stardust-guardian");
        }
    }

    /// <summary>xUnit 2 has no runtime skip, so the skip is decided when the test is discovered.</summary>
    public sealed class LocalMaterialFactAttribute : FactAttribute
    {
        public LocalMaterialFactAttribute()
        {
            if (!File.Exists(Path.Combine(LocalFolder, "character.tomestack.zip")))
                Skip = "The owner's Stardust Guardian material is not in tests/RulesFixtures/local/stardust-guardian/ (it is private and gitignored).";
        }
    }

    [LocalMaterialFact]
    public void The_owners_Stardust_Guardian_imports_and_every_mechanic_is_classified_with_a_manual_step()
    {
        var package = File.ReadAllBytes(Path.Combine(LocalFolder, "character.tomestack.zip"));
        var expectationsPath = Path.Combine(LocalFolder, "expectations.json");
        var expectations = File.Exists(expectationsPath) ? JsonSerializer.Deserialize<Expectations>(File.ReadAllText(expectationsPath), RulesJson.Options) : null;

        var outcome = Check(package, expectations);

        // The full list stays in the owner's folder (gitignored), never in the test output.
        File.WriteAllText(Path.Combine(LocalFolder, "report.md"), outcome.Report, Encoding.UTF8);
        outcome.AssertPasses();
    }

    [Fact]
    public void The_pipeline_passes_a_synthetic_stand_in_and_reports_a_misclassification_without_naming_it()
    {
        // Original stand-in content: a homebrew subclass for the SRD 5.2.1 Barbarian with the four DoD 3 mechanics.
        byte[] package;
        using (var temp = new TempApp())
        {
            var source = temp.App.CreateHomebrewSource(new("Test Stand-in Homebrew", [RulesFamilies.Srd521]));
            var ward = Publish(temp.App, source, ContentKind.Feature, "Test Star Ward", null,
                new ResourceEffect { Id = "motes", ResourceId = "motes", Label = "Test motes", Maximum = "PB" },
                new RecoveryEffect { Id = "motes-long", ResourceId = "motes", On = RestPeriod.LongRest, Amount = "all", Timing = EffectTiming.OnLongRest },
                new RollEffect { Id = "flare", RollId = "flare", Label = "Test flare", Dice = "1d6", ResourceId = "motes", Automation = AutomationStatus.Assisted, Timing = EffectTiming.OnRoll, Text = "Spend a mote, then roll." });
            var lore = Publish(temp.App, source, ContentKind.Feature, "Test Star Lore", "You know the stars. (Reference only.)");
            var path = Publish(temp.App, source, ContentKind.Subclass, "Test Path of the Stars", "A stand-in subclass.",
                new ModifierEffect { Id = "star-initiative", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" },
                new GrantEffect { Id = "grant-ward", Grant = GrantKind.Content, Content = ward, Level = 3 },
                new GrantEffect { Id = "grant-lore", Grant = GrantKind.Content, Content = lore, Level = 3 });
            // Brenna (SRD 5.2.1 Barbarian 3) with the stand-in pinned instead of her Berserker path.
            var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
            var id = temp.App.SaveCharacter(brenna with
            {
                Choices = [.. brenna.Choices.Where(c => c.ChoiceId != "barbarian-subclass")],
                Pins = [.. brenna.Pins, path],
            }).Character.Id;
            package = temp.App.ExportCharacters([id]).Content;
        }

        var passing = Check(package, new(null,
        [
            new("Test Path of the Stars", "star-initiative", AutomationStatus.Automatic),
            new("Test Star Ward", "flare", AutomationStatus.Assisted),
            new("Test Star Lore", null, AutomationStatus.Reference),
        ]));
        passing.AssertPasses();
        Assert.Contains("| Test Star Lore | (text) | text | reference |", passing.Report, StringComparison.Ordinal);

        // The owner says the flare is automatic: the pipeline reports a mismatch (by count, not by name).
        var wrong = Check(package, new(null, [new("Test Star Ward", "flare", AutomationStatus.Automatic)]));
        Assert.Equal(1, wrong.Mismatches);
        var failure = Assert.ThrowsAny<Exception>(wrong.AssertPasses);
        Assert.DoesNotContain("Star", failure.Message, StringComparison.Ordinal); // no private names in the log
    }

    private static ContentReference Publish(TomeStackApp app, SourceRecord source, ContentKind kind, string name, string? summary, params Effect[] effects)
    {
        var draft = app.SaveDraft(new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = kind, Name = name, RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(source.Id, new(1)), Status = RevisionStatus.Draft, Summary = summary, Effects = effects,
        });
        return app.Publish(draft).Published;
    }

    /// <summary>What <see cref="Check"/> found: the report (for the local folder) and the checks, asserted without names.</summary>
    private sealed record Outcome(MechanicsReport Mechanics, int Mismatches, int Expected, IReadOnlyList<Diagnostic> ImportProblems, string Report)
    {
        public void AssertPasses()
        {
            Assert.True(ImportProblems.Count == 0, $"{ImportProblems.Count} content problem(s) after import (codes: {string.Join(", ", ImportProblems.Select(p => p.Code).Distinct())}).");
            Assert.True(Mechanics.HasModifier, "DoD 3: no automatic modifier from the homebrew source.");
            Assert.True(Mechanics.HasResource, "DoD 3: no tracked class resource from the homebrew source.");
            Assert.True(Mechanics.HasLimitedUseAction, "DoD 3: no limited-use action (a roll naming its resource) from the homebrew source.");
            Assert.True(Mechanics.HasReferenceOnlyFeature, "DoD 3: no reference-only feature from the homebrew source.");
            var missing = Mechanics.Mechanics.Select((m, i) => (m, i)).Where(p => p.m.MissingManualStep).Select(p => p.i + 1).ToList();
            Assert.True(missing.Count == 0, $"DoD 3: {missing.Count} unsupported mechanic(s) without a manual step (positions {string.Join(", ", missing)} in report.md).");
            Assert.True(Mismatches == 0, $"{Mismatches} of {Expected} expected classification(s) differ from TomeStack's (see report.md).");
        }
    }

    /// <summary>Imports the package into a clean data folder and classifies every homebrew mechanic of its character.</summary>
    private static Outcome Check(byte[] package, Expectations? expectations)
    {
        using var clean = new TempApp();
        clean.App.ApplyImport(package);
        var characters = clean.App.ListCharacters();
        var summary = expectations?.Character is { } name ? characters.Single(c => c.Name == name) : characters.First();
        var view = clean.App.GetCharacter(summary.Id);
        var mechanics = clean.App.Mechanics(new(summary.Id));
        var problems = view.Sheet.Diagnostics.Where(d => d.Code is "content.missing" or "content.schema-unsupported" or "content.source-missing" or "content.unpublished").ToList();

        var report = new StringBuilder();
        report.AppendLine($"# Stardust Guardian mechanics ({DateTimeOffset.Now:yyyy-MM-dd HH:mm})");
        report.AppendLine();
        report.AppendLine($"Automatic {mechanics.Count(AutomationStatus.Automatic)} · assisted {mechanics.Count(AutomationStatus.Assisted)} · reference {mechanics.Count(AutomationStatus.Reference)} · unsupported effect types {mechanics.Unsupported.Count}");
        report.AppendLine();
        report.AppendLine("| # | Feature | Effect | Type | TomeStack | Expected | Manual step |");
        report.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        var mismatches = 0;
        for (var i = 0; i < mechanics.Mechanics.Count; i++)
        {
            var m = mechanics.Mechanics[i];
            var expected = expectations?.Mechanics.FirstOrDefault(e => e.Feature == m.FeatureName && e.Effect == m.EffectId);
            if (expected is not null && expected.Automation != m.Automation)
                mismatches++;
            var step = m.Automation == AutomationStatus.Automatic ? "" : (m.ManualStep ?? "MISSING").Replace("|", "/", StringComparison.Ordinal).ReplaceLineEndings(" ");
            report.AppendLine($"| {i + 1} | {m.FeatureName} | {m.EffectId ?? "(text)"} | {m.EffectType} | {m.Automation.ToString().ToLowerInvariant()} | {expected?.Automation.ToString().ToLowerInvariant() ?? ""} | {step} |");
        }
        var unmatched = expectations?.Mechanics.Count(e => !mechanics.Mechanics.Any(m => m.FeatureName == e.Feature && m.EffectId == e.Effect)) ?? 0;
        if (unmatched > 0)
            report.AppendLine().AppendLine($"{unmatched} expected mechanic(s) were not found on the character.");
        return new(mechanics, mismatches + unmatched, expectations?.Mechanics.Count ?? 0, problems, report.ToString());
    }
}
