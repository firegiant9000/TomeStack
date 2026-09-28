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
    public void A_second_open_of_the_same_folder_is_refused_and_changes_nothing()
    {
        // The two races the audit found: the first instance is extracting a PDF, and is half-way through copying another.
        using var first = new TempApp();
        var running = new ImportJobRecord { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Sha256 = new string('a', 64), Status = ImportJobStatus.Running };
        first.App.Store.SaveImportJob(running);
        Directory.CreateDirectory(first.App.Store.AttachmentsDirectory);
        var partial = Path.Combine(first.App.Store.AttachmentsDirectory, $"{Guid.NewGuid():N}.partial");
        File.WriteAllText(partial, "%PDF-");

        var ex = Assert.Throws<DataFolderInUseException>(() => TomeStackApp.Open(first.Directory, syncRoots: []));
        Assert.DoesNotContain(first.Directory, ex.Message, StringComparison.OrdinalIgnoreCase);

        // The refused open neither marked the running import interrupted nor cleaned up the half-written copy.
        Assert.Equal(ImportJobStatus.Running, first.App.Store.FindImportJob(running.Id)!.Status);
        Assert.True(File.Exists(partial));
    }

    [Fact]
    public void The_same_folder_opens_again_after_the_first_app_is_disposed()
    {
        using var temp = new TempApp();
        temp.Reopen();
        Assert.NotEmpty(temp.App.ListSources());
    }

    [Fact]
    public void A_folder_is_matched_whatever_its_spelling()
    {
        using var temp = new TempApp();
        var respelled = temp.Directory.ToUpperInvariant() + Path.DirectorySeparatorChar;
        Assert.Throws<DataFolderInUseException>(() => TomeStackApp.Open(respelled, syncRoots: []));
        Assert.Equal(DataFolder.InstanceKey(temp.Directory), DataFolder.InstanceKey(respelled));
        Assert.NotEqual(DataFolder.InstanceKey(temp.Directory), DataFolder.InstanceKey(temp.Directory + "-other"));
    }

    [Fact]
    public void A_failed_open_releases_the_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A database from a newer build fails inside Open, after the lock was taken.
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(folder, TomeStackApp.DatabaseFileName)};Pooling=False"))
            {
                connection.Open();
                using var set = connection.CreateCommand();
                set.CommandText = "PRAGMA user_version = 999;";
                set.ExecuteNonQuery();
            }
            Assert.Throws<Persistence.NewerDatabaseException>(() => TomeStackApp.Open(folder, syncRoots: []));
            using var again = DataFolderLock.Acquire(folder); // would throw DataFolderInUseException if the lock had leaked
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void Discovery_never_throws_and_returns_distinct_roots()
    {
        var roots = DataFolder.DiscoverSyncRoots();
        Assert.Equal(roots.Count, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
