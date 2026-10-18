namespace TomeStack.AppService.CharacterImport;

/// <summary>Whether a sheet entry was listed as a feat or as a feature (the sheet's own sections).</summary>
public enum FeatureKind { Feat, Feature }

/// <summary>
/// Item 16 (R104): the description a sheet gives under one feat or feature, kept apart from the names
/// (<see cref="DdbSheet.Feats"/> and <see cref="DdbSheet.Features"/> are unchanged). Neutral on purpose: a text parser fills the same list
/// later. <paramref name="Text"/> is the lines under the entry, joined with <c>\n</c> and trimmed; an entry with none has no record.
/// </summary>
/// <remarks>The name and the text are the sheet's own words, so <c>ToString</c> prints the kind and the length only.</remarks>
public sealed record FeatureText(string Name, FeatureKind Kind, string Text)
{
    public override string ToString() => $"FeatureText {{ Kind = {Kind}, Length = {Text.Length} }}";
}
