using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using TomeStack.AppService.Diagnostics;
using TomeStack.RulesCore;
using Xunit;
using Xunit.Abstractions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Lane S4 "Copy diagnostics": the text a user pastes into a bug report. It holds the app and Windows versions, the data
/// folder's name and the newest error-log entries: exception types and frames with file names only (no message, no folder
/// path), with the user name and source titles replaced. Every check runs on the raw text, never on an HTML-encoded copy
/// (gotcha 2026-09-29).
/// </summary>
public sealed class DiagnosticsCopyTests(ITestOutputHelper output)
{
    private const string Profile = @"C:\Users\José Fixture";

    private static readonly DiagnosticsEnvironment Machine = new("9.8.7", "Microsoft Windows 10.0.99999", Profile, "José Fixture");

    private static readonly SourceRecord Homebrew = Source("0f0e0d0c-0b0a-0908-0706-050403020100", "Fixture Vault of Embers");

    private static readonly SourceRecord Embervault = Source("1f0e0d0c-0b0a-0908-0706-050403020100", "Embervault");

    private static SourceRecord Source(string id, string title) => new()
    {
        Id = Guid.Parse(id), Title = title, Publisher = "Fixture", RulesFamilies = [RulesFamilies.Srd51], EditionVersion = "homebrew",
        License = "Fixture", Redistributable = false,
    };

    private static ErrorLogEntry Entry(string detail, string command = "character.save", string id = "ABCD1234") =>
        new("2026-10-27T12:00:00.0000000+00:00", id, command, detail);

    /// <summary>The user name as the redactor sees a word: no letter touching it.</summary>
    private static void AssertNoUserName(string name, string text) =>
        Assert.DoesNotMatch(new Regex($@"(?<!\p{{L}}){Regex.Escape(name)}(?!\p{{L}})", RegexOptions.IgnoreCase), text);

    [Fact]
    public void Redacts_user_profile_paths()
    {
        var entry = Entry(string.Join('\n',
            "System.IO.IOException: The file C:\\Users\\José Fixture\\Documents\\x.pdf is locked.",
            @"   at TomeStack.AppService.Attachments.Open(String path) in C:\Users\José Fixture\src\TomeStack\Attachments.cs:line 42",
            "   at TomeStack.AppService.Other.Run() in c:/users/josé fixture/src/Other.cs:line 7",
            @"   at TomeStack.AppService.Third.Run() in C:\Users\Someone Else\OneDrive - Contoso\src\Third.cs:line 9",
            @"   at TomeStack.AppService.Fourth.Run() in \\fs01\home$\jdoe\src\Fourth.cs:line 3"));

        var text = DiagnosticsCopy.Build([entry], Profile + @"\AppData\Local\TomeStack", [], Machine);

        Assert.DoesNotContain("José", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fixture", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Someone Else", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Contoso", text, StringComparison.Ordinal);
        Assert.DoesNotContain("jdoe", text, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\", text, StringComparison.Ordinal);
        Assert.Contains("   at TomeStack.AppService.Attachments.Open(...) in Attachments.cs:line 42", text, StringComparison.Ordinal);
        Assert.Contains("in Other.cs:line 7", text, StringComparison.Ordinal);
        Assert.Contains("in Third.cs:line 9", text, StringComparison.Ordinal);
        Assert.Contains("in Fourth.cs:line 3", text, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException: (message left out)", text, StringComparison.Ordinal);
        Assert.Contains("Data folder: TomeStack", text, StringComparison.Ordinal);
        Assert.Contains("App version: 9.8.7", text, StringComparison.Ordinal);
        Assert.Contains("Windows: Microsoft Windows 10.0.99999", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_only_the_data_folder_and_redacts_the_user_name_in_names()
    {
        var entry = Entry(@"   at TomeStack.AppService.Store.Open() in D:\Games\Arlo Tables\TomeStack Data\josé_old.cs:line 1");

        var text = DiagnosticsCopy.Build([entry], @"D:\Games\Arlo Tables\TomeStack Data", [], Machine with { UserName = "josé" });

        Assert.DoesNotContain("Games", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Arlo", text, StringComparison.Ordinal);
        Assert.Contains("Data folder: TomeStack Data", text, StringComparison.Ordinal);
        Assert.Contains("in <user>_old.cs:line 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Redacts_source_titles_to_ids()
    {
        var entry = Entry(@"   at TomeStack.AppService.Attachments.Open() in C:\src\Embervault.cs:line 1");

        var text = DiagnosticsCopy.Build([entry], @"D:\Fixture Vault of Embers", [Homebrew, Embervault], Machine);

        Assert.DoesNotContain("Vault of Embers", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Embervault", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Data folder: source:{Homebrew.Id}", text, StringComparison.Ordinal);
        Assert.Contains($"in source:{Embervault.Id}.cs:line 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_title_that_is_a_code_word_leaves_frames_and_the_header_readable()
    {
        var entry = Entry(string.Join('\n', "System.InvalidOperationException: x", "   at TomeStack.AppService.Character.Save() in Character.cs:line 1"));

        var text = DiagnosticsCopy.Build([entry], "TomeStack", [Source("2f0e0d0c-0b0a-0908-0706-050403020100", "at"), Source("3f0e0d0c-0b0a-0908-0706-050403020100", "System")], Machine);

        Assert.Contains("TomeStack diagnostics", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: (message left out)", text, StringComparison.Ordinal);
        Assert.Contains("   at TomeStack.AppService.Character.Save() in Character.cs:line 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Never_includes_gap_note_text()
    {
        // The writer never logs payloads; this guards the reader: a message (first line or continuation, even one shaped
        // like a frame) is never copied, only the exception type and the stack frames.
        const string Sentinel = "SENTINEL-GAP-7Q";
        var entry = Entry(string.Join('\n',
            $"System.InvalidOperationException: The note says {Sentinel}",
            $"and a second line of it: {Sentinel}",
            $"   at {Sentinel} words() in x.cs:line 1",
            $"   at a() in the {Sentinel} text:line 1",
            $"   at m({Sentinel} words here)",
            $" ---> System.FormatException: inner {Sentinel}",
            "   at TomeStack.AppService.GapNotes.Add(AddGapNoteRequest request)",
            "   --- End of inner exception stack trace ---",
            "   at TomeStack.AppService.CommandDispatcher.Execute(String command, Nullable`1 payload)"));

        var text = DiagnosticsCopy.Build([entry], "TomeStack", [], Machine);

        Assert.DoesNotContain(Sentinel, text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains(" ---> System.FormatException", text, StringComparison.Ordinal);
        Assert.Contains("   at a() in (file):line 1", text, StringComparison.Ordinal);
        Assert.Contains("   at TomeStack.AppService.GapNotes.Add(...)", text, StringComparison.Ordinal);
        Assert.Contains("   --- End of inner exception stack trace ---", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_the_shells_fixed_process_failure_line()
    {
        var entry = Entry("WebView2 process failed: kind=RenderProcessExited reason=Crashed exitCode=-1073741819", "webview2-RenderProcessExited");

        var text = DiagnosticsCopy.Build([entry], "TomeStack", [], Machine);

        Assert.Contains("command=webview2-RenderProcessExited", text, StringComparison.Ordinal);
        Assert.Contains("WebView2 process failed: kind=RenderProcessExited reason=Crashed exitCode=-1073741819", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_the_code_of_a_COM_exception()
    {
        var entry = Entry(new COMException("Fixture message", unchecked((int)0x80004005)).ToString());

        var text = DiagnosticsCopy.Build([entry], "TomeStack", [], Machine);

        Assert.Contains("System.Runtime.InteropServices.COMException (0x80004005): (message left out)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture message", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_every_frame_of_an_aggregate_with_two_inner_exceptions()
    {
        var aggregate = new AggregateException(Thrown("first"), Thrown("second"));

        var text = DiagnosticsCopy.Build([Entry(aggregate.ToString())], "TomeStack", [], Machine);
        output.WriteLine(text);

        Assert.Equal(2, Regex.Count(text, $@"   at {Regex.Escape(typeof(DiagnosticsCopyTests).FullName!)}\.Thrown\(\.\.\.\) in DiagnosticsCopyTests\.cs:line \d+"));
        Assert.Contains("(Inner Exception #1) System.InvalidOperationException: (message left out)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<---", text, StringComparison.Ordinal);
        Assert.DoesNotContain("second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Caps_at_max_entries()
    {
        var entries = Enumerable.Range(0, DiagnosticsCopy.MaxEntries + 5).Select(i => Entry("System.Exception: x", id: $"{i:X8}")).ToList();

        var text = DiagnosticsCopy.Build(entries, "TomeStack", [], Machine);

        // The newest are kept: the log reads oldest first.
        Assert.DoesNotContain("] 00000004 command=", text, StringComparison.Ordinal);
        Assert.Contains("] 00000005 command=", text, StringComparison.Ordinal);
        Assert.Contains($"] {DiagnosticsCopy.MaxEntries + 4:X8} command=", text, StringComparison.Ordinal);
        Assert.Equal(DiagnosticsCopy.MaxEntries, text.Split('\n').Count(l => l.Contains(" command=", StringComparison.Ordinal)));
    }

    [Fact]
    public void Says_so_when_the_log_is_empty_or_unreadable()
    {
        Assert.Contains("No errors have been logged.", DiagnosticsCopy.Build([], "TomeStack", [], Machine), StringComparison.Ordinal);
        Assert.Contains("The error log could not be read.", DiagnosticsCopy.Build(null, "TomeStack", [], Machine), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_recent_parses_entries_oldest_first_across_the_rolled_over_file_and_caps_them()
    {
        using var temp = new TempApp();
        var log = (FileErrorLog)temp.App.ErrorLog;
        log.Record("00000001", "character.list", "one");
        File.Move(log.FilePath, log.FilePath + ".1");
        log.Record("00000002", "character.get", new InvalidOperationException("two\n\nwith a blank line"));
        log.Record("00000003", "task", "three");

        var all = log.ReadRecent(10)!;
        var newest = log.ReadRecent(2)!;

        Assert.Equal(["00000001", "00000002", "00000003"], all.Select(e => e.CorrelationId));
        Assert.Equal(["character.list", "character.get", "task"], all.Select(e => e.Command));
        Assert.StartsWith("System.InvalidOperationException: two", all[1].Detail, StringComparison.Ordinal);
        Assert.Contains("with a blank line", all[1].Detail, StringComparison.Ordinal);
        Assert.Equal("three", all[2].Detail);
        Assert.Equal(["00000002", "00000003"], newest.Select(e => e.CorrelationId));
    }

    [Fact]
    public void Read_recent_reads_lower_case_ids_and_ignores_a_header_shaped_line_inside_a_message()
    {
        using var temp = new TempApp();
        var log = (FileErrorLog)temp.App.ErrorLog;
        log.Record("00000001", "character.save", "System.Exception: one\n[2026-01-01T00:00:00Z] DEADBEEF command=forged\n   at A.B()");
        File.AppendAllText(log.FilePath, $"[2026-10-27T00:00:00.0000000+00:00] 3fa2b1c0 command=import.run{Environment.NewLine}   at C.D(){Environment.NewLine}{Environment.NewLine}");
        log.Record("00000003", "task", "three");

        var all = log.ReadRecent(10)!;

        Assert.Equal(["00000001", "3fa2b1c0", "00000003"], all.Select(e => e.CorrelationId));
        Assert.Contains("command=forged", all[0].Detail, StringComparison.Ordinal);
        Assert.Equal("   at C.D()", all[1].Detail);
        Assert.Contains("] 3fa2b1c0 command=import.run", DiagnosticsCopy.Build(all, "TomeStack", [], Machine), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_recent_is_empty_without_a_log_and_null_when_it_cannot_be_read()
    {
        using var temp = new TempApp();
        var log = (FileErrorLog)temp.App.ErrorLog;

        Assert.Empty(log.ReadRecent(10)!);

        log.Record("00000001", "task", "one");
        using (new FileStream(log.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Null(log.ReadRecent(10));
    }

    [Fact]
    public void Dispatches_diagnostics_copy_with_the_titles_of_this_library_redacted()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new HomebrewSourceRequest("AshenCodex", [RulesFamilies.Srd51], ConfirmOwnWork: true));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        temp.App.ErrorLog.Record("ABCD1234", "content.publish", $"   at X.Y() in {profile}\\src\\AshenCodex.cs:line 3");
        var before = temp.App.ListCharacters().Count;

        Assert.Contains("diagnostics.copy", CommandDispatcher.Commands);
        using var response = JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch("""{"id":"1","command":"diagnostics.copy"}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean(), response.RootElement.ToString());
        var text = response.RootElement.GetProperty("result").GetProperty("text").GetString()!;
        Assert.Contains("ABCD1234 command=content.publish", text, StringComparison.Ordinal);
        Assert.Contains($"   at X.Y() in source:{source.Id}.cs:line 3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AshenCodex", text, StringComparison.Ordinal);
        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        AssertNoUserName(Environment.UserName, text);
        Assert.Equal(before, temp.App.ListCharacters().Count);
    }

    [Fact]
    public void Copies_a_real_nested_exception_as_types_and_frames_on_this_machine()
    {
        // The real Exception.ToString() shape, with this machine's profile and user name; the output is the manual check.
        using var temp = new TempApp();
        temp.App.CreateHomebrewSource(new HomebrewSourceRequest("Fixture Ashen Codex", [RulesFamilies.Srd51], ConfirmOwnWork: true));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Exception thrown;
        try
        {
            ThrowNested($@"{profile}\Documents\Fixture Ashen Codex.pdf is locked by {Environment.UserName}");
            throw new System.Diagnostics.UnreachableException();
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }
        temp.App.ErrorLog.Record("ABCD1234", "source.attachPdf", thrown);

        var text = DiagnosticsCopy.For(temp.App);
        output.WriteLine(text);

        Assert.Contains("System.InvalidOperationException: (message left out)", text, StringComparison.Ordinal);
        Assert.Contains(" ---> System.IO.IOException: (message left out)", text, StringComparison.Ordinal);
        Assert.Contains($"   at {typeof(DiagnosticsCopyTests).FullName}.{nameof(ThrowNested)}(...) in DiagnosticsCopyTests.cs:line", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ashen Codex", text, StringComparison.Ordinal);
        Assert.DoesNotContain("is locked", text, StringComparison.Ordinal);
        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        AssertNoUserName(Environment.UserName, text);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowNested(string message)
    {
        try
        {
            throw new IOException(message);
        }
        catch (IOException inner)
        {
            throw new InvalidOperationException(message, inner);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static InvalidOperationException Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }
}
