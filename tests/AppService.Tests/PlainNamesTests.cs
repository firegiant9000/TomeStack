using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>D99: the shared plain names in service messages (rules families, source titles, files this version cannot read).</summary>
public class PlainNamesTests
{
    [Fact]
    public void Known_families_read_as_the_rules_year_and_unknown_ids_stay()
    {
        Assert.Equal("the 2014 rules", PlainNames.Family(RulesFamilies.Srd51));
        Assert.Equal("the 2024 rules", PlainNames.Family(RulesFamilies.Srd521));
        Assert.Equal("homebrew-x", PlainNames.Family("homebrew-x"));
    }

    [Fact]
    public void One_unknown_source_is_not_numbered()
    {
        Assert.Equal("Harbor Notes, an unknown source", PlainNames.SourceTitles(["Harbor Notes", null]));
    }

    [Fact]
    public void Two_unknown_sources_are_numbered_so_they_do_not_read_as_one()
    {
        Assert.Equal("Harbor Notes, an unknown source (1), an unknown source (2)", PlainNames.SourceTitles([null, "Harbor Notes", null]));
    }

    [Fact]
    public void A_newer_TomeStack_file_names_its_file_version_and_never_the_format_id()
    {
        var text = PackageService.UnsupportedFormatText(true, 99, "Update TomeStack.");

        Assert.Equal($"This TomeStack file is not supported by this version (file version 99; this version reads up to {PackageManifest.CurrentFormatVersion}). Update TomeStack.", text);
        Assert.DoesNotContain(PackageManifest.FormatName, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_in_another_format_is_not_a_TomeStack_file()
    {
        Assert.Equal("This is not a TomeStack file. Update TomeStack.", PackageService.UnsupportedFormatText(false, 1, "Update TomeStack."));
    }
}
