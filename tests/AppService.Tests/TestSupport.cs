using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A throwaway data directory per test; deleted on dispose.</summary>
internal sealed class TempApp : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));

    public TempApp() => App = TomeStackApp.Open(_directory, new FixedTime(Now));

    public TomeStackApp App { get; private set; }

    public void Reopen()
    {
        App.Dispose();
        App = TomeStackApp.Open(_directory, new FixedTime(Now));
    }

    public void Dispose()
    {
        App.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort on Windows file locks */ }
    }

    public static T LoadFixture<T>(string relativePath) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", relativePath)), RulesJson.Options)!;

    public static string Json<T>(T value) => JsonSerializer.Serialize(value, RulesJson.Options);
}
