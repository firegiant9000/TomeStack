using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TomeStack.AppService;

namespace TomeStack.DesktopShell;

/// <summary>
/// ADR-005, SPEC S-04: shows one PDF at a page in WebView2's built-in PDF viewer, offline. No folder is mapped: the
/// window loads exactly <see cref="PdfViewerRequests.DocumentUrl"/>, which this window answers with the PDF's bytes, so a
/// link inside the PDF cannot reach other files next to a linked PDF. Every other request and navigation is refused and
/// reported, like the main window. The page is the viewer's <c>#page=</c> open parameter. The PDF is never parsed by
/// TomeStack, and no script is run on it.
/// </summary>
public sealed class PdfViewerWindow : Window
{
    private readonly List<Stream> _served = [];

    public PdfViewerWindow(CoreWebView2Environment environment, string path, int page, string title, Action<string> blocked, Action<bool>? navigated = null)
    {
        Title = title;
        Width = 900;
        Height = 1000;
        var view = new WebView2();
        Content = view;
        Closed += (_, _) =>
        {
            foreach (var stream in _served)
                stream.Dispose();
            _served.Clear();
        };
        Loaded += async (_, _) =>
        {
            try
            {
                await view.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
            {
                navigated?.Invoke(false);
                return;
            }
            var core = view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (PdfViewerRequests.IsAllowed(e.Uri))
                    return;
                e.Cancel = true;
                blocked(e.Uri);
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.AddWebResourceRequestedFilter("http://*", CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter("https://*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (!PdfViewerRequests.IsAllowed(e.Request.Uri))
                {
                    blocked(e.Request.Uri);
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                    return;
                }
                e.Response = Serve(core.Environment, path);
            };
            core.NavigationCompleted += (_, e) => navigated?.Invoke(e.IsSuccess);
            core.Navigate(PdfViewerRequests.Url(page));
        };
    }

    /// <summary>
    /// The PDF's bytes. Delete sharing lets the user remove or replace the attachment while it is open (ADR-005). The
    /// stream stays open while the viewer reads it and is disposed when the window closes.
    /// </summary>
    private CoreWebView2WebResourceResponse Serve(CoreWebView2Environment environment, string path)
    {
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _served.Add(stream);
            return environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: application/pdf");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
    }
}
