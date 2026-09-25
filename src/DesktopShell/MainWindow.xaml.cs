using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TomeStack.AppService;

namespace TomeStack.DesktopShell;

/// <summary>
/// Hosts the React UI in WebView2 and connects it to the in-process application service through
/// the WebView2 message bridge (ADR-006). No network listener is opened. The UI is served from a
/// local folder through a virtual host name, and navigation outside it is blocked.
/// </summary>
public partial class MainWindow : Window
{
    private const string AppHost = "app.tomestack.localhost";
    private const string AppOrigin = $"https://{AppHost}/";
    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromSeconds(30);

    private readonly TomeStackApp _tomeStack;
    private readonly CommandDispatcher _dispatcher;
    private readonly ShellOptions _options;
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private readonly Stopwatch _sinceStart = Stopwatch.StartNew();
    private readonly List<string> _smokeCommands = [];
    private readonly List<string> _blockedRequests = [];

    public MainWindow(TomeStackApp tomeStack, ShellOptions options)
    {
        _tomeStack = tomeStack;
        _dispatcher = new CommandDispatcher(tomeStack);
        _options = options;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                await InitializeWebViewAsync();
            }
            catch (Exception ex)
            {
                ShowStartupError($"TomeStack could not start its interface: {ex.Message}");
                FinishSmoke(false, $"startup-failed:{ex.GetType().Name}");
            }
        };
        if (options.Smoke)
        {
            _ = Task.Delay(SmokeTimeout).ContinueWith(_ => Dispatcher.Invoke(() => FinishSmoke(false, "timeout")), TaskScheduler.Default);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(_tomeStack.DataDirectory, "WebView2"));
            await WebView.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupError("The Microsoft Edge WebView2 Runtime is required. Install it from Microsoft (the Evergreen Standalone Installer works offline) and start TomeStack again.");
            FinishSmoke(false, "webview2-runtime-missing");
            return;
        }

        var uiFolder = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!File.Exists(Path.Combine(uiFolder, "index.html")))
        {
            ShowStartupError($"TomeStack's interface files are missing from {uiFolder}. Reinstall TomeStack; your data folder is untouched.");
            FinishSmoke(false, "ui-bundle-missing");
            return;
        }

        var core = WebView.CoreWebView2;
        var settings = core.Settings;
        settings.AreDevToolsEnabled = _options.DevTools;
        settings.AreDefaultContextMenusEnabled = _options.DevTools;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;

        core.SetVirtualHostNameToFolderMapping(AppHost, uiFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(AppOrigin, StringComparison.OrdinalIgnoreCase))
                e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;

        // Offline by default: any http(s) request outside the app's virtual host is refused by the shell,
        // independent of the page's CSP.
        core.AddWebResourceRequestedFilter("http://*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri.StartsWith(AppOrigin, StringComparison.OrdinalIgnoreCase))
                return;
            _blockedRequests.Add(e.Request.Uri);
            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
                FinishSmoke(false, $"navigation-failed:{e.WebErrorStatus}");
        };
        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate($"{AppOrigin}index.html");
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(AppOrigin, StringComparison.OrdinalIgnoreCase))
            return;

        var request = e.WebMessageAsJson;
        string response;
        await _dispatchGate.WaitAsync();
        try
        {
            response = await Task.Run(() => _dispatcher.Dispatch(request));
        }
        finally
        {
            _dispatchGate.Release();
        }
        WebView.CoreWebView2?.PostWebMessageAsJson(response);

        if (_options.Smoke)
            RecordSmoke(request, response);
    }

    /// <summary>The UI issues app.info and character.list on load; both succeeding proves UI load plus bridge round trip.</summary>
    private void RecordSmoke(string request, string response)
    {
        using var req = JsonDocument.Parse(request);
        using var res = JsonDocument.Parse(response);
        if (!res.RootElement.GetProperty("ok").GetBoolean())
        {
            FinishSmoke(false, $"command-failed:{response}");
            return;
        }
        _smokeCommands.Add(req.RootElement.GetProperty("command").GetString() ?? "");
        if (_smokeCommands.Contains("app.info") && _smokeCommands.Contains("character.list"))
            FinishSmoke(true, "ok");
    }

    private bool _smokeFinished;

    private void FinishSmoke(bool success, string detail)
    {
        if (!_options.Smoke || _smokeFinished)
            return;
        _smokeFinished = true;
        if (success && _blockedRequests.Count > 0)
            (success, detail) = (false, "ui-attempted-network-access");
        var report = JsonSerializer.Serialize(new
        {
            success,
            detail,
            elapsedMs = _sinceStart.ElapsedMilliseconds,
            processStartToReadyMs = (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds,
            commands = _smokeCommands,
            blockedRequests = _blockedRequests,
            webView2Runtime = TryGetRuntimeVersion(),
            dataDirectory = _tomeStack.DataDirectory,
        });
        File.WriteAllText(_options.SmokeReport ?? Path.Combine(_tomeStack.DataDirectory, "smoke-result.json"), report);
        Application.Current.Shutdown(success ? 0 : 2);
    }

    private static string? TryGetRuntimeVersion()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { return null; }
    }

    private void ShowStartupError(string message)
    {
        WebView.Visibility = Visibility.Collapsed;
        StartupError.Text = message;
        StartupError.Visibility = Visibility.Visible;
    }
}
