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
    string? Condition = null);

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
        var play = character.Play;
        var hp = sheet.HitPoints!;
        int? AtMaximumIsNull(int value) => value >= hp.Maximum ? null : value;

        play = command.Action switch
        {
            PlayActionKind.Spend or PlayActionKind.Regain => ChangeResource(play, sheet, command),
            PlayActionKind.Damage => Damage(play, hp, command.Amount),
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
            _ => throw new AppValidationException([new("play.action-unknown", $"Unknown play action '{command.Action}'.")]),
        };
        // SRD 5.1 p. 98, SRD 5.2.1 p. 17: regaining any hit points resets death saving throws.
        if (hp.Current == 0 && (play.CurrentHitPoints ?? hp.Maximum) > 0 && command.Action is PlayActionKind.Heal or PlayActionKind.SetHitPoints)
            play = play with { DeathSaves = new() };
        return SaveWithPlay(character with { Play = play });
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

    private static PlayState Damage(PlayState play, HitPointState hp, int amount)
    {
        var absorbed = Math.Min(play.TemporaryHitPoints, amount);
        var current = Math.Max(hp.Current - (amount - absorbed), 0);
        return play with { TemporaryHitPoints = play.TemporaryHitPoints - absorbed, CurrentHitPoints = current >= hp.Maximum ? null : current };
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
