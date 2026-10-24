namespace TomeStack.RulesCore;

/// <summary>How the character's defences changed typed damage (R169): immunity wins; otherwise resistance halves (rounding down), then vulnerability doubles.</summary>
public enum DamageEffect { None, Immune, Halved, Doubled, HalvedThenDoubled }

/// <summary>
/// What a <c>damage</c> play command did, for the result line (Wave 3 item 21, D19 amended).
/// <paramref name="Amount"/> is what was entered and <paramref name="Type"/> its canonical damage type (null: untyped, so no
/// defence applies). <paramref name="Adjusted"/> is the amount after defences (what the concentration check uses),
/// <paramref name="Absorbed"/> what temporary hit points took, <paramref name="Lost"/> what hit points lost.
/// <paramref name="Warnings"/> carries codes and numbers only (<c>play.massive-damage</c>).
/// </summary>
public sealed record DamageOutcome(int Amount, string? Type, DamageEffect Effect, int Adjusted, int Absorbed, int Lost, IReadOnlyList<Diagnostic> Warnings);

/// <summary>One hit: <paramref name="Type"/> is already canonical (null: untyped, so no defence applies).</summary>
public sealed record DamageEvent(int Amount, string? Type);

/// <summary>
/// The hit points after a hit, what happened (<paramref name="Outcome"/>), the Constitution save DC when a concentrating
/// character is still up (<paramref name="ConcentrationDc"/>), whether concentration ends because hit points reached 0, and
/// whether the hit was massive (the <c>play.massive-damage</c> warning is in <paramref name="Outcome"/>).
/// </summary>
public sealed record DamageResult(HitPointState After, DamageOutcome Outcome, int? ConcentrationDc, bool ConcentrationEnds, bool Massive);

/// <summary>
/// D43 S7 (D45): what choosing a response does, resolved on the sheet. Which members apply depends on <paramref name="Kind"/>: spend
/// and regain use <paramref name="ResourceContent"/>, <paramref name="ResourceId"/> and <paramref name="Amount"/>; the two hit point
/// kinds use <paramref name="Amount"/>; toggleOn uses <paramref name="Toggle"/>; offerRoll uses <paramref name="Roll"/>,
/// <paramref name="RollEffectId"/> (the roll effect's id, which the roll command takes) and <paramref name="RollId"/>. A reference
/// that named no content is already resolved to the response's own content.
/// </summary>
public sealed record ResponseOutcomeValue(
    ResponseOutcomeKind Kind, Guid? ResourceContent, string? ResourceId, int? Amount,
    ToggleRef? Toggle, ContentReference? Roll, string? RollEffectId, string? RollId);

/// <summary>
/// D43 S7 (D45): one damage response of the character, calculated. <paramref name="Amount"/>, <paramref name="Cost"/> and the outcome
/// amounts are the evaluated formulas (0 to <see cref="PlayState.MaxHitPoints"/>); <paramref name="AtOrBelow"/> is a threshold's
/// limit evaluated against the sheet's own hit point maximum (<c>HP.MAX</c>); <paramref name="Survived"/> marks a threshold that
/// follows a survival. <paramref name="CostResourceContent"/> is the content that declares the pool (resolved when the response
/// names none). Not available: <paramref name="UnavailableReason"/> says why.
/// </summary>
public sealed record ResponseValue(
    Guid ContentId, string ContentName, string ResponseId, string Label, ResponsePhase Phase,
    int? Amount, Guid? CostResourceContent, string? ResourceId, int? Cost,
    bool Available, string? UnavailableReason, string? Text,
    int? AtOrBelow = null, bool Survived = false,
    IReadOnlyList<ResponseOutcomeValue>? Outcomes = null);

/// <summary>A response the player chose for a hit.</summary>
public sealed record ResponseChoice(Guid ContentId, string ResponseId);

/// <summary>A response eligible for a hit: <paramref name="Offered"/> is false (with <paramref name="Reason"/>) when it cannot be chosen now.</summary>
public sealed record ResponseOffer(ResponseValue Response, bool Offered, string? Reason);

/// <summary>
/// D43 S7 (D45): one damage event planned with the chosen responses (<see cref="DamagePipeline.Plan"/>). <paramref name="Result"/>
/// is the hit; <paramref name="Reduction"/> the damage the chosen reductions took off; <paramref name="Final"/> the hit points
/// once a chosen survival is applied; <paramref name="Offers"/> the eligible responses in sheet order; <paramref name="Chosen"/> the
/// choices as given; <paramref name="Outcomes"/> what the chosen responses do (empty with any note); <paramref name="Notes"/> the
/// errors that make the choices unusable.
/// </summary>
public sealed record DamagePlan(
    DamageResult Result, int Reduction, HitPointState Final,
    IReadOnlyList<ResponseOffer> Offers, IReadOnlyList<ResponseChoice> Chosen,
    IReadOnlyList<ResponseOutcomeValue> Outcomes, IReadOnlyList<Diagnostic> Notes);

/// <summary>
/// D43 S7 (D45): a roll a response offered after a hit. It rolls nothing; the player rolls it with the roll command
/// (<paramref name="Content"/> and <paramref name="EffectId"/> name the roll effect). <paramref name="Label"/> is the roll's label as the sheet shows it.
/// </summary>
public sealed record OfferedRoll(ContentReference Content, string EffectId, string RollId, string Label);

public static class DamagePipeline
{
    /// <summary>
    /// Wave 3 item 21 (D19 amended, R169). The order: immunity gives 0, otherwise resistance halves (rounding down) and then
    /// vulnerability doubles, each kind counting once however many sources give it; then temporary hit points absorb; then hit
    /// points. Untyped damage ignores every defence. Damage that is 0 after defences changes nothing and asks for no
    /// concentration check.
    /// </summary>
    public static DamageResult Resolve(DamageEvent damage, HitPointState hp, IReadOnlyList<DamageDefence> defences, bool concentrating, RulesFamilyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(damage);
        ArgumentNullException.ThrowIfNull(hp);
        ArgumentNullException.ThrowIfNull(defences);
        ArgumentNullException.ThrowIfNull(policy);
        var type = damage.Type;
        var kinds = type is null
            ? new HashSet<DamageDefenceKind>()
            : defences.Where(d => d.Type == type).Select(d => d.Kind).ToHashSet();
        var halved = kinds.Contains(DamageDefenceKind.Resistance);
        var doubled = kinds.Contains(DamageDefenceKind.Vulnerability);
        var effect = kinds.Contains(DamageDefenceKind.Immunity) ? DamageEffect.Immune
            : halved && doubled ? DamageEffect.HalvedThenDoubled
            : halved ? DamageEffect.Halved
            : doubled ? DamageEffect.Doubled
            : DamageEffect.None;
        var amount = effect switch
        {
            DamageEffect.Immune => 0,
            DamageEffect.Halved => damage.Amount / 2,
            DamageEffect.Doubled => damage.Amount * 2,
            DamageEffect.HalvedThenDoubled => damage.Amount / 2 * 2,
            _ => damage.Amount,
        };
        if (amount == 0)
            return new(hp, new(damage.Amount, type, effect, 0, 0, 0, []), null, false, false);

        var absorbed = Math.Min(hp.Temporary, amount);
        var current = Math.Max(hp.Current - (amount - absorbed), 0);
        // Massive damage is a warning, never an automatic death: the damage left over once hit points reach 0 equals or exceeds the maximum.
        var leftOver = amount - absorbed - (hp.Current - current);
        var massive = hp.Maximum >= 1 && leftOver >= hp.Maximum;
        var warnings = massive
            ? new List<Diagnostic> { new("play.massive-damage", $"{leftOver} damage was left over after reaching 0 hit points, which equals or exceeds the hit point maximum of {hp.Maximum}.") }
            : [];
        var outcome = new DamageOutcome(damage.Amount, type, effect, amount, absorbed, hp.Current - current, warnings);
        // D19 (SRD 5.1 p. 101, SRD 5.2.1 p. 11), amended: damage while concentrating asks for a Constitution save, DC 10 or half the
        // damage after resistance, immunity and vulnerability (before temporary hit points), whichever is higher; at 0 hit points the
        // spell simply ends. The family may cap the DC (SRD 5.2.1: 30); every DC stays within what PlayState validation accepts, so a
        // huge hit is still applied.
        var ends = concentrating && current == 0;
        int? dc = concentrating && current != 0 ? ConcentrationDc(amount, policy) : null;
        return new(new(hp.Maximum, current, hp.Temporary - absorbed), outcome, dc, ends, massive);
    }

    /// <summary>The Constitution save DC for <paramref name="adjusted"/> damage (after defences, before temporary hit points): 10 or half, whichever is higher, within the family cap and what play state accepts.</summary>
    private static int ConcentrationDc(int adjusted, RulesFamilyPolicy policy) =>
        Math.Min(Math.Max(10, adjusted / 2), Math.Min(policy.ConcentrationSaveMaximumDc ?? int.MaxValue, PlayState.MaxConcentrationSaveDc));

    /// <summary>
    /// D45 (amends D19): one damage event with the player's chosen responses; it writes nothing. The order:
    /// <list type="number">
    /// <item>the chosen damage reductions come off the amount;</item>
    /// <item>defences, the concentration DC and temporary hit points then hit points are <see cref="Resolve"/>, unchanged;</item>
    /// <item>if the hit drops a character who was up to 0, the hit point outcome responses are eligible and one chosen leaves
    /// <see cref="DamagePlan.Final"/> at its amount (1 to the maximum): concentration is kept and its DC still applies
    /// (<see cref="DamagePlan.Result"/> is amended to say so: <see cref="DamageResult.ConcentrationEnds"/> false and
    /// <see cref="DamageResult.ConcentrationDc"/> set; its <see cref="DamageResult.After"/> stays the state the damage alone left);</item>
    /// <item>if the final hit points are lower than before, the threshold responses are eligible: <c>atOrBelow</c> against the final
    /// hit points (its value was calculated against the maximum before the event), <c>survived</c> after a chosen survival;</item>
    /// <item>the chosen responses' outcomes, in sheet order. Hit point and temporary hit point outcomes are not applied here, so
    /// they cannot change which thresholds were offered.</item>
    /// </list>
    /// A chosen response that is not on the sheet, not eligible for this hit, not available or listed twice is a
    /// <c>damage.response-unavailable</c> note; more than one hit point outcome (a survival, a heal or temporary hit points) is a
    /// <c>damage.one-outcome</c> note. With any note, <see cref="DamagePlan.Outcomes"/> is empty.
    /// </summary>
    public static DamagePlan Plan(DamageEvent damage, Character character, CharacterSheet sheet, IReadOnlyList<ResponseChoice> chosen, RulesFamilyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(damage);
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(policy);
        chosen ??= [];
        var hp = sheet.HitPoints ?? new HitPointState(0, 0, 0);
        var defences = sheet.DamageDefences?.Select(d => new DamageDefence(d.Type, d.Kind)).ToList() ?? [];
        var responses = sheet.Responses ?? [];
        var notes = new List<Diagnostic>();
        // One note per response, however many reasons it has.
        var noted = new HashSet<(Guid, string)>();
        void Refuse(Guid contentId, string responseId)
        {
            if (noted.Add((contentId, responseId)))
                notes.Add(Unavailable(contentId, responseId));
        }

        // The choices: unknown and repeated ones are notes; the rest are the picked responses (counted once each).
        var picked = new HashSet<(Guid, string)>();
        foreach (var choice in chosen)
        {
            var key = (choice.ContentId, choice.ResponseId);
            if (!responses.Any(r => (r.ContentId, r.ResponseId) == key) || !picked.Add(key))
                Refuse(choice.ContentId, choice.ResponseId);
        }
        var pickedResponses = responses.Where(r => picked.Contains((r.ContentId, r.ResponseId))).ToList();
        var usable = pickedResponses.Where(r => r.Available).ToList();

        // The chosen costs together must fit each pool. Eligibility depends on the hit points, which depend on the reductions and
        // the survival, so the pass runs in phases, each in sheet order: reductions, then survivals, then the eligible thresholds.
        // A response that no longer fits is refused, and nothing below uses it; one that is not eligible takes nothing from a pool.
        var committed = new Dictionary<(Guid?, string), int>();
        var fitted = new HashSet<ResponseValue>();
        bool Fit(ResponseValue response)
        {
            if (response.Cost is { } cost && response.ResourceId is { } poolId)
            {
                var key = (response.CostResourceContent, poolId);
                var pool = sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == response.CostResourceContent && r.ResourceId == poolId);
                var total = committed.GetValueOrDefault(key) + cost;
                if (pool?.Current is not { } left || total > left)
                {
                    Refuse(response.ContentId, response.ResponseId);
                    return false;
                }
                committed[key] = total;
            }
            fitted.Add(response);
            return true;
        }

        // 1. Reductions: every damage reduction is eligible.
        foreach (var response in usable.Where(r => r.Phase == ResponsePhase.DamageReduction))
            Fit(response);
        var reduction = fitted.Where(r => r.Phase == ResponsePhase.DamageReduction).Sum(r => r.Amount ?? 0);

        // 2 to 4. Defences, the concentration DC, temporary hit points, hit points: one implementation.
        var result = Resolve(new(Math.Max(damage.Amount - reduction, 0), damage.Type), hp, defences, character.Play.Concentration is not null, policy);

        // 5. A hit that would drop a character who was up to 0: the survival responses are eligible; one chosen leaves them standing.
        var drops = hp.Current > 0 && result.After.Current == 0;
        if (drops)
        {
            foreach (var response in usable.Where(r => r.Phase == ResponsePhase.HitPointOutcome))
                Fit(response);
        }
        var survival = drops ? usable.FirstOrDefault(r => r.Phase == ResponsePhase.HitPointOutcome && fitted.Contains(r)) : null;
        var final = result.After;
        if (survival is not null)
        {
            final = result.After with { Current = Math.Min(Math.Max(survival.Amount ?? 1, 1), Math.Max(hp.Maximum, 1)) };
            // The result describes the hit as it ends: the survival's hit points, the loss counted from them, concentration kept with its DC.
            result = result with
            {
                After = final,
                Outcome = result.Outcome with { Lost = Math.Max(hp.Current - final.Current, 0) },
                ConcentrationEnds = false,
                ConcentrationDc = character.Play.Concentration is not null ? ConcentrationDc(result.Outcome.Adjusted, policy) : null,
            };
        }

        // 6. The thresholds, on the final hit points.
        var dropped = final.Current < hp.Current;
        bool Eligible(ResponseValue r) => r.Phase switch
        {
            ResponsePhase.DamageReduction => true,
            ResponsePhase.HitPointOutcome => drops,
            // A survival means the hit did take the hit points to 0, even when the survival leaves as many as before.
            _ => (dropped && r.AtOrBelow is { } limit && final.Current <= limit) || (r.Survived && survival is not null),
        };
        var eligible = responses.Where(Eligible).ToList();
        var offers = eligible.Select(r => new ResponseOffer(r, r.Available, r.Available ? null : r.UnavailableReason)).ToList();

        // The eligible chosen thresholds are cost-fitted against what the reductions and the survival left.
        foreach (var response in usable.Where(r => r.Phase == ResponsePhase.Threshold && eligible.Contains(r)))
            Fit(response);
        foreach (var response in pickedResponses)
        {
            if (!eligible.Contains(response) || !response.Available)
                Refuse(response.ContentId, response.ResponseId);
        }
        var applied = responses.Where(r => fitted.Contains(r) && eligible.Contains(r)).ToList();
        // Counted over everything chosen, available and eligible (a response refused for its cost still counts).
        if (usable.Count(r => eligible.Contains(r) && IsHitPointOutcome(r)) > 1)
            notes.Add(new("damage.one-outcome", "At most one hit point outcome (a survival, a heal or temporary hit points) can be chosen for one hit."));

        // 7. The outcomes, in sheet order. Hit point and temporary hit point outcomes are applied by the caller.
        IReadOnlyList<ResponseOutcomeValue> outcomes = notes.Count > 0 ? [] : [.. applied.SelectMany(r => r.Outcomes ?? [])];
        return new(result, reduction, final, offers, [.. chosen], outcomes, notes);
    }

    /// <summary>
    /// D43 S7 (D45): writes a <see cref="Plan"/> to the play state, and throws nothing. All or nothing: with any error (a plan that has
    /// notes, a chosen response that cannot be paid for or switched on now, or a result <see cref="PlayState.Validate"/> refuses) the
    /// input <paramref name="play"/> comes back unchanged, with no rolls and the errors. The order:
    /// <list type="number">
    /// <item>the hit points the plan ended with (temporary hit points too, current stored as null at the maximum), unless the hit did nothing;</item>
    /// <item>concentration: ended, or kept with the DC the plan set, as plain damage writes it;</item>
    /// <item>each chosen response in sheet order pays its cost from its pool first (<c>damage.response-unavailable</c> when the pool
    /// cannot pay now), then runs its outcomes in order: spend and regain (clamped to 0 and the maximum; an untracked pool is left
    /// as it is), toggleOn (the S1 switch, with no cost and no refund), offerRoll (listed, never rolled), regainHitPoints (up to the
    /// maximum; leaving 0 resets death saves) and temporaryHitPoints (keeps the higher).</item>
    /// </list>
    /// Errors name content and response ids, never user text.
    /// </summary>
    public static (PlayState Play, IReadOnlyList<OfferedRoll> Rolls, IReadOnlyList<Diagnostic> Errors) Apply(PlayState play, DamagePlan plan, CharacterSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sheet);
        if (plan.Notes.Count > 0)
            return (play, [], plan.Notes);

        var errors = new List<Diagnostic>();
        var rolls = new List<OfferedRoll>();
        var maximum = sheet.HitPoints?.Maximum ?? plan.Final.Maximum;
        var result = play;
        var responses = sheet.Responses ?? [];
        var toggles = (sheet.Toggles ?? []).ToList();

        // 1 and 2. The hit, as plain damage writes it: nothing for a hit that was 0 after defences and reductions.
        if (plan.Result.Outcome.Adjusted != 0)
        {
            var concentration = plan.Result.ConcentrationEnds ? null
                : plan.Result.ConcentrationDc is { } dc && result.Concentration is { } held ? held with { PendingSaveDc = dc }
                : result.Concentration;
            result = result.WithHitPoints(plan.Final.Current, plan.Final.Temporary, maximum) with { Concentration = concentration };
        }

        // 3 and 4. The chosen responses, in sheet order. Every choice must be on the sheet and offered by the plan.
        var chosenKeys = plan.Chosen.Select(c => (c.ContentId, c.ResponseId)).ToHashSet();
        foreach (var (contentId, responseId) in chosenKeys)
        {
            if (!responses.Any(r => r.ContentId == contentId && r.ResponseId == responseId)
                || !plan.Offers.Any(o => o.Offered && o.Response.ContentId == contentId && o.Response.ResponseId == responseId))
                errors.Add(Unavailable(contentId, responseId));
        }

        ResourceValue? Pool(Guid? contentId, string? resourceId) =>
            contentId is null || resourceId is null ? null : sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == contentId && r.ResourceId == resourceId);

        bool Pay(ResponseValue response)
        {
            if (response.Cost is not { } cost || response.ResourceId is not { } poolId || cost <= 0)
                return true;
            if (Pool(response.CostResourceContent, poolId) is not { Maximum: { } poolMaximum } || response.CostResourceContent is not { } poolContent)
                return false;
            var spent = result.SpentOf(poolContent, poolId);
            if (Math.Max(poolMaximum - spent, 0) < cost)
                return false;
            result = result.WithSpent(poolContent, poolId, spent + cost);
            return true;
        }

        bool Run(ResponseOutcomeValue outcome)
        {
            switch (outcome.Kind)
            {
                case ResponseOutcomeKind.Spend or ResponseOutcomeKind.Regain:
                    if (outcome.Amount is not { } poolAmount || Pool(outcome.ResourceContent, outcome.ResourceId) is not { } pool)
                        return false;
                    if (pool.Maximum is not { } poolMaximum)
                        return true; // not tracked: left as it is
                    var spent = result.SpentOf(pool.Content.ContentId, pool.ResourceId);
                    result = result.WithSpent(
                        pool.Content.ContentId, pool.ResourceId,
                        outcome.Kind == ResponseOutcomeKind.Spend ? Math.Min(poolMaximum, spent + poolAmount) : Math.Max(0, spent - poolAmount));
                    return true;
                case ResponseOutcomeKind.ToggleOn:
                    return outcome.Toggle is { ContentId: { } toggleContent } reference && SwitchOn(toggleContent, reference.ToggleId);
                case ResponseOutcomeKind.OfferRoll:
                    if (outcome.Roll is not { } roll || outcome.RollEffectId is not { } effectId || outcome.RollId is not { } rollId)
                        return false;
                    var label = sheet.Features?.FirstOrDefault(f => f.Content == roll)?.Effects.FirstOrDefault(e => e.Id == effectId)?.Label;
                    rolls.Add(new(roll, effectId, rollId, label ?? rollId));
                    return true;
                case ResponseOutcomeKind.RegainHitPoints:
                    if (outcome.Amount is not { } heal)
                        return false;
                    var before = result.CurrentHitPoints ?? maximum;
                    var after = Math.Max(before, Math.Min(maximum, before + heal));
                    result = result.WithCurrentHitPoints(after, maximum);
                    // SRD 5.1 p. 98, SRD 5.2.1 p. 17: regaining any hit points resets death saving throws.
                    if ((play.CurrentHitPoints ?? maximum) == 0 && after > 0)
                        result = result with { DeathSaves = new() };
                    return true;
                case ResponseOutcomeKind.TemporaryHitPoints:
                    if (outcome.Amount is not { } temporary)
                        return false;
                    result = result with { TemporaryHitPoints = Math.Max(result.TemporaryHitPoints, temporary) };
                    return true;
                default:
                    return false;
            }
        }

        // The S1 switch with no cost and no refund: the occupants of the group go off, the overlays that needed them end.
        bool SwitchOn(Guid contentId, string toggleId)
        {
            var toggle = toggles.FirstOrDefault(t => t.Content.ContentId == contentId && t.ToggleId == toggleId);
            if (toggle is null || toggle.Auto == true)
                return false;
            if (toggle.On)
                return true;
            if (!ToggleGroups.OverlayAllowedNow(toggle, toggles))
                return false;
            var gone = new HashSet<(Guid, string)>();
            if (toggle is { Group: not null, Layer: not ToggleLayer.Overlay })
            {
                var groupPresent = sheet.Features is null || sheet.Features.Any(f => f.Content.ContentId == toggle.GroupContent);
                foreach (var occupant in ToggleGroups.Occupants(toggle, toggles, groupPresent))
                    gone.Add((occupant.Content.ContentId, occupant.ToggleId));
                var stored = result.Toggles.Select(s => (s.ContentId, s.ToggleId)).ToHashSet();
                foreach (var overlay in ToggleGroups.OverlaysEndedBySwitchingOn(toggle, toggles, stored))
                    gone.Add((overlay.Content.ContentId, overlay.ToggleId));
            }
            result = result with
            {
                Toggles =
                [
                    .. result.Toggles.Where(t => !gone.Contains((t.ContentId, t.ToggleId)) && !(t.ContentId == contentId && t.ToggleId == toggleId)),
                    new ActiveToggle(contentId, toggleId, result.Encounter is null ? null : toggle.Duration),
                ],
            };
            toggles = [.. toggles.Select(t =>
                gone.Contains((t.Content.ContentId, t.ToggleId)) ? t with { On = false }
                : t.Content.ContentId == contentId && t.ToggleId == toggleId ? t with { On = true }
                : t)];
            return true;
        }

        foreach (var response in responses.Where(r => chosenKeys.Contains((r.ContentId, r.ResponseId))))
        {
            if (!Pay(response) || !(response.Outcomes ?? []).All(Run))
                errors.Add(Unavailable(response.ContentId, response.ResponseId));
        }

        // 5. The result must be a state the play state accepts.
        if (errors.Count == 0)
            errors.AddRange(result.Validate());
        return errors.Count > 0 ? (play, [], errors) : (result, rolls, []);
    }

    private static bool IsHitPointOutcome(ResponseValue response) =>
        response.Phase == ResponsePhase.HitPointOutcome
        || (response.Outcomes ?? []).Any(o => o.Kind is ResponseOutcomeKind.RegainHitPoints or ResponseOutcomeKind.TemporaryHitPoints);

    private static Diagnostic Unavailable(Guid contentId, string responseId) =>
        new("damage.response-unavailable", $"Response '{responseId}' of content {contentId} cannot be used for this hit.");
}
