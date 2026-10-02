namespace TomeStack.ImportWorker.Forms;

/// <summary>
/// One terminal form field of a PDF (an AcroForm field that holds data), as the worker reads it for the character-sheet
/// importer (<c>features/ddb-pdf-import.md</c>). <paramref name="Name"/> is the full name: the partial names of the field's
/// parents and its own, joined with periods. <paramref name="Type"/> is <c>text</c>, <c>checkbox</c>, <c>radio</c>,
/// <c>combo</c>, <c>list</c> or <c>other</c>. A field with several widgets and no name of their own is listed once per widget.
/// </summary>
/// <param name="Page">1-based, when the PDF says which page the field is on.</param>
/// <param name="Value">A text field's value; null when unset.</param>
/// <param name="Checked">A checkbox's or radio button's state.</param>
/// <param name="Selected">A combo or list box's selected options.</param>
/// <param name="OnState">The appearance name a checkbox or radio button has when it is on (a layout's on-state, never a value the user typed).</param>
public sealed record FormField(string Name, string Type, int? Page, string? Value = null, bool? Checked = null, IReadOnlyList<string>? Selected = null, string? OnState = null);

/// <summary>
/// The bounds on one form read, enforced in the worker before a value is held. Names count toward
/// <see cref="MaxTotalValueChars"/> as well as values, so the line the app reads stays bounded (<see cref="MaxFieldsMessageChars"/>).
/// </summary>
public sealed record FormLimits
{
    /// <summary>Terminal fields per document (<c>ddb.too-many-fields</c>).</summary>
    public int MaxFields { get; init; } = 2_000;

    /// <summary>Characters in one field's name or value (<c>ddb.value-too-long</c>).</summary>
    public int MaxValueChars { get; init; } = 20_000;

    /// <summary>Characters of names and values in the whole document (<c>ddb.value-too-long</c>).</summary>
    public int MaxTotalValueChars { get; init; } = 1_048_576;

    public static FormLimits Default { get; } = new();

    /// <summary>
    /// The longest <c>fields</c> line the app reads from the worker under these limits: each character escaped as at most six,
    /// plus 256 for each field's keys and numbers, plus 64 KiB. Anything longer fails with <c>worker.message-too-large</c>
    /// before it is held in the app's memory.
    /// </summary>
    public static int MaxFieldsMessageChars(FormLimits form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return (int)Math.Min(int.MaxValue, (6L * form.MaxTotalValueChars) + (256L * form.MaxFields) + (64 * 1024));
    }
}

/// <summary>Reads the form fields of a local PDF. Implementations must not execute embedded scripts (ADR-009).</summary>
public interface IFormReader
{
    /// <summary>Every terminal field, or an <see cref="ExtractionException"/> with a code (never a field value).</summary>
    Task<IReadOnlyList<FormField>> ReadAsync(string path, CancellationToken cancellationToken);
}
