import type { ContentRevision, Effect } from '../api/types';
import { bonusText, parseBonus, unreadableStoredBonus } from '../weaponAuthoring';
import { StrictField } from './AttackFields';
import { ModifierFields } from './ModifierFields';

type ReportProblem = (field: string, problem: string | undefined) => void;
type ItemMod = Extract<Effect, { type: 'itemMod' }>;
type Modifier = Extract<Effect, { type: 'modifier' }>;

/** The service accepts at most this many nested modifiers on one item mod (R48). */
export const MAX_MOD_MODIFIERS = 8;

/**
 * Content v12 (MECH-X item 3): the editor of an item mod, which an item's host row takes on. Every change spreads the stored
 * mod, so keys this editor does not show survive an edit; an empty bonus deletes its key, and so does an empty modifier list.
 */
export function ItemModFields(props: {
  effect: ItemMod;
  fields: { id: string; label: string }[];
  previewRevision?: ContentRevision;
  onChange: (effect: Effect) => void;
  onProblem: ReportProblem;
}) {
  const { effect, onChange, onProblem } = props;
  const set = (field: string, value: unknown) => {
    const next: Record<string, unknown> = { ...effect, [field]: value };
    if (value === undefined) delete next[field];
    onChange(next as unknown as Effect);
  };
  const modifiers = effect.modifiers ?? [];
  const setModifiers = (list: Modifier[]) => set('modifiers', list.length === 0 ? undefined : list);
  // A stored string the service would not read as 0 to 10 stays visible and blocks until the author changes it.
  const badBonus = unreadableStoredBonus((effect as unknown as Record<string, unknown>).bonus);
  const parseStoredBonus = (text: string) =>
    badBonus !== undefined && text === badBonus
      ? { value: undefined, problem: `"${badBonus}" is not a bonus the service reads; write a whole number from 1 to 10, or leave it empty` }
      : parseBonus(text);
  const addModifier = () => {
    // The first id not taken, so a removal in the middle never makes a duplicate.
    let n = modifiers.length + 1;
    while (modifiers.some((m) => m.id === `${effect.id}-m${n}`)) n++;
    setModifiers([...modifiers, { type: 'modifier', id: `${effect.id}-m${n}`, operation: 'bonus', target: 'initiative', value: '1' }]);
  };
  return (
    <>
      <label className="field">
        Applies to
        <select value={effect.appliesTo} onChange={(e) => set('appliesTo', e.target.value)}>
          <option value="weapon">Weapons</option>
          <option value="armor">Armor and shields</option>
          <option value="any">Either</option>
        </select>
      </label>
      <StrictField
        id={`${effect.id}-bonus`}
        label="Bonus (1 to 10; empty for none)"
        initial={bonusText((effect as unknown as Record<string, unknown>).bonus)}
        parse={parseStoredBonus}
        onValue={(v) => set('bonus', v)}
        onProblem={onProblem}
      />
      {modifiers.map((m, i) => (
        <fieldset key={m.id} className="effect-editor">
          <legend>Modifier {i + 1} of this mod</legend>
          <ModifierFields effect={m} fields={props.fields} previewRevision={props.previewRevision} nested onChange={(next) => setModifiers(modifiers.map((x, j) => (j === i ? next : x)))} />
          <button type="button" onClick={() => setModifiers(modifiers.filter((_, j) => j !== i))}>
            Remove modifier {i + 1} of this mod
          </button>
        </fieldset>
      ))}
      <button
        type="button"
        onClick={addModifier}
        disabled={modifiers.length >= MAX_MOD_MODIFIERS}
        title={modifiers.length >= MAX_MOD_MODIFIERS ? `A mod takes at most ${MAX_MOD_MODIFIERS} modifiers` : undefined}
      >
        Add a modifier to this mod
      </button>
    </>
  );
}
