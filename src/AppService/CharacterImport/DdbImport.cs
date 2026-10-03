using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Forms;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// Character import from a D&amp;D Beyond PDF sheet (<c>features/ddb-pdf-import.md</c>), reading side: the form fields are
/// read once in the import worker (ADR-009), parsed with the layout map, and held in memory under a token. Nothing is
/// written to the database; <c>ddb.readData</c>'s temporary copy is deleted before the command returns. Error messages
/// carry codes and counts, never a field value.
/// </summary>
public sealed partial class TomeStackApp
{
    /// <summary>The sheet size limit of the reader (20 MB).</summary>
    public static long MaxDdbSheetBytes => WorkerFormReader.DefaultLimits.MaxBytes;

    private const string DdbTempPrefix = "ddb-";

    private IFormReader? _formReader;

    private IFormReader FormReader => _formReader ??= new WorkerFormReader(Path.Combine(AppContext.BaseDirectory, WorkerFileName));

    private ImportSessions? _ddbSessions;

    internal ImportSessions DdbSessions => _ddbSessions ??= new ImportSessions(_time);

    private string DdbTempFolder => Path.Combine(DataDirectory, "tmp");

    /// <summary><c>ddb.read</c>: reads the sheet at <paramref name="path"/> (a dialog's choice or the temporary copy, never a UI path).</summary>
    public DdbReadResult ReadDdbSheet(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        IReadOnlyList<FormField> fields;
        try
        {
            fields = FormReader.ReadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (ExtractionException ex) when (ex.Code.StartsWith("worker.", StringComparison.Ordinal))
        {
            throw Refused("ddb.read-failed", $"The sheet could not be read ({ex.Code}). Nothing was kept.");
        }
        catch (ExtractionException ex)
        {
            throw Refused(ex.Code, ex.Message); // the reader's messages never quote the document
        }

        var map = DdbParser.Recognise(fields)
            ?? throw Refused("ddb.layout-unknown", $"This PDF's {fields.Count} form field{(fields.Count == 1 ? "" : "s")} do not match a known D&D Beyond sheet layout.");
        var sheet = DdbParser.Parse(map, fields);
        var token = DdbSessions.Add(sheet);
        var classes = sheet.Classes.Status == ReadStatus.Ok
            ? string.Join(" / ", sheet.Classes.Value!.Select(c => $"{c.Name} {c.Level}{(c.Subclass is null ? "" : $" ({c.Subclass})")}"))
            : "";
        return new DdbReadResult(token, map.Id, map.SuggestedFamily,
            new DdbSummary(sheet.Name.Value ?? "", classes, sheet.Features.Count, sheet.Spells.Count, sheet.Items.Count));
    }

    /// <summary>
    /// <c>ddb.readData</c> (DevHost, browser, e2e): the bytes go to <c>tmp/ddb-&lt;guid&gt;.pdf</c> under the data folder, are
    /// read, and the file is deleted whatever happens. The size is checked before anything is written.
    /// </summary>
    public DdbReadResult ReadDdbSheetData(string fileName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.LongLength > MaxDdbSheetBytes)
            throw Refused("pdf.too-large", $"The PDF is larger than {MaxDdbSheetBytes / (1024 * 1024)} MB.");
        Directory.CreateDirectory(DdbTempFolder);
        var path = Path.Combine(DdbTempFolder, $"{DdbTempPrefix}{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, data);
            return ReadDdbSheet(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary><c>ddb.discard</c>: drops the token. False when it was unknown, used or expired.</summary>
    public bool DiscardDdbSheet(Guid token) => DdbSessions.Discard(token);

    /// <summary>At startup: a crash during <c>ddb.readData</c> can leave its temporary copy.</summary>
    private void DeleteLeftoverDdbFiles()
    {
        if (!Directory.Exists(DdbTempFolder))
            return;
        foreach (var file in Directory.EnumerateFiles(DdbTempFolder, $"{DdbTempPrefix}*.pdf"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held by another program for a moment: the next start tries again.
            }
        }
    }

    private static AppValidationException Refused(string code, string message) => new([new Diagnostic(code, message)], code);
}

/// <param name="Name">The character's name as read (shown to the user only, never logged).</param>
/// <param name="ClassText">The classes as read, "Name level (subclass)" joined with " / ", or empty when unreadable.</param>
public sealed record DdbSummary(string Name, string ClassText, int Features, int Spells, int Items);

public sealed record DdbReadResult(Guid Token, string Layout, string? SuggestedFamily, DdbSummary Summary);
