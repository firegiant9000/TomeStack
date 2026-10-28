using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>What a <see cref="PlayCommand"/> changes in the play state (SPEC C-05).</summary>
public enum PlayActionKind
{
    /// <summary>Spend <c>amount</c> uses of a resource (at most what is left).</summary>
    Spend,

    /// <summary>Regain <c>amount</c> spent uses of a resource by hand (at most what is spent).</summary>
    Regain,

    /// <summary>Take damage: temporary hit points absorb it first, and hit points do not go below 0.</summary>
    Damage,

    /// <summary>Regain hit points, up to the maximum.</summary>
    Heal,

    /// <summary>Temporary hit points become <c>amount</c> (they do not stack; the player chooses which to keep).</summary>
    SetTemporaryHitPoints,

    /// <summary>Current hit points become <c>amount</c> (0 to the maximum).</summary>
    SetHitPoints,

    AddCondition,
    RemoveCondition,

    /// <summary>The exhaustion level becomes <c>amount</c> (0–6).</summary>
    SetExhaustion,

    /// <summary>
    /// Records one death saving throw whose d20 showed <c>amount</c> (1–20), with the SRD outcome
    /// (<see cref="DeathSaves.Outcome"/>). Only at 0 hit points. A natural 20 sets hit points to 1 and resets the saves.
    /// </summary>
    RecordDeathSave,

    /// <summary>Adds <c>amount</c> (1–3) death saving throw failures, for example damage at 0 hit points (2 on a critical hit).</summary>
    AddDeathSaveFailure,

    /// <summary>Resets death saving throw successes and failures to 0.</summary>
    ClearDeathSaves,

    /// <summary>Inspiration (2014) or Heroic Inspiration (2024): <c>amount</c> 1 gives it, 0 spends or removes it.</summary>
    SetInspiration,

    /// <summary>Spends one spell slot of spell level <c>amount</c> (1–9), for example to cast a spell at that level (D04).</summary>
    SpendSlot,

    /// <summary>Regains one spent spell slot of spell level <c>amount</c> by hand.</summary>
    RegainSlot,

    /// <summary>Spends one Pact Magic slot.</summary>
    SpendPactSlot,

    /// <summary>Regains one spent Pact Magic slot by hand.</summary>
    RegainPactSlot,

    /// <summary>Content v6 (M3 B2): switches a toggle on (<c>contentId</c>, <c>toggleId</c>); if it names a resource, one use is spent in the same change.</summary>
    ToggleOn,

    /// <summary>Switches a toggle off.</summary>
    ToggleOff,

    /// <summary>Character schema v8 (D19): starts concentrating on the spell <c>contentId</c>, which must be a concentration spell in one of the character's spell lists.</summary>
    StartConcentration,

    /// <summary>Ends concentration (the spell ends).</summary>
    EndConcentration,

    /// <summary>The Constitution saving throw after damage was made and kept: clears the pending DC.</summary>
    ClearConcentrationCheck,

    /// <summary>Wave 3 item 25 (R160): spends <c>amount</c> charges of one equipment entry (<c>entryId</c>), written to the entry's <c>chargesSpent</c>.</summary>
    SpendCharge,

    /// <summary>Regains <c>amount</c> spent charges of one equipment entry by hand.</summary>
    RegainCharge,

    /// <summary>Wave 3 item 23 (D39, R174): a companion (<c>companionId</c>) takes <c>amount</c> damage, untyped: temporary hit points first, then hit points, at least 0.</summary>
    CompanionDamage,

    /// <summary>A companion regains <c>amount</c> hit points, up to its maximum; temporary hit points are not touched.</summary>
    CompanionHeal,

    /// <summary>A companion's current hit points become <c>amount</c> (0 to its maximum), and its temporary hit points <c>temporary</c> when that is sent.</summary>
    CompanionSetHitPoints,

    /// <summary>A companion's temporary hit points become <c>amount</c> (0 to the maximum allowed); its current hit points stay as stored, so a damage that landed first is kept.</summary>
    CompanionSetTemporaryHitPoints,

    /// <summary>D43 slice 3: starts an encounter at round 0 (turns are taken in an encounter). Refused <c>encounter.already-started</c> while one is running. Compound: needs the stamp, and sets the undo slot.</summary>
    StartEncounter,

    /// <summary>D43 slice 3: ends the encounter and clears every toggle's rounds left (the reply names the toggles that lost a duration). Refused <c>encounter.not-started</c> outside one. Compound, like <see cref="StartEncounter"/>.</summary>
    EndEncounter,

    /// <summary>D43 slice 8: uses one roll (<c>contentId</c> and <c>rollId</c>): its own spend (or draw-down, for a <c>drawsDown</c> roll; <c>amount</c> for a per-unit roll) and every <c>alsoChange</c> entry, all applied or none. Refused <c>use.not-found</c>, <c>use.unavailable</c>, <c>use.unaffordable</c> or <c>use.pool-full</c>. Compound; the undo label is "use {roll label}".</summary>
    UseAction,
}

/// <param name="Confirm">Must be <c>true</c>: play state changes only by an explicit user action, never as a side effect.</param>
/// <param name="Replace">With <c>toggleOn</c> of a primary form: switch off the primary of the same group that is on instead of refusing (D43).</param>
/// <param name="ExpectedUpdatedAt">The <c>Character.UpdatedAt</c> of the copy the page acted on (D43 S2). When sent and not the stored one, the command is refused with <c>character.stale</c> and nothing is written, as <see cref="TomeStackApp.SaveCharacter"/> does. Compound changes require it.</param>
/// <param name="Responses">D43 S7, with <c>damage</c>: the damage responses chosen for the hit (<c>character.damagePreview</c> lists them). A non-empty list makes the change compound: it needs <paramref name="ExpectedUpdatedAt"/> and is one undo step.</param>
/// <param name="RollId">D43 S8, with <c>useAction</c> and <paramref name="ContentId"/>: the roll (its <c>rollId</c>) to use. MECH-X item 1 (R36): else the feature attack with this effect id (a paying attack); a revision where both match is ambiguous and refused <c>use.not-found</c>.</param>
/// <param name="Clear">D43 S8, with <c>endEncounter</c> only: counting-up pools whose built-up value goes to 0 in the same change, as <c>"{contentId:D}/{resourceId}"</c> (at most 64). Refused <c>play.clear-invalid</c> with any other action or any entry that is not a counting-up pool of the character.</param>
/// <param name="ContentId">With <paramref name="ResourceId"/>, the resource to spend or regain (the content that defines it).</param>
public sealed record PlayCommand(
    Guid CharacterId,
    PlayActionKind Action,
    bool Confirm = false,
    int Amount = 1,
    Guid? ContentId = null,
    string? ResourceId = null,
    string? Condition = null,
    string? ToggleId = null,
    string? DamageType = null,
    Guid? EntryId = null,
    int? EntryIndex = null,
    Guid? CompanionId = null,
    int? Temporary = null,
    bool Replace = false,
    DateTimeOffset? ExpectedUpdatedAt = null,
    IReadOnlyList<ResponseChoice>? Responses = null,
    string? RollId = null,
    IReadOnlyList<string>? Clear = null);

/// <summary>
/// <c>character.undoPlay</c> (D43 S2): undoes the last compound play change. <paramref name="UpdatedAt"/> is the stamp of the copy the
/// page holds; it must be the stored one (<c>character.stale</c> otherwise).
/// </summary>
public sealed record UndoPlayRequest(Guid CharacterId, DateTimeOffset UpdatedAt, bool Confirm = false);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// <c>character.play</c> (M2 item 2): one confirmed change to the play state, then the recalculated view. Refused
    /// without <c>confirm</c> (<c>play.confirmation-required</c>), and nothing changes when it is refused. Rolling never
    /// calls this; the UI offers "Spend" as its own button after a roll that names a resource.
    /// </summary>
    public CharacterView Play(PlayCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.Confirm)
            throw new AppValidationException([new("play.confirmation-required", "Changing hit points, resources or conditions needs an explicit confirmation.")]);
        var character = _store.FindCharacter(command.CharacterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {command.CharacterId} does not exist.")]);
        // D43 S2: the page's stamp is the one concurrency token every character write already uses (SaveCharacter compares the same
        // value). A stale one is refused before any action runs, so every kind of play command is covered and nothing is written.
        // A compound change (several writes decided from one view) cannot run on a guess: it must name the copy it was decided on.
        if (CompoundUndo(command, null).Compound && command.ExpectedUpdatedAt is null)
            throw new AppValidationException([new("play.basis-required", "This change needs the character's current state. Reload it and try again.")], "play.basis-required");
        if (command.ExpectedUpdatedAt is { } expected && expected != character.UpdatedAt)
            throw new AppValidationException([new("character.stale", StaleMessage)], "character.stale");
        if (command.Amount is < 0 or > PlayState.MaxHitPoints)
            throw new AppValidationException([new("play.amount-out-of-range", $"The amount must be between 0 and {PlayState.MaxHitPoints}.")]);
        // D43 S8: a list of pools to clear belongs to the end of an encounter only; anywhere else it is refused, never ignored.
        if (command.Clear is { Count: > 0 } && command.Action != PlayActionKind.EndEncounter)
            throw new AppValidationException([new("play.clear-invalid", "Pools can be cleared only when an encounter ends.")], "play.clear-invalid");

        if (command.Action is PlayActionKind.CompanionDamage or PlayActionKind.CompanionHeal or PlayActionKind.CompanionSetHitPoints or PlayActionKind.CompanionSetTemporaryHitPoints)
            return SaveWithPlay(character with { Play = ChangeCompanion(character, command) }); // a companion's hit points are not the character's: no sheet needed
        var sheet = CharacterCalculator.Calculate(character, _store);
        // MECH-X item 1: a toggle that pays is compound only once the sheet shows its payment; the stale check above already ran and nothing is written yet.
        if (command.ExpectedUpdatedAt is null && CompoundUndo(command, sheet).Compound)
            throw new AppValidationException([new("play.basis-required", "This change needs the character's current state. Reload it and try again.")], "play.basis-required");
        if (command.Action is PlayActionKind.SpendCharge or PlayActionKind.RegainCharge)
            return SaveWithPlay(ChangeCharges(character, sheet, command)); // charges live on the equipment entry, not in the play state
        var play = character.Play;
        var hp = sheet.HitPoints!;
        DamageOutcome? damage = null;
        IReadOnlyList<Diagnostic>? notices = null;
        string? useLabel = null;

        play = command.Action switch
        {
            PlayActionKind.Spend or PlayActionKind.Regain => ChangeResource(play, sheet, command),
            PlayActionKind.Damage when command.Responses is { Count: > 0 } => DamageWithResponses(character, play, sheet, command, out damage, out notices),
            PlayActionKind.Damage => Damage(play, sheet, command, RulesFamilies.Get(character.RulesFamily), out damage),
            PlayActionKind.Heal => play.WithCurrentHitPoints(hp.Current + command.Amount, hp.Maximum),
            PlayActionKind.SetTemporaryHitPoints => play with { TemporaryHitPoints = command.Amount },
            PlayActionKind.SetHitPoints => command.Amount <= hp.Maximum
                ? play.WithCurrentHitPoints(command.Amount, hp.Maximum)
                : throw new AppValidationException([new("play.hit-points-above-maximum", $"Current hit points cannot be above the maximum ({hp.Maximum}).")]),
            PlayActionKind.AddCondition => play.Conditions.Contains(Condition(command))
                ? play
                : play with { Conditions = [.. play.Conditions, Condition(command)] },
            PlayActionKind.RemoveCondition => play with { Conditions = [.. play.Conditions.Where(c => c != Condition(command))] },
            PlayActionKind.SetExhaustion => command.Amount <= PlayState.MaxExhaustion
                ? play with { Exhaustion = command.Amount }
                : throw new AppValidationException([new("play.exhaustion-out-of-range", $"Exhaustion level {command.Amount} must be between 0 and {PlayState.MaxExhaustion}.")]),
            PlayActionKind.RecordDeathSave => RecordDeathSave(play, hp, command.Amount),
            PlayActionKind.AddDeathSaveFailure => command.Amount is >= 1 and <= DeathSaves.Maximum
                ? play with { DeathSaves = play.DeathSaves.After(new(0, command.Amount, false, "")) }
                : throw new AppValidationException([new("play.amount-out-of-range", $"Add 1 to {DeathSaves.Maximum} death saving throw failures.")]),
            PlayActionKind.ClearDeathSaves => play with { DeathSaves = new() },
            PlayActionKind.SetInspiration => command.Amount is 0 or 1
                ? play with { Inspiration = command.Amount == 1 }
                : throw new AppValidationException([new("play.amount-out-of-range", "Inspiration is 1 (have it) or 0 (do not).")]),
            PlayActionKind.SpendSlot or PlayActionKind.RegainSlot => ChangeSlot(play, sheet, command),
            PlayActionKind.SpendPactSlot => sheet.PactSlots is { Remaining: > 0 }
                ? play with { PactSlotsSpent = play.PactSlotsSpent + 1 }
                : throw new AppValidationException([new("slots.none-left", "No Pact Magic slots are left.")]),
            PlayActionKind.RegainPactSlot => play.PactSlotsSpent > 0
                ? play with { PactSlotsSpent = play.PactSlotsSpent - 1 }
                : throw new AppValidationException([new("slots.nothing-spent", "No Pact Magic slots are spent.")]),
            PlayActionKind.ToggleOn or PlayActionKind.ToggleOff => ChangeToggle(character, play, sheet, command),
            PlayActionKind.StartEncounter => play.Encounter is null
                ? StartEncounter(play, sheet)
                : throw new AppValidationException([new("encounter.already-started", "An encounter is already running.")]),
            PlayActionKind.EndEncounter => play.Encounter is null
                ? throw new AppValidationException([new("encounter.not-started", "No encounter is running.")])
                : EndEncounter(play, sheet, command.Clear, out notices),
            PlayActionKind.UseAction => UseAction(play, sheet, command, out notices, out useLabel),
            PlayActionKind.StartConcentration => StartConcentration(play, sheet, command),
            PlayActionKind.EndConcentration => play.Concentration is null
                ? throw new AppValidationException([new("play.not-concentrating", "The character is not concentrating on a spell.")])
                : play with { Concentration = null },
            PlayActionKind.ClearConcentrationCheck => play.Concentration is { } con
                ? play with { Concentration = con with { PendingSaveDc = null } }
                : throw new AppValidationException([new("play.not-concentrating", "The character is not concentrating on a spell.")]),
            _ => throw new AppValidationException([new("play.action-unknown", $"Unknown play action '{command.Action}'.")]),
        };
        if (command.Action == PlayActionKind.SetHitPoints && command.Amount == 0)
            play = play with { Concentration = null }; // concentration ends at 0 hit points
        // SRD 5.1 p. 98, SRD 5.2.1 p. 17: regaining any hit points resets death saving throws.
        if (hp.Current == 0 && (play.CurrentHitPoints ?? hp.Maximum) > 0 && command.Action is PlayActionKind.Heal or PlayActionKind.SetHitPoints)
            play = play with { DeathSaves = new() };
        // D43 S2: a compound change keeps the play state it started from, for one undo (the toggle exists: ChangeToggle checked it).
        // A damage with responses is compound (decided in CompoundUndo), but its label needs the outcome, which only exists here.
        var (compound, compoundLabel) = CompoundUndo(command, sheet);
        var undoLabel = compoundLabel ?? useLabel ?? (compound && command.Action == PlayActionKind.Damage && damage is not null ? DamageLabel(command, damage) : null);
        (PlayState, string)? undo = undoLabel is { } label ? (character.Play, label) : null;
        if (command.Action == PlayActionKind.Spend)
            notices = ResourceFullNotice(sheet, command);
        else if (command.Action == PlayActionKind.Damage && !compound)
            notices = SkippedResponses(character, sheet, command); // R12: plain damage names the responses it passed over
        var view = SaveWithPlay(character with { Play = play }, undo);
        if (notices is not null)
            view = view with { Notices = notices };
        return damage is null ? view : view with { Damage = damage };
    }

    /// <summary>
    /// D43 S3: a spend that fills a counting-up pool whose content gave a <c>resourceFull</c> text answers with that text (reply-only,
    /// code <c>resource.full</c>). Content-authored text only. Called after the spend was accepted, on the sheet it was decided on.
    /// </summary>
    private static IReadOnlyList<Diagnostic>? ResourceFullNotice(CharacterSheet sheet, PlayCommand command)
    {
        var pool = sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == command.ContentId && r.ResourceId == command.ResourceId);
        return FullNotice(pool, (pool?.Spent ?? 0) + command.Amount) is { } notice ? [notice] : null;
    }

    /// <summary>
    /// The <c>resource.full</c> notice (the content's own text) of a counting-up pool that was below its maximum on the sheet
    /// (<c>pool.Spent</c>) and is at it after the change (<paramref name="spentAfter"/>). Null otherwise. The one place the rule lives.
    /// </summary>
    private static Diagnostic? FullNotice(ResourceValue? pool, int spentAfter) =>
        pool is { CountsUp: true, Maximum: > 0 } && !string.IsNullOrWhiteSpace(pool.FullText) && pool.Spent < pool.Maximum && spentAfter >= pool.Maximum
            ? new("resource.full", pool.FullText)
            : null;

    /// <summary>The encounter starts at round 0, and each toggle that is on with a duration gets its rounds left (the sheet knows which are on).</summary>
    private static PlayState StartEncounter(PlayState play, CharacterSheet sheet) => play with
    {
        Encounter = new(0),
        Toggles = [.. play.Toggles.Select(t =>
            sheet.Toggles?.FirstOrDefault(v => v.On && v.Content.ContentId == t.ContentId && v.ToggleId == t.ToggleId) is { Duration: { } rounds } ? t with { RoundsLeft = rounds } : t)],
    };

    /// <summary>The encounter ends and every count of rounds left goes with it. A toggle that had one is named in <paramref name="notices"/> (no content ref: nothing is downgraded).</summary>
    private static PlayState EndEncounter(PlayState play, CharacterSheet sheet, IReadOnlyList<string>? clear, out IReadOnlyList<Diagnostic>? notices)
    {
        var pools = ClearedPools(sheet, clear); // refused whole before anything is applied
        var lost = play.Toggles.Where(t => t.RoundsLeft is not null).Select(t =>
        {
            var label = sheet.Toggles?.FirstOrDefault(v => v.Content.ContentId == t.ContentId && v.ToggleId == t.ToggleId)?.Label ?? t.ToggleId;
            return new Diagnostic("encounter.duration-by-hand", $"{label} ({t.ToggleId}): duration: track by hand");
        }).ToList();
        notices = lost.Count == 0 ? null : lost;
        play = play with { Encounter = null, Toggles = [.. play.Toggles.Select(t => t with { RoundsLeft = null })] };
        return pools.Aggregate(play, (state, pool) => state.WithSpent(pool.Content.ContentId, pool.ResourceId, 0));
    }

    /// <summary>
    /// D43 S8: the pools an <c>endEncounter</c> clears. Entries are <c>"{contentId:D}/{resourceId}"</c>, at most <see cref="MaxClearEntries"/>
    /// (duplicates allowed); each must name a counting-up pool on the sheet. Anything else refuses the whole command with
    /// <c>play.clear-invalid</c> (the message names the entry's position, never its text).
    /// </summary>
    private static List<ResourceValue> ClearedPools(CharacterSheet sheet, IReadOnlyList<string>? clear)
    {
        var pools = new List<ResourceValue>();
        if (clear is not { Count: > 0 })
            return pools;
        if (clear.Count > MaxClearEntries)
            throw new AppValidationException([new("play.clear-invalid", $"At most {MaxClearEntries} pools can be cleared; {clear.Count} were named.")], "play.clear-invalid");
        for (var i = 0; i < clear.Count; i++)
        {
            var entry = clear[i] ?? ""; // a null entry in the request is malformed like any other
            var slash = entry.IndexOf('/', StringComparison.Ordinal);
            var pool = slash > 0 && Guid.TryParseExact(entry[..slash], "D", out var content) && slash < entry.Length - 1
                ? sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == content && r.ResourceId == entry[(slash + 1)..])
                : null;
            if (pool is not { CountsUp: true })
                throw new AppValidationException([new("play.clear-invalid", $"Entry {i + 1} of the pools to clear is not a counting-up pool of this character.")], "play.clear-invalid");
            pools.Add(pool);
        }
        return pools;
    }

    /// <summary>The most pools one <c>endEncounter</c> may clear.</summary>
    internal const int MaxClearEntries = 64;

    /// <summary>
    /// D43 S8: <c>useAction</c>. Finds the roll (content id and roll id) on the calculated sheet, builds every pool change it implies (its
    /// own spend, or draw-down for a <c>drawsDown</c> roll, then each <c>alsoChange</c> entry) and applies them with
    /// <see cref="PoolChanges.Apply"/>: all together or none. The roll's own amount follows the roll command's rules (a per-unit roll takes
    /// <c>command.Amount</c> within the same bounds; any other the evaluated cost, or 1 with none declared).
    /// </summary>
    private PlayState UseAction(PlayState play, CharacterSheet sheet, PlayCommand command, out IReadOnlyList<Diagnostic>? notices, out string? label)
    {
        notices = null;
        label = null;
        if (command.ContentId is not { } contentId || string.IsNullOrEmpty(command.RollId))
            throw new AppValidationException([new("use.not-found", "Name the content and the roll to use.")], "use.not-found");
        FeatureEntry? feature = null;
        RollEffect? roll = null;
        FeatureEffect? calculated = null;
        foreach (var candidate in sheet.Features ?? [])
        {
            if (candidate.Content.ContentId != contentId)
                continue;
            // Two rolls in one revision sharing a rollId cannot be told apart: neither is used.
            var matches = _store.FindRevision(candidate.Content)?.Effects.OfType<RollEffect>().Where(e => e.RollId == command.RollId).Take(2).ToList() ?? [];
            roll = matches.Count == 1 ? matches[0] : null;
            calculated = roll is null ? null : candidate.Effects.FirstOrDefault(e => e.Id == roll.Id && e.Type == RollEffect.TypeName);
            if (calculated is not null)
            {
                feature = candidate;
                break;
            }
        }
        // MECH-X item 1 (R36): RollId names a roll, or else a feature attack by its effect id; where both match it is ambiguous and nothing is used.
        var attacks = sheet.Attacks?.Where(a => a.Source == AttackSource.Feature && a.Item.ContentId == contentId && a.EffectId == command.RollId).Take(2).ToList() ?? [];
        if (feature is null || roll is null || calculated is null)
        {
            if (attacks.Count != 1)
                throw new AppValidationException([new("use.not-found", $"Content {contentId:D} has no roll '{command.RollId}' for this character.")], "use.not-found");
            return UseFeatureAttack(play, sheet, attacks[0], out label);
        }
        if (attacks.Count > 0)
            throw new AppValidationException([new("use.not-found", $"Content {contentId:D} has both a roll and an attack '{command.RollId}', so it cannot be told which to use.")], "use.not-found");
        var reference = feature.Content;
        if (!calculated.Available)
            throw new AppValidationException([new("use.unavailable", $"Roll '{roll.Id}' is not available now.", reference, roll.Id)], "use.unavailable");
        if (calculated.Payment is { } payment)
        {
            label = $"use {(string.IsNullOrWhiteSpace(roll.Label) ? roll.RollId : roll.Label)}";
            return Pay(play, sheet, payment, PaymentAmount(payment, roll, calculated, command, reference), reference, roll.Id);
        }

        var changes = new List<PoolChange>();
        if (roll.ResourceId is { } ownPool)
        {
            // A cost that cannot be calculated refuses the use with the calculation's own diagnostic, as the roll command does for an amount.
            if (roll.Cost is not null && calculated.Cost is null)
            {
                var failure = sheet.Diagnostics.FirstOrDefault(d => d.Code == "effect.invalid-formula" && d.Content == reference && d.EffectId == roll.Id)
                    ?? new Diagnostic("effect.invalid-formula", $"The cost of roll '{roll.Id}' could not be calculated.", reference, roll.Id);
                throw new AppValidationException([failure], failure.Code);
            }
            var poolContent = calculated.ResourceContent ?? contentId;
            // Defence in depth: a draw-down gives uses back, which on an ordinary pool would refill it, so it needs a counting-up pool here.
            if (calculated.DrawsDown && sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == poolContent && r.ResourceId == ownPool) is not { CountsUp: true })
                throw new AppValidationException([new("use.unavailable", $"Pool '{ownPool}' does not count up for this character, so roll '{roll.Id}' cannot draw it down.", reference, roll.Id)], "use.unavailable");
            int amount;
            if (calculated.PerUnit is { } perUnit)
            {
                amount = command.Amount;
                var baseCost = calculated.Cost ?? 0;
                var pool = sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == poolContent && r.ResourceId == ownPool);
                if (PerUnitRangeProblem(roll.Id, reference, amount, baseCost, perUnit, pool?.Maximum) is { } outOfRange)
                    throw new AppValidationException([outOfRange], outOfRange.Code);
            }
            else
            {
                amount = calculated.Cost ?? 1;
            }
            changes.Add(new(poolContent, ownPool, calculated.DrawsDown ? UpkeepChange.Regain : UpkeepChange.Spend, amount));
        }
        foreach (var also in calculated.AlsoChange ?? [])
        {
            if (also.Amount is not { } alsoAmount)
                throw new AppValidationException([new("use.unavailable", $"An amount of roll '{roll.Id}' could not be calculated, so it cannot be used.", reference, roll.Id)], "use.unavailable");
            changes.Add(new(also.ResourceContent, also.ResourceId, also.Change, alsoAmount));
        }
        if (changes.Count == 0)
            throw new AppValidationException([new("use.unavailable", $"Roll '{roll.Id}' changes no pool, so there is nothing to use.", reference, roll.Id)], "use.unavailable");

        var (applied, errors) = PoolChanges.Apply(play, sheet, changes);
        if (errors.Count > 0)
            throw new AppValidationException(errors, errors[0].Code);

        // A use that changes nothing (a regain with nothing spent, a per-unit amount of 0) is not a change: no write, no undo slot taken.
        var pools = changes.Select(c => (c.ResourceContent, c.ResourceId)).Distinct().ToList();
        if (pools.All(p => applied.SpentOf(p.ResourceContent, p.ResourceId) == play.SpentOf(p.ResourceContent, p.ResourceId)))
            throw new AppValidationException([new("use.unavailable", $"Using '{roll.Id}' would change no pool.", reference, roll.Id)], "use.unavailable");

        // A counting-up pool this use filled says so (the content's own text): it was below its maximum before and is at it now.
        var full = new List<Diagnostic>();
        foreach (var (poolContent, poolId) in pools)
        {
            var filled = FullNotice(sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == poolContent && r.ResourceId == poolId), applied.SpentOf(poolContent, poolId));
            if (filled is not null)
                full.Add(filled);
        }
        notices = full.Count == 0 ? null : full;
        label = $"use {(string.IsNullOrWhiteSpace(roll.Label) ? roll.RollId : roll.Label)}";
        return applied;
    }

    /// <summary>
    /// MECH-X item 1 (R34): what a paying roll pays. A slot payment is one slot (the amount is ignored). A hit-point payment is the
    /// evaluated cost, or for a per-unit roll the amount sent (bounded as a pool's is, with no maximum); a cost that did not calculate
    /// refuses with the calculation's own diagnostic, and an amount of 0 changes nothing, so it is refused (<c>use.unavailable</c>).
    /// </summary>
    private static int PaymentAmount(PaymentValue payment, RollEffect roll, FeatureEffect calculated, PlayCommand command, ContentReference reference)
    {
        if (payment.Kind == PaymentKind.Slot)
            return 1;
        if (roll.Cost is not null && calculated.Cost is null)
            throw new AppValidationException([new("effect.invalid-formula", $"The cost of roll '{roll.Id}' could not be calculated.", reference, roll.Id)], "effect.invalid-formula");
        var amount = calculated.Cost ?? 0;
        if (calculated.PerUnit is { } perUnit)
        {
            amount = command.Amount;
            if (PerUnitRangeProblem(roll.Id, reference, amount, calculated.Cost ?? 0, perUnit, null) is { } outOfRange)
                throw new AppValidationException([outOfRange], outOfRange.Code);
        }
        if (amount <= 0)
            throw new AppValidationException([new("use.unavailable", $"Using '{roll.Id}' would pay nothing.", reference, roll.Id)], "use.unavailable");
        return amount;
    }

    /// <summary>
    /// MECH-X item 1 (R36): <c>useAction</c> of a feature attack by its effect id. Only a paying attack that is available can be used
    /// here (an attack with a pool cost spends through its roll's Spend button, unchanged); the payment is made through <see cref="Pay"/>.
    /// </summary>
    private static PlayState UseFeatureAttack(PlayState play, CharacterSheet sheet, AttackEntry attack, out string label)
    {
        label = $"use {attack.Name}";
        if (attack.Payment is not { } payment || !attack.Available)
            throw new AppValidationException([new("use.unavailable", $"Attack '{attack.EffectId}' cannot be used now.", attack.Item, attack.EffectId)], "use.unavailable");
        var amount = payment.Kind == PaymentKind.Slot ? 1 : attack.Cost ?? 0;
        if (amount <= 0)
            throw new AppValidationException([new("use.unavailable", $"Using '{attack.EffectId}' would pay nothing.", attack.Item, attack.EffectId)], "use.unavailable");
        return Pay(play, sheet, payment, amount, attack.Item, attack.EffectId);
    }

    /// <summary>
    /// <c>character.undoPlay</c> (D43 S2, D44): puts the play state back to what the last compound change started from. One level,
    /// in memory, and only while nothing else has written the character. Refused, writing nothing, in this order: without
    /// <c>confirm</c> (<c>undo.confirmation-required</c>); unknown character (<c>character.not-found</c>); a page stamp that is not the
    /// stored one (<c>character.stale</c>, as save and play); no slot (<c>undo.nothing</c>); a slot written for an earlier stamp
    /// (<c>undo.stale</c>). The undo is itself one <c>SaveWithPlay</c> and sets no slot, so it cannot be undone again.
    /// </summary>
    public CharacterView UndoPlay(UndoPlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("undo.confirmation-required", "Undoing a change to the play state needs an explicit confirmation.")], "undo.confirmation-required");
        var character = _store.FindCharacter(request.CharacterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {request.CharacterId} does not exist.")]);
        if (request.UpdatedAt != character.UpdatedAt)
            throw new AppValidationException([new("character.stale", StaleMessage)], "character.stale");
        var slot = _undo.Find(character.Id)
            ?? throw new AppValidationException([new("undo.nothing", "There is nothing to undo for this character.")], "undo.nothing");
        if (slot.UpdatedAfter != character.UpdatedAt)
            throw new AppValidationException([new("undo.stale", "This character changed since that change, so it can't be undone.")], "undo.stale");
        var view = SaveWithPlay(character with { Play = slot.Before });
        _undo.Remove(character.Id); // only after the write succeeded: a failed save keeps the slot
        return view;
    }

    /// <summary>
    /// D43 S2/S3: the one server switch for compound play changes. A compound change decides several writes from one view, so it needs
    /// the current stamp (<c>play.basis-required</c> without one) and keeps the play state it started from for one undo. Today:
    /// <c>toggleOn</c> with <c>replace</c> ("switch to {toggle label}", the label only once the toggle exists on the
    /// <paramref name="sheet"/>), <c>startEncounter</c> ("start encounter"), <c>endEncounter</c> ("end encounter") and <c>damage</c> with
    /// responses (D43 S7; compound here, labelled by <see cref="DamageLabel"/> once the outcome is known) and <c>useAction</c> (D43 S8;
    /// labelled "use {roll label}" by <see cref="UseAction"/>, because the roll is found by its <c>rollId</c> in the stored revision).
    /// With no <paramref name="sheet"/> (the stamp check runs before it is calculated) only <c>Compound</c> is answered.
    /// </summary>
    internal static (bool Compound, string? Label) CompoundUndo(PlayCommand command, CharacterSheet? sheet) => command.Action switch
    {
        PlayActionKind.ToggleOn when command.Replace => (true,
            sheet?.Toggles?.FirstOrDefault(t => t.Content.ContentId == command.ContentId && t.ToggleId == command.ToggleId) is { } switched ? $"switch to {switched.Label}" : null),
        // MECH-X item 1 (R36): switching on a toggle that pays hit points or a slot also changes them, so it is one compound change. Only the
        // calculated sheet knows (the pre-sheet stamp check answers false); Play repeats the basis check once it has the sheet.
        PlayActionKind.ToggleOn when sheet?.Toggles?.FirstOrDefault(t => t.Content.ContentId == command.ContentId && t.ToggleId == command.ToggleId) is { Payment: not null } paying =>
            (true, $"switch on {paying.Label}"),
        PlayActionKind.StartEncounter => (true, "start encounter"),
        PlayActionKind.EndEncounter => (true, "end encounter"),
        // D43 S7: a damage with responses decides several writes from one preview. Its label ("damage 15 fire (7 taken)") needs the outcome, so Play builds it.
        PlayActionKind.Damage when command.Responses is { Count: > 0 } => (true, null),
        PlayActionKind.UseAction => (true, null), // D43 S8: the label ("use Gather") needs the roll, which only UseAction finds
        _ => (false, null),
    };

    /// <summary>
    /// Wave 3 item 25 (R158, R160): spends or regains charges of one equipment entry, named by its id or (while it has none) by its
    /// position and item. The entry gets its id here if it had none. The inverse of a spend is the regain of the same amount (D23).
    /// </summary>
    private static Character ChangeCharges(Character character, CharacterSheet sheet, PlayCommand command)
    {
        var use = (command.EntryId is { } id
                ? sheet.ItemUses?.FirstOrDefault(u => u.EntryId == id)
                : command.EntryIndex is { } index
                    ? sheet.ItemUses?.FirstOrDefault(u => u.EntryIndex == index && u.EntryId is null && u.Item.ContentId == command.ContentId)
                    : null)
            ?? throw new AppValidationException([new("play.entry-unknown", "This character has no such item entry with charges.")]);
        if (use.ChargesMax is null)
            throw new AppValidationException([new("play.entry-unknown", "This character has no such item entry with charges.", use.Item)]);
        if (command.Amount < 1)
            throw new AppValidationException([new("play.amount-out-of-range", "Spend or regain at least one charge.", use.Item)]);
        int spent;
        if (command.Action == PlayActionKind.SpendCharge)
        {
            if (command.Amount > use.ChargesLeft)
                throw new AppValidationException([new("play.charges-none-left", $"This item entry has {use.ChargesLeft} of {use.ChargesMax} charges left; {command.Amount} cannot be spent.", use.Item)]);
            spent = use.ChargesSpent + command.Amount;
        }
        else
        {
            if (command.Amount > use.ChargesSpent)
                throw new AppValidationException([new("play.charges-full", $"This item entry has {use.ChargesSpent} charge(s) spent; {command.Amount} cannot be regained.", use.Item)]);
            spent = use.ChargesSpent - command.Amount;
        }
        var equipment = character.Equipment.ToList();
        equipment[use.EntryIndex] = equipment[use.EntryIndex] with { ChargesSpent = spent == 0 ? null : spent, Id = equipment[use.EntryIndex].Id ?? Guid.NewGuid() };
        return character with { Equipment = equipment };
    }

    /// <summary>
    /// Wave 3 item 23 (D39, R174): a companion's hit points, in <see cref="PlayState.Companions"/>. Damage is untyped (no defences,
    /// concentration, death saves or warnings): temporary hit points absorb first, then current, at least 0. Heal caps at the maximum
    /// and leaves temporary alone. Set takes exact current (0 to the maximum) and temporary (0 to <see cref="PlayState.MaxHitPoints"/>,
    /// unchanged when not sent). Set temporary takes only the temporary hit points and keeps the stored current. Current at the maximum is stored as null, and an entry that says nothing (null, 0) is dropped.
    /// Errors are codes only; the companion's name never appears in them.
    /// </summary>
    private static PlayState ChangeCompanion(Character character, PlayCommand command)
    {
        var companion = command.CompanionId is { } id ? character.Companions?.FirstOrDefault(c => c.Id == id) : null;
        if (companion is null)
            throw new AppValidationException([new("play.companion-unknown", "This character has no such companion.")]);
        var recorded = character.Play.Companions?.FirstOrDefault(p => p.Id == companion.Id);
        var maximum = companion.HitPointMaximum;
        var current = Math.Min(recorded?.Current ?? maximum, maximum); // a maximum lowered since is respected
        var temporary = recorded?.Temporary ?? 0;
        switch (command.Action)
        {
            case PlayActionKind.CompanionDamage:
                var absorbed = Math.Min(temporary, command.Amount);
                temporary -= absorbed;
                current = Math.Max(current - (command.Amount - absorbed), 0);
                break;
            case PlayActionKind.CompanionHeal:
                current = Math.Min(current + command.Amount, maximum);
                break;
            case PlayActionKind.CompanionSetTemporaryHitPoints:
                temporary = command.Amount; // current stays as stored: a damage that landed since the page drew its view is kept
                break;
            default:
                if (command.Amount > maximum)
                    throw new AppValidationException([new("play.companion-hit-points-above-maximum", $"A companion's current hit points cannot be above its maximum ({maximum}).")]);
                if (command.Temporary is < 0 or > PlayState.MaxHitPoints)
                    throw new AppValidationException([new("play.amount-out-of-range", $"The temporary hit points must be between 0 and {PlayState.MaxHitPoints}.")]);
                current = command.Amount;
                temporary = command.Temporary ?? temporary;
                break;
        }
        var entry = new CompanionPlay(companion.Id, current >= maximum ? null : current, temporary);
        var others = character.Play.Companions?.Where(p => p.Id != companion.Id).ToList() ?? [];
        List<CompanionPlay> all = recorded is null ? [.. others, entry] : character.Play.Companions!.Select(p => p.Id == companion.Id ? entry : p).ToList();
        return character.Play with { Companions = entry is { Current: null, Temporary: 0 } ? others : all };
    }

    /// <summary>
    /// D43: drops the stored overlays the calculation says are not allowed with the primary the candidate state has on (none: only
    /// overlays allowed in the base character stay), so a form switch takes its dependent overlays with it. A form turned off uses
    /// <see cref="ToggleGroups.EndingWith"/>, the same rule as the turn planner.
    /// </summary>
    private PlayState WithoutDisallowedOverlays(Character character, PlayState candidate)
    {
        var sheet = CharacterCalculator.Calculate(character with { Play = candidate }, _store);
        var gone = (sheet.Toggles ?? []).Where(t => !t.Available).Select(t => (t.Content.ContentId, t.ToggleId)).ToHashSet();
        return gone.Count == 0 ? candidate : candidate with { Toggles = [.. candidate.Toggles.Where(t => !gone.Contains((t.ContentId, t.ToggleId)))] };
    }

    private PlayState ChangeToggle(Character character, PlayState play, CharacterSheet sheet, PlayCommand command)
    {
        var toggle = sheet.Toggles?.FirstOrDefault(t => t.Content.ContentId == command.ContentId && t.ToggleId == command.ToggleId)
            ?? throw new AppValidationException([new("toggle.not-found", $"This character has no toggle '{command.ToggleId}' from content {command.ContentId}.")]);
        // D38: an auto toggle follows the hit points; it has no state to switch, in either direction.
        if (toggle.Auto == true)
            throw new AppValidationException([new("toggle.automatic", "This toggle switches itself while the character is bloodied; it cannot be switched by hand.")]);
        var others = play.Toggles.Where(t => !(t.ContentId == toggle.Content.ContentId && t.ToggleId == toggle.ToggleId)).ToList();
        if (command.Action == PlayActionKind.ToggleOff)
        {
            if (!toggle.On)
                throw new AppValidationException([new("toggle.already-off", $"'{toggle.Label}' is already off.")]);
            // D43: a primary that goes off takes the overlays that need it (the rule the turn planner shares).
            var taken = ToggleGroups.EndingWith(toggle, sheet.Toggles!, play.Toggles.Select(s => (s.ContentId, s.ToggleId)).ToHashSet()).Select(t => (t.Content.ContentId, t.ToggleId)).ToHashSet();
            return taken.Count == 0 ? play with { Toggles = others } : play with { Toggles = [.. others.Where(t => !taken.Contains((t.ContentId, t.ToggleId)))] };
        }
        if (toggle.On)
            throw new AppValidationException([new("toggle.already-on", $"'{toggle.Label}' is already on.")]);
        var isPrimary = toggle is { Group: not null, Layer: not ToggleLayer.Overlay };
        if (toggle is { Group: not null, Layer: ToggleLayer.Overlay, Available: false })
            throw new AppValidationException([new("toggle.overlay-not-allowed", $"'{toggle.Label}' ({toggle.ToggleId}) is not allowed with the form that is on now. {toggle.UnavailableReason}".TrimEnd(), toggle.Content, toggle.EffectId)]);
        var next = others;
        if (isPrimary)
        {
            // Defensive: the sheet reports no group for a group of one (C6), so a primary here always has its group's content; this only guards a sheet built otherwise.
            var groupPresent = sheet.Features is null || sheet.Features.Any(f => f.Content.ContentId == toggle.GroupContent);
            var occupants = ToggleGroups.Occupants(toggle, sheet.Toggles ?? [], groupPresent);
            if (occupants.Count > 0 && !command.Replace)
                throw new AppValidationException([new("toggle.group-occupied", $"Group '{toggle.Group}' already has '{occupants[0].Label}' ({occupants[0].ContentName}) on; turn it off first, or switch with replace.", toggle.Content, toggle.EffectId)]);
            next = [.. others.Where(t => !occupants.Any(o => o.Content.ContentId == t.ContentId && o.ToggleId == t.ToggleId))];
        }
        // Everything is computed before anything is written: the new list, then the payment.
        // D43 S3: in an encounter a timed toggle starts counting its rounds; outside one it has no count (the sheet says "track by hand").
        var candidate = play with { Toggles = [.. next, new ActiveToggle(toggle.Content.ContentId, toggle.ToggleId, play.Encounter is null ? null : toggle.Duration)] };
        if (isPrimary)
            candidate = WithoutDisallowedOverlays(character, candidate);
        // The cost comes from the pool the toggle names (a shared pool lives in another content); a v6 toggle with a resource and no cost spends one use of its own.
        // A declared cost that did not evaluate (the calculator left it null and warned) refuses; only a toggle with no cost spends the default 1.
        var declaresCost = _store.FindRevision(toggle.Content)?.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.Id == toggle.EffectId)?.Cost is not null;
        if (declaresCost && toggle.Cost is null)
            throw new AppValidationException([new("effect.invalid-formula", $"The cost of '{toggle.Label}' ({toggle.ToggleId}) could not be calculated, so it cannot be turned on.", toggle.Content, toggle.EffectId)]);
        // MECH-X item 1 (S9): a toggle that pays hit points or a slot pays through Pay, checked again on the sheet it was decided on; a
        // hit-point cost of 0 turns it on and pays nothing, as a pool toggle does. The Pay is the compound change Play labelled.
        if (toggle.Payment is { } payment)
            return Pay(candidate, sheet, payment, payment.Kind == PaymentKind.Slot ? 1 : toggle.Cost ?? 0, toggle.Content, toggle.EffectId);
        var amount = toggle.Cost ?? 1;
        if (toggle.ResourceId is { } resourceId && amount > 0)
            candidate = ChangeResource(candidate, sheet, new(command.CharacterId, PlayActionKind.Spend, Confirm: true, Amount: amount, ContentId: toggle.CostResourceContent ?? toggle.Content.ContentId, ResourceId: resourceId));
        return candidate;
    }

    private static PlayState ChangeSlot(PlayState play, CharacterSheet sheet, PlayCommand command)
    {
        if (command.Amount is < 1 or > SpellcastingEffect.MaxSpellLevel)
            throw new AppValidationException([new("play.amount-out-of-range", $"A spell slot level is 1 to {SpellcastingEffect.MaxSpellLevel}.")]);
        var slots = sheet.SpellSlots?.FirstOrDefault(s => s.Level == command.Amount);
        var spent = play.SlotsSpentOf(command.Amount);
        if (command.Action == PlayActionKind.SpendSlot)
        {
            return slots is { Remaining: > 0 }
                ? play.WithSlotsSpent(command.Amount, spent + 1)
                : throw new AppValidationException([new("slots.none-left", $"No level {command.Amount} spell slots are left.")]);
        }
        return spent > 0
            ? play.WithSlotsSpent(command.Amount, spent - 1)
            : throw new AppValidationException([new("slots.nothing-spent", $"No level {command.Amount} spell slots are spent.")]);
    }

    private static PlayState RecordDeathSave(PlayState play, HitPointState hp, int d20)
    {
        if (hp.Current > 0)
            throw new AppValidationException([new("play.not-dying", "Death saving throws are made only at 0 hit points.")]);
        if (d20 is < 1 or > 20)
            throw new AppValidationException([new("play.amount-out-of-range", "A d20 shows 1 to 20.")]);
        var outcome = DeathSaves.Outcome(d20);
        return outcome.RegainsOneHitPoint
            ? play with { CurrentHitPoints = hp.Maximum > 1 ? 1 : null, DeathSaves = new() }
            : play with { DeathSaves = play.DeathSaves.After(outcome) };
    }

    /// <summary>
    /// Wave 3 item 21 (D19 amended, R169). The order: the damage type must be one of the thirteen (case-insensitive) or the
    /// command is refused; immunity gives 0, otherwise resistance halves (rounding down) and then vulnerability doubles, each
    /// kind counting once however many sources give it; then temporary hit points absorb; then hit points. Untyped damage
    /// ignores every defence. Damage that is 0 after defences changes nothing and asks for no concentration check.
    /// </summary>
    private static PlayState Damage(PlayState play, CharacterSheet sheet, PlayCommand command, RulesFamilyPolicy family, out DamageOutcome outcome)
    {
        var type = NormalizedDamageType(command.DamageType);
        var hp = sheet.HitPoints!;
        var result = DamagePipeline.Resolve(new(command.Amount, type), hp, sheet.DamageDefences?.Select(d => new DamageDefence(d.Type, d.Kind)).ToList() ?? [], play.Concentration is not null, family);
        outcome = result.Outcome;
        if (result.Outcome.Adjusted == 0)
            return play; // nothing changes and no concentration check is asked for
        var concentration = result.ConcentrationEnds ? null
            : result.ConcentrationDc is { } dc && play.Concentration is { } con ? con with { PendingSaveDc = dc }
            : play.Concentration;
        return play.WithHitPoints(result.After.Current, result.After.Temporary, hp.Maximum) with { Concentration = concentration };
    }

    private static PlayState StartConcentration(PlayState play, CharacterSheet sheet, PlayCommand command)
    {
        var matches = sheet.Spellcasting?.SelectMany(c => c.Spells).Where(s => s.Spell.ContentId == command.ContentId).ToList() ?? [];
        // A spell on two casters' lists counts as prepared if either has it prepared.
        var spell = matches.FirstOrDefault(s => s.Prepared) ?? matches.FirstOrDefault()
            ?? throw new AppValidationException([new("play.spell-not-found", $"This character has no spell with content id {command.ContentId}.")]);
        if (!spell.Concentration)
            throw new AppValidationException([new("play.spell-not-concentration", $"'{spell.Name}' does not need concentration.", spell.Spell)]);
        if (!spell.Prepared)
            throw new AppValidationException([new("play.spell-not-prepared", $"'{spell.Name}' is not prepared.", spell.Spell)]);
        if (sheet.HitPoints is { Current: 0 })
            throw new AppValidationException([new("play.unconscious", "A character at 0 hit points cannot start concentrating.")]);
        return play with { Concentration = Concentration.On(spell.Spell, spell.Name) };
    }

    private static PlayState ChangeResource(PlayState play, CharacterSheet sheet, PlayCommand command)
    {
        var resource = sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == command.ContentId && r.ResourceId == command.ResourceId)
            ?? throw new AppValidationException([new("resource.not-found", $"This character has no resource '{command.ResourceId}' from content {command.ContentId}.")]);
        if (resource.Current is not { } current)
            throw new AppValidationException([new("resource.untracked", $"'{resource.Label}' has no calculated maximum, so TomeStack does not track its uses; track it by hand.", resource.Content, resource.EffectId)]);
        if (command.Amount < 1)
            throw new AppValidationException([new("play.amount-out-of-range", "Spend or regain at least one use.")]);
        if (command.Action == PlayActionKind.Spend)
        {
            return command.Amount <= current
                ? play.WithSpent(resource.Content.ContentId, resource.ResourceId, resource.Spent + command.Amount)
                : throw new AppValidationException([new("resource.insufficient", $"'{resource.Label}' has {current} of {resource.Maximum} left; {command.Amount} cannot be spent.", resource.Content, resource.EffectId)]);
        }
        return resource.Spent > 0
            ? play.WithSpent(resource.Content.ContentId, resource.ResourceId, Math.Max(resource.Spent - command.Amount, 0))
            : throw new AppValidationException([new("resource.nothing-spent", $"No uses of '{resource.Label}' are spent.", resource.Content, resource.EffectId)]);
    }

    private static string Condition(PlayCommand command) =>
        command.Condition is { } condition && ConditionKeys.All.Contains(condition)
            ? condition
            : throw new AppValidationException([new("play.condition-unknown", $"'{command.Condition}' is not a condition TomeStack knows.")]);
}
