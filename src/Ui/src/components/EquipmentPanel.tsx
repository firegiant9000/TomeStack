import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { CharacterView, ContentOption, EquipmentEntry } from '../api/types';

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
}

const sameRef = (a: { contentId: string; revisionId: string }, b: { contentId: string; revisionId: string }) =>
  a.contentId === b.contentId && a.revisionId === b.revisionId;

/**
 * M2 item 4: items carried, and which are equipped. Equipped armor sets the Armor Class (the trace says which item);
 * each change is one explicit action saved with the character.
 */
export function EquipmentPanel({ view, onChanged, onError }: Props) {
  const { character } = view;
  const equipment = character.equipment ?? [];
  const [items, setItems] = useState<ContentOption[]>([]);
  const [adding, setAdding] = useState('');

  useEffect(() => {
    client
      .listContent(character.rulesFamily)
      .then((options) => setItems(options.filter((o) => o.kind === 'item')))
      .catch(onError);
  }, [character.rulesFamily, onError]);

  const nameOf = (entry: EquipmentEntry) => items.find((o) => sameRef(o.reference, entry.item))?.name ?? `Item ${entry.item.revisionId}`;

  async function save(next: EquipmentEntry[]) {
    try {
      onChanged(await client.saveCharacter({ ...character, equipment: next }));
    } catch (error) {
      onError(error);
    }
  }

  // Carried items keep their names from every revision; new items are offered at their newest revision (SPEC I-06).
  const addable = items.filter((o) => o.compatible && !o.superseded && !equipment.some((e) => sameRef(e.item, o.reference)));

  // Investigation 2026-10-05: an imported 2014 sheet creates every item unequipped, and only equipped items apply.
  const unequipped = equipment.filter((e) => !e.equipped).length;

  return (
    <section aria-labelledby="equipment-heading" className="play-panel">
      <h3 id="equipment-heading">Equipment</h3>
      {equipment.length === 0 ? (
        <p className="hint">Nothing carried.</p>
      ) : (
        // Investigation 2026-10-06 item 8: equipped and carried apart, as a paper sheet lists them. Names are unchanged.
        (
          [
            ['Equipped', equipment.filter((e) => e.equipped)],
            ['Carried', equipment.filter((e) => !e.equipped)],
          ] as const
        ).map(([title, group]) =>
          group.length === 0 ? null : (
            <section key={title} className="equipment-group" aria-labelledby={`equipment-${title.toLowerCase()}`}>
              <h4 id={`equipment-${title.toLowerCase()}`}>{title}</h4>
              <ul className="resources">
                {group.map((entry) => (
                  <li key={entry.item.revisionId} className="resource">
                    <label className="choice">
                      <input
                        type="checkbox"
                        checked={entry.equipped}
                        onChange={() => save(equipment.map((e) => (e === entry ? { ...e, equipped: !e.equipped } : e)))}
                      />
                      Equip {nameOf(entry)}
                      {entry.quantity > 1 ? ` (${entry.quantity})` : ''}
                    </label>
                    <button type="button" onClick={() => save(equipment.filter((e) => e !== entry))}>
                      Remove {nameOf(entry)}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ),
        )
      )}
      <div className="inline-form">
        <label className="field">
          Add an item
          <select value={adding} onChange={(e) => setAdding(e.target.value)}>
            <option value="">Choose an item…</option>
            {addable.map((o) => (
              <option key={o.reference.revisionId} value={o.reference.revisionId}>
                {o.name} ({o.sourceTitle}
                {o.page ? `, ${o.page}` : ''})
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          disabled={!adding}
          onClick={() => {
            const option = addable.find((o) => o.reference.revisionId === adding);
            if (!option) return;
            setAdding('');
            void save([...equipment, { item: option.reference, equipped: false, quantity: 1 }]);
          }}
        >
          Add
        </button>
      </div>
      {unequipped > 0 && (
        <p className="hint">
          {unequipped === 1 ? '1 carried item is' : `${unequipped} carried items are`} not equipped. Equip a weapon to list its
          attack on Play, or armor to use it for Armor Class.
        </p>
      )}
      <p className="hint">
        Only equipped items apply. Worn armor replaces the unarmored Armor Class, so features such as Unarmored Defense stop applying.
      </p>
    </section>
  );
}

const coins = [
  ['cp', 'Copper (cp)'],
  ['sp', 'Silver (sp)'],
  ['ep', 'Electrum (ep)'],
  ['gp', 'Gold (gp)'],
  ['pp', 'Platinum (pp)'],
] as const;
type Coin = (typeof coins)[number][0];
const emptyCurrency = { cp: 0, sp: 0, ep: 0, gp: 0, pp: 0 };

/** Character schema v8 (D21): coins, saved with the character like equipment. Plain counts; nothing is converted. */
export function CurrencyPanel({ view, onChanged, onError }: Props) {
  const { character } = view;
  const stored = character.currency ?? emptyCurrency;
  const seed = (c: typeof stored) => Object.fromEntries(coins.map(([k]) => [k, String(c[k])])) as Record<Coin, string>;
  const [draft, setDraft] = useState<Record<Coin, string>>(() => seed(stored));
  // A save, snapshot restore or import changes the stored coins without a remount (the character id is the same), so the
  // draft is re-seeded here, not by a key: a remount would drop focus to the page body (WCAG 2.4.3).
  const storedKey = JSON.stringify(stored);
  const [seeded, setSeeded] = useState(storedKey);
  if (seeded !== storedKey) {
    setSeeded(storedKey);
    setDraft(seed(stored));
  }
  const heading = useRef<HTMLHeadingElement>(null);
  const parsed = Object.fromEntries(coins.map(([k]) => [k, Number(draft[k])])) as Record<Coin, number>;
  const valid = coins.every(([k]) => draft[k] !== '' && Number.isInteger(parsed[k]) && parsed[k] >= 0 && parsed[k] <= 1_000_000);
  const changed = coins.some(([k]) => parsed[k] !== stored[k]);

  async function save() {
    if (!valid) return;
    try {
      onChanged(await client.saveCharacter({ ...character, currency: parsed }));
      heading.current?.focus(); // "Save currency" is disabled now that nothing differs: focus goes to the section
    } catch (error) {
      onError(error);
    }
  }

  return (
    <section aria-labelledby="currency-heading" className="play-panel">
      <h3 id="currency-heading" tabIndex={-1} ref={heading}>
        Currency
      </h3>
      <div className="inline-form">
        {coins.map(([key, label]) => (
          <label key={key} className="field">
            {label}
            <input type="number" min={0} max={1_000_000} step={1} value={draft[key]} onChange={(e) => setDraft({ ...draft, [key]: e.target.value })} />
          </label>
        ))}
        <button type="button" disabled={!valid || !changed} onClick={save}>
          Save currency
        </button>
      </div>
    </section>
  );
}
