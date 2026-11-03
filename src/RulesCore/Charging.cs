namespace TomeStack.RulesCore;

/// <summary>
/// D43 slice 8: one change to one pool, for <see cref="PoolChanges.Apply"/>. <paramref name="Change"/> is the same on a normal pool
/// and a counting-up one: a <see cref="UpkeepChange.Spend"/> adds <paramref name="Amount"/> to the pool's stored spent (the built-up
/// value of a counting-up pool, the uses used of a normal one) and a <see cref="UpkeepChange.Regain"/> takes it off.
/// </summary>
public sealed record PoolChange(Guid ResourceContent, string ResourceId, UpkeepChange Change, int Amount);

/// <summary>
/// D43 slice 8: the pool arithmetic of a compound use (a roll's own spend and its <c>alsoChange</c> entries). Pure. All the changes
/// apply or none does: a use fills a charge and spends the source of it together or not at all.
/// </summary>
public static class PoolChanges
{
    /// <summary>
    /// Applies <paramref name="changes"/> in order, each on the state the one before left (two changes to one pool add up). Returns the new
    /// state and no errors, or the input <paramref name="play"/> itself and every error found. Codes, with ids and counts only:
    /// <c>use.unavailable</c> (a pool not on the sheet, untracked, or an amount below 0); <c>use.unaffordable</c> (a spend above the uses
    /// left of a normal pool, or a change taking a counting-up pool's built-up value below 0); <c>use.pool-full</c> (a spend taking a
    /// counting-up pool above its maximum). A regain on a normal pool stops at full, as every other regain does.
    /// </summary>
    public static (PlayState Play, IReadOnlyList<Diagnostic> Errors) Apply(PlayState play, CharacterSheet sheet, IReadOnlyList<PoolChange> changes)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(changes);
        var errors = new List<Diagnostic>();
        var running = play;
        foreach (var change in changes)
        {
            var pool = sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == change.ResourceContent && r.ResourceId == change.ResourceId);
            if (pool is not { Maximum: { } maximum })
            {
                errors.Add(new("use.unavailable", $"Pool '{change.ResourceId}' of content {change.ResourceContent:D} is not tracked on this character.", pool?.Content));
                continue;
            }
            if (change.Amount < 0)
            {
                errors.Add(new("use.unavailable", $"Pool '{change.ResourceId}' cannot change by {change.Amount}.", pool.Content, pool.EffectId));
                continue;
            }
            var spent = running.SpentOf(change.ResourceContent, change.ResourceId);
            int after;
            if (change.Change == UpkeepChange.Spend)
            {
                after = spent + change.Amount;
                if (pool.CountsUp && after > maximum)
                {
                    errors.Add(new("use.pool-full", $"Pool '{change.ResourceId}' has {spent} of {maximum} built up; {change.Amount} more would pass its maximum.", pool.Content, pool.EffectId));
                    continue;
                }
                if (!pool.CountsUp && change.Amount > Math.Max(maximum - spent, 0))
                {
                    errors.Add(new("use.unaffordable", $"Pool '{change.ResourceId}' has {Math.Max(maximum - spent, 0)} of {maximum} left; {change.Amount} cannot be spent.", pool.Content, pool.EffectId));
                    continue;
                }
            }
            else
            {
                after = spent - change.Amount;
                if (after < 0 && pool.CountsUp)
                {
                    errors.Add(new("use.unaffordable", $"Pool '{change.ResourceId}' has {spent} built up; {change.Amount} cannot be taken off.", pool.Content, pool.EffectId));
                    continue;
                }
                after = Math.Max(after, 0);
            }
            running = running.WithSpent(change.ResourceContent, change.ResourceId, after);
        }
        return errors.Count == 0 ? (running, errors) : (play, errors);
    }
}
