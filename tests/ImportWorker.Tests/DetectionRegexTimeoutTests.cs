using System.Reflection;
using System.Text.RegularExpressions;
using TomeStack.ImportWorker.Detection;

namespace TomeStack.ImportWorker.Tests;

/// <summary>R150/R151: a thread pause on a busy computer must not fail an import with detect.timeout, so no detection regex keeps the old 100 ms budget.</summary>
public class DetectionRegexTimeoutTests
{
    private static readonly Type[] DetectionTypes = [typeof(CandidateDetector), typeof(ClassProposals), typeof(ClassTableReconstruction)];

    private const BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerable<(string Name, Regex Regex)> DetectionRegexes()
    {
        foreach (var type in DetectionTypes)
        {
            foreach (var method in type.GetMethods(AllStatic).Where(m => m.ReturnType == typeof(Regex) && m.GetParameters().Length == 0))
                yield return ($"{type.Name}.{method.Name}", (Regex)method.Invoke(null, null)!);
            foreach (var property in type.GetProperties(AllStatic).Where(p => p.PropertyType == typeof(Regex) && p.GetIndexParameters().Length == 0))
                yield return ($"{type.Name}.{property.Name}", (Regex)property.GetValue(null)!);
            foreach (var field in type.GetFields(AllStatic).Where(f => f.FieldType == typeof(Regex)))
                yield return ($"{type.Name}.{field.Name}", (Regex)field.GetValue(null)!);
        }
    }

    // CandidateDetector's weapon-mastery match builds its pattern per name, so it stays inline; it uses the same constant.
    [Fact]
    public void Every_detection_regex_uses_the_shared_timeout()
    {
        var regexes = DetectionRegexes().ToList();
        Assert.True(regexes.Count >= 50, $"Only {regexes.Count} detection regexes were found (50 at R151); the reflection no longer sees them all.");

        var expected = TimeSpan.FromMilliseconds(DetectionLimits.RegexTimeoutMs);
        var wrong = regexes.Where(r => r.Regex.MatchTimeout != expected).Select(r => $"{r.Name} ({r.Regex.MatchTimeout.TotalMilliseconds} ms)").ToList();
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {regexes.Count} detection regexes do not use DetectionLimits.RegexTimeoutMs: {string.Join(", ", wrong.Take(5))}");
    }

    [Fact]
    public void The_shared_timeout_is_well_above_a_thread_pause()
    {
        Assert.True(DetectionLimits.RegexTimeoutMs >= 1000);
    }
}
