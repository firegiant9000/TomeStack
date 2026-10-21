import { useRef, useState } from 'react';
import type { CharacterView, DamageDefence, DamageDefenceValue } from '../api/types';
import { damageTypeLabel, damageTypes } from './customModel';
import { directSave, type SheetSave } from './sheetSave';

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  /** The sheet's one save queue (R132); a panel mounted alone saves directly. */
  saveCharacter?: SheetSave;
}

const kindLabels: Record<DamageDefence['kind'], string> = {
  resistance: 'Resistance',
  immunity: 'Immunity',
  vulnerability: 'Vulnerability',
};

const kinds = ['resistance', 'immunity', 'vulnerability'] as const;

const sameDefence = (a: DamageDefence, b: DamageDefence) => a.type === b.type && a.kind === b.kind;

const entryText = (d: DamageDefence | DamageDefenceValue) => `${damageTypeLabel(d.type)} ${d.kind}`;

/**
 * Wave 3 item 21 (D19 amended): the character's damage resistances, immunities and vulnerabilities. The first list is what the
 * sheet applies to typed damage, with where each comes from (content grants are read-only); the editor changes only the
 * character's own list (`damageDefences`), through the sheet's one save path. Each press saves at once.
 */
export function DefencesPanel({ view, onChanged, onError, saveCharacter }: Props) {
  const { character, sheet } = view;
  const persist = saveCharacter ?? directSave(character);
  const own = character.damageDefences ?? [];
  const merged = sheet.damageDefences ?? [];
  const [type, setType] = useState<string>(damageTypes[0]);
  const [kind, setKind] = useState<DamageDefence['kind']>('resistance');
  const heading = useRef<HTMLHeadingElement>(null);
  const duplicate = own.some((d) => sameDefence(d, { type, kind }));

  // The change is applied to the latest saved list (R132). Focus goes to the section heading: the button that was pressed
  // may be gone (Remove) or held (Add, now a duplicate), and focus must not drop to the page body (WCAG 2.4.3).
  async function save(change: (list: DamageDefence[]) => DamageDefence[]) {
    try {
      onChanged(await persist((latest) => ({ ...latest, damageDefences: change(latest.damageDefences ?? []) })));
      heading.current?.focus();
    } catch (error) {
      onError(error);
    }
  }

  return (
    <section aria-labelledby="defences-heading" className="field-group defences">
      <h3 id="defences-heading" tabIndex={-1} ref={heading}>
        Damage resistances, immunities and vulnerabilities
      </h3>
      {merged.length === 0 ? (
        <p className="hint">None. Typed damage on the Play tab is taken in full.</p>
      ) : (
        <ul aria-label="Damage defences the sheet applies">
          {merged.map((d) => (
            <li key={`${d.type}-${d.kind}`}>
              <span className="option-name">{entryText(d)}</span>
              <span className="option-source"> from {d.sources.map((s) => s.name).join(', ')}</span>
            </li>
          ))}
        </ul>
      )}
      <h4 id="defences-own-heading">Your own</h4>
      {own.length === 0 ? (
        <p className="hint">Nothing recorded by hand. Resistances from content you use are listed above.</p>
      ) : (
        <ul aria-labelledby="defences-own-heading">
          {own.map((d) => (
            <li key={`${d.type}-${d.kind}`}>
              {entryText(d)}{' '}
              <button type="button" onClick={() => save((list) => list.filter((x) => !sameDefence(x, d)))}>
                Remove {entryText(d).toLowerCase()}
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="inline-form">
        <label className="field">
          Damage type
          <select value={type} onChange={(e) => setType(e.target.value)}>
            {damageTypes.map((t) => (
              <option key={t} value={t}>
                {damageTypeLabel(t)}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          Kind
          <select value={kind} onChange={(e) => setKind(e.target.value as DamageDefence['kind'])}>
            {kinds.map((k) => (
              <option key={k} value={k}>
                {kindLabels[k]}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          aria-disabled={duplicate || undefined}
          aria-describedby={duplicate ? 'defences-duplicate' : undefined}
          onClick={() => {
            if (duplicate) return; // held: a press sends nothing and stamps nothing
            void save((list) => (list.some((x) => sameDefence(x, { type, kind })) ? list : [...list, { type, kind }]));
          }}
        >
          Add
        </button>
      </div>
      {duplicate && (
        <p id="defences-duplicate" className="hint">
          Already recorded.
        </p>
      )}
    </section>
  );
}
