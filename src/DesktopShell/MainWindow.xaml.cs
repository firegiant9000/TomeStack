using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
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
    private readonly int _charactersAtStart;
    private const string AdditionalBrowserArguments = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
    private bool _browserArgumentsSet;

    public MainWindow(TomeStackApp tomeStack, ShellOptions options)
    {
        _tomeStack = tomeStack;
        _dispatcher = new CommandDispatcher(tomeStack, host: new ShellHostServices(this));
        _options = options;
        _charactersAtStart = options.Smoke ? tomeStack.ListCharacters().Count : 0;
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
            // Test-only (smoke): the loader's own overrides proved unreliable on hosted runners (ADR-006), so the
            // missing-runtime check forces the same handler the loader's exception reaches.
            if (_options.SimulateMissingRuntime)
                throw new WebView2RuntimeNotFoundException("Simulated by --simulate-missing-webview2.");
            // Audit 2026-09-28: the loader reads extra browser arguments from the environment, which could open a
            // DevTools protocol port (ADR-006: no listening socket). Outside a development session they are dropped.
            _browserArgumentsSet = Environment.GetEnvironmentVariable(AdditionalBrowserArguments) is { Length: > 0 };
            if (!_options.AllowBrowserArguments)
                Environment.SetEnvironmentVariable(AdditionalBrowserArguments, null);
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
        // Investigation 2026-10-06 item 5 (owner answer 5): WebView2's browser accelerators are off outside a DevTools session, so
        // F5 and Ctrl+R cannot reload the app mid-session, Ctrl+P cannot print the whole page past the preview, and Ctrl+F, F3,
        // F7 and Alt+arrows do nothing. Zoom (Ctrl+plus, Ctrl+minus, Ctrl+0) is kept by the host below (ADR-006 "Security
        // measures in the shell"; no bridge command).
        settings.AreBrowserAcceleratorKeysEnabled = _options.DevTools;
        WebView.KeyDown += OnWebViewKeyDown;

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

    private const double MinZoom = 0.5;
    private const double MaxZoom = 3.0;
    private const double ZoomStep = 1.1;

    /// <summary>
    /// Ctrl+plus, Ctrl+minus and Ctrl+0 as the browser would do them. The WebView2 WPF control raises WPF key events for
    /// accelerator keys while the browser process waits, so the zoom is applied on the dispatcher, not inside the handler.
    /// </summary>
    private void OnWebViewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        var current = WebView.ZoomFactor;
        double next;
        switch (e.Key)
        {
            case Key.OemPlus:
            case Key.Add:
                next = Math.Min(MaxZoom, current * ZoomStep);
                break;
            case Key.OemMinus:
            case Key.Subtract:
                next = Math.Max(MinZoom, current / ZoomStep);
                break;
            case Key.D0:
            case Key.NumPad0:
                next = 1.0;
                break;
            default:
                return;
        }
        e.Handled = true;
        Dispatcher.BeginInvoke(() => WebView.ZoomFactor = next);
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
        if (!_smokeFinished && _smokeCommands.Contains("app.info") && _smokeCommands.Contains("character.list"))
        {
            try
            {
                var (ok, detail) = RunSmokeDataCheck();
                // The PDF viewer check finishes the smoke when the viewer has loaded (or failed); see OpenPdfViewer.
                if (!ok || !_awaitingViewer)
                    FinishSmoke(ok, detail);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
            {
                FinishSmoke(false, $"data-check-failed:{ex.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// Exit gate in the shipped binary: the bundled SRD content is seeded, a character using it saves, and it exports to
    /// a package that re-validates. Runs through the same dispatcher as the bridge. Persistence across restarts is shown
    /// by running the smoke twice on one <c>--data-dir</c> and comparing <c>charactersAtStart</c>.
    /// </summary>
    private (bool Ok, string Detail) RunSmokeDataCheck()
    {
        var halfOrc = new { contentId = "51c00000-0000-4000-8000-000000000001", revisionId = "51e00000-0000-4000-8000-000000000001" };
        using var created = Command("character.create", new
        {
            name = "Smoke Test",
            rulesFamily = "srd-5.1",
            baseAbilities = new { str = 15, dex = 14, con = 10, @int = 10, wis = 10, cha = 10 },
            pins = new[] { halfOrc },
        });
        if (!created.RootElement.GetProperty("ok").GetBoolean())
            return (false, "data-check-failed:character.create");
        var result = created.RootElement.GetProperty("result");
        var id = result.GetProperty("character").GetProperty("id").GetString();
        // SRD 5.1 Half-Orc: Strength +2 (species ability increases apply under 2014 rules).
        var strength = result.GetProperty("sheet").GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("field").GetString() == "ability.str.score").GetProperty("value").GetInt32();
        if (strength != 17)
            return (false, $"data-check-failed:strength={strength}");

        using var exported = Command("package.export", new { characterIds = new[] { id } });
        if (!exported.RootElement.GetProperty("ok").GetBoolean())
            return (false, "data-check-failed:package.export");
        var base64 = exported.RootElement.GetProperty("result").GetProperty("base64").GetString();
        using var preview = Command("package.preview", new { base64 });
        if (!preview.RootElement.GetProperty("ok").GetBoolean() || !preview.RootElement.GetProperty("result").GetProperty("canApply").GetBoolean())
            return (false, "data-check-failed:package.preview");

        _smokeCommands.AddRange(["character.create", "package.export", "package.preview"]);

        // M2 item 6 (ADR-005, SPEC S-04): a managed PDF opens at a cited page in the offline viewer. The smoke attaches a
        // generated two-page PDF to a throwaway homebrew source and opens page 2; the viewer's load finishes the smoke.
        using var source = Command("source.createHomebrew", new { title = "Smoke PDF", rulesFamilies = new[] { "srd-5.1" } });
        if (!source.RootElement.GetProperty("ok").GetBoolean())
            return (false, "data-check-failed:source.createHomebrew");
        var sourceId = source.RootElement.GetProperty("result").GetProperty("id").GetString();
        using var attached = Command("source.attachPdfData", new { sourceId, fileName = "smoke.pdf", base64 = Convert.ToBase64String(SmokePdf.Create(pages: 2)) });
        if (!attached.RootElement.GetProperty("ok").GetBoolean())
            return (false, "data-check-failed:source.attachPdfData");

        // M4 D2 (ADR-009 (c)): the shipped worker, TomeStack.ImportWorker.Host.exe next to this exe, extracts the PDF in a
        // child process (no socket). The job must complete with both pages.
        using var import = Command("import.start", new { sourceId, wholeDocument = true });
        if (!import.RootElement.GetProperty("ok").GetBoolean())
            return (false, "data-check-failed:import.start");
        var jobId = import.RootElement.GetProperty("result").GetProperty("id").GetString();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        string? status = null;
        var pages = 0;
        while (DateTime.UtcNow < deadline)
        {
            using var job = Command("import.status", new { jobId });
            var state = job.RootElement.GetProperty("result");
            status = state.GetProperty("status").GetString();
            pages = state.GetProperty("pagesDone").GetInt32();
            if (status is not ("queued" or "running"))
                break;
            Thread.Sleep(100);
        }
        if (status != "completed" || pages != 2)
            return (false, $"data-check-failed:import:{status}:{pages}");
        _smokeCommands.AddRange(["import.start", "import.status"]);

        // M2.1: the full backup of this folder, PDF included, restores into a second, clean data folder.
        if (LibraryRoundTrip() is { } failed)
            return (false, $"data-check-failed:library:{failed}");
        _smokeCommands.AddRange(["library.backup", "library.restore"]);

        _awaitingViewer = true;
        using var opened = Command("source.openPage", new { sourceId, page = 2 });
        if (!opened.RootElement.GetProperty("ok").GetBoolean() || !opened.RootElement.GetProperty("result").GetProperty("opened").GetBoolean())
        {
            _awaitingViewer = false;
            return (false, "data-check-failed:source.openPage");
        }
        _smokeCommands.AddRange(["source.createHomebrew", "source.attachPdfData", "source.openPage"]);
        return (true, "ok");
    }

    /// <summary>
    /// Smoke only: writes a full backup of this data folder to a scratch file, restores it into a fresh data folder next
    /// to it, and compares characters, sources and PDF copies. Both scratch items are deleted. Returns null on success.
    /// </summary>
    private string? LibraryRoundTrip()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "tomestack-smoke", $"library-{Guid.NewGuid():N}");
        var backup = scratch + ".tomestack.zip";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scratch)!);
            using (var file = File.Create(backup))
            {
                if (_tomeStack.WriteLibraryBackup(file).Warnings.Count > 0)
                    return "backup-warnings";
            }
            using var clean = TomeStackApp.Open(scratch, syncRoots: []);
            var preview = clean.PreviewLibraryRestore(backup);
            if (!preview.CanApply)
                return $"preview:{preview.Errors.FirstOrDefault()?.Code}";
            var restored = clean.ApplyLibraryRestore(backup);
            if (restored.PdfsCopied != 1)
                return $"pdfs:{restored.PdfsCopied}";
            if (clean.ListCharacters().Count != _tomeStack.ListCharacters().Count)
                return "characters";
            var withPdf = _tomeStack.ListSources().Where(s => s.AttachmentId is not null).Select(s => s.Id).Order().ToList();
            if (!clean.ListSources().Where(s => s.AttachmentId is not null).Select(s => s.Id).Order().SequenceEqual(withPdf))
                return "attachments";
            return null;
        }
        finally
        {
            try
            {
                File.Delete(backup);
                foreach (var file in Directory.Exists(scratch) ? Directory.GetFiles(scratch, "*", SearchOption.AllDirectories) : [])
                    File.SetAttributes(file, FileAttributes.Normal);
                if (Directory.Exists(scratch))
                    Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort; under %TEMP% */ }
        }
    }

    private bool _awaitingViewer;

    /// <summary>ADR-005, SPEC S-04: opens a PDF at a page in its own offline viewer window. Called on the UI thread.</summary>
    public bool OpenPdfViewer(string path, int page, string title)
    {
        if (WebView.CoreWebView2?.Environment is not { } environment)
            return false;
        Action<bool>? navigated = _options.Smoke
            ? ok =>
            {
                if (!_awaitingViewer)
                    return;
                _awaitingViewer = false;
                _smokeCommands.Add("pdf.viewer");
                FinishSmoke(ok, ok ? "ok" : "pdf-viewer-failed");
            }
            : null;
        var viewer = new PdfViewerWindow(environment, path, page, title, uri => _blockedRequests.Add(uri), navigated) { Owner = this };
        viewer.Show();
        return true;
    }

    private JsonDocument Command(string command, object payload) =>
        JsonDocument.Parse(_dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "smoke", command, payload })));

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
            charactersAtStart = _charactersAtStart,
            schemaVersion = _tomeStack.GetInfo().SchemaVersion,
            appVersion = _tomeStack.GetInfo().Version,
            blockedRequests = _blockedRequests,
            webView2Runtime = TryGetRuntimeVersion(),
            simulatedMissingRuntime = _options.SimulateMissingRuntime,
            // Development switches, honoured only in a Debug build or with --smoke (LIVING_SPECS D11).
            devTools = _options.DevTools,
            devFixtures = _options.DevFixtures,
            // Evidence only (no paths): which WebView2 loader overrides this process could see.
            webView2LoaderOverrides = new
            {
                environmentVariable = Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER") is { Length: > 0 },
                // Whether extra browser arguments were set, and whether they were applied (a development session only).
                additionalArguments = _browserArgumentsSet,
                additionalArgumentsApplied = _browserArgumentsSet && _options.AllowBrowserArguments,
                additionalArgumentsPolicy = HasWebView2Policy(Microsoft.Win32.Registry.LocalMachine, "AdditionalBrowserArguments")
                    || HasWebView2Policy(Microsoft.Win32.Registry.CurrentUser, "AdditionalBrowserArguments"),
                policy = HasBrowserFolderPolicy(Microsoft.Win32.Registry.LocalMachine) || HasBrowserFolderPolicy(Microsoft.Win32.Registry.CurrentUser),
            },
            dataDirectory = _tomeStack.DataDirectory,
        });
        File.WriteAllText(_options.SmokeReport ?? Path.Combine(_tomeStack.DataDirectory, "smoke-result.json"), report);
        Application.Current.Shutdown(success ? 0 : 2);
    }

    private static bool HasBrowserFolderPolicy(Microsoft.Win32.RegistryKey root) => HasWebView2Policy(root, "BrowserExecutableFolder");

    /// <summary>
    /// A WebView2 policy key with any value (keyed by exe name or AUMID). Evidence only: the shell cannot clear a policy,
    /// and whoever can write the per-user key can also replace the per-user install, so it is reported, not blocked.
    /// </summary>
    private static bool HasWebView2Policy(Microsoft.Win32.RegistryKey root, string policy)
    {
        using var key = root.OpenSubKey($@"Software\Policies\Microsoft\Edge\WebView2\{policy}");
        return key is not null && key.ValueCount > 0;
    }

    private static string? TryGetRuntimeVersion()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { return null; }
    }

    /// <summary>M2.1: a second launch on this data folder asked for this window. Called on the UI thread.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        // Windows may refuse focus to a background process; a brief Topmost raises the window anyway.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ShowStartupError(string message)
    {
        WebView.Visibility = Visibility.Collapsed;
        StartupError.Text = message;
        StartupError.Visibility = Visibility.Visible;
    }
}
