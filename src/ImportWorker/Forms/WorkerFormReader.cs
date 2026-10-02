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

    /// <summary>A character sheet is a few pages: 20 MB, 50 pages, and 30 s for the line and the whole read.</summary>
    public static ExtractionLimits DefaultLimits { get; } = new()
    {
        MaxBytes = 20L << 20,
        MaxPages = 50,
        PageTimeout = TimeSpan.FromSeconds(30),
        RunTimeout = TimeSpan.FromSeconds(30),
    };

    public async Task<IReadOnlyList<FormField>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        await using var session = WorkerSession.Start(workerPath, _limits, FormLimits.MaxFieldsMessageChars(_form), cancellationToken);
        await session.SendAsync(new WorkerRequest(Path.GetFullPath(path), null, null, _limits, Environment.ProcessId, WorkerRequest.FormFieldsKind, _form)).ConfigureAwait(false);
        await session.ReadHelloAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<FormField>? fields = null;
        while (true)
        {
            var message = await session.NextAsync(_limits.PageTimeout, cancellationToken).ConfigureAwait(false);
            switch (message.Type)
            {
                case "fields" when fields is null && message.Fields is { } read && WithinLimits(read):
                    fields = read;
                    break;
                case "error":
                    throw new ExtractionException(message.Code ?? "worker.failed", message.Message ?? "The import worker failed.");
                case "done" when fields is not null:
                    return fields;
                default:
                    throw session.Fail("worker.protocol", "The import worker sent an unexpected message.");
            }
        }
    }

    /// <summary>The child is trusted with nothing: the list must hold to the limits it was given.</summary>
    private bool WithinLimits(IReadOnlyList<FormField> fields)
    {
        if (fields.Count > _form.MaxFields)
            return false;
        var total = 0L;
        foreach (var field in fields)
        {
            // JSON can send null where the record says it cannot.
            if (field?.Name is null || field.Type is null || field.Selected?.Any(s => s is null) == true || _form.Chars(field) is not { } chars)
                return false;
            total += chars;
        }
        return total <= _form.MaxTotalValueChars;
    }
}
