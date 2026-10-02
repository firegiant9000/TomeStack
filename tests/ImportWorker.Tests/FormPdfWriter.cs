using System.Globalization;
using System.Text;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// Writes a minimal fillable PDF by hand (PdfPig's builder has no form API): a catalog with <c>/AcroForm</c> and
/// <c>/NeedAppearances true</c>, the pages, one widget annotation per field, and a computed cross-reference table
/// (<see cref="FixturePdfs.Raw"/>). The output is uncompressed and the same for the same input. Every value a test
/// passes is invented (SPEC Q-03).
/// </summary>
internal static class FormPdfWriter
{
    /// <summary>
    /// A text field when <paramref name="Checked"/> and <paramref name="Selected"/> are null, a checkbox when
    /// <paramref name="Checked"/> is set, a combo box when <paramref name="Selected"/> is set.
    /// </summary>
    public sealed record FormSpec(string Name, string? Text = null, bool? Checked = null, int Page = 1, IReadOnlyList<string>? Selected = null);

    /// <param name="nested">Names with periods become a parent chain (<c>/Kids</c> and <c>/Parent</c>), each part its own partial name.</param>
    /// <param name="withJavaScript">The catalog gets an <c>/OpenAction</c> and an <c>/AA</c> JavaScript action.</param>
    /// <param name="javaScript">The script of those actions.</param>
    public static byte[] Write(
        IReadOnlyList<FormSpec> fields,
        int pages = 2,
        bool needAppearances = true,
        string onState = "Yes",
        bool withJavaScript = false,
        bool nested = false,
        string javaScript = "app.alert('Fixture');")
    {
        ArgumentNullException.ThrowIfNull(fields);
        // Object numbers: 1 catalog, 2 pages, 3.. one per page, then fields (and parents), then the script.
        var objects = new List<string?> { null, null };
        int Reserve()
        {
            objects.Add(null);
            return objects.Count;
        }
        var pageRefs = Enumerable.Range(0, pages).Select(_ => Reserve()).ToList();
        var annots = pageRefs.ToDictionary(p => p, _ => new List<int>());
        var roots = new List<int>();
        var parents = new Dictionary<string, (int Ref, List<int> Kids)>(StringComparer.Ordinal);

        var x = 50;
        foreach (var field in fields)
        {
            var parts = nested ? field.Name.Split('.') : [field.Name];
            int? parent = null;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var key = string.Join('.', parts[..(i + 1)]);
                if (!parents.TryGetValue(key, out var node))
                {
                    node = (Reserve(), []);
                    parents[key] = node;
                    if (parent is not null)
                        parents[string.Join('.', parts[..i])].Kids.Add(node.Ref);
                    else
                        roots.Add(node.Ref);
                }
                parent = node.Ref;
            }

            var self = Reserve();
            if (parent is not null)
                parents[string.Join('.', parts[..^1])].Kids.Add(self);
            else
                roots.Add(self);
            var page = pageRefs[Math.Clamp(field.Page, 1, pages) - 1];
            annots[page].Add(self);
            var rect = $"/Rect [{x} 600 {x + 40} 620]";
            x = x >= 500 ? 50 : x + 50;
            var common = $"/Type /Annot /Subtype /Widget /T {Str(parts[^1])} /P {page} 0 R {rect} /F 4" + (parent is { } q ? $" /Parent {q} 0 R" : "");
            objects[self - 1] = field switch
            {
                { Checked: { } on } => $"<< {common} /FT /Btn /V /{(on ? onState : "Off")} /AS /{(on ? onState : "Off")} /AP << /N << /{onState} 0 0 R /Off 0 0 R >> >> >>",
                { Selected: { } selected } => $"<< {common} /FT /Ch /Ff 131072 /Opt [{string.Concat(selected.Select(Str))}] /V {(selected.Count == 1 ? Str(selected[0]) : $"[{string.Concat(selected.Select(Str))}]")} >>",
                _ => $"<< {common} /FT /Tx" + (field.Text is { } text ? $" /V {Str(text)}" : "") + " >>",
            };
        }
        foreach (var (name, node) in parents)
        {
            var up = name.Contains('.', StringComparison.Ordinal) ? $" /Parent {parents[name[..name.LastIndexOf('.')]].Ref} 0 R" : "";
            objects[node.Ref - 1] = $"<< /T {Str(name[(name.LastIndexOf('.') + 1)..])} /Kids [{Refs(node.Kids)}]{up} >>";
        }

        var script = "";
        if (withJavaScript)
        {
            var js = Reserve();
            objects[js - 1] = $"<< /Type /Action /S /JavaScript /JS {Str(javaScript)} >>";
            script = $" /OpenAction {js} 0 R /AA << /WC {js} 0 R /DS {js} 0 R >>";
        }
        objects[0] = $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [{Refs(roots)}] /NeedAppearances {(needAppearances ? "true" : "false")} >>{script} >>";
        objects[1] = $"<< /Type /Pages /Kids [{Refs(pageRefs)}] /Count {pages} >>";
        foreach (var page in pageRefs)
            objects[page - 1] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [{Refs(annots[page])}] >>";
        return FixturePdfs.Raw([.. objects.Select(o => Encoding.ASCII.GetBytes(o!))]);
    }

    private static string Refs(IEnumerable<int> refs) => string.Join(' ', refs.Select(r => $"{r} 0 R"));

    /// <summary>A PDF string: literal for printable ASCII, else UTF-16BE hex with a byte-order mark.</summary>
    private static string Str(string text)
    {
        if (text.All(c => c is >= ' ' and <= '~'))
            return "(" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal) + ")";
        var hex = new StringBuilder("<FEFF");
        foreach (var b in Encoding.BigEndianUnicode.GetBytes(text))
            hex.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        return hex.Append('>').ToString();
    }
}
