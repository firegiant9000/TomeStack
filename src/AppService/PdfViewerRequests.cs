namespace TomeStack.AppService;

/// <summary>
/// ADR-005, SPEC S-04: the requests the shell's offline PDF viewer window may make. The viewer loads exactly one URL,
/// <see cref="DocumentUrl"/>, and the shell answers it with the PDF's bytes. No folder is mapped, so a link inside the PDF
/// cannot reach other files next to a linked PDF (Downloads, Documents). Pure, so it is tested here; the shell
/// (<c>PdfViewerWindow</c>) applies it.
/// </summary>
public static class PdfViewerRequests
{
    public const string Host = "pdf.tomestack.localhost";

    /// <summary>The one document URL. The file's real name is never part of it.</summary>
    public const string DocumentUrl = $"https://{Host}/document.pdf";

    /// <summary>The URL the viewer opens: the PDF at <paramref name="page"/> (the viewer's <c>#page=</c> open parameter).</summary>
    public static string Url(int page) => $"{DocumentUrl}#page={page}";

    /// <summary>Whether the viewer may load <paramref name="uri"/>: the document itself, at any page, and nothing else.</summary>
    public static bool IsAllowed(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme == Uri.UriSchemeHttps
        && string.Equals(parsed.Host, Host, StringComparison.OrdinalIgnoreCase)
        && parsed.IsDefaultPort
        && parsed.AbsolutePath == "/document.pdf"
        && parsed.Query.Length == 0;
}
