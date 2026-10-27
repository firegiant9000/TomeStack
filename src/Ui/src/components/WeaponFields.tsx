import { useRef } from 'react';
import { bonusText, parseBonus, unreadableStoredBonus, parseDamageType, parseOptionalDice, parseProperties, parseRange, parseWeaponDice, parseWeaponKey, weaponReads, type Weapon } from '../weaponAuthoring';
import type { Effect } from '../api/types';
import { StrictField } from './AttackFields';

type ReportProblem = (field: string, problem: string | undefined) => void;

/**
 * Content v12 (MECH-X item 0): the editor of an item's weapon. Every change spreads the stored weapon, so the fields this
 * editor does not show (automation, text, unknown keys) and the order of the properties survive an edit; an empty value
 * deletes its key. A weapon that does not read gets a notice instead of fields (it can still be removed).
 */
export function WeaponFields(props: { effect: Weapon; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
  const { effect, onChange, onProblem } = props;
  // Whether the weapon came with a properties key: clearing the field then writes [], otherwise it adds no key.
  const hadProperties = useRef(Object.prototype.hasOwnProperty.call(effect, 'properties'));
  if (!weaponReads(effect)) return <p className="hint">This weapon doesn&apos;t read, so it can&apos;t be edited here.</p>;
  const set = (field: string, value: unknown) => {
    const next: Record<string, unknown> = { ...effect, [field]: value };
    if (value === undefined) delete next[field];
    onChange(next as unknown as Effect);
  };
  // A stored string the service would not read as 0 to 10 stays visible and blocks until the author changes it.
  const badBonus = unreadableStoredBonus((effect as unknown as Record<string, unknown>).bonus);
  const parseStoredBonus = (text: string) =>
    badBonus !== undefined && text === badBonus
      ? { value: undefined, problem: `"${badBonus}" is not a bonus the service reads; write a whole number from 1 to 10, or leave it empty` }
      : parseBonus(text);
  return (
    <>
      <StrictField id={`${effect.id}-key`} label="Weapon key (a name the sheet uses; set once)" initial={effect.weaponKey} parse={parseWeaponKey} onValue={(v) => set('weaponKey', v)} onProblem={onProblem} />
      <label className="field">
        Category
        <select value={effect.category} onChange={(e) => set('category', e.target.value)}>
          <option value="simple">Simple</option>
          <option value="martial">Martial</option>
        </select>
      </label>
      <label className="field">
        Attack
        <select value={effect.attack} onChange={(e) => set('attack', e.target.value)}>
          <option value="melee">Melee</option>
          <option value="ranged">Ranged</option>
        </select>
      </label>
      <StrictField id={`${effect.id}-damage`} label="Damage (dice such as 1d8 or 1d8+1)" initial={effect.damage} parse={parseWeaponDice} onValue={(v) => set('damage', v)} onProblem={onProblem} />
      <StrictField id={`${effect.id}-type`} label="Damage type" initial={effect.damageType} parse={parseDamageType} onValue={(v) => set('damageType', v)} onProblem={onProblem} />
      <StrictField
        id={`${effect.id}-properties`}
        label="Properties (separated by commas, such as light, finesse)"
        initial={(effect.properties ?? []).join(', ')}
        parse={parseProperties}
        onValue={(v) => set('properties', v.length === 0 && !hadProperties.current ? undefined : v)}
        onProblem={onProblem}
      />
      <StrictField id={`${effect.id}-versatile`} label="Versatile damage (two-handed dice; empty for none)" initial={effect.versatile ?? ''} parse={parseOptionalDice} onValue={(v) => set('versatile', v)} onProblem={onProblem} />
      <StrictField id={`${effect.id}-range`} label="Range (such as 80/320; empty for none)" initial={effect.range ?? ''} parse={parseRange} onValue={(v) => set('range', v)} onProblem={onProblem} />
      <label className="field">
        Mastery (empty for none)
        <input value={effect.mastery ?? ''} onChange={(e) => set('mastery', e.target.value || undefined)} />
      </label>
      <StrictField
        id={`${effect.id}-bonus`}
        label="Bonus to hit and damage (1 to 10; empty for none)"
        initial={bonusText((effect as unknown as Record<string, unknown>).bonus)}
        parse={parseStoredBonus}
        onValue={(v) => set('bonus', v)}
        onProblem={onProblem}
      />
    </>
  );
}
