using TomeStack.ImportWorker.Detection;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <param name="CandidateIds">At most <see cref="TomeStackApp.MaxAcceptMany"/> (200).</param>
/// <param name="ListKey">Normalised by the service (<see cref="SpellListKey"/>); the UI shows the same key before saving.</param>
public sealed record CandidateSpellListRequest(IReadOnlyList<Guid> CandidateIds, string ListKey);

/// <param name="ListKey">The normalised key that was added.</param>
/// <param name="Updated">The candidates whose edited version now carries the key.</param>
/// <param name="Skipped">The rest, with a code each (<c>candidate.not-found</c>, <c>candidate.already-reviewed</c>, <c>spell-list.not-a-spell</c>).</param>
/// <param name="Notes">Updated candidates that need a word (ruling R59): <c>spell-list.other-lists-unreviewed</c> means other proposed lists are still unsure and the candidate cannot be accepted until they are checked.</param>
public sealed record CandidateSpellListResult(string ListKey, IReadOnlyList<StoredCandidate> Updated, IReadOnlyList<BlockedCandidate> Skipped, IReadOnlyList<BlockedCandidate> Notes);

/// <param name="ContentIds">At most <see cref="TomeStackApp.MaxPublishMany"/>.</param>
public sealed record ContentSpellListRequest(IReadOnlyList<Guid> ContentIds, string ListKey);

public sealed record SkippedContent(Guid ContentId, IReadOnlyList<string> Codes);

/// <param name="Drafts">One new draft per changed spell, in request order. Nothing is published.</param>
/// <param name="Note">States that characters keep the spell revisions they recorded until a reviewed update.</param>
public sealed record ContentSpellListResult(string ListKey, IReadOnlyList<ContentReference> Drafts, IReadOnlyList<SkippedContent> Skipped, string Note);

/// <summary>A spell list key that exists in published content, and how many spells are on it.</summary>
public sealed record SpellListKeyCount(string Key, int Spells);

public sealed partial class TomeStackApp
{
    private static string RequireSpellListKey(string? text)
    {
        if (!SpellListKey.TryNormalise(text, out var key))
            throw new AppValidationException([new("spell-list.invalid-key", "A spell list key is 1 to 60 letters, digits or hyphens.")]);
        return key;
    }

    /// <summary>
    /// <c>import.candidate.addToSpellList</c> (item 10, ruling R43): for each pending spell candidate, saves an edit whose spell
    /// effect gains the key and which no longer waits on its <c>lists</c> field. Unlike an ordinary edit it clears no other
    /// unsure field. Anything else is returned as skipped with a code, never forced. One transaction.
    /// </summary>
    public CandidateSpellListResult AddCandidatesToSpellList(CandidateSpellListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = RequireSpellListKey(request.ListKey);
        var ids = (request.CandidateIds ?? []).Distinct().ToList();
        if (ids.Count is < 1 or > MaxAcceptMany)
            throw new AppValidationException([new("import.limit-out-of-range", $"Add 1 to {MaxAcceptMany} candidates to a spell list at a time.")]);
        var updated = new List<StoredCandidate>();
        var skipped = new List<BlockedCandidate>();
        var notes = new List<BlockedCandidate>();
        _store.InTransaction(() =>
        {
            foreach (var id in ids)
            {
                if (_store.FindImportCandidate(id) is not { } stored)
                {
                    skipped.Add(new(id, ["candidate.not-found"]));
                    continue;
                }
                if (stored.Status != CandidateStatus.Pending)
                {
                    skipped.Add(new(id, ["candidate.already-reviewed"]));
                    continue;
                }
                var current = stored.Current;
                if (current.ProposedKind != ContentKind.Spell || current.ProposedEffects.OfType<SpellEffect>().FirstOrDefault() is not { } effect)
                {
                    skipped.Add(new(id, ["spell-list.not-a-spell"]));
                    continue;
                }
                // R59: the reviewer's key confirms the "lists" field only when it is the only key the spell has. A candidate that
                // also carries other proposed keys (read from a list page) still has them to check, so its mark stays and it
                // cannot be accepted until the reviewer has looked at them. A key that is already there and is the only one is
                // a confirmation, not a skip.
                IReadOnlyList<string> lists = effect.Lists.Contains(key, StringComparer.Ordinal) ? effect.Lists : [.. effect.Lists, key];
                var onlyThisKey = lists.All(l => string.Equals(l, key, StringComparison.Ordinal));
                var marked = current.LowConfidenceFields.Contains("lists", StringComparer.Ordinal);
                var fields = new Dictionary<string, string>(current.Fields, StringComparer.Ordinal) { ["lists"] = string.Join(", ", lists) };
                var edited = current with
                {
                    ProposedEffects = [.. current.ProposedEffects.Select(e => ReferenceEquals(e, effect) ? effect with { Lists = lists } : e)],
                    Fields = fields,
                    LowConfidenceFields = onlyThisKey ? [.. current.LowConfidenceFields.Where(f => f != "lists")] : current.LowConfidenceFields,
                    Uncertainties = [.. current.Uncertainties.Where(u => u != CandidateDetector.NoListsNote && (!onlyThisKey || !u.StartsWith("Spell list '", StringComparison.Ordinal)))],
                };
                var saved = stored with { Edited = edited, UpdatedAt = _time.GetUtcNow() };
                _store.SaveImportCandidate(saved);
                updated.Add(_store.FindImportCandidate(saved.Id) ?? saved); // the row as stored, with its fresh Etag (R134)
                if (marked && !onlyThisKey)
                    notes.Add(new(id, ["spell-list.other-lists-unreviewed"]));
            }
            // Counts only: no name, key or document text (the audit and the error log never quote them).
            foreach (var job in updated.GroupBy(c => c.JobId))
                _store.AddImportAudit(job.Key, _time.GetUtcNow(), "candidate-edited", $"spell list added to {job.Count()} spell candidates");
        });
        return new(key, updated, skipped, notes);
    }

    /// <summary>
    /// <c>content.addToSpellList</c> (item 10, ruling R44): for each content id, a new **draft** from its newest revision
    /// (insertion order) with the key added to its <c>spell</c> effect. Content whose newest revision already has the key, is
    /// not a spell or does not exist is skipped with a code. A bundled source is refused by <see cref="SaveDraft"/>, and then
    /// nothing is written. It never publishes; publishing is the previewed <c>content.publishMany</c> with these ids.
    /// </summary>
    public ContentSpellListResult AddToSpellList(ContentSpellListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = RequireSpellListKey(request.ListKey);
        var ids = (request.ContentIds ?? []).Distinct().ToList();
        if (ids.Count is < 1 or > MaxPublishMany)
            throw new AppValidationException([new("content.limit-out-of-range", $"Add 1 to {MaxPublishMany} spells to a spell list at a time.")]);
        var drafts = new List<ContentReference>();
        var skipped = new List<SkippedContent>();
        _store.InTransaction(() =>
        {
            foreach (var id in ids)
            {
                var revisions = _store.ListRevisions(id); // insertion order: the last one is the newest
                var newest = revisions.LastOrDefault();
                if (newest is null)
                {
                    skipped.Add(new(id, ["content.not-found"]));
                    continue;
                }
                // R60: a published spell whose newest revision is a draft has unpublished edits of its own. A second draft from it
                // would carry them into the publish panel, ticked, and "already on this list" would be false when only the draft
                // has the key. It is left for the author to publish or discard that draft first.
                if (newest.Kind == ContentKind.Spell && newest.Status == RevisionStatus.Draft && revisions.Any(r => r.Status == RevisionStatus.Published))
                {
                    skipped.Add(new(id, ["spell-list.has-unpublished-draft"]));
                    continue;
                }
                if (newest.Kind != ContentKind.Spell || newest.Effects.OfType<SpellEffect>().FirstOrDefault() is not { } effect)
                {
                    skipped.Add(new(id, ["spell-list.not-a-spell"]));
                    continue;
                }
                if (effect.Lists.Contains(key, StringComparer.Ordinal))
                {
                    skipped.Add(new(id, ["spell-list.already-on-list"]));
                    continue;
                }
                IReadOnlyList<string> lists = [.. effect.Lists, key];
                drafts.Add(SaveDraft(newest with
                {
                    RevisionId = Guid.NewGuid(),
                    Status = RevisionStatus.Draft,
                    Effects = [.. newest.Effects.Select(e => ReferenceEquals(e, effect) ? effect with { Lists = lists } : e)],
                }));
            }
        });
        return new(key, drafts, skipped, "Characters keep the spell revisions they recorded until you review an update for each one.");
    }

    /// <summary>
    /// <c>content.spellListKeys</c> (item 10, ruling R45): every list key on the newest published revision of each spell, with
    /// its spell count, and every list a published class or feature's <c>spellcasting</c> effect names (0 when no spell is on
    /// it). Read only, sorted by key.
    /// </summary>
    public IReadOnlyList<SpellListKeyCount> SpellListKeys()
    {
        // Review fix 5: the store returns only the newest published revision of each content, and only those holding a spell or a
        // spellcasting effect, so the rest of the library is never read into memory.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var revision in _store.ListNewestPublishedWithEffects([SpellEffect.TypeName, SpellcastingEffect.TypeName]))
        {
            if (revision.Kind == ContentKind.Spell)
            {
                foreach (var key in revision.Effects.OfType<SpellEffect>().SelectMany(s => s.Lists).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal))
                    counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            foreach (var caster in revision.Effects.OfType<SpellcastingEffect>().Where(c => !string.IsNullOrWhiteSpace(c.SpellList)))
                counts.TryAdd(caster.SpellList, 0);
        }
        return [.. counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new SpellListKeyCount(c.Key, c.Value))];
    }
}
