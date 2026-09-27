namespace TomeStack.AppService.Tests;

/// <summary>ADR-005 (D02): warn when the data folder is inside a cloud sync root such as OneDrive.</summary>
public class DataFolderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "tomestack-sync-test", "OneDrive");

    [Theory]
    [InlineData("TomeStack", true)]
    [InlineData("Documents\\TomeStack", true)]
    [InlineData("", true)]
    public void Folder_inside_or_equal_to_a_sync_root_is_found(string relative, bool inside)
    {
        var folder = relative.Length == 0 ? Root : Path.Combine(Root, relative);
        Assert.Equal(inside, DataFolder.FindSyncRoot(folder, [Root]) is not null);
    }

    [Fact]
    public void A_sibling_that_only_shares_a_name_prefix_is_not_inside()
    {
        Assert.Null(DataFolder.FindSyncRoot(Root + "Backup\\TomeStack", [Root]));
    }

    [Fact]
    public void Matching_ignores_case_and_trailing_separators()
    {
        Assert.NotNull(DataFolder.FindSyncRoot(Path.Combine(Root.ToUpperInvariant(), "TomeStack"), [Root + "\\"]));
    }

    [Fact]
    public void No_roots_or_blank_roots_find_nothing()
    {
        Assert.Null(DataFolder.FindSyncRoot(Path.Combine(Root, "TomeStack"), []));
        Assert.Null(DataFolder.FindSyncRoot(Path.Combine(Root, "TomeStack"), ["", "  "]));
    }

    [Fact]
    public void App_info_warns_about_a_data_folder_in_a_sync_root_without_showing_the_full_path()
    {
        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        var app = TomeStackApp.Open(folder, syncRoots: [Root]);
        try
        {
            var warning = Assert.Single(app.GetInfo().Warnings);
            Assert.Equal(DataFolder.SyncRootWarningCode, warning.Code);
            Assert.Contains("'OneDrive'", warning.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetTempPath(), warning.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            app.Dispose();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void App_info_has_no_warning_outside_sync_roots()
    {
        using var temp = new TempApp();
        Assert.Empty(temp.App.GetInfo().Warnings);
    }

    [Fact]
    public void Discovery_never_throws_and_returns_distinct_roots()
    {
        var roots = DataFolder.DiscoverSyncRoots();
        Assert.Equal(roots.Count, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
