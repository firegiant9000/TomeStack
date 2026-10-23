using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// What to roll: a content roll effect (<paramref name="Content"/> and <paramref name="EffectId"/>), a sheet field as
/// a d20 test (<paramref name="Field"/>: an ability modifier, saving throw, skill or initiative), one of the character's
/// hit dice (<paramref name="HitDie"/>: the die alone; a short rest adds the Constitution modifier), or a death saving
/// throw (<paramref name="DeathSave"/>: a d20 with no modifier), or one of the character's spells (<paramref name="Spell"/>:
/// its attack roll with the caster's spell attack bonus when <paramref name="SpellAttack"/>, else its dice), or an
/// equipped weapon (<paramref name="Weapon"/>: its attack roll, or its damage when <paramref name="Damage"/>, two-handed
/// when <paramref name="Versatile"/>), or one of the character's own hand-typed attacks (<paramref name="CustomAttack"/>:
/// d20 plus its typed to-hit, or its damage dice when <paramref name="Damage"/>), or an attack line of one of the character's
/// companions (<paramref name="CompanionAttack"/>: rolled the same way).
/// </summary>
public sealed record RollCommand(
    Guid CharacterId,
    ContentReference? Content = null,
    string? EffectId = null,
    string? Field = null,
    RollMode Mode = RollMode.Normal,
    bool Critical = false,
    int? HitDie = null,
    bool DeathSave = false,
    ContentReference? Spell = null,
    bool SpellAttack = false,
    ContentReference? Weapon = null,
    bool Damage = false,
    bool Versatile = false,
    Guid? CustomAttack = null,
    string? ChargeEntry = null,
    CompanionAttackTarget? CompanionAttack = null,
    int? Amount = null);

/// <summary>Wave 3 item 23: one attack line of one of the character's companions (<see cref="Companion.Attacks"/>).</summary>
public sealed record CompanionAttackTarget(Guid CompanionId, Guid AttackId);

public sealed partial class TomeStackApp
{
    /// <summary>Dice for play: the OS CSPRNG. Tests replace it with a seeded source.</summary>
    internal IRandomSource Random { get; set; } = SystemRandomSource.Instance;

    /// <summary>
    /// SPEC C-04, M1 item 7: rolls and returns a <see cref="RollRecord"/> with its formula, dice, modifiers and
    /// provenance. It **never** changes the character: nothing is saved, and a roll linked to a resource only names
    /// that resource (spending is a separate, confirmed command, M2; ARCHITECTURE "commands vs calculation").
    /// </summary>
    public RollRecord Roll(RollCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var character = _store.FindCharacter(command.CharacterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {command.CharacterId} does not exist.")]);
        var sheet = CharacterCalculator.Calculate(character, _store);

        RollRequest request;
        RollSpend? spend = null;
        (RollMode Automatic, IReadOnlyList<string> Sources)? modeNote = null;
        // An amount means something only on a content roll effect (a per-unit amount); on any other roll it is refused, not ignored.
        var amountMeansSomething = false;
        var targets = (command.Content is not null ? 1 : 0) + (command.Field is not null ? 1 : 0) + (command.HitDie is not null ? 1 : 0)
            + (command.DeathSave ? 1 : 0) + (command.Spell is not null ? 1 : 0) + (command.Weapon is not null ? 1 : 0)
            + (command.CustomAttack is not null ? 1 : 0) + (command.ChargeEntry is not null ? 1 : 0) + (command.CompanionAttack is not null ? 1 : 0);
        if (targets > 1)
            throw new AppValidationException([new("roll.ambiguous", "Roll one thing: a content effect, a field, a hit die, a death saving throw, a spell, a weapon, your own attack or a companion's attack.")]);
        if (command.CustomAttack is { } customId)
        {
            // Hand-typed attacks (D42): the name goes only into the provenance label; no message below quotes it.
            var custom = character.CustomAttacks?.FirstOrDefault(a => a.Id == customId)
                ?? throw new AppValidationException([new("roll.custom-attack-unknown", "This character has no such attack of their own.")]);
            request = CustomAttackRequest(custom, command, "custom", custom.Name, "roll.custom-attack-no-damage");
        }
        else if (command.CompanionAttack is { } companionAttack)
        {
            // Wave 3 item 23 (D39): a companion's attack line is a CustomAttack, rolled the same way; the names go only into the label.
            var companion = character.Companions?.FirstOrDefault(c => c.Id == companionAttack.CompanionId)
                ?? throw new AppValidationException([new("roll.companion-unknown", "This character has no such companion.")]);
            var line = companion.Attacks?.FirstOrDefault(a => a.Id == companionAttack.AttackId)
                ?? throw new AppValidationException([new("roll.companion-attack-unknown", "This companion has no such attack.")]);
            request = CustomAttackRequest(line, command, "companion", $"{companion.Name}: {line.Name}", "roll.companion-attack-no-damage");
        }
        else if (command.Weapon is { } weaponReference)
        {
            var attack = sheet.Attacks?.FirstOrDefault(a => a.Source == AttackSource.Weapon && a.Item == weaponReference)
                ?? throw new AppValidationException([new("roll.weapon-unknown", "This character has no such weapon equipped.", weaponReference)]);
            var provenance = new RollProvenance(
                $"weapon.{(command.Damage ? "damage" : "attack")}", command.Damage ? $"{attack.Name} damage ({attack.DamageType})" : $"{attack.Name} attack",
                attack.Item, attack.Name, attack.EffectId, attack.Origin.SourceId, attack.Origin.SourceTitle, attack.Origin.Page);
            if (!command.Damage)
            {
                // M2.2: say when a lower roll is already a critical hit (Improved Critical); the player marks the damage roll.
                var critical = sheet.Field(FieldIds.CriticalRange).Value;
                if (critical < 20)
                    provenance = provenance with { Label = $"{provenance.Label} (critical hit on {critical}–20)" };
                request = new RollRequest("1d20", command.Mode, false, [new RollModifier($"{attack.Name} to hit", attack.ToHit, attack.Origin)], provenance);
            }
            else
            {
                var dice = command.Versatile
                    ? attack.VersatileDamage ?? throw new AppValidationException([new("roll.weapon-not-versatile", $"'{attack.Name}' is not versatile.", weaponReference)])
                    : attack.Damage;
                request = new RollRequest(dice, RollMode.Normal, command.Critical, [], provenance);
            }
        }
        else if (command.Spell is { } spellReference)
        {
            var (caster, spell) = sheet.Spellcasting?.SelectMany(c => c.Spells.Select(s => (c, s))).FirstOrDefault(p => p.s.Spell == spellReference) ?? default;
            if (spell is null)
                throw new AppValidationException([new("roll.spell-unknown", "This character has no such spell ready.", spellReference)]);
            var provenance = new RollProvenance(
                $"spell.{(command.SpellAttack ? "attack" : "dice")}", command.SpellAttack ? $"{spell.Name} (spell attack)" : spell.Name,
                spell.Spell, spell.Name, spell.Origin.EffectId, spell.Origin.SourceId, spell.Origin.SourceTitle, spell.Origin.Page);
            if (command.SpellAttack)
            {
                if (spell.Attack == SpellAttackKind.None)
                    throw new AppValidationException([new("roll.spell-no-attack", $"'{spell.Name}' makes no attack roll.", spellReference)]);
                request = new RollRequest("1d20", command.Mode, false, [new RollModifier($"{caster!.Name} spell attack", caster.AttackBonus, caster.Origin)], provenance);
            }
            else
            {
                request = spell.Dice is { } dice
                    ? new RollRequest(dice, RollMode.Normal, command.Critical, [], provenance)
                    : throw new AppValidationException([new("roll.spell-no-dice", $"'{spell.Name}' has no dice to roll.", spellReference)]);
            }
        }
        else if (command.ChargeEntry is { } entry)
        {
            // Wave 3 item 25 (R171): the dice an item regains charges with on a rest, rolled here so they appear in the roll strip. Nothing is saved.
            var use = sheet.ItemUses?.FirstOrDefault(u => u.EntryKey == entry)
                ?? throw new AppValidationException([new("roll.charges-unknown", "This character has no such item entry with charges.")]);
            var dice = use.Recovery?.Regain
                ?? throw new AppValidationException([new("roll.charges-no-dice", "This item entry has no dice to roll for its charges.", use.Item)]);
            request = new RollRequest(dice, RollMode.Normal, false, [], new RollProvenance("charges.regain", $"{use.Name} charges", use.Item, use.Name));
        }
        else if (command.HitDie is { } die)
        {
            if (sheet.HitDice?.Any(h => h.Die == die) != true)
                throw new AppValidationException([new("rest.hit-die-unknown", $"This character has no d{die} hit dice.")]);
            request = new RollRequest($"1d{die}", RollMode.Normal, false, [], new RollProvenance($"hitDie.d{die}", $"Hit die (d{die})"));
        }
        else if (command.DeathSave)
        {
            request = new RollRequest("1d20", command.Mode, false, [], new RollProvenance("deathSave", "Death saving throw"));
        }
        else if (command.Content is { } featureReference
            && sheet.Attacks?.FirstOrDefault(a => a.Source == AttackSource.Feature && a.Item == featureReference && a.EffectId == command.EffectId) is { } featureAttack)
        {
            // D43 S4: an attack a feature gives, rolled like a weapon. Rider dice are in the formula, so a critical doubles them; the flat total does not double.
            request = FeatureAttackRequest(featureAttack, sheet.Diagnostics, command);
        }
        else if (command.Content is { } reference)
        {
            if (sheet.Active?.Contains(reference) != true)
                throw new AppValidationException([new("roll.content-inactive", $"Revision {reference.RevisionId} does not apply to this character, so its rolls are not available.", reference)]);
            var revision = _store.FindRevision(reference)!;
            var effect = revision.Effects.OfType<RollEffect>().FirstOrDefault(e => e.Id == command.EffectId)
                ?? throw new AppValidationException([new("roll.effect-not-found", $"'{revision.Name}' has no roll effect '{command.EffectId}'.", reference, command.EffectId)]);
            // Content v8 (M2.2): the roll's bonus formula, as the sheet evaluated it for this character (a flat modifier,
            // with its sign). A bonus that could not be evaluated refuses the roll: rolling without it would be wrong.
            // D43 S5: a roll with no dice has nothing to roll, with or without an amount.
            if (effect.Dice is null)
                throw new AppValidationException([new("roll.no-dice", $"'{effect.Label}' has no dice to roll.", reference, effect.Id)], "roll.no-dice");
            var calculated = sheet.Features?.FirstOrDefault(f => f.Content == reference)?.Effects.FirstOrDefault(e => e.Id == effect.Id);
            // The calculation reports a roll's bonus before its cost and save DC, so the first diagnostic is the bonus's own when the bonus fails.
            var failure = sheet.Diagnostics.FirstOrDefault(d => d.Code == "effect.invalid-formula" && d.Content == reference && d.EffectId == effect.Id);
            // A failing cost (calculation leaves Cost null while the effect declares one) or save DC is not a bonus problem: a failing
            // cost refuses only an amount, and a failing DC refuses nothing here.
            var costFailed = failure is not null && effect.Cost is not null && calculated?.Cost is null;
            if (failure is not null && effect.Bonus is not null && calculated?.Bonus is null)
                throw new AppValidationException([new("roll.bonus-invalid", $"'{effect.Label}' cannot be rolled: its bonus could not be calculated. {failure.Message}", reference, effect.Id)]);
            var bonus = calculated?.Bonus;
            IReadOnlyList<RollModifier>? modifiers = bonus is { } amount && amount != 0
                ? [new RollModifier($"{effect.Label} bonus", amount, new TraceOrigin(TraceOriginKind.Content, character.RulesFamily, reference, revision.Name, effect.Id, revision.Provenance.SourceId, null, revision.Provenance.Page))]
                : null;
            request = DiceRoller.FromEffect(effect, revision, _store.FindSource(revision.Provenance.SourceId), command.Mode, command.Critical, modifiers);
            amountMeansSomething = true;
            spend = EffectSpend(command, effect, revision, sheet, calculated?.Cost, costFailed, failure, ref request);
        }
        else if (command.Field is { } field)
        {
            if (!IsD20Field(field))
                throw new AppValidationException([new("roll.field-not-rollable", $"'{field}' is not an ability check, saving throw, skill or initiative.")]);
            var value = sheet.Field(field);
            // The modifier cites the step that set the displayed value: the override if there is one, else the last step.
            var origin = value.Trace.Count > 0 ? value.Trace[^1].Origin : null;
            // Investigation 2026-10-06: an ability modifier field is a check, named as the summary's button names it
            // ("Strength check"); saves, skills and initiative keep "{label} (d20 test)" (the e2e asserts the save form).
            var label = field.StartsWith("ability.", StringComparison.Ordinal) && field.EndsWith(".mod", StringComparison.Ordinal)
                ? $"{(value.Label.EndsWith(" modifier", StringComparison.Ordinal) ? value.Label[..^" modifier".Length] : value.Label)} check"
                : $"{value.Label} (d20 test)";
            // D43 S6: the player's choice and the sheet's automatic mode combine (advantage and disadvantage cancel); the record says which.
            var automatic = value.Mode?.Reasons.Where(r => r.IsAutomatic).ToList();
            var mode = automatic is { Count: > 0 } ? RollModes.Combine([command.Mode, .. automatic.Select(r => r.Mode)]) : command.Mode;
            request = new RollRequest("1d20", mode, command.Critical, [new RollModifier(value.Label, value.Value, origin)], new RollProvenance(field, label));
            if (automatic is { Count: > 0 })
                modeNote = (value.Mode!.Automatic, [.. automatic.Select(r => r.Source)]);
        }
        else
        {
            throw new AppValidationException([new("roll.target-required", "Name a content roll effect or a field to roll.")]);
        }

        if (command.Amount is not null && !amountMeansSomething)
            throw new AppValidationException([new("roll.amount-not-allowed", "An amount applies only to a roll effect with per-unit dice.", command.Content, command.EffectId)], "roll.amount-not-allowed");
        if (!DiceRoller.TryRoll(request, Random, out var record, out var error))
            throw new AppValidationException([new(error!.Code, error.Message, command.Content, command.EffectId)]);
        if (modeNote is { } note)
            record = record! with { AutomaticMode = note.Automatic, ModeSources = note.Sources };
        return spend is null ? record! : record! with { Spend = spend };
    }

    /// <summary>
    /// D43 S5: the amount of a roll effect's pool the roll implies spending (the request's amount, else the evaluated cost; informational,
    /// the Spend button sends it later), and, for a per-unit amount, the formula with the extra dice. Every check runs before any die is rolled.
    /// Messages name ids and numbers only.
    /// </summary>
    private static RollSpend? EffectSpend(
        RollCommand command, RollEffect effect, ContentRevision revision, CharacterSheet sheet, int? cost, bool costFailed, Diagnostic? failure, ref RollRequest request)
    {
        var poolContent = effect.ResourceContent ?? revision.ContentId;
        var reference = revision.Reference;
        if (command.Amount is { } amount)
        {
            if (effect.PerUnit is not { } perUnit)
                throw new AppValidationException([new("roll.amount-not-allowed", $"'{effect.Id}' spends a fixed amount; it takes no per-unit amount.", reference, effect.Id)], "roll.amount-not-allowed");
            if (costFailed)
                throw new AppValidationException([new(failure!.Code, $"'{effect.Id}' cannot be rolled with an amount: {failure.Message}", reference, effect.Id)], failure.Code);
            var baseCost = cost ?? 0;
            var pool = effect.ResourceId is null ? null : sheet.Resources?.FirstOrDefault(r => r.Content.ContentId == poolContent && r.ResourceId == effect.ResourceId);
            var cap = perUnit.MaxUnits is { } units ? baseCost + units : pool?.Maximum;
            if (amount < baseCost || (cap is { } limit && amount > limit))
                throw new AppValidationException([new("roll.amount-out-of-range", $"'{effect.Id}' takes an amount from {baseCost} to {cap?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "the pool's maximum"}, not {amount}.", reference, effect.Id)], "roll.amount-out-of-range");
            if (effect.ResourceId is not null)
            {
                if (pool?.Current is not { } current)
                    throw new AppValidationException([new("roll.amount-unaffordable", $"Pool '{effect.ResourceId}' is untracked, so {amount} cannot be paid from it.", reference, effect.Id)], "roll.amount-unaffordable");
                if (amount > current)
                    throw new AppValidationException([new("roll.amount-unaffordable", $"Pool '{effect.ResourceId}' has {current} left; {amount} cannot be paid.", reference, effect.Id)], "roll.amount-unaffordable");
            }
            // Unreachable for validated content; stored content with a per-unit die this cannot add is refused, never rolled as the base dice.
            if (!DiceRoller.TryPerUnitFormula(request.Formula, perUnit.Dice, baseCost, amount, out var formula))
                throw new AppValidationException([new("roll.amount-out-of-range", $"'{effect.Id}' has a per-unit die that cannot be added to its dice, so no amount can be rolled.", reference, effect.Id)], "roll.amount-out-of-range");
            request = request with { Formula = formula };
        }
        if (effect.ResourceId is null)
            return null;
        var spent = command.Amount ?? cost ?? (effect.Cost is null ? 1 : null);
        return spent is { } uses ? new RollSpend(poolContent, effect.ResourceId, uses) : null;
    }

    /// <summary>
    /// A feature attack (D43 S4) as a roll. One that is unavailable for a reason other than its form (an invalid die or count, a failing
    /// formula) is refused with its own calculation code, because rolling its empty damage would be wrong; one that is only out of its
    /// form is rolled (the UI disables it, and a roll writes nothing).
    /// </summary>
    private static RollRequest FeatureAttackRequest(AttackEntry attack, IReadOnlyList<Diagnostic> diagnostics, RollCommand command)
    {
        // The calculation leaves Damage empty exactly when the attack itself cannot be calculated (a failing cost formula or the form leave it set).
        if (!attack.Available && attack.Damage.Length == 0
            && diagnostics.FirstOrDefault(d => d.Code is "attack.die-invalid" or "effect.invalid-formula" && d.Content == attack.Item && d.EffectId == attack.EffectId) is { } problem)
            throw new AppValidationException([new(problem.Code, $"'{attack.Name}' cannot be rolled: {problem.Message}", attack.Item, attack.EffectId)], problem.Code);
        var provenance = new RollProvenance(
            $"attack.{(command.Damage ? "damage" : "attack")}", command.Damage ? $"{attack.Name} damage ({attack.DamageType})" : $"{attack.Name} attack",
            attack.Item, attack.Name, attack.EffectId, attack.Origin.SourceId, attack.Origin.SourceTitle, attack.Origin.Page);
        // Only the attack roll names the pool, so the record offers "Spend" once per attack, never again on its damage roll.
        if (!command.Damage && attack.ResourceId is not null)
            provenance = provenance with { LinkedResourceId = attack.ResourceId, LinkedResourceContent = attack.CostResourceContent };
        return command.Damage
            ? new RollRequest(attack.Damage, RollMode.Normal, command.Critical, [], provenance)
            : new RollRequest("1d20", command.Mode, false, [new RollModifier($"{attack.Name} to hit", attack.ToHit, attack.Origin)], provenance);
    }

    /// <summary>
    /// A hand-typed attack line (the character's own, D42, or a companion's, D39): d20 plus its typed to-hit, or its damage dice. The
    /// names go only into labels; the refusal says nothing but its code.
    /// </summary>
    private static RollRequest CustomAttackRequest(CustomAttack attack, RollCommand command, string rollIdPrefix, string displayName, string noDamageCode)
    {
        if (!command.Damage)
        {
            return new RollRequest("1d20", command.Mode, false, [new RollModifier($"{attack.Name} to hit", attack.ToHit)],
                new RollProvenance($"{rollIdPrefix}.attack", $"{displayName} attack"));
        }
        var dice = attack.Damage ?? throw new AppValidationException([new(noDamageCode, "This attack has no damage dice to roll.")]);
        var label = string.IsNullOrWhiteSpace(attack.DamageType) ? $"{displayName} damage" : $"{displayName} damage ({attack.DamageType})";
        return new RollRequest(dice, RollMode.Normal, command.Critical, [], new RollProvenance($"{rollIdPrefix}.damage", label));
    }

    private static bool IsD20Field(string field) =>
        CharacterCalculator.IsField(field)
        && (field == FieldIds.Initiative
            || field.StartsWith("save.", StringComparison.Ordinal)
            || field.StartsWith("skill.", StringComparison.Ordinal)
            || (field.StartsWith("ability.", StringComparison.Ordinal) && field.EndsWith(".mod", StringComparison.Ordinal)));
}
