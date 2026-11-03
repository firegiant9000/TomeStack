using System.Reflection;
using System.Text.RegularExpressions;
using TomeStack.AppService.CharacterImport;

namespace TomeStack.AppService.Tests;

/// <summary>
/// As R151 for PDF detection: a thread pause on a busy computer must not make a valid sheet line unreadable, so no
/// character-import regex keeps the old 100 ms timeout (a hit-dice line timed out under a loaded test run, 2026-10-25).
/// </summary>
public class ImportRegexTimeoutTests
{
    private const BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerable<(string Name, Regex Regex)> ImportRegexes()
    {
        var types = typeof(ImportRegex).Assembly.GetTypes().Where(t => t.Namespace == typeof(ImportRegex).Namespace && !t.ContainsGenericParameters);
        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(AllStatic).Where(m => m.ReturnType == typeof(Regex) && m.GetParameters().Length == 0))
                yield return ($"{type.Name}.{method.Name}", (Regex)method.Invoke(null, null)!);
            foreach (var property in type.GetProperties(AllStatic).Where(p => p.PropertyType == typeof(Regex) && p.GetIndexParameters().Length == 0))
                yield return ($"{type.Name}.{property.Name}", (Regex)property.GetValue(null)!);
            foreach (var field in type.GetFields(AllStatic).Where(f => f.FieldType == typeof(Regex)))
                yield return ($"{type.Name}.{field.Name}", (Regex)field.GetValue(null)!);
        }
    }

    [Fact]
    public void Every_character_import_regex_uses_the_shared_timeout()
    {
        var regexes = ImportRegexes().ToList();
        Assert.True(regexes.Count >= 14, $"Only {regexes.Count} character-import regexes were found (14 on 2026-10-25); the reflection no longer sees them all.");

        var expected = TimeSpan.FromMilliseconds(ImportRegex.TimeoutMs);
        var wrong = regexes.Where(r => r.Regex.MatchTimeout != expected).Select(r => $"{r.Name} ({r.Regex.MatchTimeout.TotalMilliseconds} ms)").ToList();
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {regexes.Count} character-import regexes do not use ImportRegex.TimeoutMs: {string.Join(", ", wrong.Take(5))}");
    }

    [Fact]
    public void A_layout_map_pattern_compiles_with_the_shared_timeout()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(ImportRegex.TimeoutMs), LayoutMaps.Compile("^[0-9]+$").MatchTimeout);
    }

    [Fact]
    public void The_shared_timeout_is_well_above_a_thread_pause()
    {
        Assert.True(ImportRegex.TimeoutMs >= 1000);
    }
}
