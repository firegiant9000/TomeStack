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

/// <summary>What the caller of <see cref="CrashReporter.ShowUnrecoverableDialog"/> does next.</summary>
public enum UnrecoverableResult
{
    /// <summary>Another dialog was already open; do nothing.</summary>
    NotOwner,

    /// <summary>Close this window only.</summary>
    Close,

    /// <summary>A fatal was reported while the dialog was open; shut the app down.</summary>
    ShutdownApp,
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
    private readonly HashSet<string> _closedHosts = [];
    private readonly Dictionary<string, string> _lastKind = [];
    private bool _dialogOpen;
    private bool _openIsFatal;
    private bool _fatalPending;

    /// <summary>
    /// Logs a fatal (<c>dispatcher</c> or <c>appdomain</c>) and shows the dialog unless one is already up. Returns true when
    /// this call owned the dialog (or none is shown, in smoke), so the caller may exit afterwards; false when a nested
    /// failure only logged, because exiting would close the dialog the user has not read yet.
    /// </summary>
    public bool ReportFatal(string kind, Exception exception)
    {
        Record(kind, exception.ToString);
        return ShowDialog(FatalText, fatal: true);
    }

    /// <summary>Logs a failure that is not a crash (an unobserved task fault). No dialog.</summary>
    public void ReportNonFatal(string kind, Exception exception) => Record(kind, exception.ToString);

    /// <summary>
    /// Logs a WebView2 process failure and says what the host window does. A renderer that exits is reloaded once per
    /// host; the second exit is <see cref="ProcessFailureAction.Close"/>. A browser-process exit is Close for the main
    /// host only (its shutdown closes the viewers). An unresponsive renderer is only logged (the first time until another
    /// kind is reported), never reloaded: a busy page would lose unsaved work. After Close, a host only logs.
    /// The caller then posts <see cref="ShowUnrecoverableDialog"/> and closes the host, outside the WebView2 callback.
    /// </summary>
    /// <param name="host">Identifies the window (<c>main</c> or <c>pdf-…</c>), so each gets its own one reload.</param>
    /// <param name="kind">The <c>CoreWebView2ProcessFailedKind</c> name.</param>
    public ProcessFailureAction ReportProcessFailure(string host, string kind, string reason, int exitCode)
    {
        bool repeatUnresponsive;
        lock (_gate)
        {
            repeatUnresponsive = kind == "RenderProcessUnresponsive" && _lastKind.GetValueOrDefault(host) == kind;
            _lastKind[host] = kind;
        }
        if (!repeatUnresponsive)
            Record($"webview2-{kind}", () => string.Create(CultureInfo.InvariantCulture, $"WebView2 process failed: kind={kind} reason={reason} exitCode={exitCode}"));
        lock (_gate)
        {
            if (_closedHosts.Contains(host))
                return ProcessFailureAction.LogOnly;
            var action = kind switch
            {
                "RenderProcessExited" => NextRenderAction(host),
                "BrowserProcessExited" when host == MainHost => ProcessFailureAction.Close,
                _ => ProcessFailureAction.LogOnly,
            };
            if (action == ProcessFailureAction.Close)
                _closedHosts.Add(host);
            return action;
        }
    }

    /// <summary>The host name of the main window; a browser-process exit closes the app only through it.</summary>
    public const string MainHost = "main";

    /// <summary>
    /// The dialog for a window that could not be recovered (after <see cref="ProcessFailureAction.Close"/>). Returns
    /// <see cref="UnrecoverableResult.NotOwner"/> when another dialog was already open, <see cref="UnrecoverableResult.Close"/>
    /// when the host may close, and <see cref="UnrecoverableResult.ShutdownApp"/> when a fatal was reported while this
    /// dialog was open: that fatal only logged (it could not exit under the open dialog), so the fatal text is shown here
    /// and the caller shuts the app down.
    /// </summary>
    public UnrecoverableResult ShowUnrecoverableDialog()
    {
        if (!ShowDialog($"TomeStack's window stopped responding and could not be recovered. Details were saved to:\n{logPath}", fatal: false))
            return UnrecoverableResult.NotOwner;
        bool fatalPending;
        lock (_gate)
        {
            fatalPending = _fatalPending;
            _fatalPending = false;
        }
        if (!fatalPending)
            return UnrecoverableResult.Close;
        ShowDialog(FatalText, fatal: true);
        return UnrecoverableResult.ShutdownApp;
    }

    private string FatalText => $"TomeStack hit an unexpected error and has to close. Details were saved to:\n{logPath}";

    /// <remarks>Called under <c>_gate</c>.</remarks>
    private ProcessFailureAction NextRenderAction(string host)
    {
        _renderFailures[host] = _renderFailures.GetValueOrDefault(host) + 1;
        return _renderFailures[host] == 1 ? ProcessFailureAction.Reload : ProcessFailureAction.Close;
    }

    private void Record(string kind, Func<string> detail)
    {
        try
        {
            log.Record(FileErrorLog.NewCorrelationId(), kind, Scrub(detail()));
        }
        catch (Exception)
        {
            // Reporting a crash must never be a second crash.
        }
    }

    private string Scrub(string text) =>
        userProfile.Length == 0 ? text : text.Replace(userProfile, ProfileToken, StringComparison.OrdinalIgnoreCase);

    /// <summary>At most one dialog is open at a time; a failure while it is open only logs. Later failures can show again.</summary>
    /// <returns>True when this call owned the dialog, or when dialogs are suppressed (smoke); false when one was already open.</returns>
    private bool ShowDialog(string text, bool fatal)
    {
        if (showDialog is null)
            return true;
        lock (_gate)
        {
            if (_dialogOpen)
            {
                // A fatal behind a viewer's dialog cannot exit now; the viewer's owner shuts the app down when it returns.
                if (fatal && !_openIsFatal)
                    _fatalPending = true;
                return false;
            }
            _dialogOpen = true;
            _openIsFatal = fatal;
        }
        try
        {
            showDialog(text);
        }
        catch (Exception)
        {
            // Nothing more can be done; the entry is already written.
        }
        finally
        {
            lock (_gate)
                _dialogOpen = false;
        }
        return true;
    }
}
