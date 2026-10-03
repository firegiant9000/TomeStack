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
/// (<c>pdf.too-many-pages</c>), the form (<c>ddb.no-form-fields</c>), the field count (<c>ddb.too-many-fields</c>), then
/// each name and value (<c>ddb.value-too-long</c>), all before anything is sent. PdfPig builds the form's fields with their
/// values before they can be counted, so that parse is bounded by the file size and the worker's memory and time caps,
/// not by the field limits. Messages never quote the document.
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
            // S0 finding (features/ddb-pdf-import.md): a real export had named widget fields on its pages but no /AcroForm in
            // its catalog, so the form is also read from the pages' widget annotations when the catalog has none.
            List<(string Name, Func<FormField> Read)> pending;
            if (document.TryGetForm(out var acroForm) && acroForm is not null && Terminals(acroForm.Fields, form) is { Count: > 0 } terminals)
                pending = [.. terminals.Select(t => (t.Name, (Func<FormField>)(() => ToField(document, t.Name, t.Field))))];
            else
                pending = Widgets(document, pageCount, form);
            if (pending.Count == 0)
                throw NoForm();

            var total = 0L;
            var fields = new List<FormField>(pending.Count);
            foreach (var (_, readField) in pending)
            {
                var read = readField();
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

    /// <summary>
    /// The form fields of a PDF whose catalog has no <c>/AcroForm</c>: every widget annotation on every page, named by its
    /// own <c>/T</c> and its parents' (period-joined), with the type, flags and value taken from the nearest dictionary in
    /// that chain that has them (PDF field inheritance). Widgets sharing a full name are one field (a checkbox is on if any
    /// of its widgets is). The pages are walked as raw dictionaries, so no page content is parsed. Every annotation entry
    /// counts toward a cap (four times <see cref="FormLimits.MaxFields"/>), named or not and repeated or not, and a widget
    /// object listed twice is read once; the walk stops at <see cref="FormLimits.MaxFields"/> fields and at a name over the
    /// per-value limit, before any value is read. A widget without a name is skipped: nothing could map it.
    /// </summary>
    private static List<(string Name, Func<FormField> Read)> Widgets(PdfDocument document, int pageCount, FormLimits form)
    {
        var byName = new Dictionary<string, (int Page, List<DictionaryToken> Widgets, List<DictionaryToken> Chain)>(StringComparer.Ordinal);
        var order = new List<string>();
        var seen = new HashSet<UglyToad.PdfPig.Core.IndirectReference>();
        var maxAnnotations = 4L * form.MaxFields;
        var annotations = 0L;
        var page = 0;
        foreach (var pageDictionary in PageDictionaries(document, pageCount))
        {
            page++;
            if (Resolve(document, pageDictionary.Data.GetValueOrDefault(NameToken.Annots.Data)) is not ArrayToken annots)
                continue;
            foreach (var entry in annots.Data)
            {
                if (++annotations > maxAnnotations)
                    throw new ExtractionException("ddb.too-many-fields", $"The PDF has more than {form.MaxFields} form fields.");
                if (entry is IndirectReferenceToken reference && !seen.Add(reference.Data))
                    continue;
                if (Resolve(document, entry) is not DictionaryToken annotation
                    || (Resolve(document, annotation.Data.GetValueOrDefault(NameToken.Subtype.Data)) as NameToken)?.Data != "Widget")
                    continue;
                var chain = Chain(document, annotation);
                var name = string.Join('.', chain.Select(d => Text(document, d.Data.GetValueOrDefault(NameToken.T.Data))).Where(t => !string.IsNullOrEmpty(t)).Reverse());
                if (name.Length == 0)
                    continue;
                if (name.Length > form.MaxValueChars)
                    throw TooLong(form);
                if (byName.TryGetValue(name, out var known))
                {
                    known.Widgets.Add(annotation);
                    continue;
                }
                if (order.Count == form.MaxFields)
                    throw new ExtractionException("ddb.too-many-fields", $"The PDF has more than {form.MaxFields} form fields.");
                byName[name] = (page, [annotation], chain);
                order.Add(name);
            }
        }
        return [.. order.Select(name => (name, (Func<FormField>)(() => WidgetField(document, name, byName[name]))))];
    }

    /// <summary>
    /// The page dictionaries in page order, walked from the catalog's <c>/Pages</c> without recursion and without building
    /// a page (no content stream, font or image is parsed). At most <paramref name="pageCount"/> pages (already checked
    /// against the limit), each node once, so a looped page tree ends.
    /// </summary>
    private static IEnumerable<DictionaryToken> PageDictionaries(PdfDocument document, int pageCount)
    {
        var pending = new Stack<IToken>();
        if (document.Structure.Catalog.CatalogDictionary.Data.GetValueOrDefault(NameToken.Pages.Data) is { } root)
            pending.Push(root);
        var visited = new HashSet<DictionaryToken>(ReferenceEqualityComparer.Instance);
        var pages = 0;
        while (pending.Count > 0 && pages < pageCount)
        {
            if (Resolve(document, pending.Pop()) is not DictionaryToken node || !visited.Add(node))
                continue;
            if (Resolve(document, node.Data.GetValueOrDefault(NameToken.Kids.Data)) is ArrayToken kids)
            {
                for (var i = kids.Data.Count - 1; i >= 0; i--)
                    pending.Push(kids.Data[i]);
                continue;
            }
            pages++;
            yield return node;
        }
    }

    /// <summary>The widget and its parents, nearest first; at most 32 deep, and a loop stops the walk.</summary>
    private static List<DictionaryToken> Chain(PdfDocument document, DictionaryToken widget)
    {
        var chain = new List<DictionaryToken> { widget };
        var seen = new HashSet<DictionaryToken>(ReferenceEqualityComparer.Instance) { widget };
        while (chain.Count < 32 && Resolve(document, chain[^1].Data.GetValueOrDefault(NameToken.Parent.Data)) is DictionaryToken parent && seen.Add(parent))
            chain.Add(parent);
        return chain;
    }

    private const int RadioFlag = 1 << 15;
    private const int PushButtonFlag = 1 << 16;
    private const int ComboFlag = 1 << 17;

    private static FormField WidgetField(PdfDocument document, string name, (int Page, List<DictionaryToken> Widgets, List<DictionaryToken> Chain) field)
    {
        IToken? Inherited(string key) => field.Chain.Select(d => d.Data.GetValueOrDefault(key)).FirstOrDefault(t => t is not null) is { } token ? Resolve(document, token) : null;
        var type = (Inherited(NameToken.Ft.Data) as NameToken)?.Data;
        var flags = Inherited(NameToken.Ff.Data) is NumericToken number ? number.Int : 0;
        var value = Inherited(NameToken.V.Data);
        switch (type)
        {
            case "Tx":
                return new(name, "text", field.Page, Value: Text(document, value));
            case "Btn" when (flags & PushButtonFlag) != 0:
                return new(name, "other", field.Page);
            case "Btn":
                var states = field.Widgets.Select(w => (Resolve(document, w.Data.GetValueOrDefault(NameToken.As.Data)) as NameToken)?.Data).ToList();
                var on = states.Any(s => s is not null)
                    ? states.Any(s => s is not null && s != "Off")
                    : value is NameToken current && current.Data != "Off";
                // The on-state of the widget that is on; otherwise the first widget's.
                var onState = states.FirstOrDefault(s => s is not null && s != "Off")
                    ?? field.Widgets.Select(w => OnStateOf(document, w)).FirstOrDefault(s => s is not null)
                    ?? (value is NameToken v && v.Data != "Off" ? v.Data : null);
                return new(name, (flags & RadioFlag) != 0 ? "radio" : "checkbox", field.Page, Checked: on, OnState: onState);
            case "Ch":
                IReadOnlyList<string> selected = value is ArrayToken array
                    ? [.. array.Data.Select(t => Text(document, t)).OfType<string>()]
                    : Text(document, value) is { } one ? [one] : [];
                return new(name, (flags & ComboFlag) != 0 ? "combo" : "list", field.Page, Selected: selected);
            default:
                return new(name, "other", field.Page);
        }
    }

    /// <summary>A widget's normal appearance that is not <c>Off</c>.</summary>
    private static string? OnStateOf(PdfDocument document, DictionaryToken widget) =>
        Resolve(document, widget.Data.GetValueOrDefault(NameToken.Ap.Data)) is DictionaryToken appearance
        && Resolve(document, appearance.Data.GetValueOrDefault(NameToken.N.Data)) is DictionaryToken normal
            ? normal.Data.Keys.FirstOrDefault(k => k != "Off")
            : null;

    /// <summary>A string, hex string or name as text; anything else is no text.</summary>
    private static string? Text(PdfDocument document, IToken? token) => Resolve(document, token) switch
    {
        StringToken s => s.Data,
        HexToken h => h.Data,
        NameToken n => n.Data,
        _ => null,
    };

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
