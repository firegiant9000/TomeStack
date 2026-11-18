using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Exports;

/// <summary>What the HTML sheet shows besides the sheet export model: neither is part of <c>sheet-export.v1</c>.</summary>
/// <param name="Notes">Session notes; empty for a shared copy.</param>
/// <param name="DeathSaves">The character's current death saves, shown pre-ticked.</param>
public sealed record HtmlSheetExtras(IReadOnlyList<SessionNote> Notes, DeathSaves DeathSaves);

/// <summary>
/// The static session sheet (4.8): one self-contained HTML page with no script, link, image or form, for a phone or
/// tablet. Every model string goes through <see cref="Text"/>; ids are generated, never derived from names.
/// <see cref="Validate"/> is the adapter's tag-level self-check before anything is written.
/// </summary>
public static class HtmlSheet
{
    public const string Target = "html-sheet";
    public const string AdapterVersion = "1.0.0";
    public const string FileSuffix = ".tomestack-sheet.html";

    /// <summary>Largest pool drawn as tick boxes; the same number as PrintView's <c>maxTickBoxes</c>.</summary>
    public const int MaxTickBoxes = 20;

    private static readonly string Css = """
        :root{--bg:#ffffff;--fg:#1a1a1a;--muted:#4a4a4a;--line:#767676;--card:#f3f3f3;--accent:#0b4fb3}
        @media (prefers-color-scheme:dark){:root{--bg:#121212;--fg:#ececec;--muted:#bdbdbd;--line:#8a8a8a;--card:#1f1f1f;--accent:#8ab4f8}}
        *{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
        main{max-width:64rem;margin:0 auto;padding:1rem}
        h1{font-size:1.6rem;margin:0 0 .5rem}
        h2{font-size:1.25rem;margin:0 0 .5rem;border-bottom:1px solid var(--line)}
        h3{font-size:1.05rem;margin:.75rem 0 .25rem;text-transform:capitalize}
        section{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:.75rem 1rem;margin:0 0 1rem;break-inside:avoid-column}
        .muted{color:var(--muted)}
        dl{margin:0;display:grid;grid-template-columns:max-content 1fr;gap:.25rem 1rem}
        dt{color:var(--muted)}
        dd{margin:0}
        table{border-collapse:collapse;width:100%}
        th,td{text-align:left;padding:.25rem .5rem;border-bottom:1px solid var(--line);vertical-align:top}
        ul{margin:0;padding-left:1.25rem}
        details{margin:.25rem 0}
        summary{cursor:pointer;color:var(--accent);padding:.35rem 0;min-height:24px}
        input[type=checkbox]{width:28px;height:28px;margin:.2rem .35rem .2rem 0;vertical-align:middle}
        input[type=number]{width:6rem;min-height:44px;font:inherit;background:var(--bg);color:var(--fg);border:1px solid var(--line);border-radius:4px;padding:0 .5rem}
        .pool{margin:.5rem 0}
        .boxes{display:inline-block}
        .note{white-space:pre-wrap;margin:.25rem 0 .75rem}
        @media (min-width:700px){main{columns:2;column-gap:1rem}h1{column-span:all}}
        @media print{:root{--bg:#fff;--fg:#000;--muted:#333;--line:#666;--card:#fff;--accent:#000}main{columns:1}details>*:not(summary){display:none}}
        """.ReplaceLineEndings("\n").Insert(0, "\n") + "\n";

    private static readonly string StyleHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Css)));

    private static readonly string[] ForbiddenTags = ["script", "iframe", "object", "embed", "form", "base", "link", "img", "svg", "math"];
    private static readonly string[] ForbiddenAttributes = ["src", "srcset", "style", "action", "formaction", "poster", "xlink:href"];

    /// <summary>Renders the sheet. Pure: the same inputs give the same text.</summary>
    public static string Render(SheetExport sheet, HtmlSheetExtras extras)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(extras);
        var doc = new Doc();
        var sb = doc.Sb;
        var character = sheet.Character;
        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'sha256-").Append(StyleHash)
            .Append("'; form-action 'none'; base-uri 'none'\">\n");
        sb.Append("<title>").Append(Text(character.Name)).Append(" &mdash; TomeStack sheet</title>\n");
        sb.Append("<style>").Append(Css).Append("</style>\n</head>\n<body>\n<main>\n");
        sb.Append("<h1>").Append(Text(character.Name)).Append("</h1>\n");

        RenderHeader(doc, sheet);
        RenderCore(doc, sheet, extras.DeathSaves);
        RenderAttacks(doc, sheet);
        RenderSpellcasting(doc, sheet);
        RenderResources(doc, sheet);
        RenderFeatures(doc, sheet);
        RenderNotes(doc, extras.Notes);
        RenderSources(doc, sheet);

        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>
    /// The self-check: empty when the page holds none of the forbidden constructs. A single-pass tokenizer that follows the
    /// HTML start-tag states (no regex), so it reads tags the way a browser does. Encoded text never holds a raw
    /// <c>&lt;</c>, so any <c>&lt;</c> outside the doctype must start a tag, and text such as "javascript:" passes.
    /// </summary>
    public static IReadOnlyList<string> Validate(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var reasons = new List<string>();
        void Add(string reason)
        {
            if (!reasons.Contains(reason))
                reasons.Add(reason);
        }

        const string doctype = "<!doctype html>";
        var n = html.Length;
        var i = 0;
        while (true)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
                break;
            i = lt + 1;
            if (html.AsSpan(lt).StartsWith(doctype, StringComparison.OrdinalIgnoreCase))
            {
                i = lt + doctype.Length;
                continue;
            }
            if (i >= n || !(IsLetter(html[i]) || (html[i] == '/' && i + 1 < n && IsLetter(html[i + 1]))))
            {
                Add(i < n && html[i] is '!' or '?' ? "markup declaration" : "stray <");
                continue;
            }
            var endTag = html[i] == '/';
            if (endTag)
                i++;
            var nameStart = i;
            while (i < n && !IsSpace(html[i]) && html[i] is not ('/' or '>'))
            {
                if (html[i] is '"' or '\'' or '<' or '=')
                    Add("malformed tag name");
                i++;
            }
            var name = html[nameStart..i].ToLowerInvariant();
            if (endTag)
            {
                while (i < n && html[i] is not ('>' or '<'))
                    i++;
                if (i >= n || html[i] == '<')
                    Add("unterminated tag");
                else
                    i++;
                continue;
            }
            if (Array.IndexOf(ForbiddenTags, name) >= 0)
                Add($"tag <{name}>");

            var closed = false;
            while (i < n)
            {
                while (i < n && (IsSpace(html[i]) || html[i] == '/'))
                    i++;
                if (i >= n)
                    break;
                if (html[i] == '>')
                {
                    i++;
                    closed = true;
                    break;
                }
                var attributeStart = i;
                if (html[i] == '=')
                    i++;
                while (i < n && !IsSpace(html[i]) && html[i] is not ('/' or '>' or '='))
                {
                    if (html[i] is '"' or '\'' or '<')
                        Add("malformed attribute name");
                    i++;
                }
                var attributeName = html[attributeStart..i].ToLowerInvariant();
                while (i < n && IsSpace(html[i]))
                    i++;
                var value = "";
                if (i < n && html[i] == '=')
                {
                    i++;
                    while (i < n && IsSpace(html[i]))
                        i++;
                    if (i < n && html[i] is '"' or '\'')
                    {
                        var close = html.IndexOf(html[i], i + 1);
                        if (close < 0)
                        {
                            Add("unterminated quoted value");
                            i = n;
                            break;
                        }
                        value = html[(i + 1)..close];
                        i = close + 1;
                    }
                    else
                    {
                        var valueStart = i;
                        while (i < n && !IsSpace(html[i]) && html[i] != '>')
                        {
                            if (html[i] is '"' or '\'' or '<' or '=' or '`')
                                Add("malformed unquoted value");
                            i++;
                        }
                        value = html[valueStart..i];
                    }
                }
                if (attributeName.StartsWith("on", StringComparison.Ordinal))
                    Add($"event attribute {attributeName}");
                else if (Array.IndexOf(ForbiddenAttributes, attributeName) >= 0)
                    Add($"attribute {attributeName}");
                else if (attributeName == "href" && !value.StartsWith('#'))
                    Add("href that is not an in-page anchor");
                else if (name == "meta" && attributeName == "http-equiv" && !value.Equals("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
                    Add($"meta http-equiv {value}");
            }
            if (!closed)
            {
                Add("unterminated tag");
                break;
            }
            if (name == "style")
            {
                var end = html.IndexOf("</style", i, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    Add("unterminated style");
                    break;
                }
                var body = html[i..end];
                if (body.Contains("@import", StringComparison.OrdinalIgnoreCase))
                    Add("style: @import");
                if (body.Contains("url(", StringComparison.OrdinalIgnoreCase))
                    Add("style: url(");
                if (body.Contains('\\', StringComparison.Ordinal))
                    Add("style: backslash escape");
                i = end;
            }
        }
        return reasons;
    }

    private static bool IsLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    // The one place model strings are encoded, for text and for attribute values alike.
    private static string Text(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Signed(int value) => value >= 0 ? "+" + N(value) : N(value);

    private sealed class Doc
    {
        private int _count;

        public StringBuilder Sb { get; } = new();

        public void Open(string heading)
        {
            _count++;
            var id = "s" + N(_count);
            Sb.Append("<section aria-labelledby=\"").Append(id).Append("\">\n<h2 id=\"").Append(id).Append("\">").Append(Text(heading)).Append("</h2>\n");
        }

        public void Close() => Sb.Append("</section>\n");
    }

    private static void Boxes(StringBuilder sb, string label, int count, int ticked)
    {
        sb.Append("<span class=\"boxes\">");
        for (var i = 1; i <= count; i++)
            sb.Append("<input type=\"checkbox\" aria-label=\"").Append(Text(label + " " + N(i))).Append('"').Append(i <= ticked ? " checked" : "").Append('>');
        sb.Append("</span>\n");
    }

    /// <summary>A pool of spendable things: tick boxes when small, a number when large, text when unbounded.</summary>
    private static void Pool(StringBuilder sb, string label, string boxLabel, int? maximum, int spent)
    {
        sb.Append("<div class=\"pool\"><strong>").Append(Text(label)).Append("</strong>\n");
        if (maximum is null)
            sb.Append("<p>Spent ").Append(N(spent)).Append(" (no fixed maximum)</p>\n");
        else if (maximum <= 0)
        {
            // Nothing to spend.
        }
        else if (maximum <= MaxTickBoxes)
            Boxes(sb, boxLabel, maximum.Value, Math.Min(spent, maximum.Value));
        else
        {
            sb.Append("<p>Spent ").Append(N(spent)).Append(" of ").Append(N(maximum.Value)).Append("</p>\n");
            sb.Append("<label>Spent now <input type=\"number\" inputmode=\"numeric\" aria-label=\"").Append(Text(boxLabel + " spent now")).Append("\"></label>\n");
        }
        sb.Append("</div>\n");
    }

    private static void Details(StringBuilder sb, string summary, Action body)
    {
        sb.Append("<details><summary>").Append(Text(summary)).Append("</summary>\n");
        body();
        sb.Append("</details>\n");
    }

    private static void Paragraph(StringBuilder sb, string? text)
    {
        if (!string.IsNullOrEmpty(text))
            sb.Append("<p>").Append(Text(text)).Append("</p>\n");
    }

    private static string ProficiencyText(SheetProficiency proficiency) => proficiency switch
    {
        SheetProficiency.Proficient => "Proficient",
        SheetProficiency.Expertise => "Expertise",
        _ => "",
    };

    private static void RenderHeader(Doc doc, SheetExport sheet)
    {
        var sb = doc.Sb;
        var character = sheet.Character;
        doc.Open("Character");
        sb.Append("<dl>\n");
        sb.Append("<dt>Level</dt><dd>").Append(N(character.Level)).Append("</dd>\n");
        foreach (var @class in character.Classes)
        {
            sb.Append("<dt>Class</dt><dd>").Append(Text(@class.Name)).Append(' ').Append(N(@class.Level));
            if (@class.Subclass is not null)
                sb.Append(" (").Append(Text(@class.Subclass)).Append(')');
            sb.Append("</dd>\n");
        }
        sb.Append("<dt>Rules</dt><dd>").Append(Text(character.RulesFamily)).Append("</dd>\n");
        sb.Append("<dt>Exported</dt><dd>").Append(sheet.ExportedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</dd>\n");
        sb.Append("<dt>Copy</dt><dd>").Append(sheet.Purpose == SheetPurpose.Personal ? "Personal copy &mdash; do not share" : "For a shared game").Append("</dd>\n");
        sb.Append("</dl>\n");
        doc.Close();
    }

    private static void RenderCore(Doc doc, SheetExport sheet, DeathSaves deathSaves)
    {
        var sb = doc.Sb;
        doc.Open("Core");
        sb.Append("<dl>\n");
        foreach (var field in sheet.Fields.Where(f => f.Id is not (FieldIds.HitPoints or FieldIds.SpellAttack or FieldIds.SpellSaveDc)))
            sb.Append("<dt>").Append(Text(field.Label)).Append("</dt><dd>").Append(N(field.Value)).Append("</dd>\n");
        var hp = sheet.HitPoints;
        sb.Append("<dt>Hit points</dt><dd>Max ").Append(N(hp.Maximum)).Append(", current ").Append(N(hp.Current)).Append(", temporary ").Append(N(hp.Temporary)).Append("</dd>\n");
        sb.Append("</dl>\n");
        sb.Append("<p><label>Current hit points now <input type=\"number\" inputmode=\"numeric\" aria-label=\"Current hit points now\"></label></p>\n");
        sb.Append("<p><label>Temporary hit points now <input type=\"number\" inputmode=\"numeric\" aria-label=\"Temporary hit points now\"></label></p>\n");

        sb.Append("<table>\n<thead><tr><th scope=\"col\">Ability</th><th scope=\"col\">Score</th><th scope=\"col\">Modifier</th><th scope=\"col\">Save</th><th scope=\"col\">Save proficiency</th></tr></thead>\n<tbody>\n");
        foreach (var ability in sheet.Abilities)
        {
            sb.Append("<tr><th scope=\"row\">").Append(Text(ability.Ability.ToUpperInvariant())).Append("</th><td>").Append(N(ability.Score)).Append("</td><td>").Append(Signed(ability.Modifier))
                .Append("</td><td>").Append(Signed(ability.Save)).Append("</td><td>").Append(ProficiencyText(ability.SaveProficiency)).Append("</td></tr>\n");
        }
        sb.Append("</tbody>\n</table>\n");

        if (sheet.Skills.Count > 0)
        {
            sb.Append("<h3>Skills</h3>\n<ul>\n");
            foreach (var skill in sheet.Skills)
            {
                sb.Append("<li>").Append(Text(skill.Label)).Append(' ').Append(Signed(skill.Total));
                if (skill.Proficiency != SheetProficiency.None)
                    sb.Append(" <span class=\"muted\">(").Append(ProficiencyText(skill.Proficiency)).Append(")</span>");
                sb.Append("</li>\n");
            }
            sb.Append("</ul>\n");
        }

        sb.Append("<h3>Conditions</h3>\n");
        sb.Append("<p>").Append(sheet.Conditions.Active.Count > 0 ? string.Join(", ", sheet.Conditions.Active.Select(Text)) : "None")
            .Append(". Exhaustion ").Append(N(sheet.Conditions.Exhaustion)).Append(".</p>\n");

        sb.Append("<h3>Death saves</h3>\n<p>Successes ");
        Boxes(sb, "Death save success", DeathSaves.Maximum, Math.Clamp(deathSaves.Successes, 0, DeathSaves.Maximum));
        sb.Append("</p>\n<p>Failures ");
        Boxes(sb, "Death save failure", DeathSaves.Maximum, Math.Clamp(deathSaves.Failures, 0, DeathSaves.Maximum));
        sb.Append("</p>\n");
        doc.Close();
    }

    private static void RenderAttacks(Doc doc, SheetExport sheet)
    {
        if (sheet.Attacks.Count == 0)
            return;
        var sb = doc.Sb;
        doc.Open("Attacks");
        sb.Append("<table>\n<thead><tr><th scope=\"col\">Attack</th><th scope=\"col\">To hit</th><th scope=\"col\">Damage</th><th scope=\"col\">Range</th><th scope=\"col\">Properties</th></tr></thead>\n<tbody>\n");
        foreach (var attack in sheet.Attacks)
        {
            sb.Append("<tr><th scope=\"row\">").Append(Text(attack.Name)).Append("</th><td>").Append(Signed(attack.ToHit)).Append("</td><td>").Append(Text(attack.Damage)).Append(' ').Append(Text(attack.DamageType));
            if (attack.VersatileDamage is not null)
                sb.Append(" (versatile ").Append(Text(attack.VersatileDamage)).Append(')');
            sb.Append("</td><td>").Append(Text(attack.Range)).Append("</td><td>").Append(string.Join(", ", attack.Properties.Select(Text))).Append("</td></tr>\n");
        }
        sb.Append("</tbody>\n</table>\n");
        doc.Close();
    }

    private static void RenderSpellcasting(Doc doc, SheetExport sheet)
    {
        var slots = sheet.Slots.Where(s => s.Maximum > 0).ToList();
        var pact = sheet.PactSlots is { Maximum: > 0 } p ? p : null;
        if (sheet.Spellcasting.Count == 0 && slots.Count == 0 && pact is null)
            return;
        var sb = doc.Sb;
        doc.Open("Spellcasting");
        if (slots.Count > 0 || pact is not null)
            sb.Append("<p class=\"muted\">Ticked means spent.</p>\n");
        foreach (var slot in slots)
            Pool(sb, "Level " + N(slot.Level) + " slots", "Level " + N(slot.Level) + " slot", slot.Maximum, slot.Spent);
        if (pact is not null)
            Pool(sb, "Pact slots (level " + N(pact.Level) + ")", "Pact slot", pact.Maximum, pact.Spent);
        foreach (var caster in sheet.Spellcasting)
        {
            sb.Append("<h3>").Append(Text(caster.Name)).Append("</h3>\n");
            sb.Append("<p>Ability ").Append(Text(caster.Ability.ToUpperInvariant())).Append(", spell attack ").Append(Signed(caster.AttackBonus)).Append(", save DC ").Append(N(caster.SaveDc)).Append("</p>\n");
            foreach (var level in caster.Spells.GroupBy(s => s.Level).OrderBy(g => g.Key))
            {
                sb.Append("<h4>").Append(level.Key == 0 ? "Cantrips" : "Level " + N(level.Key)).Append("</h4>\n");
                foreach (var spell in level)
                {
                    var markers = (spell.Prepared ? " (prepared)" : "") + (spell.Concentration ? " (concentration)" : "") + (spell.Ritual ? " (ritual)" : "");
                    Details(sb, spell.Name + markers, () =>
                    {
                        var facts = new[] { spell.School, spell.CastingTime, spell.Range, spell.Components, spell.Duration }.Where(f => !string.IsNullOrEmpty(f));
                        Paragraph(sb, string.Join(" | ", facts));
                        Paragraph(sb, spell.Summary);
                        Paragraph(sb, spell.Text);
                    });
                }
            }
        }
        doc.Close();
    }

    private static void RenderResources(Doc doc, SheetExport sheet)
    {
        if (sheet.Resources.Count == 0 && sheet.HitDice.Count == 0 && sheet.Toggles.Count == 0 && sheet.Scales.Count == 0)
            return;
        var sb = doc.Sb;
        doc.Open("Resources");
        if (sheet.Resources.Count > 0 || sheet.HitDice.Count > 0)
            sb.Append("<p class=\"muted\">Ticked means spent.</p>\n");
        foreach (var resource in sheet.Resources)
        {
            var title = string.Equals(resource.Label, resource.Feature, StringComparison.Ordinal) ? resource.Label : resource.Label + " (" + resource.Feature + ")";
            Pool(sb, title, resource.Label, resource.Maximum, resource.Spent);
            if (resource.Recovery.Count > 0)
                sb.Append("<p class=\"muted\">Recovers: ").Append(string.Join("; ", resource.Recovery.Select(Text))).Append("</p>\n");
        }
        foreach (var dice in sheet.HitDice)
            Pool(sb, "Hit dice d" + N(dice.Die), "Hit die d" + N(dice.Die), dice.Total, dice.Spent);
        if (sheet.Toggles.Count > 0)
        {
            sb.Append("<h3>Switches</h3>\n<ul>\n");
            foreach (var toggle in sheet.Toggles)
                sb.Append("<li>").Append(Text(toggle.Label)).Append(": ").Append(toggle.On ? "on" : "off").Append("</li>\n");
            sb.Append("</ul>\n");
        }
        if (sheet.Scales.Count > 0)
        {
            sb.Append("<h3>Class columns</h3>\n<ul>\n");
            foreach (var scale in sheet.Scales)
                sb.Append("<li>").Append(Text(scale.Label)).Append(" (").Append(Text(scale.ClassName)).Append("): ").Append(N(scale.Value)).Append("</li>\n");
            sb.Append("</ul>\n");
        }
        doc.Close();
    }

    private static void RenderFeatures(Doc doc, SheetExport sheet)
    {
        if (sheet.Features.Count == 0)
            return;
        var sb = doc.Sb;
        doc.Open("Features");
        foreach (var kind in sheet.Features.GroupBy(f => f.Kind))
        {
            sb.Append("<h3>").Append(Text(kind.Key)).Append("</h3>\n");
            foreach (var feature in kind)
            {
                Details(sb, feature.Name, () =>
                {
                    Paragraph(sb, feature.Summary);
                    foreach (var text in feature.Texts)
                        Paragraph(sb, text);
                });
            }
        }
        doc.Close();
    }

    private static void RenderNotes(Doc doc, IReadOnlyList<SessionNote> notes)
    {
        if (notes.Count == 0)
            return;
        var sb = doc.Sb;
        doc.Open("Session notes");
        foreach (var note in notes.OrderByDescending(n => n.Date).ThenByDescending(n => n.CreatedAt))
        {
            sb.Append("<h3>").Append(note.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</h3>\n");
            sb.Append("<p class=\"note\">").Append(Text(note.Text)).Append("</p>\n");
        }
        doc.Close();
    }

    private static void RenderSources(Doc doc, SheetExport sheet)
    {
        if (sheet.Notices.Count == 0 && sheet.Dropped.Count == 0)
            return;
        var sb = doc.Sb;
        doc.Open("Sources");
        if (sheet.Notices.Count > 0)
        {
            sb.Append("<ul>\n");
            foreach (var notice in sheet.Notices)
            {
                sb.Append("<li><strong>").Append(Text(notice.Title)).Append("</strong> by ").Append(Text(notice.Publisher)).Append(". Licence: ").Append(Text(notice.License)).Append('.');
                if (!string.IsNullOrEmpty(notice.Attribution))
                    sb.Append(' ').Append(Text(notice.Attribution));
                if (!string.IsNullOrEmpty(notice.ModificationNotice))
                    sb.Append(' ').Append(Text(notice.ModificationNotice));
                if (notice.TotalsOnly)
                    sb.Append(" <span class=\"muted\">(totals only)</span>");
                sb.Append("</li>\n");
            }
            sb.Append("</ul>\n");
        }
        if (sheet.Dropped.Count > 0)
        {
            sb.Append("<h3>Left out of this file</h3>\n<ul>\n");
            foreach (var dropped in sheet.Dropped)
                sb.Append("<li>").Append(Text(dropped.Source)).Append(" by ").Append(Text(dropped.Publisher)).Append(": ").Append(N(dropped.Items)).Append(dropped.Items == 1 ? " item" : " items").Append("</li>\n");
            sb.Append("</ul>\n");
        }
        doc.Close();
    }
}
