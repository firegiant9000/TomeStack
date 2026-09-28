import { useEffect, useState } from 'react';
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

  const addable = items.filter((o) => o.compatible && !equipment.some((e) => sameRef(e.item, o.reference)));

  return (
    <section aria-labelledby="equipment-heading" className="play-panel">
      <h3 id="equipment-heading">Equipment</h3>
      {equipment.length === 0 ? (
        <p className="hint">Nothing carried.</p>
      ) : (
        <ul className="resources">
          {equipment.map((entry) => (
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
      <p className="hint">
        Only equipped items apply. Worn armor replaces the unarmored Armor Class, so features such as Unarmored Defense stop applying.
      </p>
    </section>
  );
}
