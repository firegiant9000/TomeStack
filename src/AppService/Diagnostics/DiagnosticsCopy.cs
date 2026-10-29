using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Diagnostics;

/// <summary>What the copied text says about this machine; <see cref="Current"/> outside tests.</summary>
/// <param name="UserProfile">Its last folder name is redacted like <paramref name="UserName"/>.</param>
/// <param name="UserName">Replaced by <c>&lt;user&gt;</c> where it stands as a word in a copied name.</param>
public sealed record DiagnosticsEnvironment(string AppVersion, string OsVersion, string UserProfile, string UserName)
{
    public static DiagnosticsEnvironment Current => new(
        AppVersionStamp.CurrentVersion, RuntimeInformation.OSDescription,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);
}

/// <summary>
/// Lane S4 "Copy diagnostics" (base plan §5.4, §9): text the user pastes into a bug report. It holds the app and Windows
/// versions, the data folder's name and the newest <see cref="MaxEntries"/> error-log entries. Of each entry only the time,
/// the correlation id, the command, the exception type names (with a COM or Win32 code), the stack frames as method names (parameter
/// lists become <c>(...)</c>) with their source <b>file name</b> only, and the shell's fixed WebView2 line are copied. An exception message can quote anything (a title,
/// a file name), so messages are left out, and the log never holds payloads (gap-note or homebrew text) in the first
/// place; no folder path is copied at all. In the names that are copied (the data folder, source files, the command), the
/// Windows user name becomes <c>&lt;user&gt;</c> and every source title <c>source:&lt;id&gt;</c>. All of it runs on the raw
/// text, never on an encoded copy (gotcha 2026-09-29). Reads only.
/// </summary>
public static partial class DiagnosticsCopy
{
    public const int MaxEntries = 20;

    /// <summary>The text for this app's own log, data folder and sources.</summary>
    public static string For(TomeStackApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var entries = app.ErrorLog is FileErrorLog file ? file.ReadRecent(MaxEntries) : [];
        return Build(entries, app.DataDirectory, app.ListSources(), DiagnosticsEnvironment.Current);
    }

    /// <param name="entries">Oldest first; null when the log exists but could not be read.</param>
    public static string Build(IReadOnlyList<ErrorLogEntry>? entries, string dataFolder, IReadOnlyList<SourceRecord> sources, DiagnosticsEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(dataFolder);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(environment);

        var redact = Redactor(sources, environment);
        var kept = entries?.TakeLast(MaxEntries).ToList() ?? [];
        var text = new StringBuilder();
        text.AppendLine("TomeStack diagnostics");
        text.AppendLine(CultureInfo.InvariantCulture, $"App version: {environment.AppVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Windows: {environment.OsVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Data folder: {redact(Path.GetFileName(dataFolder.TrimEnd('\\', '/')))}");
        text.AppendLine("Exception messages and folder paths are left out; the user name and source titles are replaced.");
        text.AppendLine();
        if (entries is null)
            text.AppendLine("The error log could not be read.");
        else if (kept.Count == 0)
            text.AppendLine("No errors have been logged.");
        else
            text.AppendLine(CultureInfo.InvariantCulture, $"Newest {kept.Count} error-log {(kept.Count == 1 ? "entry" : "entries, oldest first")}:");
        foreach (var entry in kept)
        {
            text.AppendLine();
            var command = CommandName().IsMatch(entry.Command) ? redact(entry.Command) : "(other)";
            text.AppendLine(CultureInfo.InvariantCulture, $"[{Field(entry.At, TimeStamp())}] {Field(entry.CorrelationId, CorrelationId())} command={command}");
            foreach (var line in Detail(entry.Detail, redact))
                text.AppendLine(line);
        }
        return text.ToString();
    }

    /// <summary>A header field that is not in the shape the writer uses is not copied.</summary>
    private static string Field(string value, Regex shape) => shape.IsMatch(value) ? value : "(other)";

    /// <summary>The lines of an entry that can only hold code names: exception types, stack frames and fixed shell wording.</summary>
    private static IEnumerable<string> Detail(string detail, Func<string, string> redact)
    {
        var first = true;
        foreach (var raw in detail.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            // AggregateException.ToString() closes each inner after the first with "<---".
            var line = raw.EndsWith("<---", StringComparison.Ordinal) ? raw[..^4].TrimEnd() : raw;
            // The parameter list is not copied: two words in parentheses could be message text shaped like a frame.
            if (StackFrame().Match(line) is { Success: true } frame)
                yield return frame.Groups["lead"].Value + frame.Groups["method"].Value + (frame.Groups["parameters"].Length > 0 ? "(...)" : "()")
                    + (frame.Groups["path"].Success ? $" in {FileName(frame.Groups["path"].Value, redact)}:line {frame.Groups["line"].Value}" : "");
            else if (StackSeparator().IsMatch(line) || ProcessFailure().IsMatch(line))
                yield return line;
            else if ((first || InnerMarker().IsMatch(line)) && ExceptionType().Match(line) is { Success: true } type)
                yield return type.Groups["lead"].Value + type.Groups["type"].Value + type.Groups["code"].Value
                    + (type.Groups["rest"].Success ? ": (message left out)" : "");
            first = false;
        }
    }

    /// <summary>A frame's source file, without its folder; anything that is not a plain file name is not copied.</summary>
    private static string FileName(string path, Func<string, string> redact)
    {
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        return PlainFileName().IsMatch(name) ? redact(name) : "(file)";
    }

    /// <summary>
    /// Replaces the user name and every source title where no letter touches it (so <c>jdoe2</c> and <c>jdoe_old</c> are
    /// caught); a timeout hides the whole name. It only ever sees short names, never a whole frame or the header.
    /// </summary>
    private static Func<string, string> Redactor(IReadOnlyList<SourceRecord> sources, DiagnosticsEnvironment environment)
    {
        var rules = new List<(Regex Pattern, string Token)>();
        var names = new[] { environment.UserName, Path.GetFileName(environment.UserProfile.TrimEnd('\\', '/')) };
        foreach (var name in names.Where(n => n.Trim().Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            rules.Add((Word(name.Trim()), "<user>"));
        foreach (var source in sources.Where(s => s.Title.Trim().Length > 0).OrderByDescending(s => s.Title.Trim().Length))
            rules.Add((Word(source.Title.Trim()), $"source:{source.Id}"));
        return value =>
        {
            try
            {
                return rules.Aggregate(value, (text, rule) => rule.Pattern.Replace(text, rule.Token));
            }
            catch (RegexMatchTimeoutException)
            {
                return "(redacted)";
            }
        };
    }

    private static Regex Word(string value) =>
        new($@"(?<!\p{{L}}){Regex.Escape(value)}(?!\p{{L}})",RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>A .NET frame: a method name and its parameter list, and optionally its source file and line.</summary>
    [GeneratedRegex(@"^(?<lead>\s+at )(?<method>[\w.`<>\[\],+|$@\-]+)\((?<parameters>[\w.`<>\[\], &*]*)\)(?: in (?<path>.+):line (?<line>\d+))?$")]
    private static partial Regex StackFrame();

    [GeneratedRegex(@"^[\w.\-]{1,120}$")]
    private static partial Regex PlainFileName();

    [GeneratedRegex(@"^\s*--- End of [A-Za-z ]+ ---$")]
    private static partial Regex StackSeparator();

    /// <summary>The shell's own line for a WebView2 process failure (<c>CrashReporter.ReportProcessFailure</c>): fixed words and enum names.</summary>
    [GeneratedRegex(@"^WebView2 process failed: kind=\w+ reason=\w+ exitCode=-?\d+$")]
    private static partial Regex ProcessFailure();

    [GeneratedRegex(@"^\s*--->\s")]
    private static partial Regex InnerMarker();

    /// <summary>
    /// A dotted type name ending in <c>Exception</c>, optionally after <c>---&gt;</c> and an aggregate's index, and with the
    /// code that <c>ExternalException</c> (COM, SQLite) and <c>Win32Exception</c> print after it.
    /// </summary>
    [GeneratedRegex(@"^(?<lead>\s*(?:--->\s+)?(?:\(Inner Exception #\d+\)\s+)?)(?<type>[A-Za-z_][\w`]*(?:\.[A-Za-z_][\w`+]*)*\.[A-Za-z_][\w`+]*Exception)(?<code> \((?:0x[0-9A-Fa-f]+|-?\d+)\))?(?<rest>:.*)?$")]
    private static partial Regex ExceptionType();

    [GeneratedRegex(@"^[0-9T:.+\-Z]{10,40}$")]
    private static partial Regex TimeStamp();

    /// <remarks>Lower-case ids are accepted: earlier builds wrote import failures that way.</remarks>
    [GeneratedRegex(@"^[0-9A-Fa-f]{8}$")]
    private static partial Regex CorrelationId();

    [GeneratedRegex(@"^[A-Za-z0-9_.()\-]{1,64}$")]
    private static partial Regex CommandName();
}
