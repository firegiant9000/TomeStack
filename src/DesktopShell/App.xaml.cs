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
            // The shipped app seeds only the SRD packs; the original fixtures are for development (TOMESTACK_DEV_FIXTURES=1).
            _tomeStack = TomeStackApp.Open(dataDirectory, devFixtures: Environment.GetEnvironmentVariable("TOMESTACK_DEV_FIXTURES") == "1");
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

/// <param name="SimulateMissingRuntime">
/// Test-only: take the "WebView2 Runtime not found" path without asking the loader. Honoured only with
/// <c>--smoke</c>, so a normal launch cannot be switched into it.
/// </param>
public sealed record ShellOptions(bool Smoke, bool DevTools, string? DataDirectory, string? SmokeReport, bool SimulateMissingRuntime = false)
{
    public static ShellOptions Parse(string[] args)
    {
        string? Value(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var smoke = args.Contains("--smoke");
        return new ShellOptions(
            Smoke: smoke,
            DevTools: args.Contains("--devtools"),
            DataDirectory: Value("--data-dir"),
            SmokeReport: Value("--smoke-report"),
            SimulateMissingRuntime: smoke && args.Contains("--simulate-missing-webview2"));
    }
}
