using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// T3 property 2 (ADR-002, ADR-007): what is stored or exported reads back as the same value. Records holding
/// <c>IReadOnlyList</c> members compare lists by reference, so equality is the serializer output, as the store sees it.
/// </summary>
public class SerializationProperties
{
    private static string Write<T>(T value) => JsonSerializer.Serialize(value, RulesJson.Compact);

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, RulesJson.Compact)!;

    [Property(MaxTest = 500)]
    public Property The_character_generator_makes_valid_characters_of_one_known_rules_family() =>
        Prop.ForAll(Generators.Characters.ToArbitrary(), c =>
            (c.Validate().Count == 0 && RulesFamilies.IsKnown(c.RulesFamily))
                .Label(string.Join(", ", c.Validate().Select(d => d.Code))));

    [Property(MaxTest = 500)]
    public Property A_character_round_trips_through_its_stored_form_unchanged() =>
        Prop.ForAll(Generators.Characters.ToArbitrary(), c =>
        {
            var written = Write(c);
            var read = Read<Character>(written);
            return (Write(read) == written && read.TotalLevel == c.TotalLevel && read.SchemaVersion == Character.CurrentSchemaVersion)
                .Label(written);
        });

    [Property(MaxTest = 500)]
    public Property A_content_revision_round_trips_unchanged_once_stored() =>
        Prop.ForAll(Generators.AnyRevisionArb(), r =>
        {
            var stored = Read<ContentRevision>(Write(Read<ContentRevision>(r.Json.ToJsonString())));
            var again = Read<ContentRevision>(Write(stored));
            return (Write(again) == Write(stored)
                    && again.Reference == stored.Reference
                    && again.SchemaVersion == stored.SchemaVersion
                    && again.RulesFamilies.SequenceEqual(stored.RulesFamilies)
                    && again.Effects.Select(e => (e.GetType(), e.Id)).SequenceEqual(stored.Effects.Select(e => (e.GetType(), e.Id))))
                .Label($"v{r.Version}");
        });
}
