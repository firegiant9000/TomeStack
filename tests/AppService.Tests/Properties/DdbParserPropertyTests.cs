using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker.Forms;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>
/// Character-sheet import S2: the parser is total and deterministic over any field set the worker could return (names the
/// maps know, row numbers, and names they do not; values of any length up to the 20,000-character limit, control
/// characters included; any checkbox state). Values are generated, so none comes from a real sheet.
/// </summary>
public class DdbParserPropertyTests
{
    /// <summary>Values that reach the parser's branches: numbers, signs, class text, marks, separators.</summary>
    private static readonly string[] Shaped =
    [
        "", " ", "+3", "-1", "−7", "25", "0", "99999", "abc", "Yes", "yes", "Off", "\n\n", "\r\n", "/ / /", "(",
        "Fixture Fighter 3", "Fixture Fighter 5 (Fixture Path) / Fixture Mage 3", "Fixture Fighter 20 / Fixture Mage 1",
        "Fixture A 1/Fixture B 1/Fixture C 1/Fixture D 1/Fixture E 1/Fixture F 1/Fixture G 1/Fixture H 1/Fixture I 1/Fixture J 1",
        "Fixture Steady Breath\nFixture Bold Surge",
    ];

    /// <summary>Text with any character outside the surrogate range (the worker sends well-formed UTF-16), occasionally long.</summary>
    private static Gen<string> Text { get; } =
        from length in Gen.Frequency((8, Gen.Choose(0, 60)), (1, Gen.Choose(0, 20_000)))
        from chars in Gen.Choose(0, 0xD7FF).Select(c => (char)c).ArrayOf(length)
        select new string(chars);

    private static Gen<string> Names { get; } =
        Gen.OneOf(
            Gen.Elements(LayoutMaps.All.SelectMany(m => m.Fields.Keys.Concat(m.Required)).Distinct().ToArray())
                .SelectMany(name => Gen.Choose(0, 1_200).Select(n => name.Replace("{n}", n.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))),
            Text);

    private static Gen<FormField> Fields { get; } =
        from name in Names
        from type in Gen.Elements("text", "checkbox", "radio", "combo", "list", "other")
        from value in Gen.Frequency((1, Gen.Constant<string?>(null)), (3, Gen.Elements(Shaped).Select(s => (string?)s)), (3, Text.Select(s => (string?)s)))
        from isChecked in Gen.Elements<bool?>(null, true, false)
        from page in Gen.Elements<int?>(null, 1, 2, 51)
        select new FormField(name, type, page, value, isChecked);

    /// <summary>Sometimes a whole map's required names, so the field set is recognised and every semantic is reached.</summary>
    private static Gen<List<FormField>> FieldSets { get; } =
        from count in Gen.Choose(0, 80)
        from fields in Fields.ArrayOf(count)
        from required in Gen.Elements(true, false)
        from map in Gen.Elements(LayoutMaps.All.ToArray())
        select required ? [.. map.Required.Select(r => new FormField(r, "text", 1, "Fixture")), .. fields] : fields.ToList();

    [Property(MaxTest = 300)]
    public Property The_parser_never_throws_over_any_field_set() =>
        Prop.ForAll(FieldSets.ToArbitrary(), fields =>
        {
            _ = DdbParser.Recognise(fields);
            foreach (var map in LayoutMaps.All)
                _ = DdbParser.Parse(map, fields);
        });

    [Property(MaxTest = 200)]
    public Property Parsing_is_deterministic() =>
        Prop.ForAll(FieldSets.ToArbitrary(), fields =>
            LayoutMaps.All.All(map => DdbParserTests.Json(DdbParser.Parse(map, fields)) == DdbParserTests.Json(DdbParser.Parse(map, [.. fields]))));
}
