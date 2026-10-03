using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

public enum MatchKind { Species, Background, Class, Subclass, Feat, Skill, Spell, Item, Feature }

/// <summary>
/// What became of one thing the sheet names (<c>features/ddb-pdf-import.md</c> step 3). <see cref="Choose"/> rows wait for
/// the user; <see cref="NotFound"/>, <see cref="NoPlace"/>, <see cref="Unreadable"/> and <see cref="LeftOut"/> rows are
/// left out of the character and listed.
/// </summary>
public enum MatchStatus { Matched, Choose, NotFound, NoPlace, Unreadable, LeftOut }

/// <summary>
/// Where a match goes on the character: a pin, a selection on a choice of the draft, a class entry, a known spell (with
/// its caster's content id), an equipment entry, nothing to add because the draft already has it (<see cref="None"/>), or
/// nowhere: installed, but no open choice offers it and it cannot be pinned (<see cref="Nowhere"/>, a "No place" row).
/// </summary>
public enum PlacementKind { Pin, Choice, Class, Spell, Equipment, None, Nowhere }

public sealed record Placement(PlacementKind Kind, ContentReference? ChoiceSource = null, string? ChoiceId = null, Guid? Caster = null);

/// <summary>An installed option a row could be, with its source and families shown to the user (SPEC S-03).</summary>
/// <param name="AllowedInCampaign">With a campaign, whether it allows the option's source; null without one.</param>
public sealed record MatchCandidate(ContentReference Reference, string Name, string SourceTitle, IReadOnlyList<string> Families, bool? AllowedInCampaign, Placement Placement);

/// <param name="RowId">Stable per sheet item: <c>species</c>, <c>class:0</c>, <c>class:0:subclass</c>, <c>skill:athletics</c>, <c>spell:3</c>, …</param>
/// <param name="Label">The sheet's text, shown to the user only, never logged.</param>
/// <param name="Note">A code: why a row has no place, or a warning on a match (<c>campaign.source-not-allowed</c>).</param>
public sealed record MatchRow(string RowId, MatchKind Kind, string Label, MatchStatus Status, IReadOnlyList<MatchCandidate> Candidates, MatchCandidate? Chosen, string? Note);

/// <summary>The user's answer for one row: a candidate (and, for a spell, its caster), or leave it out.</summary>
public sealed record Resolution(string RowId, ContentReference? Chosen, bool LeaveOut, Guid? Caster = null);
