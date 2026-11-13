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

    [Fact]
    public void A_title_of_exactly_200_characters_is_unchanged()
    {
        var title = new string('a', 200);

        Assert.Equal(title, WindowTitle.From(title));
    }

    [Fact]
    public void A_201_character_title_is_cut_to_200_without_an_ellipsis()
    {
        var result = WindowTitle.From(new string('a', 201));

        Assert.Equal(new string('a', 200), result);
    }

    [Fact]
    public void A_surrogate_pair_straddling_the_cut_is_not_split()
    {
        var title = new string('a', 199) + "\U0001F409" + "bbb"; // the pair occupies chars 199 and 200

        var result = WindowTitle.From(title);

        Assert.Equal(new string('a', 199), result);
    }

    [Fact]
    public void A_surrogate_pair_ending_exactly_at_the_cut_is_kept()
    {
        var title = new string('a', 198) + "\U0001F409" + "bbb";

        var result = WindowTitle.From(title);

        Assert.Equal(200, result.Length);
        Assert.EndsWith("\U0001F409", result);
    }
}
