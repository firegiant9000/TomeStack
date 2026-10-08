import type { CharacterView, DerivedValue, PlayAction, RollMode, RollRecord } from '../api/types';
import { hitDiceText } from '../format';
import { inspirationLabel, RollModePicker, RollResult } from './PlayPanels';

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
  act: (action: PlayAction) => void;
  /** Rolls a d20 test for a field id (for example `ability.str.mod`) in the current mode. */
  onRoll: (field: string) => void;
  /** True when the sheet offers a Spells tab; the hint names it only then. */
  spellsTab?: boolean;
}

/**
 * ADR-014 (slice 2): the numbers a player looks at every turn, always visible above the tabs. A definition list, never
 * headings or named regions: the panels own "Hit points: …" and "Armor Class: …" (two regions with one name break
 * `getByRole` and confuse screen readers). Traces and overrides stay on the field cards (Stats, Spells). An overridden
 * value says so in text (WCAG 1.4.1). The d20 roll mode and the last roll live here so they are reachable from every tab.
 */
export function SheetSummary({ view, rollMode, onRollMode, lastRoll, act, onRoll, spellsTab = false }: Props) {
  const { character, sheet } = view;
  const field = (id: string): DerivedValue | undefined => sheet.fields.find((f) => f.field === id);
  const show = (id: string): string => {
    const f = field(id);
    if (!f) return '—';
    const text = f.units === 'modifier' || f.units === 'bonus' ? signed(f.value) : f.units === 'feet' ? `${f.value} ft.` : `${f.value}`;
    return f.override ? `${text} (overridden)` : text;
  };
  const hp = sheet.hitPoints;
  const play = character.play;
  const conditions = play?.conditions ?? [];
  const exhaustion = play?.exhaustion ?? 0;
  const loseAtLimit = hp ? hp.current <= 0 && hp.temporary <= 0 : false;
  const regainAtLimit = hp ? hp.current >= hp.maximum : false;
  const maximumOverridden =Boolean(field('hitPoints')?.override);

  return (
    <section className="sheet-summary" role="region" aria-label="Summary">
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
                  {score.override && <span className="override-label"> (overridden)</span>}
                </span>{' '}
                {/* D28 (owner, 2026-10-07): a compact control. The visible word starts the accessible name (WCAG 2.5.3). */}
                <button type="button" className="roll" onClick={() => onRoll(mod.field)}>
                  Roll{' '}
                  <span className="visually-hidden"> {abilityNames[a]} check ({signed(mod.value)})</span>
                </button>
                {mod.override && <span className="override-label"> (modifier overridden)</span>}
              </dd>
            </div>
          );
        })}
      </dl>
      <dl className="summary-line">
        <div>
          <dt>Proficiency bonus</dt>
          <dd>{show('proficiencyBonus')}</dd>
        </div>
        <div>
          <dt>Armor Class</dt>
          <dd>{show('armorClass')}</dd>
        </div>
        <div>
          <dt>Initiative</dt>
          <dd>{show('initiative')}</dd>
        </div>
        <div>
          <dt>Speed</dt>
          <dd>{show('speed')}</dd>
        </div>
        {hp && (
          <div>
            <dt>Hit points</dt>
            <dd>
              {hp.current} of {hp.maximum}
              {maximumOverridden ? ' (maximum overridden)' : ''}
              {hp.temporary > 0 ? `, ${hp.temporary} temporary` : ''}
            </dd>
            {/* ADR-015: one-point adjustments, the same confirmed play command as the Hit points panel. A second <dd> so the
                first keeps only the text (summaryValue reads the first). */}
            {/* aria-disabled, not disabled: the click that reaches the limit must not drop focus to <body> (WCAG 2.4.3).
                Damage at 0 still consumes temporary hit points, so "Lose" is at its limit only with none left. */}
            <dd className="quick-hp">
              <button
                type="button"
                aria-label="Lose 1 hit point"
                aria-disabled={loseAtLimit || undefined}
                onClick={() => {
                  if (loseAtLimit) return;
                  act({ action: 'damage', amount: 1 });
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
            </dd>
          </div>
        )}
        {(sheet.hitDice ?? []).length > 0 && (
          <div>
            <dt>Hit dice</dt>
            <dd>{hitDiceText(sheet.hitDice!)}</dd>
          </div>
        )}
        <div>
          <dt aria-hidden="true">{inspirationLabel(character.rulesFamily)}</dt>
          <dd>
            <label className="choice">
              <input
                type="checkbox"
                checked={play?.inspiration ?? false}
                onChange={(e) => act({ action: 'setInspiration', amount: e.target.checked ? 1 : 0 })}
              />{' '}
              {inspirationLabel(character.rulesFamily)}
            </label>
          </dd>
        </div>
        {(conditions.length > 0 || exhaustion > 0) && (
          <div>
            <dt>Conditions</dt>
            <dd>{[...conditions.map(conditionLabel), ...(exhaustion > 0 ? [`exhaustion ${exhaustion}`] : [])].join(', ')}</dd>
          </div>
        )}
      </dl>
      <RollModePicker mode={rollMode} onChange={onRollMode} />
      <p className="hint">
        {spellsTab
          ? 'Rolling never spends anything. Roll a check here; a save, skill or initiative from its field on Stats; an attack or feature on Play; or a spell on Spells.'
          : 'Rolling never spends anything. Roll a check here; a save, skill or initiative from its field on Stats; or an attack or feature on Play.'}
      </p>
      <RollResult record={lastRoll} resources={sheet.resources ?? []} features={sheet.features ?? []} act={act} />
    </section>
  );
}
