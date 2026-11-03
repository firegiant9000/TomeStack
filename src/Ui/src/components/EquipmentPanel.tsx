import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { CharacterView, ContentOption, ContentReference, EquipmentEntry, ItemUseValue, PlayAction } from '../api/types';
import { Pips } from './Pips';
import { directSave, EntryGone, type SheetSave } from './sheetSave';

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  /** Polite status (4.1.3): a refused change is announced here, not as an error. */
  onStatus: (text: string) => void;
  /** The sheet's one save queue (R132); a panel mounted alone saves directly. */
  saveCharacter?: SheetSave;
  /** The sheet's play command (so an Undo is offered, D23). A panel mounted alone sends the command itself. */
  act?: (action: PlayAction) => void | Promise<unknown>;
}

const sameRef = (a: { contentId: string; revisionId: string }, b: { contentId: string; revisionId: string }) =>
  a.contentId === b.contentId && a.revisionId === b.revisionId;

/**
 * R159: an entry is named by its id; an entry without one (a plain item, which can be carried once) by its item. Two copies of a
 * wand therefore never act on each other.
 */
const sameEntry = (a: EquipmentEntry, b: EquipmentEntry) => (a.id || b.id ? a.id === b.id : sameRef(a.item, b.item));

/** The rules allow attuning to three items at a time (both SRDs); more is a warning, never a refusal. */
const attunementLimit = 3;

/** Character schema v11: at most this many mods on one entry (the service refuses more). */
const maxMods = 4;

/**
 * M2 item 4: items carried, and which are equipped. Equipped armor sets the Armor Class (the trace says which item);
 * each change is one explicit action saved with the character.
 */
export function EquipmentPanel({ view, onChanged, onError, onStatus, saveCharacter, act }: Props) {
  const { character } = view;
  const persist = saveCharacter ?? directSave(character);
  const equipment = character.equipment ?? [];
  const [items, setItems] = useState<ContentOption[]>([]);
  const [adding, setAdding] = useState('');
  // The mod chosen per entry (by its id, or its position while it has none), so two copies never share a choice.
  const [picks, setPicks] = useState<Record<string, string>>({});

  useEffect(() => {
    client
      .listContent(character.rulesFamily)
      .then((options) => setItems(options.filter((o) => o.kind === 'item')))
      .catch(onError);
  }, [character.rulesFamily, onError]);

  const nameOf = (entry: EquipmentEntry) => items.find((o) => sameRef(o.reference, entry.item))?.name ?? `Item ${entry.item.revisionId}`;

  // The change is applied to the latest saved list (R132), so entries are matched by their item, not by object identity.
  async function save(change: (list: EquipmentEntry[]) => EquipmentEntry[]) {
    try {
      onChanged(await persist((latest) => ({ ...latest, equipment: change(latest.equipment ?? []) })));
    } catch (error) {
      if (error instanceof EntryGone) onStatus(error.message);
      else onError(error);
    }
  }

  // The new value is the opposite of the latest saved one, not of what this render shows (G10); an item that is gone is refused (G9).
  function toggled(list: EquipmentEntry[], entry: EquipmentEntry): EquipmentEntry[] {
    if (!list.some((e) => sameEntry(e, entry))) throw new EntryGone(`${nameOf(entry)} was removed meanwhile, so nothing was saved.`);
    return list.map((e) => (sameEntry(e, entry) ? { ...e, equipped: !e.equipped } : e));
  }

  // Attunement is an ordinary saved field of the entry; the service gives the entry its id when it has none (R158).
  function attuned(list: EquipmentEntry[], entry: EquipmentEntry): EquipmentEntry[] {
    if (!list.some((e) => sameEntry(e, entry))) throw new EntryGone(`${nameOf(entry)} was removed meanwhile, so nothing was saved.`);
    return list.map((e) => (sameEntry(e, entry) ? { ...e, attuned: e.attuned ? undefined : true } : e));
  }

  // Mods are an ordinary saved field of the entry (R55); an emptied list is dropped, never saved as [].
  function withMods(list: EquipmentEntry[], entry: EquipmentEntry, change: (mods: ContentReference[]) => ContentReference[]): EquipmentEntry[] {
    if (!list.some((e) => sameEntry(e, entry))) throw new EntryGone(`${nameOf(entry)} was removed meanwhile, so nothing was saved.`);
    return list.map((e) => {
      if (!sameEntry(e, entry)) return e;
      const mods = change(e.mods ?? []);
      if (mods.length > maxMods) throw new EntryGone(`${nameOf(entry)} already has ${maxMods} mods, so nothing was saved.`);
      return { ...e, mods: mods.length > 0 ? mods : undefined };
    });
  }

  // Charges are play state: they change only by the play command (R160), never by the save above.
  async function charge(use: ItemUseValue, kind: 'spendCharge' | 'regainCharge', label: string) {
    const left = use.chargesLeft ?? 0;
    if (kind === 'spendCharge' ? left < 1 : use.chargesSpent < 1) {
      onStatus(kind === 'spendCharge' ? `${label} has no charges left.` : `${label} has no spent charges to regain.`);
      return;
    }
    const command: PlayAction = {
      action: kind,
      amount: 1,
      ...(use.entryId ? { entryId: use.entryId } : { entryIndex: use.entryIndex, contentId: use.item.contentId }),
    };
    if (act) {
      await act(command);
      return;
    }
    try {
      onChanged(await client.play(character.id, command));
    } catch (error) {
      onError(error);
    }
  }

  // Carried items keep their names from every revision; new items are offered at their newest revision (SPEC I-06).
  // R159: an item with charges or attunement may be carried again as a separate entry; any other item is one entry with a quantity.
  const uses = view.sheet.itemUses ?? [];
  // Every weapon or armor entry has a row now (for its mods), so a row alone no longer means charges or attunement.
  const repeatable = (o: ContentOption) => uses.some((u) => sameRef(u.item, o.reference) && (u.requiresAttunement || (u.chargesMax !== undefined && u.chargesMax !== null)));
  const modOptions = items.filter((o) => o.compatible && !o.superseded && o.modOf);
  const addable = items.filter((o) => o.compatible && !o.superseded && (!equipment.some((e) => sameRef(e.item, o.reference)) || repeatable(o)));
  const attunedCount = uses.filter((u) => u.attuned).length;

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
            ['Equipped', equipment.map((entry, index) => ({ entry, index })).filter((p) => p.entry.equipped)],
            ['Carried', equipment.map((entry, index) => ({ entry, index })).filter((p) => !p.entry.equipped)],
          ] as const
        ).map(([title, group]) =>
          group.length === 0 ? null : (
            <section key={title} className="equipment-group" aria-labelledby={`equipment-${title.toLowerCase()}`}>
              <h4 id={`equipment-${title.toLowerCase()}`}>{title}</h4>
              <ul className="resources">
                {group.map(({ entry, index }) => {
                  const use = uses.find((u) => u.entryIndex === index);
                  const copies = equipment.filter((e) => sameRef(e.item, entry.item));
                  const name = `${nameOf(entry)}${copies.length > 1 ? ` (copy ${copies.indexOf(entry) + 1} of ${copies.length})` : ''}`;
                  const left = use?.chargesLeft ?? 0;
                  const key = entry.id ?? `#${index}`;
                  const attached = use?.mods ?? [];
                  const offered = !use
                    ? []
                    : modOptions.filter(
                        (o) =>
                          (o.modOf === 'any' ? use.hosts.length > 0 : o.modOf !== undefined && use.hosts.includes(o.modOf)) &&
                          !attached.some((m) => sameRef(m.reference, o.reference)),
                      );
                  const picked = offered.find((o) => o.reference.revisionId === picks[key]);
                  const canAttach = entry.equipped && attached.length < maxMods && offered.length > 0;
                  return (
                    <li key={entry.id ?? `item-${entry.item.revisionId}`} className="resource">
                      <label className="choice">
                        <input type="checkbox" checked={entry.equipped} onChange={() => save((list) => toggled(list, entry))} />
                        Equip {name}
                        {entry.quantity > 1 ? ` (${entry.quantity})` : ''}
                      </label>
                      <button type="button" onClick={() => save((list) => list.filter((e) => !sameEntry(e, entry)))}>
                        Remove {name}
                      </button>
                      {use?.requiresAttunement && entry.equipped && (
                        <label className="choice">
                          <input type="checkbox" checked={use.attuned} onChange={() => save((list) => attuned(list, entry))} />
                          Attuned: {name}
                        </label>
                      )}
                      {use?.requiresAttunement && entry.equipped && !use.attuned && (
                        <p className="hint">{name} is not attuned, so its effects do not apply.</p>
                      )}
                      {attached.length > 0 && (
                        <div className="item-mods">
                          <span>{entry.equipped ? `Mods on ${name}: ${attached.map((m) => m.name).join(', ')}` : `Mods (not equipped): ${attached.map((m) => m.name).join(', ')}`}</span>
                          {entry.equipped &&
                            attached.map((m) => (
                              <button key={m.reference.revisionId} type="button" onClick={() => save((list) => withMods(list, entry, (mods) => mods.filter((r) => !sameRef(r, m.reference))))}>
                                Remove mod {m.name} from {name}
                              </button>
                            ))}
                        </div>
                      )}
                      {canAttach && (
                        <div className="inline-form">
                          <label className="field">
                            Attach a mod to {name}
                            <select value={picked?.reference.revisionId ?? ''} onChange={(e) => setPicks({ ...picks, [key]: e.target.value })}>
                              <option value="">Choose a mod…</option>
                              {offered.map((o) => (
                                <option key={o.reference.revisionId} value={o.reference.revisionId}>
                                  {o.name} ({o.sourceTitle}
                                  {o.page ? `, ${o.page}` : ''})
                                </option>
                              ))}
                            </select>
                          </label>
                          <button
                            type="button"
                            disabled={!picked}
                            aria-label={picked ? `Attach ${picked.name} to ${name}` : `Attach a mod to ${name}`}
                            onClick={() => {
                              if (!picked) return;
                              setPicks({ ...picks, [key]: '' });
                              void save((list) => withMods(list, entry, (mods) => (mods.some((r) => sameRef(r, picked.reference)) ? mods : [...mods, picked.reference])));
                            }}
                          >
                            Attach
                          </button>
                          <button
                            type="button"
                            disabled={!picked}
                            onClick={() => {
                              if (!picked) return;
                              setPicks({ ...picks, [key]: '' });
                              void save((list) => [...list, { item: entry.item, equipped: false, quantity: 1, mods: [picked.reference] }]);
                            }}
                          >
                            Attach to a new copy of {name}
                          </button>
                        </div>
                      )}
                      {use?.chargesMax !== undefined && use.chargesMax !== null && (
                        <div className="charges">
                          <span>
                            {left} of {use.chargesMax} charges left
                          </span>
                          <Pips filled={left} total={use.chargesMax} />
                          <button type="button" aria-disabled={left < 1} onClick={() => void charge(use, 'spendCharge', name)}>
                            Spend a charge: {name}
                          </button>
                          <button type="button" aria-disabled={use.chargesSpent < 1} onClick={() => void charge(use, 'regainCharge', name)}>
                            Regain a charge: {name}
                          </button>
                          {use.recovery && (
                            <span className="hint">
                              Regains {use.recovery.regain ?? 'all charges'} on a {use.recovery.on === 'shortRest' ? 'short' : 'long'} rest.
                            </span>
                          )}
                        </div>
                      )}
                    </li>
                  );
                })}
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
            void save((list) => [...list, { item: option.reference, equipped: false, quantity: 1 }]);
          }}
        >
          Add
        </button>
      </div>
      {attunedCount > attunementLimit && (
        <p className="warnings" role="status">
          {attunedCount} items are attuned. The rules allow attuning to {attunementLimit} at a time; TomeStack does not stop you.
        </p>
      )}
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
export function CurrencyPanel({ view, onChanged, onError, saveCharacter }: Omit<Props, 'onStatus'>) {
  const { character } = view;
  const persist = saveCharacter ?? directSave(character);
  const stored = character.currency ?? emptyCurrency;
  const seed = (c: typeof stored) => Object.fromEntries(coins.map(([k]) => [k, String(c[k])])) as Record<Coin, string>;
  const [draft, setDraft] = useState<Record<Coin, string>>(() => seed(stored));
  // A save, snapshot restore or import changes the stored coins without a remount (the character id is the same), so the
  // draft is re-seeded here, not by a key: a remount would drop focus to the page body (WCAG 2.4.3). Only coins the user
  // has not edited since the last seed take the new value, so a coin typed while a save runs is kept.
  const storedKey = JSON.stringify(stored);
  const [seeded, setSeeded] = useState(storedKey);
  if (seeded !== storedKey) {
    const previous = seed(JSON.parse(seeded) as typeof stored);
    const next = seed(stored);
    setSeeded(storedKey);
    setDraft(Object.fromEntries(coins.map(([k]) => [k, draft[k] === previous[k] ? next[k] : draft[k]])) as Record<Coin, string>);
  }
  const heading = useRef<HTMLHeadingElement>(null);
  const parsed = Object.fromEntries(coins.map(([k]) => [k, Number(draft[k])])) as Record<Coin, number>;
  const valid = coins.every(([k]) => draft[k] !== '' && Number.isInteger(parsed[k]) && parsed[k] >= 0 && parsed[k] <= 1_000_000);
  const changed = coins.some(([k]) => parsed[k] !== stored[k]);

  async function save() {
    if (!valid) return;
    try {
      onChanged(await persist((latest) => ({ ...latest, currency: parsed })));
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
