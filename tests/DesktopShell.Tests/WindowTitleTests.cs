using TomeStack.DesktopShell;

namespace TomeStack.DesktopShell.Tests;

/// <summary>
/// Wave 3 task 2.4: the window title mirrors the web page's document title, with a fixed fallback and a length cut.
/// </summary>
public class WindowTitleTests
{
    [Fact]
    public void A_document_title_is_used_as_the_window_title()
    {
        Assert.Equal("Wren Ashdown – TomeStack", WindowTitle.From("Wren Ashdown – TomeStack"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_missing_or_blank_title_falls_back_to_the_app_name(string? documentTitle)
    {
        Assert.Equal("TomeStack", WindowTitle.From(documentTitle));
    }

    [Fact]
    public void The_title_is_trimmed_before_it_is_cut()
    {
        Assert.Equal("Wren Ashdown", WindowTitle.From("  Wren Ashdown  "));
    }

    [Fact]
    public void A_title_longer_than_200_characters_is_cut_to_200()
    {
        var title = new string('a', 300);

        var result = WindowTitle.From(title);

        Assert.Equal(200, result.Length);
        Assert.Equal(title[..200], result);
    }
}
