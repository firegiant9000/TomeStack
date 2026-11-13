import { useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import type { CharacterView, DerivedValue, PlayAction, RollMode, RollRecord, RulesFamilyPolicy } from '../api/types';
import { DiceBox } from './DiceBox';
import { FieldWhy } from './sheet/FieldWhy';
import { attacksAgainstText, rollButtonSuffix } from '../rollModes';
import { inspirationLabel } from '../format';
import { RollModePicker, RollResult, type RecordSpent } from './PlayPanels';
import { RollLog, type LoggedRoll } from './RollLog';
import { RollWhy } from './RollWhy';

const abilities = ['str', 'dex', 'con', 'int', 'wis', 'cha'] as const;
const abilityNames: Record<(typeof abilities)[number], string> = {
  str: 'Strength',
  dex: 'Dexterity',
  con: 'Constitution',
  int: 'Intelligence',
  wis: 'Wisdom',
  cha: 'Charisma',
};
const conditionLabel = (key: string) => key.charAt(0).toUpperCase() + key.slice(1);
const signed = (n: number) => (n >= 0 ? `+${n}` : `${n}`);

interface Props {
  view: CharacterView;
  rollMode: RollMode;
  onRollMode: (mode: RollMode) => void;
  lastRoll?: RollRecord;
  /** D28: the previous rolls of this sheet session, newest first. */
  rollLog: LoggedRoll[];
  act: (action: PlayAction) => void | Promise<boolean>;
  /** D43 S7: opens the damage panel for the amount and type ('' is untyped); used by "Lose 1 hit point" when the sheet has damage responses. */
  onDamage?: (amount: number, type: string) => void;
  /** Rolls a d20 test for a field id (for example `ability.str.mod`) in the current mode. */
  onRoll: (field: string) => void;
  /** Rolls typed dice (item 8); rejects with the engine's refusal, which the box shows. */
  onRollDice: (formula: string) => Promise<void>;
  /** True when the sheet offers a Spells tab; the hint names it only then. */
  spellsTab?: boolean;
  /** D73: the quickbar, in its own row after the summary line (never inside the roll strip). */
  quickbar?: ReactNode;
  /** D73: whether the bar has any pin; the hint then says only a button named "and spend" spends. */
  hasQuickbar?: boolean;
  /** D73: the sheet's record-spent state, shared with the quickbar; absent, Last roll keeps its own. */
  spent?: RecordSpent;
  /** The families from `app.info`; the inspiration line is named by the character's family policy. */
  rulesFamilies?: RulesFamilyPolicy[];
  /** The sheet's calculated fields, for the number buttons' traces; absent, the view's own. */
  fields?: DerivedValue[];
  /** Q2: the six ability boxes render above the bar (not sticky); off, they live in the Play panel (Task 3.4). */
  abilityBoxesTop?: boolean;
}

/**
 * Wave 3 (design 3.4): the status bar, the numbers a player looks at every turn, always visible above the tabs. No headings
 * or named regions of its own except the last-roll status: the panels own "Hit points: …" and "Armor Class: …" (two regions
 * with one name break `getByRole` and confuse screen readers). Armor Class, Initiative, Speed and hit points are buttons that
 * open their trace in the one `#status-why` region. An overridden value says so in text (WCAG 1.4.1). The d20 roll mode and
 * the last roll live here so they are reachable from every tab.
 */
export function SheetSummary({ view, rollMode, onRollMode, lastRoll, rollLog, act, onDamage, onRoll, onRollDice, spellsTab = false, quickbar, hasQuickbar = false, spent, rulesFamilies = [], fields, abilityBoxesTop = false }: Props) {
  const { character, sheet } = view;
  const allFields = fields ?? sheet.fields;
  const [whyField, setWhyField] = useState<string | null>(null);
  const field = (id: string): DerivedValue | undefined => allFields.find((f) => f.field === id);
  const show = (id: string): string => {
    const f = field(id);
    if (!f) return '—';
    const text = f.units === 'modifier' || f.units === 'bonus' ? signed(f.value) : f.units === 'feet' ? `${f.value} ft.` : `${f.value}`;
    return f.override ? `${text} (set by hand)` : text;
  };
  const attacksAgainst = attacksAgainstText(sheet.attacksAgainst);
  const hp = sheet.hitPoints;
  const play = character.play;
  const conditions = play?.conditions ?? [];
  const exhaustion = play?.exhaustion ?? 0;
  const loseAtLimit = hp ? hp.current <= 0 && hp.temporary <= 0 : false;
  const regainAtLimit = hp ? hp.current >= hp.maximum : false;
  const maximumOverridden = Boolean(field('hitPoints')?.override);
  const labels = new Map(allFields.map((f) => [f.field, f.label]));
  const whyValue = whyField ? field(whyField) : undefined;
  const barRef = useRef<HTMLElement>(null);

  // Task 3.3: over a third of the viewport the bar sets data-tall and the stylesheet makes it static. Layout effect: measured before
  // the first paint, so the hint never sits under the bar for a frame. --status-bar-height keeps the hint's room; --status-bar-pin
  // is the page's scroll-padding-top (focus never lands under the sticky bar) and is 0 when the bar is static.
  useLayoutEffect(() => {
    const bar = barRef.current;
    if (!bar) return;
    const root = document.documentElement;
    const measure = () => {
      const height = bar.getBoundingClientRect().height;
      const tall = height > window.innerHeight / 3;
      root.style.setProperty('--status-bar-height', `${height}px`);
      root.style.setProperty('--status-bar-pin', tall ? '0px' : `${height}px`);
      if (tall) bar.setAttribute('data-tall', '');
      else bar.removeAttribute('data-tall');
    };
    measure();
    // Without ResizeObserver the bar is measured once and on window resize (the listener stays either way).
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure);
    observer?.observe(bar);
    window.addEventListener('resize', measure);
    return () => {
      observer?.disconnect();
      window.removeEventListener('resize', measure);
      root.style.removeProperty('--status-bar-height');
      root.style.removeProperty('--status-bar-pin');
    };
  }, []);

  const item = (label: string, value: string): ReactNode => (
    <div className="status-item">
      <span className="status-label">{label}</span> <span className="status-value">{value}</span>
    </div>
  );
  /** A number that opens its trace: "Armor Class 18, show why". The visible text starts the accessible name (WCAG 2.5.3). */
  const numberButton = (id: string, label: string, value: string): ReactNode =>
    field(id) ? (
      <button type="button" className="status-number" aria-expanded={whyField === id} aria-controls="status-why" onClick={() => setWhyField((open) => (open === id ? null : id))}>
        <span className="status-label">{label}</span> <span className="status-value">{value}</span>
        <span className="visually-hidden">, show why</span>
      </button>
    ) : (
      item(label, value)
    );

  return (
    <>
      {abilityBoxesTop && (
      <dl className="summary-abilities">
        {abilities.map((a) => {
          const score = field(`ability.${a}.score`);
          const mod = field(`ability.${a}.mod`);
          if (!score || !mod) return null;
          return (
            <div key={a}>
              <dt>{abilityNames[a]}</dt>
              {/* Owner (2026-10-06): modifier first by default, score first by the "Ability boxes" setting; one markup, CSS `order`.
                  The modifier is what the roll button adds, so it is the large number. DOM (and screen-reader) order stays modifier, score. */}
              <dd className="ability">
                <span className="derived">{signed(mod.value)}</span>{' '}
                <span className="score">
                  {score.value}
                  {score.override && <span className="override-label"> (set by hand)</span>}
                </span>{' '}
                {/* D28 (owner, 2026-10-07): a compact control. The visible word starts the accessible name (WCAG 2.5.3). */}
                <button type="button" className="roll" onClick={() => onRoll(mod.field)}>
                  Roll{rollButtonSuffix(mod)}{' '}
                  <span className="visually-hidden"> {abilityNames[a]} check ({signed(mod.value)})</span>
                </button>
                {mod.override && <span className="override-label"> (modifier set by hand)</span>}
              </dd>
            </div>
          );
        })}
      </dl>
      )}
      <section ref={barRef} className="sheet-summary status-bar" aria-label="Status">
        <div className="status-line">
          {hp && (
            <div className="status-hp">
              {numberButton('hitPoints', 'Hit points', `${hp.current} of ${hp.maximum}${maximumOverridden ? ' (maximum set by hand)' : ''}${hp.temporary > 0 ? `, ${hp.temporary} temporary` : ''}`)}
              {/* ADR-015: one-point adjustments, the same confirmed play command as the Hit points panel. */}
              {/* aria-disabled, not disabled: the click that reaches the limit must not drop focus to <body> (WCAG 2.4.3).
                  Damage at 0 still consumes temporary hit points, so "Lose" is at its limit only with none left. */}
              <span className="quick-hp">
                <button
                  type="button"
                  aria-label="Lose 1 hit point"
                  aria-disabled={loseAtLimit || undefined}
                  onClick={() => {
                    if (loseAtLimit) return;
                    // D43 S7: with damage responses on the sheet, even one point is previewed first (untyped).
                    if (onDamage && (sheet.responses ?? []).length > 0) onDamage(1, '');
                    else act({ action: 'damage', amount: 1 });
                  }}
                >
                  −
                </button>
                <button
                  type="button"
                  aria-label="Regain 1 hit point"
                  aria-disabled={regainAtLimit || undefined}
                  onClick={() => {
                    if (regainAtLimit) return;
                    act({ action: 'heal', amount: 1 });
                  }}
                >
                  +
                </button>
                {/* D38: plain text, not colour alone, and not a live region (damage is already announced by the confirmed change).
                    It sits in this row so the box does not grow when it appears. */}
                {sheet.bloodied && <span className="bloodied-chip">Bloodied</span>}
              </span>
            </div>
          )}
          {numberButton('armorClass', 'Armor Class', show('armorClass'))}
          {numberButton('initiative', 'Initiative', show('initiative'))}
          {numberButton(
            'speed',
            'Speed',
            // Fly, climb and swim: shown above 0, and at 0 when the player set that override (so the override is visible, as on its Stats card).
            show('speed') +
              ([['speed.fly', 'Fly'], ['speed.climb', 'Climb'], ['speed.swim', 'Swim']] as const)
                .map(([id, label]) => {
                  const f = field(id);
                  return f && (f.value > 0 || f.override !== undefined) ? `, ${label} ${f.value} ft.` : '';
                })
                .join(''),
          )}
          {item('Proficiency bonus', show('proficiencyBonus'))}
          <div className="status-item">
            <label className="choice">
              <input
                type="checkbox"
                checked={play?.inspiration ?? false}
                onChange={(e) => act({ action: 'setInspiration', amount: e.target.checked ? 1 : 0 })}
              />{' '}
              {inspirationLabel(rulesFamilies, character.rulesFamily)}
            </label>
          </div>
          {(conditions.length > 0 || exhaustion > 0) &&
            item('Conditions', [...conditions.map(conditionLabel), ...(exhaustion > 0 ? [`exhaustion ${exhaustion}`] : [])].join(', '))}
          {attacksAgainst && item('Attacks against you', attacksAgainst)}
        </div>
        {quickbar}
        <div className="roll-controls">
          <RollModePicker mode={rollMode} onChange={onRollMode} />
          <DiceBox onRoll={onRollDice} />
        </div>
        {/* RollResult is the single live node (role=status); Why and the log stay outside it so opening them announces nothing. */}
        <div className="last-roll roll-strip">
          <RollResult record={lastRoll} resources={sheet.resources ?? []} features={sheet.features ?? []} attacks={sheet.attacks ?? []} act={act} spent={spent} />
          <RollWhy record={lastRoll} />
          <RollLog rolls={rollLog} />
        </div>
        <div id="status-why">{whyValue && <FieldWhy field={whyValue} label={whyValue.label} labels={labels} />}</div>
      </section>
      <p className="hint">
        {/* D73 R13: a quickbar button whose name says "and spend" rolls and then makes that spend once; other pins only roll (Use and toggle pins make their own play change). */}
        {hasQuickbar ? 'Rolling from here never spends anything; a quickbar button that says "and spend" rolls and then spends.' : 'Rolling never spends anything.'}
        {spellsTab
          ? ' Roll a check here; a save, skill or initiative from its field on Skills and saves; an attack or feature on Play; or a spell on Spells.'
          : ' Roll a check here; a save, skill or initiative from its field on Skills and saves; or an attack or feature on Play.'}
      </p>
    </>
  );
}
