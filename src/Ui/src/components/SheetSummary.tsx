import type { CharacterView, DerivedValue, PlayAction, RollMode, RollRecord } from '../api/types';
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
    const text = f.units === 'score' ? `${f.value}` : signed(f.value);
    return f.override ? `${text} (overridden)` : text;
  };
  const hp = sheet.hitPoints;
  const play = character.play;
  const conditions = play?.conditions ?? [];
  const exhaustion = play?.exhaustion ?? 0;
  const maximumOverridden = Boolean(field('hitPoints')?.override);

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
              <dd>
                <span className="derived">{score.value}</span>
                {score.override && <span className="override-label"> (overridden)</span>}{' '}
                <button type="button" className="link" onClick={() => onRoll(mod.field)}>
                  Roll {abilityNames[a]} check ({signed(mod.value)})
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
        {hp && (
          <div>
            <dt>Hit points</dt>
            <dd>
              {hp.current} of {hp.maximum}
              {maximumOverridden ? ' (maximum overridden)' : ''}
              {hp.temporary > 0 ? `, ${hp.temporary} temporary` : ''}
            </dd>
          </div>
        )}
        {(sheet.hitDice ?? []).length > 0 && (
          <div>
            <dt>Hit dice</dt>
            <dd>{sheet.hitDice!.map((h) => `d${h.die} ${h.remaining} of ${h.total}`).join(', ')}</dd>
          </div>
        )}
        <div>
          <dt>{inspirationLabel(character.rulesFamily)}</dt>
          <dd>{play?.inspiration ? 'yes' : 'no'}</dd>
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
