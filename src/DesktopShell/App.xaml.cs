using System.IO;
using System.Windows;
using TomeStack.AppService;

namespace TomeStack.DesktopShell;

public partial class App : Application
{
    private TomeStackApp? _tomeStack;

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
            _tomeStack = TomeStackApp.Open(dataDirectory);
        }
        catch (Exception ex) when (!options.Smoke)
        {
            MessageBox.Show($"TomeStack could not open its data folder:\n{dataDirectory}\n\n{ex.Message}", "TomeStack", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainWindow = new MainWindow(_tomeStack, options);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tomeStack?.Dispose();
        base.OnExit(e);
    }
}

public sealed record ShellOptions(bool Smoke, bool DevTools, string? DataDirectory, string? SmokeReport)
{
    public static ShellOptions Parse(string[] args)
    {
        string? Value(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        return new ShellOptions(
            Smoke: args.Contains("--smoke"),
            DevTools: args.Contains("--devtools"),
            DataDirectory: Value("--data-dir"),
            SmokeReport: Value("--smoke-report"));
    }
}
