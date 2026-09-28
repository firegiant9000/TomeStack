using System.IO;
using System.Windows;
using TomeStack.AppService;
using Velopack;

namespace TomeStack.DesktopShell;

public partial class App : Application
{
    private TomeStackApp? _tomeStack;

    /// <summary>
    /// Velopack runs its install/update/uninstall hooks here and exits when launched for one (ADR-008). It makes no
    /// network call: TomeStack never creates an <c>UpdateManager</c> (ADR-001, no update check).
    /// </summary>
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = ShellOptions.Parse(e.Args);
        var dataDirectory = options.DataDirectory
            ?? (options.Smoke
                ? Path.Combine(Path.GetTempPath(), "tomestack-smoke", Guid.NewGuid().ToString("N"))
                : TomeStackApp.DefaultDataDirectory());

        try
        {
            // The shipped app seeds only the SRD packs; the original fixtures are for development (TOMESTACK_DEV_FIXTURES=1,
            // honoured only in a Debug build or with --smoke; LIVING_SPECS D11).
            _tomeStack = TomeStackApp.Open(dataDirectory, devFixtures: options.DevFixtures);
        }
        catch (DataFolderInUseException ex)
        {
            // M2.1: a second launch on the same folder brings the running window forward and exits. It never touches the data.
            var activated = SingleInstance.ActivateExisting(dataDirectory);
            if (options.Smoke)
                SingleInstance.WriteSmokeReport(options, dataDirectory, activated);
            else if (!activated)
                MessageBox.Show(ex.Message, "TomeStack", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(SingleInstance.InUseExitCode);
            return;
        }
        catch (Exception ex) when (!options.Smoke)
        {
            MessageBox.Show($"TomeStack could not open its data folder:\n{dataDirectory}\n\n{ex.Message}", "TomeStack", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow(_tomeStack, options);
        MainWindow = window;
        _activation = SingleInstance.ListenForActivation(dataDirectory, () => Dispatcher.BeginInvoke(window.BringToFront));
        window.Show();
    }

    private IDisposable? _activation;

    protected override void OnExit(ExitEventArgs e)
    {
        _activation?.Dispose();
        _tomeStack?.Dispose();
        base.OnExit(e);
    }
}

/// <param name="DevTools">
/// <c>--devtools</c>: the WebView2 developer tools and context menus. Honoured only in a Debug build, or with
/// <c>--smoke</c> on the smoke's own throwaway data folder (no <c>--data-dir</c>) (audit 2026-09-28). The shell also clears
/// <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c> outside those cases, so no variable can open a debugging port either.
/// </param>
/// <param name="SimulateMissingRuntime">
/// Test-only: take the "WebView2 Runtime not found" path without asking the loader. Honoured only with
/// <c>--smoke</c>, so a normal launch cannot be switched into it.
/// </param>
/// <param name="DevFixtures">
/// <c>TOMESTACK_DEV_FIXTURES=1</c>: seed the original test fixtures into the data folder. Honoured only in a Debug build
/// (LIVING_SPECS D11): seeded revisions cannot be removed, so no Release launch, smoke included, seeds them.
/// </param>
/// <param name="AllowBrowserArguments">
/// Whether WebView2 may apply <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c> (for example a remote debugging port). Only in a
/// development session, the same one <paramref name="DevTools"/> needs; otherwise the shell clears the variable first.
/// </param>
public sealed record ShellOptions(
    bool Smoke, bool DevTools, string? DataDirectory, string? SmokeReport, bool SimulateMissingRuntime = false, bool DevFixtures = false,
    bool AllowBrowserArguments = false)
{
#if DEBUG
    private const bool DebugBuild = true;
#else
    private const bool DebugBuild = false;
#endif

    public static ShellOptions Parse(string[] args) =>
        Parse(args, Environment.GetEnvironmentVariable("TOMESTACK_DEV_FIXTURES"), DebugBuild);

    /// <param name="devFixturesVariable">The value of <c>TOMESTACK_DEV_FIXTURES</c>.</param>
    /// <param name="debugBuild">Whether this is a Debug build, where the development switches are always honoured.</param>
    public static ShellOptions Parse(string[] args, string? devFixturesVariable, bool debugBuild)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? Value(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var smoke = args.Contains("--smoke");
        var dataDirectory = Value("--data-dir");
        // A smoke run counts only on its own throwaway folder (no --data-dir), never on a chosen library.
        var development = debugBuild || (smoke && dataDirectory is null);
        return new ShellOptions(
            Smoke: smoke,
            DevTools: development && args.Contains("--devtools"),
            AllowBrowserArguments: development,
            DataDirectory: dataDirectory,
            SmokeReport: Value("--smoke-report"),
            SimulateMissingRuntime: smoke && args.Contains("--simulate-missing-webview2"),
            // Seeded fixtures cannot be removed (published revisions are insert-only), so only a Debug build seeds them.
            DevFixtures: debugBuild && devFixturesVariable == "1");
    }
}
