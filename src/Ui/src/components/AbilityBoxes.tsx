import type { DerivedValue } from '../api/types';
import { rollButtonSuffix } from '../rollModes';

const abilities = ['str', 'dex', 'con', 'int', 'wis', 'cha'] as const;
const abilityNames: Record<(typeof abilities)[number], string> = {
  str: 'Strength',
  dex: 'Dexterity',
  con: 'Constitution',
  int: 'Intelligence',
  wis: 'Wisdom',
  cha: 'Charisma',
};
const signed = (n: number) => (n >= 0 ? `+${n}` : `${n}`);

/**
 * The six ability boxes (one `dl`): a modifier, a score and a Roll button each. Shared by the Abilities panel on Play and by the
 * "boxes at the top of the sheet" setting, which renders the same list above the status bar. The ability-order setting is CSS only.
 */
export function AbilityBoxes({ fields, onRoll }: { fields: DerivedValue[]; onRoll: (field: string) => void }) {
  const field = (id: string): DerivedValue | undefined => fields.find((f) => f.field === id);
  return (
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
  );
}
