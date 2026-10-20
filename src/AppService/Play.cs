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
}

/// <param name="Confirm">Must be <c>true</c>: play state changes only by an explicit user action, never as a side effect.</param>
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
    int? EntryIndex = null);

/// <summary>How the character's defences changed typed damage (R169): immunity wins; otherwise resistance halves (rounding down), then vulnerability doubles.</summary>
public enum DamageEffect { None, Immune, Halved, Doubled, HalvedThenDoubled }

/// <summary>
/// What a <see cref="PlayActionKind.Damage"/> command did, for the result line (Wave 3 item 21, D19 amended).
/// <paramref name="Amount"/> is what was entered and <paramref name="Type"/> its canonical damage type (null: untyped, so no
/// defence applies). <paramref name="Adjusted"/> is the amount after defences (what the concentration check uses),
/// <paramref name="Absorbed"/> what temporary hit points took, <paramref name="Lost"/> what hit points lost.
/// <paramref name="Warnings"/> carries codes and numbers only (<c>play.massive-damage</c>).
/// </summary>
public sealed record DamageOutcome(int Amount, string? Type, DamageEffect Effect, int Adjusted, int Absorbed, int Lost, IReadOnlyList<Diagnostic> Warnings);

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
        if (command.Amount is < 0 or > PlayState.MaxHitPoints)
            throw new AppValidationException([new("play.amount-out-of-range", $"The amount must be between 0 and {PlayState.MaxHitPoints}.")]);

        var sheet = CharacterCalculator.Calculate(character, _store);
        if (command.Action is PlayActionKind.SpendCharge or PlayActionKind.RegainCharge)
            return SaveWithPlay(ChangeCharges(character, sheet, command)); // charges live on the equipment entry, not in the play state
        var play = character.Play;
        var hp = sheet.HitPoints!;
        int? AtMaximumIsNull(int value) => value >= hp.Maximum ? null : value;
        DamageOutcome? damage = null;

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
            PlayActionKind.ToggleOn or PlayActionKind.ToggleOff => ChangeToggle(play, sheet, command),
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
        var view = SaveWithPlay(character with { Play = play });
        return damage is null ? view : view with { Damage = damage };
    }

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
                throw new AppValidationException([new("play.charges-none-left", $"'{use.Name}' has {use.ChargesLeft} of {use.ChargesMax} charges left; {command.Amount} cannot be spent.", use.Item)]);
            spent = use.ChargesSpent + command.Amount;
        }
        else
        {
            if (command.Amount > use.ChargesSpent)
                throw new AppValidationException([new("play.charges-full", $"{use.ChargesSpent} charge(s) of '{use.Name}' are spent; {command.Amount} cannot be regained.", use.Item)]);
            spent = use.ChargesSpent - command.Amount;
        }
        var equipment = character.Equipment.ToList();
        equipment[use.EntryIndex] = equipment[use.EntryIndex] with { ChargesSpent = spent == 0 ? null : spent, Id = equipment[use.EntryIndex].Id ?? Guid.NewGuid() };
        return character with { Equipment = equipment };
    }

    private static PlayState ChangeToggle(PlayState play, CharacterSheet sheet, PlayCommand command)
    {
        var toggle = sheet.Toggles?.FirstOrDefault(t => t.Content.ContentId == command.ContentId && t.ToggleId == command.ToggleId)
            ?? throw new AppValidationException([new("toggle.not-found", $"This character has no toggle '{command.ToggleId}' from content {command.ContentId}.")]);
        // D38: an auto toggle follows the hit points; it has no state to switch, in either direction.
        if (toggle.Auto == true)
            throw new AppValidationException([new("toggle.automatic", "This toggle switches itself while the character is bloodied; it cannot be switched by hand.")]);
        var others = play.Toggles.Where(t => !(t.ContentId == toggle.Content.ContentId && t.ToggleId == toggle.ToggleId)).ToList();
        if (command.Action == PlayActionKind.ToggleOff)
        {
            return toggle.On
                ? play with { Toggles = others }
                : throw new AppValidationException([new("toggle.already-off", $"'{toggle.Label}' is already off.")]);
        }
        if (toggle.On)
            throw new AppValidationException([new("toggle.already-on", $"'{toggle.Label}' is already on.")]);
        if (toggle.ResourceId is { } resourceId)
        {
            // Turning it on spends one use, in the same confirmed change (a use that is not there refuses the whole change).
            play = ChangeResource(play, sheet, new(command.CharacterId, PlayActionKind.Spend, Confirm: true, Amount: 1, ContentId: toggle.Content.ContentId, ResourceId: resourceId));
        }
        return play with { Toggles = [.. others, new ActiveToggle(toggle.Content.ContentId, toggle.ToggleId)] };
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
        var kinds = type is null
            ? new HashSet<DamageDefenceKind>()
            : (sheet.DamageDefences ?? []).Where(d => d.Type == type).Select(d => d.Kind).ToHashSet();
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
            DamageEffect.Halved => command.Amount / 2,
            DamageEffect.Doubled => command.Amount * 2,
            DamageEffect.HalvedThenDoubled => command.Amount / 2 * 2,
            _ => command.Amount,
        };
        if (amount == 0)
        {
            outcome = new(command.Amount, type, effect, 0, 0, 0, []);
            return play;
        }

        var absorbed = Math.Min(play.TemporaryHitPoints, amount);
        var current = Math.Max(hp.Current - (amount - absorbed), 0);
        // Massive damage is a warning, never an automatic death: the damage left over once hit points reach 0 equals or exceeds the maximum.
        var leftOver = amount - absorbed - (hp.Current - current);
        var warnings = hp.Maximum >= 1 && leftOver >= hp.Maximum
            ? new List<Diagnostic> { new("play.massive-damage", $"{leftOver} damage was left over after reaching 0 hit points, which equals or exceeds the hit point maximum of {hp.Maximum}.") }
            : [];
        outcome = new(command.Amount, type, effect, amount, absorbed, hp.Current - current, warnings);
        // D19 (SRD 5.1 p. 101, SRD 5.2.1 p. 11), amended: damage while concentrating asks for a Constitution save, DC 10 or half the
        // damage after resistance, immunity and vulnerability (before temporary hit points), whichever is higher; at 0 hit points the
        // spell simply ends. The family may cap the DC (SRD 5.2.1: 30); every DC stays within what PlayState validation accepts, so a
        // huge hit is still applied.
        var concentration = play.Concentration is { } con
            ? current == 0 ? null : con with { PendingSaveDc = Math.Min(Math.Max(10, amount / 2), Math.Min(family.ConcentrationSaveMaximumDc ?? int.MaxValue, PlayState.MaxConcentrationSaveDc)) }
            : play.Concentration;
        return play with { TemporaryHitPoints = play.TemporaryHitPoints - absorbed, CurrentHitPoints = current >= hp.Maximum ? null : current, Concentration = concentration };
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
