using System.Globalization;
using TomeStack.AppService;

namespace TomeStack.DesktopShell;

/// <summary>What a host window does after a WebView2 process failure.</summary>
public enum ProcessFailureAction
{
    /// <summary>Only the log entry; WebView2 recovers on its own.</summary>
    LogOnly,

    /// <summary>Reload the page once.</summary>
    Reload,

    /// <summary>The dialog has been shown (or suppressed); the host closes.</summary>
    Close,
}

/// <summary>
/// Every fatal in the shell leaves an <c>errors.log</c> entry and at most one plain dialog. The entry holds the detail with
/// the user-profile folder replaced by <c>%USERPROFILE%</c>; the dialog holds fixed wording and the log path, never
/// exception text. Holds no WPF types, so it is tested without a window. It never throws.
/// </summary>
/// <param name="log">Where entries go.</param>
/// <param name="logPath">Shown in the dialog.</param>
/// <param name="userProfile">The user-profile folder, scrubbed from every entry.</param>
/// <param name="showDialog">Shows the text to the user; null suppresses the dialog (<c>--smoke</c>).</param>
public sealed class CrashReporter(IErrorLog log, string logPath, string userProfile, Action<string>? showDialog)
{
    private const string ProfileToken = "%USERPROFILE%";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _renderFailures = [];
    private int _dialogShown;

    /// <summary>Logs a fatal (<c>dispatcher</c> or <c>appdomain</c>) and shows the dialog unless one is already up.</summary>
    public void ReportFatal(string kind, Exception exception)
    {
        Record(kind, exception.ToString());
        ShowOnce($"TomeStack hit an unexpected error and has to close. Details were saved to:\n{logPath}");
    }

    /// <summary>Logs a failure that is not a crash (an unobserved task fault). No dialog.</summary>
    public void ReportNonFatal(string kind, Exception exception) => Record(kind, exception.ToString());

    /// <summary>
    /// Logs a WebView2 process failure and says what the host window does. A renderer that exits or stops responding is
    /// reloaded once per host; the second time, and any browser-process exit, shows the dialog and closes the host.
    /// </summary>
    /// <param name="host">Identifies the window, so each viewer gets its own one reload.</param>
    /// <param name="kind">The <c>CoreWebView2ProcessFailedKind</c> name.</param>
    public ProcessFailureAction ReportProcessFailure(string host, string kind, string reason, int exitCode)
    {
        Record($"webview2-{kind}", string.Create(CultureInfo.InvariantCulture, $"WebView2 process failed: kind={kind} reason={reason} exitCode={exitCode}"));
        var action = kind switch
        {
            "RenderProcessExited" or "RenderProcessUnresponsive" => NextRenderAction(host),
            "BrowserProcessExited" => ProcessFailureAction.Close,
            _ => ProcessFailureAction.LogOnly,
        };
        if (action == ProcessFailureAction.Close)
            ShowOnce($"TomeStack's window stopped responding and could not be recovered. Details were saved to:\n{logPath}");
        return action;
    }

    private ProcessFailureAction NextRenderAction(string host)
    {
        lock (_gate)
        {
            _renderFailures[host] = _renderFailures.GetValueOrDefault(host) + 1;
            return _renderFailures[host] == 1 ? ProcessFailureAction.Reload : ProcessFailureAction.Close;
        }
    }

    private void Record(string kind, string detail)
    {
        try
        {
            log.Record(FileErrorLog.NewCorrelationId(), kind, Scrub(detail));
        }
        catch (Exception)
        {
            // Reporting a crash must never be a second crash.
        }
    }

    private string Scrub(string text) =>
        userProfile.Length == 0 ? text : text.Replace(userProfile, ProfileToken, StringComparison.OrdinalIgnoreCase);

    private void ShowOnce(string text)
    {
        if (showDialog is null || Interlocked.Exchange(ref _dialogShown, 1) == 1)
            return;
        try
        {
            showDialog(text);
        }
        catch (Exception)
        {
            // Nothing more can be done; the entry is already written.
        }
    }
}
