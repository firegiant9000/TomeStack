using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Down-conversion export, lane A: the release ladder (D83) and the "opens in" lookup.</summary>
public class ReleaseLadderTests
{
    [Fact]
    public void Rows_are_ascending_and_match_the_spec_table()
    {
        var rows = ReleaseLadder.Rows;

        Assert.Equal([new Version(0, 4, 0), new Version(0, 5, 0), new Version(0, 6, 0), new Version(0, 7, 0)], rows.Select(r => r.Release));
        Assert.Equal([(7, 9, 1, 9, 9), (8, 9, 2, 9, 10), (9, 10, 2, 9, 11), (10, 11, 2, 9, 11)],
            rows.Select(r => (r.CharacterSchema, r.ContentSchema, r.GapNoteSchema, r.FormatVersion, r.DatabaseSchema)));
        Assert.Equal([false, true, true, false], rows.Select(r => r.OfferedAsTarget));
    }

    [Fact]
    public void CurrentRow_is_the_row_of_the_build_version()
    {
        Assert.Equal(new Version(AppVersionStamp.CurrentVersion).Minor, ReleaseLadder.CurrentRow.Release.Minor);
    }

    [Fact]
    public void PreviousMinor_is_the_row_before_current()
    {
        Assert.Equal(new Version(0, 6, 0), ReleaseLadder.PreviousMinor?.Release);
    }

    [Fact]
    public void Constants_never_fall_below_the_current_row()
    {
        var row = ReleaseLadder.CurrentRow;

        Assert.True(row.CharacterSchema <= Character.CurrentSchemaVersion);
        Assert.True(row.ContentSchema <= ContentRevision.CurrentSchemaVersion);
        Assert.True(row.GapNoteSchema <= GapNote.CurrentSchemaVersion);
        Assert.Equal(1, Campaign.CurrentSchemaVersion); // no ladder column: campaign data has been v1 in every release
        Assert.True(row.FormatVersion <= PackageManifest.CurrentFormatVersion);
        Assert.True(row.DatabaseSchema <= SqliteStore.LatestSchemaVersion);
    }

    [Fact]
    public void Bundled_sets_are_subsets_of_the_catalog()
    {
        var catalog = TomeStackApp.BundledRevisionIds;

        Assert.All(ReleaseLadder.Rows, row =>
        {
            Assert.NotEmpty(row.BundledRevisionIds);
            Assert.True(row.BundledRevisionIds.IsSubsetOf(catalog), $"{row.Release} has a bundled revision the current packs lack");
        });
    }

    [Theory]
    [InlineData("0.6.0", true)]
    [InlineData("0.5.0", true)]
    [InlineData("0.4.0", false)]
    [InlineData("0.7.0", false)]
    [InlineData("0.7.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("0.6", false)]
    public void Target_accepts_offered_rows_only(string? release, bool offered)
    {
        var target = ReleaseLadder.Target(release);

        Assert.Equal(offered, target is not null);
        if (offered) Assert.Equal(release, target!.Release.ToString(3));
    }
}
