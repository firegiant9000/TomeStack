using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.AppService.Extensions;

/// <summary>
/// ADR-011 <c>import.file</c>: the one file an import hook reads, parsed by TomeStack, never by the extension, under fixed
/// bounds: at most 5 MB, JSON nested at most 32 levels, CSV at most 50,000 rows and 10,000 characters per field, with
/// strict quoting (RFC 4180). A CSV becomes <c>{ "rows": [ { header: value } ] }</c>, so a transform reads both kinds
/// with JSON Pointers. Messages name a row or a limit, never the file's text.
/// </summary>
public static class ExtensionInput
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxJsonDepth = 32;
    public const int MaxCsvRows = 50_000;
    public const int MaxCsvFieldChars = 10_000;
    public const int MaxCsvColumns = 200;

    /// <exception cref="TransformException"><c>input.too-large</c>, <c>input.invalid-json</c>, <c>input.invalid-csv</c>, <c>input.encoding</c>.</exception>
    public static JsonNode Parse(byte[] bytes, string kind)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxBytes)
            throw new TransformException("input.too-large", $"The file is larger than {MaxBytes / (1024 * 1024)} MB.");
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new TransformException("input.encoding", "The file is not UTF-8 text.");
        }
        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];
        return kind switch
        {
            "json" => ParseJson(text),
            "csv" => ParseCsv(text),
            _ => throw new TransformException("input.kind", "An import hook reads JSON or CSV."),
        };
    }

    private static JsonNode ParseJson(string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = MaxJsonDepth })
                ?? throw new TransformException("input.invalid-json", "The file holds no JSON value.");
        }
        catch (JsonException)
        {
            throw new TransformException("input.invalid-json", $"The file is not valid JSON, or it nests deeper than {MaxJsonDepth} levels.");
        }
    }

    /// <summary>
    /// RFC 4180 quoting: comma-separated, a header row, fields quoted with " and "" for a quote inside; a quote anywhere
    /// else is refused. Lines may end in CRLF, LF or a bare CR, and blank lines are skipped (unless there is one column).
    /// </summary>
    private static JsonNode ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var afterQuote = false;
        var atFieldStart = true;
        void EndField()
        {
            if (record.Count >= MaxCsvColumns)
                throw new TransformException("input.invalid-csv", $"Row {records.Count + 1} has more than {MaxCsvColumns} columns.");
            record.Add(field.ToString());
            field.Clear();
            afterQuote = false;
            atFieldStart = true;
        }
        void EndRecord()
        {
            EndField();
            records.Add(record);
            record = [];
            if (records.Count > MaxCsvRows + 1)
                throw new TransformException("input.too-many-rows", $"The file has more than {MaxCsvRows:N0} rows.");
        }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; afterQuote = true; }
                }
                else field.Append(c);
            }
            else if (c == ',') EndField();
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                EndRecord();
            }
            else if (c == '"' && atFieldStart) { quoted = true; atFieldStart = false; }
            else if (c == '"' || afterQuote)
                throw new TransformException("input.invalid-csv", $"Row {records.Count + 1} has a quote inside an unquoted field, or text after a closing quote.");
            else { field.Append(c); atFieldStart = false; }
            if (field.Length > MaxCsvFieldChars)
                throw new TransformException("input.invalid-csv", $"Row {records.Count + 1} has a field longer than {MaxCsvFieldChars:N0} characters.");
        }
        if (quoted)
            throw new TransformException("input.invalid-csv", "The file ends inside a quoted field.");
        if (field.Length > 0 || record.Count > 0)
            EndRecord();
        if (records.Count == 0)
            throw new TransformException("input.invalid-csv", "The file has no header row.");

        var header = records[0];
        if (header.Any(string.IsNullOrWhiteSpace) || header.Distinct(StringComparer.Ordinal).Count() != header.Count)
            throw new TransformException("input.invalid-csv", "The header row needs a distinct, non-empty name for every column.");
        var rows = new JsonArray();
        for (var r = 1; r < records.Count; r++)
        {
            var values = records[r];
            if (values.Count == 1 && values[0].Length == 0 && header.Count > 1)
                continue; // a blank line (with one column, it is a row whose value is empty)
            if (values.Count != header.Count)
                throw new TransformException("input.invalid-csv", $"Row {r + 1} has {values.Count} fields; the header has {header.Count}.");
            var row = new JsonObject();
            for (var c = 0; c < header.Count; c++)
                row[header[c]] = values[c];
            rows.Add(row);
        }
        return new JsonObject { ["rows"] = rows };
    }
}
