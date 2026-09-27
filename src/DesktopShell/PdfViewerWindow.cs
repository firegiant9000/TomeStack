using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TomeStack.DesktopShell;

/// <summary>
/// ADR-005, SPEC S-04: shows one PDF at a page in WebView2's built-in PDF viewer, offline. The PDF's folder is mapped
/// to its own virtual host for this window only (deny CORS); the page is the viewer's <c>#page=</c> open parameter.
/// Every http(s) request outside that host is refused and reported, like the main window. The PDF is never parsed by
/// TomeStack, and no script is run on it.
/// </summary>
public sealed class PdfViewerWindow : Window
{
    private const string PdfHost = "pdf.tomestack.localhost";
    private const string PdfOrigin = $"https://{PdfHost}/";

    public PdfViewerWindow(CoreWebView2Environment environment, string path, int page, string title, Action<string> blocked, Action<bool>? navigated = null)
    {
        Title = title;
        Width = 900;
        Height = 1000;
        var view = new WebView2();
        Content = view;
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
            core.SetVirtualHostNameToFolderMapping(PdfHost, Path.GetDirectoryName(Path.GetFullPath(path))!, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith(PdfOrigin, StringComparison.OrdinalIgnoreCase))
                    e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.AddWebResourceRequestedFilter("http://*", CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter("https://*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (e.Request.Uri.StartsWith(PdfOrigin, StringComparison.OrdinalIgnoreCase))
                    return;
                blocked(e.Request.Uri);
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            core.NavigationCompleted += (_, e) => navigated?.Invoke(e.IsSuccess);
            core.Navigate($"{PdfOrigin}{Uri.EscapeDataString(Path.GetFileName(path))}#page={page}");
        };
    }
}
