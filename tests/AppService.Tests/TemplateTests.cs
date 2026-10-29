namespace TomeStack.AppService.Tests;

/// <summary>
/// <see cref="TempApp"/> opens a copy of a seeded template instead of an empty folder (CI time, 2026-10-25). That shortcut is
/// only sound while the copy opens to exactly what a first launch gives, whatever the clock.
/// </summary>
public class TemplateTests
{
    private static string State(TomeStackApp app) => string.Join("\n",
        TempApp.Json(app.Store.ListSources()),
        string.Join("\n", app.Store.ListRevisionsInOrder().Select(r => $"{TempApp.Json(r)}:{app.Store.RevisionHash(r.RevisionId)}")),
        TempApp.Json(app.ListCharacters()),
        TempApp.Json(app.ListCampaigns()),
        TempApp.Json(app.GetInfo()));

    [Fact]
    public void A_copy_of_the_template_opens_to_what_a_first_launch_gives_at_any_clock()
    {
        var later = TempApp.Now.AddDays(3);
        using var fresh = new TempApp(now: later, fresh: true);
        using var copy = new TempApp(now: later);

        Assert.Equal(State(fresh.App), State(copy.App));
        Assert.Equal(
            Directory.GetFileSystemEntries(fresh.Directory, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(fresh.Directory, f)).Order(),
            Directory.GetFileSystemEntries(copy.Directory, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(copy.Directory, f)).Order());
    }

    [Fact]
    public void Each_copy_is_its_own_folder()
    {
        using var first = new TempApp();
        using var second = new TempApp();
        first.App.SaveCharacter(TempApp.LoadFixture<RulesCore.Character>("characters/srd51-quickfoot.json"));
        using var third = new TempApp();

        Assert.Single(first.App.ListCharacters());
        Assert.Empty(second.App.ListCharacters());
        Assert.Empty(third.App.ListCharacters());
    }
}
