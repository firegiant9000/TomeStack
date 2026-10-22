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
}

/// <param name="Confirm">Must be <c>true</c>: play state changes only by an explicit user action, never as a side effect.</param>
/// <param name="Replace">With <c>toggleOn</c> of a primary form: switch off the primary of the same group that is on instead of refusing (D43).</param>
/// <param name="ExpectedUpdatedAt">The <c>Character.UpdatedAt</c> of the copy the page acted on (D43 S2). When sent and not the stored one, the command is refused with <c>character.stale</c> and nothing is written, as <see cref="TomeStackApp.SaveCharacter"/> does. Compound changes require it.</param>
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
    DateTimeOffset? ExpectedUpdatedAt = null);

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

        if (command.Action is PlayActionKind.CompanionDamage or PlayActionKind.CompanionHeal or PlayActionKind.CompanionSetHitPoints or PlayActionKind.CompanionSetTemporaryHitPoints)
            return SaveWithPlay(character with { Play = ChangeCompanion(character, command) }); // a companion's hit points are not the character's: no sheet needed
        var sheet = CharacterCalculator.Calculate(character, _store);
        if (command.Action is PlayActionKind.SpendCharge or PlayActionKind.RegainCharge)
            return SaveWithPlay(ChangeCharges(character, sheet, command)); // charges live on the equipment entry, not in the play state
        var play = character.Play;
        var hp = sheet.HitPoints!;
        int? AtMaximumIsNull(int value) => value >= hp.Maximum ? null : value;
        DamageOutcome? damage = null;
        IReadOnlyList<Diagnostic>? notices = null;

        play = command.Action switch
        {
            PlayActionKind.Spend or PlayActionKind.Regain => ChangeResource(play, sheet, command),
            PlayActionKind.Damage => Damage(play, sheet, command, RulesFamilies.Get(character.RulesFamily), out damage),
            PlayActionKind.Heal => play with { CurrentHitPoints = AtMaximumIsNull(hp.Current + command.Amount) },
            PlayActionKind.SetTemporaryHitPoints => play with { TemporaryHitPoints = command.Amount },
            PlayActionKind.SetHitPoints => command.Amount <= hp.Maximum
                ? play with { CurrentHitPoints = AtMaximumIsNull(command.Amount) }
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
                : EndEncounter(play, sheet, out notices),
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
        (PlayState, string)? undo = CompoundUndo(command, sheet).Label is { } label ? (character.Play, label) : null;
        if (command.Action == PlayActionKind.Spend)
            notices = ResourceFullNotice(sheet, command);
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
        return pool is { CountsUp: true, Maximum: > 0 } && !string.IsNullOrWhiteSpace(pool.FullText) && pool.Spent + command.Amount >= pool.Maximum
            ? [new("resource.full", pool.FullText)]
            : null;
    }

    /// <summary>The encounter starts at round 0, and each toggle that is on with a duration gets its rounds left (the sheet knows which are on).</summary>
    private static PlayState StartEncounter(PlayState play, CharacterSheet sheet) => play with
    {
        Encounter = new(0),
        Toggles = [.. play.Toggles.Select(t =>
            sheet.Toggles?.FirstOrDefault(v => v.On && v.Content.ContentId == t.ContentId && v.ToggleId == t.ToggleId) is { Duration: { } rounds } ? t with { RoundsLeft = rounds } : t)],
    };

    /// <summary>The encounter ends and every count of rounds left goes with it. A toggle that had one is named in <paramref name="notices"/> (no content ref: nothing is downgraded).</summary>
    private static PlayState EndEncounter(PlayState play, CharacterSheet sheet, out IReadOnlyList<Diagnostic>? notices)
    {
        var lost = play.Toggles.Where(t => t.RoundsLeft is not null).Select(t =>
        {
            var label = sheet.Toggles?.FirstOrDefault(v => v.Content.ContentId == t.ContentId && v.ToggleId == t.ToggleId)?.Label ?? t.ToggleId;
            return new Diagnostic("encounter.duration-by-hand", $"{label} ({t.ToggleId}): duration: track by hand");
        }).ToList();
        notices = lost.Count == 0 ? null : lost;
        return play with { Encounter = null, Toggles = [.. play.Toggles.Select(t => t with { RoundsLeft = null })] };
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
    /// <paramref name="sheet"/>), <c>startEncounter</c> ("start encounter") and <c>endEncounter</c> ("end encounter"). Later slices (S7, S8) extend this switch.
    /// With no <paramref name="sheet"/> (the stamp check runs before it is calculated) only <c>Compound</c> is answered.
    /// </summary>
    internal static (bool Compound, string? Label) CompoundUndo(PlayCommand command, CharacterSheet? sheet) => command.Action switch
    {
        PlayActionKind.ToggleOn when command.Replace => (true,
            sheet?.Toggles?.FirstOrDefault(t => t.Content.ContentId == command.ContentId && t.ToggleId == command.ToggleId) is { } switched ? $"switch to {switched.Label}" : null),
        PlayActionKind.StartEncounter => (true, "start encounter"),
        PlayActionKind.EndEncounter => (true, "end encounter"),
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
            var occupants = !groupPresent ? [] : (sheet.Toggles ?? []).Where(t => t.On && t.Group == toggle.Group && t.GroupContent == toggle.GroupContent && t.Layer != ToggleLayer.Overlay
                && !(t.Content.ContentId == toggle.Content.ContentId && t.ToggleId == toggle.ToggleId)).ToList();
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
        string? type = null;
        if (command.DamageType is { } written)
        {
            if (!DamageTypes.TryNormalize(written, out var canonical))
                throw new AppValidationException([new("play.damage-type-unknown", $"Damage type must be one of: {string.Join(", ", DamageTypes.All)}.")]);
            type = canonical;
        }
        var hp = sheet.HitPoints!;
        var result = DamagePipeline.Resolve(new(command.Amount, type), hp, sheet.DamageDefences?.Select(d => new DamageDefence(d.Type, d.Kind)).ToList() ?? [], play.Concentration is not null, family);
        outcome = result.Outcome;
        if (result.Outcome.Adjusted == 0)
            return play; // nothing changes and no concentration check is asked for
        var concentration = result.ConcentrationEnds ? null
            : result.ConcentrationDc is { } dc && play.Concentration is { } con ? con with { PendingSaveDc = dc }
            : play.Concentration;
        return play with { TemporaryHitPoints = result.After.Temporary, CurrentHitPoints = result.After.Current >= hp.Maximum ? null : result.After.Current, Concentration = concentration };
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
