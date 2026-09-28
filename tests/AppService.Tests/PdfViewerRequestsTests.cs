namespace TomeStack.AppService.Tests;

/// <summary>
/// ADR-005, SPEC S-04: the offline PDF viewer loads exactly one file. A linked PDF can sit in a folder with other files
/// (Downloads, Documents); a link inside the PDF must not reach them.
/// </summary>
public class PdfViewerRequestsTests
{
    [Fact]
    public void The_viewer_opens_the_one_pdf_at_a_page()
    {
        var url = PdfViewerRequests.Url(42); // the file's own name ("Player Handbook.pdf") is never in the URL

        Assert.Equal("https://pdf.tomestack.localhost/document.pdf#page=42", url);
        Assert.True(PdfViewerRequests.IsAllowed(url));
        Assert.True(PdfViewerRequests.IsAllowed("https://pdf.tomestack.localhost/document.pdf"));
        Assert.True(PdfViewerRequests.IsAllowed("https://PDF.tomestack.localhost/document.pdf#page=3"));
    }

    [Theory]
    [InlineData("https://pdf.tomestack.localhost/notes.html")] // a file next to the PDF
    [InlineData("https://pdf.tomestack.localhost/")]
    [InlineData("https://pdf.tomestack.localhost/document.pdf/../secrets.txt")]
    [InlineData("https://pdf.tomestack.localhost/%2e%2e/secrets.txt")]
    [InlineData("https://pdf.tomestack.localhost/document.pdf?x=1")]
    [InlineData("https://pdf.tomestack.localhost.example.com/document.pdf")]
    [InlineData("http://pdf.tomestack.localhost/document.pdf")]
    [InlineData("https://example.com/document.pdf")]
    [InlineData("file:///C:/Users/someone/Downloads/document.pdf")]
    [InlineData("not a url")]
    public void Anything_else_is_refused(string uri) => Assert.False(PdfViewerRequests.IsAllowed(uri));
}
