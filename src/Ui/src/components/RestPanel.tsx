import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { ChargeRoll, CharacterView, HitDiceValue, HitDieRoll, RestChange, RestPeriod, RestPreview, RollRecord } from '../api/types';

interface Props {
  characterId: string;
  kind: RestPeriod;
  /** The character's hit dice pools; a short rest spends from them. */
  hitDice: HitDiceValue[];
  /** Identity of the character view the proposal is for; a new one (any change to the character) fetches the proposal again. */
  version: unknown;
  onRested: (view: CharacterView, applied: number) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
  /** Every hit-die roll and every item-charge roll, so the sheet can log it (D28); a rest never shows it as the Last roll. */
  onRoll?: (record: RollRecord) => void;
}

/**
 * SPEC C-05, D01: the proposal is shown first, every change ticked. The player unticks what does not apply (for example,
 * no food and drink) and confirms; nothing changes before "Finish … rest". A short rest first asks which hit dice to
 * spend: each is rolled here or entered from the table, and the proposal follows the dice chosen.
 */
export function RestPanel({ characterId, kind, hitDice, version, onRested, onCancel, onError, onRoll }: Props) {
  // The proposal belongs to the view and the hit dice it was worked out for: after any other change (for example "Lose 1 hit
  // point" in the summary, or a die added or removed) it is stale and fetched again. The last proposal stays on screen
  // meanwhile (so a focused control is not unmounted) but "Finish" waits for the one for the current view and dice.
  const [fetched, setFetched] = useState<{ preview: RestPreview; version: unknown; rolls: HitDieRoll[]; chargeRolls: ChargeRoll[] }>();
  const [rolls, setRolls] = useState<HitDieRoll[]>([]);
  // Wave 3 item 25: the totals rolled for the dice an item regains charges with, one per entry (the same way as the hit dice).
  const [chargeRolls, setChargeRolls] = useState<ChargeRoll[]>([]);
  const [failure, setFailure] = useState<{ version: unknown; rolls: HitDieRoll[]; chargeRolls: ChargeRoll[] }>();
  const preview = fetched?.preview;
  const ready = fetched !== undefined && fetched.version === version && fetched.rolls === rolls && fetched.chargeRolls === chargeRolls;
  const failed = failure !== undefined && failure.version === version && failure.rolls === rolls && failure.chargeRolls === chargeRolls;
  const [skipped, setSkipped] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const name = kind === 'shortRest' ? 'Short rest' : 'Long rest';

  useEffect(() => {
    let current = true;
    client
      .restPreview(characterId, kind, rolls, chargeRolls)
      .then((next) => {
        if (current) setFetched({ preview: next, version, rolls, chargeRolls });
      })
      .catch((error) => {
        if (!current) return;
        setFailure({ version, rolls, chargeRolls });
        onError(error);
      });
    return () => {
      current = false;
    };
  }, [characterId, kind, rolls, chargeRolls, version, onError]);

  // WCAG 2.4.3: the panel opens with focus on its heading.
  useEffect(() => heading.current?.focus(), []);

  // A new set of dice is a new proposal: the old one stays until it arrives, and "Finish" waits for the one the player will see.
  function changeRolls(change: (current: HitDieRoll[]) => HitDieRoll[]) {
    setRolls(change);
  }

  // A roll in flight already counts against the dice left (the service checks only the die size), so a second press of Roll or
  // Add cannot spend a die that is not there.
  const [pending, setPending] = useState<Record<number, number>>({});
  const bump = (die: number, by: number) => setPending((p) => ({ ...p, [die]: (p[die] ?? 0) + by }));

  async function roll(die: number) {
    bump(die, 1);
    try {
      const record = await client.roll(characterId, { hitDie: die });
      changeRolls((r) => [...r, { die, roll: record.dice[0]!.value }]);
      onRoll?.(record);
    } catch (error) {
      onError(error);
    } finally {
      bump(die, -1);
    }
  }

  function remove(index: number) {
    if (!ready) return; // the list shown may be for other dice than the current ones
    // The removed die's row goes when the new proposal arrives, so focus moves to the heading now (WCAG 2.4.3).
    heading.current?.focus();
    changeRolls((r) => r.filter((_, i) => i !== index));
  }

  // The charge rows: asking for a roll, or showing the one that was made. A roll in flight counts against finishing, like a hit die.
  const [chargePending, setChargePending] = useState(0);

  async function rollCharges(change: RestChange) {
    const entry = change.resourceId;
    if (!entry || !ready) return;
    setChargePending((n) => n + 1);
    try {
      const record = await client.roll(characterId, { chargeEntry: entry });
      setChargeRolls((r) => [...r.filter((c) => c.entry !== entry), { entry, roll: record.total }]);
      onRoll?.(record);
    } catch (error) {
      onError(error);
    } finally {
      setChargePending((n) => n - 1);
    }
  }

  function addChargeRoll(change: RestChange, value: number) {
    const entry = change.resourceId;
    if (entry) setChargeRolls((r) => [...r.filter((c) => c.entry !== entry), { entry, roll: value }]);
  }

  function clearChargeRoll(change: RestChange) {
    // The row asks for a roll again once the new proposal arrives, so focus moves to the heading now (WCAG 2.4.3).
    if (!ready) return;
    heading.current?.focus();
    setChargeRolls((r) => r.filter((c) => c.entry !== change.resourceId));
  }

  // A roll in flight is a die the proposal does not hold yet: finishing now would leave it out (and spend it anyway).
  const rollingAny = Object.values(pending).some((n) => n > 0) || chargePending > 0;

  async function finish() {
    if (!preview || !ready || failed || busy || rollingAny) return;
    setBusy(true);
    try {
      // A charges row still waiting for its roll applies nothing, so it is not counted.
      const applied = preview.changes.filter((c) => !skipped.includes(c.id) && !(c.kind === 'charges' && c.spentAfter == null && c.dice)).length;
      onRested(await client.rest(characterId, kind, preview.basis, skipped, rolls, chargeRolls), applied);
    } catch (error) {
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  const diceCharges = preview?.changes.filter((c) => c.kind === 'charges' && c.dice) ?? [];
  const toggles = preview?.changes.filter((c) => c.kind !== 'hitDie' && !diceCharges.includes(c)) ?? [];
  const spent = preview?.changes.filter((c) => c.kind === 'hitDie') ?? [];

  return (
    <section aria-labelledby="rest-heading" className="play-panel">
      <h3 id="rest-heading" tabIndex={-1} ref={heading}>
        {name}
      </h3>
      {kind === 'shortRest' && (
        <fieldset>
          <legend>Spend hit dice (each heals its roll plus your Constitution modifier)</legend>
          {hitDice.length === 0 && <p className="hint">This character has no hit dice.</p>}
          {hitDice.map((pool) => (
            <HitDiePicker
              key={pool.die}
              pool={pool}
              left={pool.remaining - rolls.filter((r) => r.die === pool.die).length - (pending[pool.die] ?? 0)}
              onRoll={() => roll(pool.die)}
              onAdd={(value) => changeRolls((r) => [...r, { die: pool.die, roll: value }])}
            />
          ))}
          {spent.length > 0 && (
            <ul aria-label="Hit dice to spend">
              {spent.map((change, index) => (
                <li key={change.id}>
                  {change.label}: {change.reason}. Hit points {change.from} → {change.to}{' '}
                  <button type="button" onClick={() => remove(index)} aria-disabled={!ready}>
                    Remove {change.label.replace('Spend a ', '')} ({fetched?.rolls[index]?.roll})
                  </button>
                </li>
              ))}
            </ul>
          )}
        </fieldset>
      )}
      {failed && <p className="error">Could not work out this rest. Cancel and try again.</p>}
      {!preview ? (
        !failed && <p className="hint">Working out what a {name.toLowerCase()} recovers…</p>
      ) : (
        <>
          {!ready && !failed && <p className="hint">Updating the proposal…</p>}
          {diceCharges.length > 0 && (
            <fieldset>
              <legend>Item charges (roll the dice to see how many come back)</legend>
              {diceCharges.map((change) =>
                change.spentAfter == null ? (
                  <ChargeRollPicker key={change.id} change={change} onRoll={() => rollCharges(change)} onAdd={(value) => addChargeRoll(change, value)} />
                ) : (
                  <div key={change.id} className="inline-form">
                    <label className="choice">
                      <input
                        type="checkbox"
                        checked={!skipped.includes(change.id)}
                        onChange={() => setSkipped((s) => (s.includes(change.id) ? s.filter((id) => id !== change.id) : [...s, change.id]))}
                      />
                      {change.label}: {change.from} → {change.to}
                      <span className="option-source">{change.reason}</span>
                    </label>
                    <button type="button" aria-disabled={!ready} onClick={() => clearChargeRoll(change)}>
                      Clear the roll: {change.label}
                    </button>
                  </div>
                ),
              )}
            </fieldset>
          )}
          {toggles.length === 0 && spent.length === 0 && diceCharges.length === 0 ? (
            <p>A {name.toLowerCase()} would change nothing{kind === 'shortRest' ? ' yet' : ''}.</p>
          ) : (
            toggles.length > 0 && (
              <fieldset>
                <legend>These changes will be applied. Untick any that do not apply.</legend>
                {toggles.map((change) => (
                  <label key={change.id} className="choice">
                    <input
                      type="checkbox"
                      checked={!skipped.includes(change.id)}
                      onChange={() => setSkipped((s) => (s.includes(change.id) ? s.filter((id) => id !== change.id) : [...s, change.id]))}
                    />
                    {change.label}: {change.from} → {change.to}
                    <span className="option-source">
                      {change.reason}
                      {change.condition ? `. ${change.condition}` : ''}
                    </span>
                  </label>
                ))}
              </fieldset>
            )
          )}
          {preview.manual.length > 0 && (
            <>
              <p>Do these by hand:</p>
              <ul className="warnings" aria-label="Rest steps to do by hand">
                {preview.manual.map((m) => (
                  <li key={`${m.code}-${m.content?.revisionId ?? ''}-${m.effectId ?? ''}`}>{m.message}</li>
                ))}
              </ul>
            </>
          )}
        </>
      )}
      <div className="actions">
        <button type="button" onClick={finish} aria-disabled={!ready || failed || busy || rollingAny}>
          {busy ? 'Resting…' : `Finish ${name.toLowerCase()}`}
        </button>
        <button type="button" onClick={() => !busy && onCancel()} aria-disabled={busy}>
          Cancel rest
        </button>
      </div>
    </section>
  );
}

function ChargeRollPicker({ change, onRoll, onAdd }: { change: RestChange; onRoll: () => void; onAdd: (value: number) => void }) {
  const [entered, setEntered] = useState('');
  const value = Number(entered);
  const valid = entered !== '' && Number.isInteger(value) && value >= 0;
  const item = change.label.replace(/ charges$/, '');
  return (
    <div className="inline-form">
      <span>
        {change.label}: {change.from} left. Regains {change.dice}.
      </span>
      <button type="button" onClick={onRoll}>
        Roll {change.dice}: {item}
      </button>
      <label className="field">
        {item}: {change.dice} rolled at the table (total)
        <input type="number" min={0} value={entered} onChange={(e) => setEntered(e.target.value)} />
      </label>
      <button
        type="button"
        aria-disabled={!valid}
        onClick={() => {
          if (!valid) return;
          onAdd(value);
          setEntered('');
        }}
      >
        Add the {change.dice} total: {item}
      </button>
    </div>
  );
}

function HitDiePicker({ pool, left, onRoll, onAdd }: { pool: HitDiceValue; left: number; onRoll: () => void; onAdd: (value: number) => void }) {
  const [entered, setEntered] = useState('');
  const value = Number(entered);
  const valid = entered !== '' && Number.isInteger(value) && value >= 1 && value <= pool.die;
  return (
    <div className="inline-form">
      <span>
        d{pool.die}: {left} of {pool.total} left ({pool.classes.join(', ')})
      </span>
      <button type="button" aria-disabled={left <= 0} onClick={() => left > 0 && onRoll()}>
        Roll a d{pool.die}
      </button>
      <label className="field">
        d{pool.die} rolled at the table
        <input type="number" min={1} max={pool.die} value={entered} onChange={(e) => setEntered(e.target.value)} />
      </label>
      <button
        type="button"
        aria-disabled={left <= 0 || !valid}
        onClick={() => {
          if (left <= 0 || !valid) return;
          onAdd(value);
          setEntered('');
        }}
      >
        Add d{pool.die}
      </button>
    </div>
  );
}
