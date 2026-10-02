using System.Globalization;
using System.Text;
using System.Text.Json;
using TomeStack.ImportWorker.Forms;

namespace TomeStack.AppService.Diagnostics;

/// <summary>
/// Character-sheet import S0, the spike (<c>features/ddb-pdf-import.md</c>): the form fields of an exported sheet, so the
/// layout maps can be written without committing a value. Each entry keeps a field's full name, type, page, the length
/// of its value, and a checkbox's state and on-state; the value itself is dropped here, in the app, as soon as the worker
/// returns it. The PDF is read through <see cref="WorkerFormReader"/> in the import worker, because the app never parses
/// a PDF itself (ADR-009). Only the dev-only DevHost runs it (<c>--ddb-fields</c>); its output belongs in the gitignored
/// <c>tests/RulesFixtures/local/ddb-import/</c>.
/// </summary>
public static class FormInventory
{
    public sealed record Report(int FieldCount, IReadOnlyList<Entry> Fields);

    /// <param name="ValueLength">Characters in a text value, or in a choice field's selected options; 0 when unset.</param>
    public sealed record Entry(string Name, string Type, int? Page, int ValueLength, bool? Checked, string? OnState);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The inventory, or an <see cref="ImportWorker.ExtractionException"/> with the reader's code.</summary>
    public static Report Read(string workerPath, string pdfPath)
    {
        ArgumentNullException.ThrowIfNull(workerPath);
        ArgumentNullException.ThrowIfNull(pdfPath);
        var fields = new WorkerFormReader(workerPath).ReadAsync(pdfPath, CancellationToken.None).GetAwaiter().GetResult();
        return new Report(fields.Count,
        [
            .. fields.Select(f => new Entry(f.Name, f.Type, f.Page, f.Value?.Length ?? f.Selected?.Sum(s => s.Length) ?? 0, f.Checked, f.OnState)),
        ]);
    }

    /// <summary>The report as indented JSON: names and lengths, no value.</summary>
    public static string ToJson(Report report) => JsonSerializer.Serialize(report, Json);

    /// <summary>A console summary: counts by type and page only, never a name.</summary>
    public static string Format(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{report.FieldCount} fields");
        foreach (var type in report.Fields.GroupBy(f => f.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
            text.AppendLine(CultureInfo.InvariantCulture, $"  {type.Key} {type.Count()}");
        foreach (var page in report.Fields.GroupBy(f => f.Page).OrderBy(g => g.Key ?? int.MaxValue))
            text.AppendLine(CultureInfo.InvariantCulture, $"  page {page.Key?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}: {page.Count()}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  names with a period: {report.Fields.Count(f => f.Name.Contains('.', StringComparison.Ordinal))}");
        return text.ToString();
    }
}
