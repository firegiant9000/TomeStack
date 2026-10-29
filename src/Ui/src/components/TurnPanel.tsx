import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { CharacterView, ToggleValue, TurnChoiceOption, TurnChoicePrompt, TurnMoment, TurnPreview } from '../api/types';

interface Props {
  characterId: string;
  at: TurnMoment;
  /** Identity of the character view the panel was opened over; a new one (any change to the character) fetches the previews again. */
  version: unknown;
  /** The sheet's toggles, to name the toggle a shortfall prompt is about. */
  toggles?: ToggleValue[];
  onDone: (view: CharacterView) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
  /** Runs the confirm in the sheet's save queue, so a save never starts from the stamp it replaces. Direct when absent. */
  run?: (command: () => Promise<CharacterView>) => Promise<CharacterView>;
}

type Choices = Record<string, TurnChoiceOption>;
/** A preview with the selection it was asked for (the selection is what Confirm sends back with the preview's own stamp). */
interface Fetched {
  preview: TurnPreview;
  version: unknown;
  refresh: number;
  skip: string[];
  choices: Choices;
  /** `choices` as a key, to compare with the selection now. */
  choicesKey: string;
}

const sameChoices = (a: Choices, b: Choices) => {
  const keys = Object.keys(a);
  return keys.length === Object.keys(b).length && keys.every((k) => a[k] === b[k]);
};
const sameList = (a: string[], b: string[]) => a.length === b.length && a.every((x, i) => x === b[i]);

/**
 * D43 slice 3: the start or end of the character's turn, shown before anything changes (the rest panel's pattern).
 *
 * The checklist is built once from the unselected preview (nothing unticked, no choices): the planner replaces an unticked
 * upkeep spend with a required ending, so the list built from the current preview would lose the row to re-tick. The current
 * preview, for the selection as it stands, is shown as "What happens". A prompt can vanish when the selection changes, so a choice
 * whose prompt is not in the latest preview is dropped before the next preview and the confirm (the service refuses an unknown one).
 * Changing a tick or a choice fetches a new preview and Confirm waits for it. Confirm sends the stamp of the preview on screen,
 * never the queue's latest, so a change in between is refused `turn.preview-stale` and the panel stays open and previews again.
 */
export function TurnPanel({ characterId, at, version, toggles = [], onDone, onCancel, onError, run }: Props) {
  const [skip, setSkip] = useState<string[]>([]);
  const [choices, setChoices] = useState<Choices>({});
  const [base, setBase] = useState<TurnPreview>();
  const [fetched, setFetched] = useState<Fetched>();
  const [refresh, setRefresh] = useState(0); // bumped after a refused confirm, to preview again
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false); // the outcome preview failed
  const [baseFailed, setBaseFailed] = useState(false); // the checklist preview failed
  const heading = useRef<HTMLHeadingElement>(null);

  // WCAG 2.4.3: the panel opens with focus on its heading.
  useEffect(() => heading.current?.focus(), []);

  // The checklist: the unselected preview for this view.
  useEffect(() => {
    let current = true;
    client
      .turnPreview(characterId, at, [], {})
      .then((next) => {
        if (!current) return;
        setBaseFailed(false);
        setBase(next);
      })
      .catch((error) => {
        if (!current) return;
        setBaseFailed(true);
        onError(error);
      });
    return () => {
      current = false;
    };
  }, [characterId, at, version, refresh, onError]);

  // A choice whose prompt is not in the latest preview is dropped (adjusting state during render, guarded by the comparison), so it is
  // neither sent nor remembered. A tick whose row the unselected preview does not have goes too.
  if (fetched) {
    const ids = fetched.preview.choices.map((c) => c.id);
    const kept = Object.fromEntries(Object.entries(choices).filter(([id]) => ids.includes(id)));
    if (!sameChoices(kept, choices)) setChoices(kept);
  }
  if (base) {
    const kept = skip.filter((id) => base.changes.some((c) => c.id === id));
    if (!sameList(kept, skip)) setSkip(kept);
  }

  // The choices sent: every prompt of the latest preview with End unless the player picked Keep. The service refuses a prompt with no
  // choice, and a radio that is already checked fires no change, so the default is sent explicitly, in the outcome preview and the
  // confirm alike. The key keeps the effect from refiring on a new object each render.
  const prompts = fetched?.preview.choices ?? base?.choices ?? [];
  const effective: Choices = { ...Object.fromEntries(prompts.map((p) => [p.id, 'end' as const])), ...Object.fromEntries(Object.entries(choices).filter(([id]) => prompts.some((p) => p.id === id))) };
  const choicesKey = JSON.stringify(Object.entries(effective).sort(([a], [b]) => (a < b ? -1 : 1)));

  // The preview for the current selection.
  useEffect(() => {
    let current = true;
    const selection = Object.fromEntries(JSON.parse(choicesKey) as [string, TurnChoiceOption][]);
    client
      .turnPreview(characterId, at, skip, selection)
      .then((next) => {
        if (!current) return;
        setFailed(false);
        setFetched({ preview: next, version, refresh, skip, choices: selection, choicesKey });
      })
      .catch((error) => {
        if (!current) return;
        setFailed(true);
        onError(error);
      });
    return () => {
      current = false;
    };
  }, [characterId, at, version, refresh, skip, choicesKey, onError]);

  const preview = fetched?.preview;
  const ready = fetched !== undefined && fetched.version === version && fetched.refresh === refresh && sameList(fetched.skip, skip) && fetched.choicesKey === choicesKey;
  const name = at === 'turnStart' ? 'Start of turn' : 'End of turn';
  const confirmName = at === 'turnStart' ? 'Confirm start of turn' : 'Confirm end of turn';
  const toggleName = (prompt: TurnChoicePrompt) =>
    toggles.find((t) => t.content.contentId === prompt.contentId && t.toggleId === prompt.toggleId)?.label ?? prompt.toggleId.replace(/[-_]+/g, ' ');
  const nothing = ready && preview !== undefined && preview.changes.length === 0 && preview.choices.length === 0 && preview.manual.length === 0 && preview.prompts.length === 0;
  const tick = (id: string) => setSkip((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));

  async function confirm() {
    if (!fetched || !ready || busy || failed || baseFailed) return;
    const { preview: shown, skip: shownSkip, choices: shownChoices } = fetched;
    setBusy(true);
    try {
      // Never the queue's latest: the stamp of the preview the player is looking at, with exactly the selection it was planned for.
      const applying = () => client.turn(characterId, at, shown.updatedAt, shownSkip, shownChoices);
      onDone(await (run ? run(applying) : applying()));
    } catch (error) {
      onError(error);
      setRefresh((n) => n + 1); // a stale or otherwise refused turn: stay open and look again
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby="turn-heading" className="play-panel">
      <h3 id="turn-heading" tabIndex={-1} ref={heading}>
        {name}
      </h3>
      {(failed || baseFailed) && <p className="error">Could not work out this turn. Close and try again.</p>}
      {!base || !preview ? (
        !failed && !baseFailed && <p className="hint">Working out what this turn changes…</p>
      ) : (
        <>
          <p className="hint" aria-live="polite">
            {!ready && !failed && !baseFailed ? 'Updating the turn…' : ''}
          </p>
          {base.changes.length > 0 && (
            <fieldset>
              <legend>These changes will be applied. Untick any that do not apply.</legend>
              {base.changes.map((change) => (
                <label key={change.id} className="choice">
                  <input type="checkbox" checked={change.required || !skip.includes(change.id)} disabled={change.required} onChange={() => tick(change.id)} />
                  {change.label}: {change.from} → {change.to}
                  <span className="option-source">
                    {change.reason}
                    {change.required ? ' (required)' : ''}
                  </span>
                </label>
              ))}
            </fieldset>
          )}
          {preview.choices.map((prompt) => (
            <fieldset key={prompt.id} role="radiogroup" aria-label={prompt.label}>
              <legend>{prompt.label}</legend>
              {(['end', 'keep'] as const).map((option) => (
                <label key={option} className="choice">
                  <input
                    type="radio"
                    name={`${prompt.id}`}
                    checked={(choices[prompt.id] ?? 'end') === option}
                    onChange={() => setChoices((c) => ({ ...c, [prompt.id]: option }))}
                  />
                  {option === 'end' ? `End ${toggleName(prompt)}` : 'Keep it on'}
                </label>
              ))}
            </fieldset>
          ))}
          {nothing ? (
            <p>Nothing changes this turn</p>
          ) : (
            <>
              <p id="turn-outcome">What happens</p>
              <ul aria-labelledby="turn-outcome">
                {preview.changes.map((change) => (
                  <li key={change.id}>
                    {change.label}: {change.from} → {change.to}
                    {change.required ? ' (required)' : ''}
                  </li>
                ))}
              </ul>
            </>
          )}
          {preview.prompts.length > 0 && (
            <ul aria-label="Pool messages">
              {preview.prompts.map((p) => (
                <li key={p.id}>{p.text}</li>
              ))}
            </ul>
          )}
          {preview.manual.length > 0 && (
            <>
              <p>Do these by hand:</p>
              <ul className="warnings" aria-label="Turn steps to do by hand">
                {preview.manual.map((m) => (
                  <li key={`${m.code}-${m.message}`}>{m.message}</li>
                ))}
              </ul>
            </>
          )}
        </>
      )}
      <div className="actions">
        {nothing ? (
          <button type="button" onClick={() => !busy && onCancel()}>
            Close
          </button>
        ) : (
          <>
            <button type="button" onClick={confirm} aria-disabled={!ready || failed || baseFailed || busy}>
              {busy ? 'Applying…' : confirmName}
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
