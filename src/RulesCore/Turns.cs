using System.Collections.Frozen;

namespace TomeStack.RulesCore;

/// <summary>What one proposed change of a turn is about (D43 slice 3).</summary>
public enum TurnChangeKind { Round, Duration, ToggleEnd, Spend, Regain }

/// <summary>
/// One proposed change of a turn. <paramref name="Id"/> is stable for the same character state (<c>round</c>,
/// <c>duration:{content}:{toggle}</c>, <c>end:{content}:{toggle}</c>, <c>upkeep:{content}:{effect}</c>), so the player can untick it and
/// the confirmed turn skips it. For <see cref="TurnChangeKind.Spend"/> and <see cref="TurnChangeKind.Regain"/>, <paramref name="From"/>
/// and <paramref name="To"/> are the pool's spent count before and after. <paramref name="Required"/> means it cannot be unticked: a
/// toggle that ends because it cannot pay (<c>ifShort: end</c>), or because of an earlier change or choice.
/// </summary>
public sealed record TurnChange(
    string Id,
    TurnChangeKind Kind,
    string Label,
    int From,
    int To,
    string Reason,
    TraceOrigin Origin,
    bool Required = false,
    Guid? ContentId = null,
    string? ResourceId = null,
    string? ToggleId = null);

/// <summary>What the player does about an upkeep a pool cannot cover: end the toggle, or keep it and settle the rest by hand.</summary>
public enum TurnChoiceOption { End, Keep }

/// <summary>A question the plan asks: an upkeep the pool cannot cover. <see cref="Id"/> is <c>short:{content}:{effect}</c>.</summary>
public sealed record TurnChoicePrompt(string Id, string Label, Guid ContentId, string ToggleId, int Needs, int Has);

/// <summary>A message to show the player when a counting-up pool becomes full (the pool's <c>resourceFull</c> text).</summary>
public sealed record TurnPrompt(string Id, string Text, TraceOrigin Origin);

/// <summary>What the player unticked (change ids) and chose (choice id to option). A choice that is missing means <see cref="TurnChoiceOption.End"/>.</summary>
public sealed record TurnSelection(IReadOnlySet<string> Skip, IReadOnlyDictionary<string, TurnChoiceOption> Choices)
{
    public static readonly TurnSelection None = new(FrozenSet<string>.Empty, FrozenDictionary<string, TurnChoiceOption>.Empty);
}

/// <summary>A turn's proposal, already reflecting the selection it was planned with.</summary>
public sealed record TurnPlan(
    TurnMoment At,
    IReadOnlyList<TurnChange> Changes,
    IReadOnlyList<TurnChoicePrompt> Choices,
    IReadOnlyList<Diagnostic> Manual,
    IReadOnlyList<TurnPrompt> Prompts);

/// <summary>
/// D43 slice 3: the start or end of the character's turn, planned from the calculated sheet and never applied by itself. Pure and
/// deterministic: one sequential pass over a working copy, so an earlier change (a duration running out, a toggle that cannot pay)
/// decides whether a later one exists. The same input gives the same plan.
/// </summary>
public static class TurnPlanner
{
    /// <summary>Why the character cannot take a turn now (not in an encounter), or <c>null</c>.</summary>
    public static Diagnostic? CannotTakeTurn(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.Play.Encounter is null
            ? new("turn.not-in-encounter", "A turn can only be taken during an encounter. Start an encounter first.")
            : null;
    }

    public static TurnPlan Plan(Character character, CharacterSheet sheet, TurnMoment at, TurnSelection selection)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(selection);

        var original = sheet.Toggles ?? [];
        var working = original.ToList();
        var changes = new List<TurnChange>();
        var choices = new List<TurnChoicePrompt>();
        var manual = new List<Diagnostic>();
        var prompts = new List<TurnPrompt>();
        var spent = new Dictionary<(Guid, string), int>(); // looked up only, never enumerated: the output order never depends on it
        var rules = new TraceOrigin(TraceOriginKind.RulesPolicy, character.RulesFamily);

        TraceOrigin OriginOf(ContentReference content, string effectId) =>
            new(TraceOriginKind.Content, character.RulesFamily, content, sheet.Features?.FirstOrDefault(f => f.Content == content)?.Name, effectId);

        bool IsOn(ToggleValue toggle) => working.Any(t => t.On && t.Content.ContentId == toggle.Content.ContentId && t.ToggleId == toggle.ToggleId);

        void MarkOff(ToggleValue toggle)
        {
            var index = working.FindIndex(t => t.Content.ContentId == toggle.Content.ContentId && t.ToggleId == toggle.ToggleId);
            if (index >= 0)
                working[index] = working[index] with { On = false };
        }

        void AddEnd(ToggleValue toggle, string label, string reason)
        {
            changes.Add(new(
                $"end:{toggle.Content.ContentId:D}:{toggle.ToggleId}", TurnChangeKind.ToggleEnd, label, 1, 0, reason, OriginOf(toggle.Content, toggle.EffectId),
                Required: true, ContentId: toggle.Content.ContentId, ToggleId: toggle.ToggleId));
            MarkOff(toggle);
        }

        // The toggle ends, and so do the overlays that needed it: the one rule the toggle command shares.
        void End(ToggleValue toggle, string label, string reason)
        {
            var dependents = ToggleGroups.EndingWith(toggle, working);
            AddEnd(toggle, label, reason);
            foreach (var overlay in dependents)
                AddEnd(overlay, overlay.Label, $"{overlay.Label} needs {toggle.Label}, which ends");
        }

        ResourceValue? PoolOf(Guid content, string resourceId) =>
            sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == content && r.ResourceId == resourceId);

        int SpentOf(ResourceValue pool) => spent.TryGetValue((pool.Content.ContentId, pool.ResourceId), out var value) ? value : pool.Spent;

        // ---- 1 and 2: the round, then durations -------------------------------------------------------------------------
        if (at == TurnMoment.TurnStart)
        {
            var round = character.Play.Encounter?.Round ?? 0;
            var nextRound = Math.Min(round + 1, PlayState.MaxRounds);
            changes.Add(new("round", TurnChangeKind.Round, "Round", round, nextRound, $"Round {nextRound} begins", rules));
            foreach (var toggle in original)
            {
                if (toggle.Auto == true || toggle.RoundsLeft is not { } left || !IsOn(toggle))
                    continue;
                var id = $"duration:{toggle.Content.ContentId:D}:{toggle.ToggleId}";
                if (selection.Skip.Contains(id))
                    continue;
                var next = Math.Max(left - 1, 0);
                changes.Add(new(
                    id, TurnChangeKind.Duration, toggle.Label, left, next, $"{toggle.Label}: {next} round(s) left", OriginOf(toggle.Content, toggle.EffectId),
                    ContentId: toggle.Content.ContentId, ToggleId: toggle.ToggleId));
                if (next == 0)
                    End(toggle, toggle.Label, $"{toggle.Label}: its duration ran out");
            }
        }

        // ---- upkeeps at this moment: primaries, then overlays, then ungrouped, each in sheet order ------------------------
        ToggleValue? ToggleOf(UpkeepValue u) =>
            u.ToggleId is null ? null : original.FirstOrDefault(t => t.Content.ContentId == u.ToggleContent && t.ToggleId == u.ToggleId);
        int Rank(UpkeepValue u) => ToggleOf(u) is { Group: not null } t ? (t.Layer == ToggleLayer.Overlay ? 1 : 0) : 2;
        var upkeeps = (sheet.Upkeeps ?? []).Where(u => u.At == at).Select((u, index) => (u, index)).OrderBy(x => Rank(x.u)).ThenBy(x => x.index).Select(x => x.u).ToList();

        // No toggle: always. A toggle: only while it is still on after the earlier changes (one the sheet does not list is not on).
        bool Eligible(UpkeepValue u) => u.ToggleId is null || (ToggleOf(u) is { } t && IsOn(t));
        string IdOf(UpkeepValue u) => $"upkeep:{u.Content.ContentId:D}:{u.EffectId}";
        void ByHand(UpkeepValue u, string text) => manual.Add(new("turn.settle-by-hand", $"{u.Label}: {text}, settle by hand", u.Content, u.EffectId));

        // ---- 3: spends from ordinary pools ------------------------------------------------------------------------------
        foreach (var u in upkeeps)
        {
            if (u.Change != UpkeepChange.Spend || !Eligible(u))
                continue;
            var pool = PoolOf(u.ResourceContent, u.ResourceId);
            if (pool?.CountsUp == true)
                continue; // step 5
            var toggle = ToggleOf(u) is { Auto: not true } endable ? endable : null; // a toggle the sheet switches itself cannot be ended here
            var id = IdOf(u);
            if (selection.Skip.Contains(id))
            {
                if (toggle is not null)
                    End(toggle, toggle.Label, $"{toggle.Label}: its upkeep was unticked");
                continue;
            }
            if (pool?.Maximum is not { } maximum || u.Amount is not { } amount)
            {
                ByHand(u, "this cannot be calculated");
                continue;
            }
            if (amount == 0)
                continue;
            var current = SpentOf(pool);
            var remaining = Math.Max(maximum - current, 0);
            if (amount <= remaining)
            {
                changes.Add(Spend(u, pool, id, current, current + amount, $"{pool.Label}: spend {amount}"));
                spent[(pool.Content.ContentId, pool.ResourceId)] = current + amount;
                continue;
            }
            var shortBy = amount - remaining;
            var endLabel = toggle is null ? u.Label : $"{toggle.Label} (needs {amount}, has {remaining})";
            if (toggle is not null && u.IfShort == UpkeepShortfall.End)
            {
                End(toggle, endLabel, $"{pool.Label}: needs {amount}, has {remaining}");
                continue;
            }
            if (toggle is not null)
            {
                var promptId = $"short:{u.Content.ContentId:D}:{u.EffectId}";
                choices.Add(new(promptId, $"{toggle.Label} needs {amount}, has {remaining}", toggle.Content.ContentId, toggle.ToggleId, amount, remaining));
                if (!selection.Choices.TryGetValue(promptId, out var choice) || choice == TurnChoiceOption.End)
                {
                    End(toggle, endLabel, $"{pool.Label}: needs {amount}, has {remaining}");
                    continue;
                }
            }
            // Keep (or no toggle to end): spend what is left and leave the rest to the player.
            if (remaining > 0)
            {
                changes.Add(Spend(u, pool, id, current, maximum, $"{pool.Label}: spend {remaining} of {amount}"));
                spent[(pool.Content.ContentId, pool.ResourceId)] = maximum;
            }
            manual.Add(new("turn.settle-by-hand", $"{toggle?.Label ?? u.Label}: short {shortBy} {pool.Label.ToLowerInvariant()}, settle by hand", u.Content, u.EffectId));
        }

        // ---- 4: regains -------------------------------------------------------------------------------------------------
        foreach (var u in upkeeps)
        {
            if (u.Change != UpkeepChange.Regain || !Eligible(u))
                continue;
            var id = IdOf(u);
            if (selection.Skip.Contains(id))
                continue;
            var pool = PoolOf(u.ResourceContent, u.ResourceId);
            if (pool?.Maximum is null || (!u.All && u.Amount is null))
            {
                ByHand(u, "this cannot be calculated");
                continue;
            }
            var current = SpentOf(pool);
            var after = u.All ? 0 : Math.Max(current - u.Amount!.Value, 0);
            if (after == current)
                continue;
            changes.Add(new(
                id, TurnChangeKind.Regain, u.Label, current, after, u.All ? $"{pool.Label}: regain all" : $"{pool.Label}: regain {current - after}", OriginOf(u.Content, u.EffectId),
                ContentId: pool.Content.ContentId, ResourceId: pool.ResourceId, ToggleId: u.ToggleId));
            spent[(pool.Content.ContentId, pool.ResourceId)] = after;
        }

        // ---- 5: spends into counting-up pools (never past full) -----------------------------------------------------------
        foreach (var u in upkeeps)
        {
            if (u.Change != UpkeepChange.Spend || !Eligible(u) || PoolOf(u.ResourceContent, u.ResourceId) is not { CountsUp: true } pool)
                continue;
            var id = IdOf(u);
            if (selection.Skip.Contains(id))
                continue;
            if (pool.Maximum is not { } maximum || u.Amount is not { } amount)
            {
                ByHand(u, "this cannot be calculated");
                continue;
            }
            if (amount == 0)
                continue;
            var current = SpentOf(pool);
            var after = Math.Min(current + amount, maximum);
            var reason = current >= maximum ? $"{pool.Label}: already full" : $"{pool.Label}: {current} to {after} of {maximum}";
            changes.Add(Spend(u, pool, id, current, after, reason));
            spent[(pool.Content.ContentId, pool.ResourceId)] = after;
            if (after >= maximum && maximum > 0 && !string.IsNullOrWhiteSpace(pool.FullText))
                prompts.Add(new(id, pool.FullText, OriginOf(pool.Content, pool.EffectId)));
        }

        TurnChange Spend(UpkeepValue u, ResourceValue pool, string id, int from, int to, string reason) =>
            new(id, TurnChangeKind.Spend, u.Label, from, to, reason, OriginOf(u.Content, u.EffectId),
                ContentId: pool.Content.ContentId, ResourceId: pool.ResourceId, ToggleId: u.ToggleId);

        return new(at, changes, choices, manual, prompts);
    }

    /// <summary>The play state after the plan's changes, in list order. The plan already reflects the selection, so there is no skip list.</summary>
    public static PlayState Apply(PlayState play, TurnPlan plan)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var change in plan.Changes)
        {
            switch (change.Kind)
            {
                case TurnChangeKind.Round:
                    play = play with { Encounter = new EncounterState(Math.Clamp(change.To, 0, PlayState.MaxRounds)) };
                    break;
                case TurnChangeKind.Duration:
                    play = play with
                    {
                        Toggles = [.. play.Toggles.Select(t => t.ContentId == change.ContentId && t.ToggleId == change.ToggleId ? t with { RoundsLeft = Math.Max(change.To, 0) } : t)],
                    };
                    break;
                case TurnChangeKind.ToggleEnd:
                    play = play with { Toggles = [.. play.Toggles.Where(t => !(t.ContentId == change.ContentId && t.ToggleId == change.ToggleId))] };
                    break;
                case TurnChangeKind.Spend:
                case TurnChangeKind.Regain:
                    play = play.WithSpent(change.ContentId!.Value, change.ResourceId!, Math.Max(change.To, 0));
                    break;
            }
        }
        return play;
    }
}
