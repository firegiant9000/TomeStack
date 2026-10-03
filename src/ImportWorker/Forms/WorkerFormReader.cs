using TomeStack.ImportWorker.Extraction;

namespace TomeStack.ImportWorker.Forms;

/// <summary>
/// The app's side of a form read (<c>features/ddb-pdf-import.md</c>, ADR-009 (c)): one <c>formFields</c> request to a fresh
/// <c>TomeStack.ImportWorker.Host.exe</c> child over stdin and stdout, under the sheet limits (<see cref="DefaultLimits"/>,
/// <see cref="FormLimits.Default"/>) and the shared <see cref="WorkerSession"/> checks. It runs outside the import-job
/// queue. It accepts exactly <c>worker</c>, one <c>fields</c>, then <c>done</c>, or an <c>error</c>; anything else, and a
/// field list over the form limits, is <c>worker.protocol</c>. Messages never quote a field.
/// </summary>
public sealed class WorkerFormReader(string workerPath, ExtractionLimits? limits = null, FormLimits? form = null) : IFormReader
{
    private readonly ExtractionLimits _limits = limits ?? DefaultLimits;
    private readonly FormLimits _form = form ?? FormLimits.Default;

    /// <summary>
    /// A character sheet is a few pages: 20 MB, 50 pages, and 30 s for the line and the whole read. Memory is sheet-sized
    /// too (a 256 MiB managed heap, 512 MiB in all), not the book limits: PdfPig builds the form's fields with their values
    /// before the field limits can count them, so these caps are what bound that parse.
    /// </summary>
    public static ExtractionLimits DefaultLimits { get; } = new()
    {
        MaxBytes = 20L << 20,
        MaxPages = 50,
        PageTimeout = TimeSpan.FromSeconds(30),
        RunTimeout = TimeSpan.FromSeconds(30),
        HeapHardLimit = 256L << 20,
        MaxWorkingSet = 512L << 20,
    };

    /// <summary>The field types the worker reports (<see cref="FormField.Type"/>).</summary>
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "text", "checkbox", "radio", "combo", "list", "other" };

    public async Task<IReadOnlyList<FormField>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        await using var session = WorkerSession.Start(workerPath, _limits, FormLimits.MaxFieldsMessageChars(_form), cancellationToken);
        await session.SendAsync(new WorkerRequest(Path.GetFullPath(path), null, null, _limits, Environment.ProcessId, WorkerRequest.FormFieldsKind, _form)).ConfigureAwait(false);
        await session.ReadHelloAsync(cancellationToken, KnownError).ConfigureAwait(false);

        IReadOnlyList<FormField>? fields = null;
        while (true)
        {
            var message = await session.NextAsync(_limits.PageTimeout, cancellationToken).ConfigureAwait(false);
            switch (message.Type)
            {
                case "fields" when fields is null && message.Fields is { } read && WithinLimits(read):
                    fields = read;
                    break;
                case "error" when message.Code is { } code && KnownError(code) is { } text:
                    throw new ExtractionException(code, text);
                case "done" when fields is not null:
                    return fields;
                default:
                    throw session.Fail("worker.protocol", "The import worker sent an unexpected message.");
            }
        }
    }

    /// <summary>
    /// The child is trusted with nothing: an error keeps its code only when it is one the reader can raise, with the app's
    /// own message for it (built from the limits, never from the child's text). Any other code is <c>worker.protocol</c>.
    /// </summary>
    private string? KnownError(string code) => code switch
    {
        "worker.bad-request" => "The import worker could not read the request. Reinstall TomeStack if this keeps happening.",
        "pdf.missing" => "The PDF file is missing.",
        "pdf.too-large" => $"The PDF is larger than {_limits.MaxBytes / (1024 * 1024)} MB.",
        "pdf.not-a-pdf" => "The file is not a PDF.",
        "pdf.encrypted" => "The PDF is encrypted. Remove the password in another program, then import it again.",
        "pdf.unreadable" => "The PDF could not be read. It may be damaged, or open in another program.",
        "pdf.too-many-pages" => $"The PDF has more than {_limits.MaxPages} pages, the most that can be read.",
        "ddb.no-form-fields" => "This PDF has no form fields. Export the sheet again from D&D Beyond as a PDF, not printed to PDF.",
        "ddb.too-many-fields" => $"The PDF has more than {_form.MaxFields} form fields.",
        "ddb.value-too-long" => $"The form fields hold more text than can be read (at most {_form.MaxValueChars} characters in one, {_form.MaxTotalValueChars} in all).",
        _ => null,
    };

    /// <summary>The child is trusted with nothing: the list must hold to the limits it was given, with a known type and a page in range.</summary>
    private bool WithinLimits(IReadOnlyList<FormField> fields)
    {
        if (fields.Count > _form.MaxFields)
            return false;
        var total = 0L;
        foreach (var field in fields)
        {
            // JSON can send null where the record says it cannot.
            if (field?.Name is null || field.Type is null || !Types.Contains(field.Type) || field.Page is < 1 || field.Page > _limits.MaxPages
                || field.Selected?.Any(s => s is null) == true || _form.Chars(field) is not { } chars)
                return false;
            total += chars;
        }
        return total <= _form.MaxTotalValueChars;
    }
}
