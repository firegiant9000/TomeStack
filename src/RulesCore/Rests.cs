namespace TomeStack.RulesCore;

/// <summary>What a proposed rest change is about.</summary>
public enum RestChangeKind { HitPoints, TemporaryHitPoints, Resource, Exhaustion }

/// <summary>
/// One proposed change of a rest preview (SPEC C-05). <paramref name="Id"/> is stable for the same character state, so
/// the player can untick it and the confirmed rest skips it. <paramref name="Condition"/> is set when the change depends
/// on the situation (for example, food and drink), and the player decides.
/// </summary>
public sealed record RestChange(
    string Id,
    RestChangeKind Kind,
    string Label,
    int From,
    int To,
    string Reason,
    TraceOrigin Origin,
    Guid? ContentId = null,
    string? ResourceId = null,
    string? Condition = null,
    int? SpentAfter = null);

/// <summary>A rest's proposal: every change it would make, plus what the player must handle by hand.</summary>
public sealed record RestPlan(RestPeriod Kind, IReadOnlyList<RestChange> Changes, IReadOnlyList<Diagnostic> Manual);

/// <summary>
/// ARCHITECTURE "commands vs calculation": a rest is planned from the calculated sheet and never applies itself. Pure;
/// the application service shows the plan and applies the confirmed part in one transaction.
/// </summary>
public static class RestPlanner
{
    /// <summary>
    /// SRD long rest (both families): regain all lost hit points, lose temporary hit points, recover every resource by its
    /// <c>longRest</c> recovery, and remove one exhaustion level (2014: only with food and drink,
    /// <see cref="RulesFamilyPolicy.LongRestExhaustionNeedsFoodAndDrink"/>). Short-rest recoveries are not part of a long
    /// rest; content that recovers on both declares both. Hit dice and spell slots are not tracked yet.
    /// </summary>
    public static RestPlan LongRest(Character character, CharacterSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(sheet);
        var policy = RulesFamilies.Get(character.RulesFamily);
        var rules = new TraceOrigin(TraceOriginKind.RulesPolicy, character.RulesFamily);
        var changes = new List<RestChange>();
        var manual = new List<Diagnostic>();

        if (sheet.HitPoints is { } hp)
        {
            if (hp.Current < hp.Maximum)
                changes.Add(new("hitPoints", RestChangeKind.HitPoints, "Hit points", hp.Current, hp.Maximum, "A long rest restores all lost hit points", rules));
            if (hp.Temporary > 0)
                changes.Add(new("temporaryHitPoints", RestChangeKind.TemporaryHitPoints, "Temporary hit points", hp.Temporary, 0, "Temporary hit points last until you finish a long rest", rules));
        }

        foreach (var resource in sheet.Resources ?? [])
        {
            if (resource.Spent == 0)
                continue;
            if (resource.Recoveries.Count == 0)
            {
                // Nothing says how it recovers; its text may, so the player decides instead of it being silently skipped.
                manual.Add(new(
                    "rest.no-recovery-encoded",
                    $"'{resource.Label}' ({resource.ContentName}) has no recovery rule in its content. If its text says it recovers on a long rest, recover it by hand.",
                    resource.Content,
                    resource.EffectId));
                continue;
            }
            var recovery = resource.Recoveries.FirstOrDefault(r => r.On == RestPeriod.LongRest);
            if (recovery is null)
                continue; // it recovers on a short rest only, which a long rest is not
            var origin = resource.Trace.Count > 0 ? resource.Trace[0].Origin : rules;
            if (resource.Current is not { } current || resource.Maximum is not { } maximum || (!recovery.All && recovery.Value is null))
            {
                manual.Add(new(
                    "rest.recover-by-hand",
                    $"'{resource.Label}' ({resource.ContentName}) recovers on a long rest ({recovery.Amount}), but TomeStack cannot calculate it; recover it by hand.",
                    resource.Content,
                    recovery.EffectId));
                continue;
            }
            var regained = recovery.All ? resource.Spent : Math.Min(recovery.Value!.Value, resource.Spent);
            if (regained == 0)
                continue;
            var spentAfter = resource.Spent - regained;
            changes.Add(new(
                $"resource:{resource.Content.ContentId:D}:{resource.ResourceId}",
                RestChangeKind.Resource,
                resource.Label,
                current,
                Math.Max(maximum - spentAfter, 0),
                recovery.All ? $"{resource.ContentName}: regain all expended uses on a long rest" : $"{resource.ContentName}: regain {regained} use(s) on a long rest ({recovery.Amount})",
                origin with { EffectId = recovery.EffectId },
                resource.Content.ContentId,
                resource.ResourceId,
                SpentAfter: spentAfter));
        }

        if (character.Play.Exhaustion > 0)
        {
            changes.Add(new(
                "exhaustion",
                RestChangeKind.Exhaustion,
                "Exhaustion level",
                character.Play.Exhaustion,
                character.Play.Exhaustion - 1,
                "A long rest removes one exhaustion level",
                rules,
                Condition: policy.LongRestExhaustionNeedsFoodAndDrink ? "Only if the character has had food and drink (2014 rules)" : null));
        }

        return new(RestPeriod.LongRest, changes, manual);
    }

    /// <summary>The play state after the plan's changes, except those in <paramref name="skipped"/>.</summary>
    public static PlayState Apply(PlayState play, RestPlan plan, IReadOnlySet<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var change in plan.Changes.Where(c => !skipped.Contains(c.Id)))
        {
            play = change.Kind switch
            {
                RestChangeKind.HitPoints => play with { CurrentHitPoints = null }, // null is "at the maximum"
                RestChangeKind.TemporaryHitPoints => play with { TemporaryHitPoints = change.To },
                RestChangeKind.Exhaustion => play with { Exhaustion = change.To },
                RestChangeKind.Resource => play.WithSpent(change.ContentId!.Value, change.ResourceId!, change.SpentAfter!.Value),
                _ => play,
            };
        }
        return play;
    }
}
