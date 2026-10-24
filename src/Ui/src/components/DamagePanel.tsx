import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { CharacterView, DamagePreview, ResponseChoice, ResponseOffer, PlayAction, ResponseValue, RollTarget } from '../api/types';
import { damageTypeLabel } from './customModel';
import { effectWords, skippedLabelsOf } from './PlayPanels';

interface Props {
  /** The sheet the damage is taken on; its stamp keys the previews, its `responses` name the skipped ones. */
  view: CharacterView;
  amount: number;
  /** One of the thirteen damage types; '' is untyped. */
  type: string;
  /** The reply of the applied damage. The panel stays open (Close) while the reply carries prompts, and closes itself otherwise. */
  onDone: (view: CharacterView) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
  /** The confirm timed out (the damage may or may not have been applied): the sheet reloads the character, says so and closes the panel. */
  onTimeout: () => void;
  /** The sheet's one-step play change, used when Apply is pressed with no response chosen (so Undo works as for any plain damage). */
  act: (action: PlayAction) => void | Promise<boolean>;
  /** Rolls an offered roll through the sheet's one roll call. */
  onRoll: (target: RollTarget) => void;
  /** Runs the confirm in the sheet's save queue. Direct when absent. */
  run?: (command: () => Promise<CharacterView>) => Promise<CharacterView>;
}

const keyOf = (c: ResponseChoice) => `${c.contentId}|${c.responseId}`;
const choiceFromKey = (key: string): ResponseChoice => {
  const at = key.indexOf('|');
  return { contentId: key.slice(0, at), responseId: key.slice(at + 1) };
};

/** A preview with the selection (and the sheet stamp and refresh round) it was asked for. */
interface Fetched {
  preview: DamagePreview;
  version: string;
  refresh: number;
  selKey: string;
}

/**
 * D43 slice 7: one hit, with the responses the player may choose, shown before anything changes (the turn panel's pattern).
 *
 * The preview is fetched on open and again on every selection change; Apply sends the selection with the stamp of the preview on
 * screen, so a change in between is refused `character.stale` and the panel previews again. A selected response that the latest
 * preview for that selection no longer offers is dropped (adjusting state during render, guarded), so a stale choice never reaches
 * the confirm. When the preview carries notes the choices cannot be applied: `after` is then the current state, so Apply is off.
 * Every number is the server's.
 */
export function DamagePanel({ view, amount, type, onDone, onCancel, onError, onTimeout, onRoll, act, run }: Props) {
  const characterId = view.character.id;
  const version = view.character.updatedAt ?? '';
  const responses = view.sheet.responses ?? [];
  const [selection, setSelection] = useState<ResponseChoice[]>([]);
  const [fetched, setFetched] = useState<Fetched>();
  const [refresh, setRefresh] = useState(0); // bumped after a refused or timed-out confirm, to preview again
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const [applied, setApplied] = useState<{ reply: CharacterView; shown: DamagePreview }>();
  const [message, setMessage] = useState(''); // "The sheet changed…"
  const [available, setAvailable] = useState<string[]>([]); // thresholds the latest selection newly offers
  const offeredThresholds = useRef<Set<string> | undefined>(undefined);
  const heading = useRef<HTMLHeadingElement>(null);
  const groupName = useId();

  // WCAG 2.4.3: the panel opens with focus on its heading.
  useEffect(() => heading.current?.focus(), []);

  const selKey = JSON.stringify(selection.map(keyOf).sort());

  useEffect(() => {
    if (applied) return;
    let current = true;
    const choices = (JSON.parse(selKey) as string[]).map(choiceFromKey);
    client
      .damagePreview(characterId, amount, type || undefined, choices)
      .then((next) => {
        if (!current) return;
        const now = next.offers.filter((o) => o.offered && o.response.phase === 'threshold');
        const before = offeredThresholds.current;
        setAvailable(before ? now.filter((o) => !before.has(keyOf(o.response))).map((o) => o.response.label) : []);
        offeredThresholds.current = new Set(now.map((o) => keyOf(o.response)));
        setFailed(false);
        setFetched({ preview: next, version, refresh, selKey });
      })
      .catch((error) => {
        if (!current) return;
        setFailed(true);
        onError(error);
      });
    return () => {
      current = false;
    };
  }, [characterId, amount, type, version, refresh, selKey, applied, onError]);

  // A choice the preview for exactly this selection does not offer is dropped, so it is neither shown as chosen nor sent.
  if (fetched && fetched.selKey === selKey && !applied) {
    const offered = new Set(fetched.preview.offers.filter((o) => o.offered).map((o) => keyOf(o.response)));
    const kept = selection.filter((c) => offered.has(keyOf(c)));
    if (kept.length !== selection.length) setSelection(kept);
  }

  const preview = fetched?.preview;
  // The preview must also have been planned from the stamp this sheet shows: a newer stored stamp means the sheet is behind.
  const ready =
    fetched !== undefined && fetched.version === version && fetched.refresh === refresh && fetched.selKey === selKey && fetched.preview.updatedAt === version;
  const notes = preview?.notes ?? [];
  const canApply = ready && !busy && !failed && notes.length === 0 && !applied;
  const phaseOf = (c: ResponseChoice) => responses.find((r) => r.contentId === c.contentId && r.responseId === c.responseId)?.phase;
  const chosen = (r: ResponseValue) => selection.some((c) => keyOf(c) === keyOf(r));
  const poolLabel = (r: ResponseValue) =>
    view.sheet.resources?.find((p) => p.content.contentId === r.costResourceContent && p.resourceId === r.resourceId)?.label ?? r.resourceId ?? 'points';

  function toggle(r: ResponseValue) {
    setMessage('');
    setSelection((s) => (s.some((c) => keyOf(c) === keyOf(r)) ? s.filter((c) => keyOf(c) !== keyOf(r)) : [...s, { contentId: r.contentId, responseId: r.responseId }]));
  }

  /** One survival at most: choosing one replaces any other; None clears it. */
  function pickOutcome(r?: ResponseValue) {
    setMessage('');
    setSelection((s) => [...s.filter((c) => phaseOf(c) !== 'hitPointOutcome'), ...(r ? [{ contentId: r.contentId, responseId: r.responseId }] : [])]);
  }

  async function apply() {
    if (!fetched || !canApply) return;
    const { preview: shown, selKey: shownKey } = fetched;
    setBusy(true);
    try {
      if (shownKey === '[]') {
        // Nothing chosen: the one-step damage, so the sheet's client Undo works as it always did. A refusal (act reports it) previews again.
        const ok = await act({ action: 'damage', amount, ...(type ? { damageType: type } : {}) });
        if (ok === false) setRefresh((n) => n + 1);
        else onCancel();
        return;
      }
      // The stamp of the preview the player is looking at, with exactly the selection it was planned for.
      const applying = () =>
        client.play(characterId, {
          action: 'damage',
          amount,
          ...(type ? { damageType: type } : {}),
          responses: (JSON.parse(shownKey) as string[]).map(choiceFromKey),
          expectedUpdatedAt: shown.updatedAt,
        });
      const reply = await (run ? run(applying) : applying());
      const prompting = (reply.notices ?? []).some((n) => n.code === 'damage.roll-offered' || n.code === 'damage.responses-skipped');
      onDone(reply);
      if (prompting) setApplied({ reply, shown });
      else onCancel();
    } catch (error) {
      if (error instanceof TomeStackError && error.code === 'timeout') {
        // The outcome is unknown: the damage may have been applied. A new preview would carry the new stored stamp and invite a second
        // apply, so the sheet reloads the character and closes this panel instead.
        onTimeout();
        return;
      }
      onError(error);
      if (error instanceof TomeStackError && error.code === 'character.stale') setMessage('The sheet changed. Check the damage again.');
      setRefresh((n) => n + 1); // refused: look again
    } finally {
      setBusy(false);
    }
  }

  const offers = preview?.offers ?? [];
  const checkboxOffers = offers.filter((o) => o.response.phase !== 'hitPointOutcome');
  const outcomeOffers = offers.filter((o) => o.response.phase === 'hitPointOutcome');
  const hasOutcome = selection.some((c) => phaseOf(c) === 'hitPointOutcome');
  const reasonId = (r: ResponseValue) => `${groupName}-${r.contentId}-${r.responseId}-reason`.replace(/[^\w-]/g, '_');
  const detail = (r: ResponseValue) =>
    [
      r.phase === 'damageReduction' && r.amount !== undefined ? `reduces the damage by ${r.amount}` : '',
      r.phase === 'hitPointOutcome' && r.amount !== undefined ? `leaves you at ${r.amount} hit points` : '',
      r.cost !== undefined ? `costs ${r.cost} ${poolLabel(r)}` : '',
      r.text ?? '',
    ]
      .filter(Boolean)
      .join('; ');

  const offerLabel = (o: ResponseOffer, input: ReactNode) => (
    <label key={keyOf(o.response)} className="choice">
      {input}
      {o.response.label}
      {detail(o.response) && <span className="option-source">{detail(o.response)}</span>}
    </label>
  );
  const reasonNote = (o: ResponseOffer) =>
    !o.offered && (
      <span id={reasonId(o.response)} className="option-source">
        {o.reason ?? o.response.unavailableReason}
      </span>
    );

  const skipped = skippedLabelsOf(applied?.reply.notices, responses);
  const rolls = (applied?.reply.notices ?? []).filter((n) => n.code === 'damage.roll-offered' && n.content && n.effectId);

  return (
    <section aria-labelledby="damage-heading" className="play-panel">
      <h3 id="damage-heading" tabIndex={-1} ref={heading}>
        {applied ? 'Damage applied' : `Take ${amount} ${type ? damageTypeLabel(type).toLowerCase() : 'untyped'} damage`}
      </h3>
      {failed && <p className="error">Could not work out this damage. Close and try again.</p>}
      {!preview ? (
        !failed && <p className="hint">Working out the damage…</p>
      ) : (
        <>
          {notes.length > 0 && (
            <ul className="warnings" aria-label="Problems with these choices">
              {notes.map((n) => (
                <li key={`${n.code}-${n.message}`}>{n.message}</li>
              ))}
            </ul>
          )}
          <ul aria-label="What happens">
            <li>
              {preview.amount} {preview.type ?? 'untyped'} damage
            </li>
            {preview.reduction > 0 && (
              <li>
                Reduced by {preview.reduction}: {preview.outcome.amount}
              </li>
            )}
            {preview.type && preview.outcome.effect !== 'none' && (
              <li>
                {effectWords[preview.outcome.effect].replace(/^./, (c) => c.toUpperCase())}: {preview.outcome.adjusted}
              </li>
            )}
            {preview.outcome.absorbed > 0 && <li>Temporary hit points took {preview.outcome.absorbed}</li>}
            <li>Hit points lost: {preview.outcome.lost}</li>
            <li>
              Hit points after: {preview.after.current} of {preview.after.maximum}
              {preview.after.temporary > 0 ? `, ${preview.after.temporary} temporary` : ''}
            </li>
            {preview.concentrationEnds ? <li>Concentration ends</li> : preview.concentrationDc !== undefined && <li>Concentration save: DC {preview.concentrationDc}</li>}
            {preview.outcome.warnings.map((w) => (
              <li key={w.code} className="warn">
                {w.message}
              </li>
            ))}
          </ul>
          {checkboxOffers.length > 0 && (
            <fieldset disabled={!!applied}>
              <legend>Responses</legend>
              {checkboxOffers.map((o) => (
                <div key={keyOf(o.response)}>
                  {offerLabel(
                    o,
                    <input
                      type="checkbox"
                      checked={chosen(o.response)}
                      disabled={!o.offered}
                      aria-describedby={o.offered ? undefined : reasonId(o.response)}
                      onChange={() => toggle(o.response)}
                    />,
                  )}
                  {reasonNote(o)}
                </div>
              ))}
            </fieldset>
          )}
          {outcomeOffers.length > 0 && (
            <fieldset role="radiogroup" aria-label="Survive at 0 hit points" disabled={!!applied}>
              <legend>Survive at 0 hit points</legend>
              <label className="choice">
                <input type="radio" name={groupName} checked={!hasOutcome} onChange={() => pickOutcome()} />
                None
              </label>
              {outcomeOffers.map((o) => (
                <div key={keyOf(o.response)}>
                  {offerLabel(
                    o,
                    <input
                      type="radio"
                      name={groupName}
                      checked={chosen(o.response)}
                      disabled={!o.offered}
                      aria-describedby={o.offered ? undefined : reasonId(o.response)}
                      onChange={() => pickOutcome(o.response)}
                    />,
                  )}
                  {reasonNote(o)}
                </div>
              ))}
            </fieldset>
          )}
        </>
      )}
      {/* One polite region, not role="status" (the page has one status line). */}
      <section aria-label="Prompts" aria-live="polite">
        {message && <p>{message}</p>}
        {available.map((label) => (
          <p key={label}>{label} is available</p>
        ))}
        {skipped.length > 0 && <p>Available and not chosen: {skipped.join(', ')}.</p>}
        {rolls.map((n) => (
          <button
            key={`${n.content!.contentId}-${n.effectId}`}
            type="button"
            onClick={() => onRoll({ content: n.content, effectId: n.effectId })}
          >
            Roll {applied?.shown.rolls.find((r) => r.effectId === n.effectId && r.content.contentId === n.content!.contentId)?.label ?? 'offered roll'}
          </button>
        ))}
      </section>
      <div className="actions">
        {applied ? (
          <button type="button" onClick={onCancel}>
            Close
          </button>
        ) : (
          <>
            <button type="button" onClick={apply} aria-disabled={!canApply} aria-busy={busy || undefined}>
              Apply damage
            </button>
            <button type="button" onClick={() => !busy && onCancel()} aria-disabled={busy}>
              Cancel
            </button>
          </>
        )}
      </div>
    </section>
  );
}
