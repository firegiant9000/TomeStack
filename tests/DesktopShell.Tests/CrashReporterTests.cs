using System.IO;
using TomeStack.AppService;

namespace TomeStack.DesktopShell.Tests;

/// <summary>Every fatal leaves a log entry with no profile path and at most one plain dialog.</summary>
public sealed class CrashReporterTests : IDisposable
{
    private const string Profile = @"C:\Users\fakeperson";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tomestack-crash-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _dialogs = [];
    private readonly FileErrorLog _log;

    public CrashReporterTests()
    {
        _log = new FileErrorLog(_folder, TimeProvider.System);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private CrashReporter Reporter(bool withDialog = true) => new(_log, _log.FilePath, Profile, withDialog ? _dialogs.Add : null);

    private string[] Entries() => File.Exists(_log.FilePath) ? File.ReadAllLines(_log.FilePath).Where(l => l.Contains(" command=", StringComparison.Ordinal)).ToArray() : [];

    private static Exception Failure() => new InvalidOperationException(@"could not open C:\USERS\FakePerson\AppData\Local\secret.json");

    [Theory]
    [InlineData("dispatcher")]
    [InlineData("appdomain")]
    public void A_fatal_writes_one_entry_named_for_its_kind(string kind)
    {
        Reporter().ReportFatal(kind, Failure());

        var entry = Assert.Single(Entries());
        Assert.EndsWith($" command={kind}", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unobserved_task_fault_is_logged_without_a_dialog()
    {
        Reporter().ReportNonFatal("task", Failure());

        Assert.EndsWith(" command=task", Assert.Single(Entries()), StringComparison.Ordinal);
        Assert.Empty(_dialogs);
    }

    [Fact]
    public void The_entry_holds_no_path_under_the_profile_and_the_dialog_no_exception_text()
    {
        var exception = Failure();
        try { throw exception; } catch (InvalidOperationException) { /* gives it a stack trace */ }

        Reporter().ReportFatal("dispatcher", exception);

        var text = File.ReadAllText(_log.FilePath);
        Assert.DoesNotContain(Profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\secret.json", text, StringComparison.Ordinal);
        var dialog = Assert.Single(_dialogs);
        Assert.Equal($"TomeStack hit an unexpected error and has to close. Details were saved to:\n{_log.FilePath}", dialog);
        Assert.DoesNotContain("secret.json", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fatal_after_the_dialog_was_closed_shows_it_again()
    {
        var reporter = Reporter();

        reporter.ReportFatal("dispatcher", Failure());
        reporter.ReportFatal("appdomain", Failure());

        Assert.Equal(2, _dialogs.Count);
        Assert.Equal(2, Entries().Length);
    }

    [Fact]
    public void Smoke_has_no_dialog_and_still_logs()
    {
        Reporter(withDialog: false).ReportFatal("dispatcher", Failure());

        Assert.Single(Entries());
        Assert.Empty(_dialogs);
    }

    [Fact]
    public void A_dialog_that_throws_does_not_escape()
    {
        var reporter = new CrashReporter(_log, _log.FilePath, Profile, _ => throw new InvalidOperationException("no window"));

        reporter.ReportFatal("dispatcher", Failure());

        Assert.Single(Entries());
    }

    [Fact]
    public void A_renderer_failure_reloads_once_then_closes_with_the_viewer_dialog()
    {
        var reporter = Reporter();

        Assert.Equal(ProcessFailureAction.Reload, reporter.ReportProcessFailure("main", "RenderProcessExited", "Crashed", 5));
        Assert.Empty(_dialogs);
        Assert.Equal(ProcessFailureAction.Close, reporter.ReportProcessFailure("main", "RenderProcessUnresponsive", "Unresponsive", 0));
        Assert.Empty(_dialogs); // the host posts the dialog outside the WebView2 callback
        reporter.ShowUnrecoverableDialog();

        Assert.Equal($"TomeStack's window stopped responding and could not be recovered. Details were saved to:\n{_log.FilePath}", Assert.Single(_dialogs));
        var entries = Entries();
        Assert.EndsWith(" command=webview2-RenderProcessExited", entries[0], StringComparison.Ordinal);
        Assert.EndsWith(" command=webview2-RenderProcessUnresponsive", entries[1], StringComparison.Ordinal);
        Assert.Contains("kind=RenderProcessExited reason=Crashed exitCode=5", File.ReadAllText(_log.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_host_gets_its_own_reload()
    {
        var reporter = Reporter();

        Assert.Equal(ProcessFailureAction.Reload, reporter.ReportProcessFailure("main", "RenderProcessExited", "Crashed", 1));
        Assert.Equal(ProcessFailureAction.Reload, reporter.ReportProcessFailure("pdf-1", "RenderProcessExited", "Crashed", 1));
    }

    [Fact]
    public void A_browser_process_exit_closes_at_once()
    {
        var action = Reporter().ReportProcessFailure("main", "BrowserProcessExited", "Terminated", 1);

        Assert.Equal(ProcessFailureAction.Close, action);
        Assert.Single(Entries());
    }

    [Fact]
    public void A_viewer_dialog_does_not_use_up_the_dialog_for_a_later_fatal()
    {
        var reporter = Reporter();
        reporter.ShowUnrecoverableDialog();

        reporter.ReportFatal("dispatcher", Failure());

        Assert.Equal(2, _dialogs.Count);
    }

    [Fact]
    public void A_fatal_while_a_dialog_is_open_only_logs()
    {
        CrashReporter? reporter = null;
        reporter = new CrashReporter(_log, _log.FilePath, Profile, text =>
        {
            _dialogs.Add(text);
            reporter!.ReportFatal("appdomain", Failure());
        });

        reporter.ReportFatal("dispatcher", Failure());

        Assert.Single(_dialogs);
        Assert.Equal(2, Entries().Length);
    }

    [Fact]
    public void An_exception_whose_text_throws_does_not_escape()
    {
        Reporter().ReportFatal("dispatcher", new ThrowingException());

        Assert.Single(_dialogs);
    }

    private sealed class ThrowingException : Exception
    {
        public override string ToString() => throw new InvalidOperationException("no text");
    }

    [Fact]
    public void Other_process_failures_are_only_logged()
    {
        var action = Reporter().ReportProcessFailure("main", "GpuProcessExited", "Crashed", 1);

        Assert.Equal(ProcessFailureAction.LogOnly, action);
        Assert.Empty(_dialogs);
        Assert.EndsWith(" command=webview2-GpuProcessExited", Assert.Single(Entries()), StringComparison.Ordinal);
    }
}
