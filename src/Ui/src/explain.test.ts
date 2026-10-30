import { describe, expect, it } from 'vitest';
import type { RollMode, RollRecord, TraceEntry, TraceOrigin } from './api/types';
import { attackSteps, explainCritical, explainMode, explainRoll, explainSteps, type StepContext } from './explain';
import { rollModeText } from './rollText';

const rules: TraceOrigin = { kind: 'rulesPolicy', rulesFamily: 'srd-5.1' };
const choice: TraceOrigin = { kind: 'characterChoice', rulesFamily: 'srd-5.1' };
const override: TraceOrigin = { kind: 'override', rulesFamily: 'srd-5.1' };
const content = (contentName?: string, sourceTitle?: string): TraceOrigin => ({ kind: 'content', rulesFamily: 'srd-5.1', contentName, sourceTitle });

function step(operation: string, description: string, result: number, origin: TraceOrigin, amount?: number): TraceEntry {
  return { order: 0, operation, description, result, origin, ...(amount === undefined ? {} : { amount }) };
}

function rec(over: Partial<RollRecord> = {}): RollRecord {
  return {
    formula: '1d20',
    mode: 'normal',
    critical: false,
    dice: [{ term: 0, sides: 20, value: 12, kept: true, fromCritical: false }],
    diceTotal: 12,
    expressionConstant: 0,
    modifiers: [],
    total: 12,
    ...over,
  };
}

describe('explainSteps', () => {
  const rows: [string, TraceEntry, StepContext | undefined, string][] = [
    ['base with an amount', step('base', 'Ability score', 14, rules, 14), undefined, 'Starts at 14: Ability score'],
    ['add already holding its signed amount', step('add', 'Fixture Edge: +2 to hit (attack rider)', 5, content('Fixture Edge'), 2), 'attack', 'Fixture Edge: +2 to hit (attack rider)'],
    ['add with a negative amount already in the text', step('add', 'Worn gear -1 penalty', 3, rules, -1), undefined, 'Worn gear -1 penalty'],
    ['add whose amount is only the start of a bigger number in the text', step('add', 'Stride +20 ft', 5, rules, 2), undefined, '+2 Stride +20 ft (from the rules)'],
    ['add whose amount appears in the text beside other words', step('add', 'Bonus: gains +2 here', 5, rules, 2), undefined, 'Bonus: gains +2 here'],
    ['add whose amount sits inside a longer signed number', step('add', 'Stride +10 ft', 5, rules, 1), undefined, '+1 Stride +10 ft (from the rules)'],
    ['add with a negative amount that is only the start of a longer number', step('add', 'Worn gear -10 penalty', 3, rules, -1), undefined, '-1 Worn gear -10 penalty (from the rules)'],
    ['add with a content origin name', step('add', 'Proficiency bonus', 5, content('Fixture Training'), 2), undefined, '+2 Proficiency bonus (from Fixture Training)'],
    ['add with a content origin that has only a source title', step('add', 'Bonus', 5, content(undefined, 'Fixture Handbook'), 1), undefined, '+1 Bonus (from Fixture Handbook)'],
    ['add whose description already names the content', step('add', 'Fixture Training grants this', 5, content('Fixture Training'), 3), undefined, '+3 Fixture Training grants this'],
    ['add with a content origin and no names', step('add', 'Mystery bonus', 5, content(), 2), undefined, '+2 Mystery bonus'],
    ['add with the rules as origin', step('add', 'Proficiency bonus', 5, rules, 2), undefined, '+2 Proficiency bonus (from the rules)'],
    ['add with the player as origin', step('add', 'Bonus', 5, choice, 1), undefined, '+1 Bonus (from your choice)'],
    ['add with an override origin', step('add', 'Bonus', 5, override, 1), undefined, '+1 Bonus'],
    ['add with no amount', step('add', '1d6 fire damage (attack rider)', 5, content('Fixture Edge')), 'attack', '1d6 fire damage (attack rider)'],
    ['derive', step('derive', 'Dexterity modifier', 3, rules), undefined, 'Dexterity modifier: 3'],
    ['set with an amount', step('set', 'Armor sets the base', 16, rules, 16), undefined, 'Set to 16: Armor sets the base'],
    ['replace with an amount', step('replace', 'Fixed speed', 40, content('Fixture Boots'), 40), undefined, 'Set to 40: Fixed speed'],
    ['set with no amount, value context', step('set', 'Dexterity save DC, from Fixture Spark', 13, content('Fixture Spark')), 'value', 'Dexterity save DC, from Fixture Spark: 13'],
    ['replace with no amount, default context', step('replace', 'Replaced score', 9, rules), undefined, 'Replaced score: 9'],
    ['set with no amount, attack context', step('set', 'Damage 1d8 slashing', 7, rules), 'attack', 'Damage 1d8 slashing'],
    ['override', step('override', 'Typed in', 18, override), undefined, 'Your override: 18'],
    ['ignored', step('ignored', 'Fixture Edge (not applicable)', 4, content('Fixture Edge')), undefined, 'Not counted: Fixture Edge (not applicable)'],
    ['rider.not-applied', step('rider.not-applied', 'Fixture Edge needs a ranged weapon', 4, content('Fixture Edge')), 'attack', 'Not counted: Fixture Edge needs a ranged weapon'],
    ['an unknown operation', step('mystery', 'Something new', 4, rules, 2), undefined, 'Something new'],
  ];

  it.each(rows)('%s', (_name, entry, context, expected) => {
    expect(explainSteps([entry], context)).toEqual([expected]);
  });

  it('never says "Unknown source" and keeps the order of the steps', () => {
    const lines = explainSteps([step('base', 'Start', 10, rules, 10), step('add', 'Thing', 12, content(), 2), step('derive', 'End', 12, rules)]);
    expect(lines).toEqual(['Starts at 10: Start', '+2 Thing', 'End: 12']);
    expect(lines.join(' ')).not.toMatch(/unknown/i);
  });
});

describe('explainMode', () => {
  const rows: [string, Partial<RollRecord>, string | undefined, RollMode][] = [
    ['a plain normal roll', { mode: 'normal' }, undefined, 'normal'],
    ['advantage by choice', { mode: 'advantage' }, 'Advantage: your choice', 'advantage'],
    ['disadvantage by choice', { mode: 'disadvantage' }, 'Disadvantage: your choice', 'disadvantage'],
    ['automatic advantage kept', { mode: 'advantage', automaticMode: 'advantage', modeSources: ['Fixture Swift Instinct'] }, 'Advantage from Fixture Swift Instinct', 'advantage'],
    ['automatic disadvantage kept, two sources', { mode: 'disadvantage', automaticMode: 'disadvantage', modeSources: ['Fixture A', 'Fixture B'] }, 'Disadvantage from Fixture A, Fixture B', 'disadvantage'],
    ['automatic advantage cancelled by the player', { mode: 'normal', automaticMode: 'advantage', modeSources: ['Fixture Swift Instinct'] }, 'Normal (cancelled): advantage from Fixture Swift Instinct; your choice: disadvantage', 'normal'],
    ['automatic disadvantage cancelled by the player', { mode: 'normal', automaticMode: 'disadvantage', modeSources: ['Fixture Fog'] }, 'Normal (cancelled): disadvantage from Fixture Fog; your choice: advantage', 'normal'],
    ['automatic advantage and disadvantage cancel', { mode: 'normal', automaticMode: 'normal', modeSources: ['Fixture A', 'Fixture B'] }, 'Normal (cancelled): advantage and disadvantage from Fixture A, Fixture B', 'normal'],
    ['automatic cancel with the player choosing advantage', { mode: 'advantage', automaticMode: 'normal', modeSources: ['Fixture A', 'Fixture B'] }, 'Advantage: your choice; advantage and disadvantage from Fixture A, Fixture B cancel', 'advantage'],
    ['automatic cancel with the player choosing disadvantage', { mode: 'disadvantage', automaticMode: 'normal', modeSources: ['Fixture A', 'Fixture B'] }, 'Disadvantage: your choice; advantage and disadvantage from Fixture A, Fixture B cancel', 'disadvantage'],
  ];

  it.each(rows)('%s', (_name, over, sentence, finalMode) => {
    const record = rec(over);
    expect(explainMode(record)).toBe(sentence);
    // The headline names the same final mode.
    const headline = rollModeText(record);
    if (finalMode === 'normal') expect(headline).not.toMatch(/^, (advantage|disadvantage)/);
    else expect(headline.startsWith(`, ${finalMode}`)).toBe(true);
    // And the sentence leads with it (or says normal).
    if (sentence) expect(sentence.toLowerCase().startsWith(finalMode)).toBe(true);
  });
});

describe('attackSteps', () => {
  const kindled = [
    step('base', 'Ability modifier', 3, rules, 3),
    step('add', 'Proficiency bonus', 5, rules, 2),
    step('add', 'Fixture Kindled: 1d6 fire damage (attack rider)', 5, content('Fixture Kindled')),
    step('add', 'Fixture Kindled: +2 fire damage (attack rider)', 7, content('Fixture Kindled'), 2),
    step('rider.not-applied', 'Fixture Edge: needs a ranged weapon', 7, content('Fixture Edge')),
  ];

  it('keeps the base and the damage steps for damage', () => {
    expect(attackSteps(kindled, 'damage')).toEqual([kindled[0], kindled[2], kindled[3]]);
  });

  it('keeps everything that is not a damage step for to-hit', () => {
    expect(attackSteps(kindled, 'toHit')).toEqual([kindled[0], kindled[1], kindled[4]]);
  });

  it('splits a weapon bonus: the to-hit step stays with to-hit, the damage step goes with damage', () => {
    const trace = [
      step('base', 'Dexterity modifier', 3, rules, 3),
      step('add', 'Proficiency bonus', 5, rules, 2),
      step('add', 'Blade: +2 to hit (weapon)', 7, content('Blade'), 2),
      step('add', 'Blade: +2 damage (weapon)', 7, content('Blade'), 2),
    ];
    expect(attackSteps(trace, 'toHit')).toEqual([trace[0], trace[1], trace[2]]);
    expect(attackSteps(trace, 'damage')).toEqual([trace[0], trace[3]]);
    expect(explainSteps(trace.slice(3), 'attack')).toEqual(['Blade: +2 damage (weapon)']);
    expect(explainSteps(trace.slice(2, 3), 'attack')).toEqual(['Blade: +2 to hit (weapon)']);
  });

  it('splits a weapon mod: the to-hit step stays with to-hit, the damage (mod) step goes with damage', () => {
    const trace = [
      step('base', 'Dexterity modifier', 3, rules, 3),
      step('add', 'Proficiency bonus', 5, rules, 2),
      step('add', 'Whetstone: +1 to hit (mod)', 6, content('Whetstone'), 1),
      step('add', 'Whetstone: +1 damage (mod)', 6, content('Whetstone'), 1),
    ];
    expect(attackSteps(trace, 'toHit')).toEqual([trace[0], trace[1], trace[2]]);
    expect(attackSteps(trace, 'damage')).toEqual([trace[0], trace[3]]);
  });

  it('treats a feature-attack damage step (set, no amount, starts "Damage ") as damage', () => {
    const trace = [step('base', 'Ability modifier', 3, rules, 3), step('add', 'Proficiency bonus', 5, rules, 2), step('set', 'Damage 1d8 slashing', 5, rules)];
    expect(attackSteps(trace, 'damage')).toEqual([trace[0], trace[2]]);
    expect(attackSteps(trace, 'toHit')).toEqual([trace[0], trace[1]]);
  });
});

describe('explainCritical', () => {
  it('is undefined for a roll that is not critical', () => {
    expect(explainCritical(rec(), [])).toBeUndefined();
  });

  it('says the dice are doubled', () => {
    expect(explainCritical(rec({ critical: true }), [step('base', 'Ability modifier', 3, rules, 3)])).toBe('Critical hit: the dice are doubled; flat bonuses are not.');
  });

  it('says rider dice are doubled when a rider adds dice', () => {
    const steps = [step('add', 'Fixture Kindled: 1d6 fire damage (attack rider)', 5, content('Fixture Kindled'))];
    expect(explainCritical(rec({ critical: true }), steps)).toBe('Critical hit: all dice are doubled, rider dice included; flat bonuses are not.');
  });

  it('does not count a rider damage bonus (with an amount) as rider dice', () => {
    const steps = [step('add', 'Fixture Kindled: +2 fire damage (attack rider)', 7, content('Fixture Kindled'), 2)];
    expect(explainCritical(rec({ critical: true }), steps)).toBe('Critical hit: the dice are doubled; flat bonuses are not.');
  });
});

describe('explainRoll', () => {
  const kindled = [
    step('base', 'Ability modifier', 3, rules, 3),
    step('add', 'Proficiency bonus', 5, rules, 2),
    step('add', 'Fixture Kindled: 1d6 fire damage (attack rider)', 5, content('Fixture Kindled')),
    step('add', 'Fixture Kindled: +2 fire damage (attack rider)', 7, content('Fixture Kindled'), 2),
  ];
  const attack = (rollId: string, over: Partial<RollRecord> = {}) =>
    rec({ provenance: { rollId, label: 'Fixture Blade' }, explain: kindled, ...over });

  it('reads content advantage cancelled by the player as normal (cancelled)', () => {
    expect(explainMode(rec({ mode: 'normal', automaticMode: 'advantage', modeSources: ['Fixture Swift Instinct'] })))
      .toBe('Normal (cancelled): advantage from Fixture Swift Instinct; your choice: disadvantage');
  });

  it('says rider dice double on a critical weapon damage roll', () => {
    const lines = explainRoll(attack('weapon.damage', { critical: true }));
    expect(lines[lines.length - 1]).toBe('Critical hit: all dice are doubled, rider dice included; flat bonuses are not.');
  });

  it('shows no damage lines under a to-hit roll and no proficiency line under a damage roll', () => {
    const toHit = explainRoll(attack('attack.attack')).join('\n');
    expect(toHit).toContain('Proficiency bonus');
    expect(toHit).not.toContain('fire damage');
    const damage = explainRoll(attack('attack.damage')).join('\n');
    expect(damage).not.toContain('Proficiency bonus');
    expect(damage).toContain('1d6 fire damage (attack rider)');
    expect(damage).toContain('+2 fire damage (attack rider)');
  });

  it('uses the weapon to-hit and damage ids too', () => {
    expect(explainRoll(attack('weapon.attack')).join('\n')).not.toContain('fire damage');
    expect(explainRoll(attack('weapon.damage')).join('\n')).not.toContain('Proficiency bonus');
  });

  it('puts the mode sentence first, then the steps, for a field roll', () => {
    const record = rec({
      mode: 'advantage',
      automaticMode: 'advantage',
      modeSources: ['Fixture Swift Instinct'],
      provenance: { rollId: 'ability.dex.mod', label: 'Dexterity check' },
      explain: [step('derive', 'Dexterity modifier', 3, rules)],
    });
    expect(explainRoll(record)).toEqual(['Advantage from Fixture Swift Instinct', 'Dexterity modifier: 3']);
  });

  it('falls back to modifiers when a roll has no trace', () => {
    expect(explainRoll(rec({ modifiers: [{ label: 'Wave bonus', amount: 2 }, { label: 'Chill', amount: -1 }] }))).toEqual(['Wave bonus: +2', 'Chill: -1']);
  });

  it('says nothing for a plain roll with no trace and no modifiers', () => {
    expect(explainRoll(rec())).toEqual([]);
  });
});
