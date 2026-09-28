using System.Text.Json;

namespace TomeStack.RulesCore;

public enum ChangeKind { Added, Removed, Changed }

/// <summary>One effect (by id) that differs between two revisions. JSON is the compact serialized effect.</summary>
public sealed record EffectChange(string EffectId, ChangeKind Change, string? Type, string? Before, string? After);

/// <summary>One descriptive property (name, kind, families, page, summary) that differs.</summary>
public sealed record PropertyChange(string Property, string? Before, string? After);

/// <summary>Mechanics and metadata differences between two revisions of the same content (SPEC I-06, BACKLOG B07 seed).</summary>
public sealed record ContentDiff(ContentReference From, ContentReference To, IReadOnlyList<PropertyChange> Properties, IReadOnlyList<EffectChange> Effects)
{
    public bool IsEmpty => Properties.Count == 0 && Effects.Count == 0;

    /// <summary>Compares effects by id, and the properties that describe the content. Pure; nothing is resolved.</summary>
    public static ContentDiff Compare(ContentRevision before, ContentRevision after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var properties = new List<PropertyChange>();
        void Property(string name, string? a, string? b)
        {
            if (!string.Equals(a, b, StringComparison.Ordinal))
                properties.Add(new(name, a, b));
        }
        Property("name", before.Name, after.Name);
        Property("kind", before.Kind.ToString(), after.Kind.ToString());
        Property("rulesFamilies", string.Join(", ", before.RulesFamilies), string.Join(", ", after.RulesFamilies));
        Property("page", before.Provenance.Page?.ToString(), after.Provenance.Page?.ToString());
        Property("source", before.Provenance.SourceId.ToString(), after.Provenance.SourceId.ToString());
        Property("summary", before.Summary, after.Summary);

        string Json(Effect effect) => JsonSerializer.Serialize(effect, RulesJson.Compact);
        var old = before.Effects.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var @new = after.Effects.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var effects = new List<EffectChange>();
        foreach (var effect in before.Effects.Where(e => !@new.ContainsKey(e.Id)))
            effects.Add(new(effect.Id, ChangeKind.Removed, effect.Type, Json(effect), null));
        foreach (var effect in after.Effects)
        {
            if (!old.TryGetValue(effect.Id, out var previous))
                effects.Add(new(effect.Id, ChangeKind.Added, effect.Type, null, Json(effect)));
            else if (Json(previous) is var a && Json(effect) is var b && a != b)
                effects.Add(new(effect.Id, ChangeKind.Changed, effect.Type, a, b));
        }
        return new ContentDiff(before.Reference, after.Reference, properties, effects);
    }
}
