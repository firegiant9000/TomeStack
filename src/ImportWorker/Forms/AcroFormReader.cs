using TomeStack.ImportWorker.Extraction;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Tokens;

namespace TomeStack.ImportWorker.Forms;

/// <summary>
/// Reads the AcroForm fields of a PDF with PdfPig, which has no scripting engine, so actions and JavaScript in the file
/// never run (ADR-009). This class parses untrusted bytes, so it runs only inside the worker process
/// (<see cref="WorkerMain"/>), never in the app. The checks run in this order: the file (<c>pdf.missing</c>,
/// <c>pdf.too-large</c>, <c>pdf.not-a-pdf</c>), opening it (<c>pdf.encrypted</c>, <c>pdf.unreadable</c>), the page count
/// (<c>pdf.too-many-pages</c>), the form (<c>ddb.no-form-fields</c>), the field count (<c>ddb.too-many-fields</c>, before
/// any value is read), then each name and value (<c>ddb.value-too-long</c>). Messages never quote the document.
/// </summary>
public static class AcroFormReader
{
    public static IReadOnlyList<FormField> Read(string path, ExtractionLimits limits, FormLimits form)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(form);
        PdfPigExtractor.CheckFile(path, limits);
        using var document = PdfPigExtractor.Open(path);
        try
        {
            var pageCount = document.NumberOfPages;
            if (pageCount > limits.MaxPages)
                throw new ExtractionException("pdf.too-many-pages", $"The PDF has {pageCount} pages; at most {limits.MaxPages} can be read.");
            if (!document.TryGetForm(out var acroForm) || acroForm is null)
                throw NoForm();
            var terminals = Terminals(acroForm.Fields, form);
            if (terminals.Count == 0)
                throw NoForm();

            var total = 0L;
            var fields = new List<FormField>(terminals.Count);
            foreach (var (name, field) in terminals)
            {
                var read = ToField(document, name, field);
                total += form.Chars(read) ?? throw TooLong(form);
                if (total > form.MaxTotalValueChars)
                    throw new ExtractionException("ddb.value-too-long", $"The form fields hold more than {form.MaxTotalValueChars} characters in total.");
                fields.Add(read);
            }
            return fields;
        }
        catch (Exception ex) when (ex is not ExtractionException and not OutOfMemoryException and not OperationCanceledException)
        {
            // Parser messages can quote document bytes, so none is kept.
            throw new ExtractionException("pdf.unreadable", "The PDF could not be read. It may be damaged.");
        }
    }

    private static ExtractionException NoForm() =>
        new("ddb.no-form-fields", "This PDF has no form fields. Export the sheet again from D&D Beyond as a PDF, not printed to PDF.");

    /// <summary>
    /// The terminal fields in document order with their full names, walked without recursion (a hostile file can nest
    /// deeply). Stops at <see cref="FormLimits.MaxFields"/> and at a name longer than <see cref="FormLimits.MaxValueChars"/>.
    /// </summary>
    private static List<(string Name, AcroFieldBase Field)> Terminals(IEnumerable<AcroFieldBase> roots, FormLimits form)
    {
        var terminals = new List<(string, AcroFieldBase)>();
        var pending = new Stack<(string Prefix, AcroFieldBase Field)>();
        foreach (var root in roots.Reverse())
            pending.Push(("", root));
        while (pending.Count > 0)
        {
            var (prefix, field) = pending.Pop();
            var partial = field.Information?.PartialName;
            var name = string.IsNullOrEmpty(partial) ? prefix : prefix.Length == 0 ? partial : $"{prefix}.{partial}";
            if (name.Length > form.MaxValueChars)
                throw TooLong(form);
            if (field is AcroNonTerminalField parent)
            {
                foreach (var child in parent.Children.Reverse())
                    pending.Push((name, child));
                continue;
            }
            if (terminals.Count == form.MaxFields)
                throw new ExtractionException("ddb.too-many-fields", $"The PDF has more than {form.MaxFields} form fields.");
            terminals.Add((name, field));
        }
        return terminals;
    }

    private static FormField ToField(PdfDocument document, string name, AcroFieldBase field) => field switch
    {
        AcroTextField text => new(name, "text", field.PageNumber, Value: text.Value),
        AcroCheckboxField box => new(name, "checkbox", field.PageNumber, Checked: box.IsChecked, OnState: OnState(document, field, box.IsChecked ? box.CurrentValue : null)),
        AcroRadioButtonField radio => new(name, "radio", field.PageNumber, Checked: radio.IsSelected, OnState: OnState(document, field, radio.IsSelected ? radio.CurrentValue : null)),
        AcroComboBoxField combo => new(name, "combo", field.PageNumber, Selected: [.. combo.SelectedOptions ?? []]),
        AcroListBoxField list => new(name, "list", field.PageNumber, Selected: [.. list.SelectedOptions ?? []]),
        _ => new(name, "other", field.PageNumber),
    };

    /// <summary>The name of the "on" appearance: the current value when on, else the normal appearance that is not <c>Off</c>.</summary>
    private static string? OnState(PdfDocument document, AcroFieldBase field, NameToken? current)
    {
        if (current is { Data: { Length: > 0 } on } && on != "Off")
            return on;
        if (Resolve(document, field.Dictionary.Data.GetValueOrDefault(NameToken.Ap.Data)) is not DictionaryToken appearance
            || Resolve(document, appearance.Data.GetValueOrDefault(NameToken.N.Data)) is not DictionaryToken normal)
            return null;
        return normal.Data.Keys.FirstOrDefault(k => k != "Off");
    }

    private static IToken? Resolve(PdfDocument document, IToken? token) =>
        token is IndirectReferenceToken reference ? document.Structure.GetObject(reference.Data)?.Data : token;

    private static ExtractionException TooLong(FormLimits form) =>
        new("ddb.value-too-long", $"A form field's name or value is longer than {form.MaxValueChars} characters.");
}
