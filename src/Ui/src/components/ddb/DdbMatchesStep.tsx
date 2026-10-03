import { useEffect, useState } from 'react';
import { client } from '../../api/client';
import type { ChoiceSelection, ChoiceStatus, DdbPreview, MatchCandidate, MatchRow, MatchStatus, Resolution, RulesFamilyId } from '../../api/types';
import { kindLabels, kindOrder, noteLabels, statusLabels } from './ddbLabels';

interface Props {
  preview?: DdbPreview;
  family: RulesFamilyId;
  campaignId?: string;
  resolutions: Record<string, Resolution>;
  answers: ChoiceSelection[];
  onResolve: (resolution: Resolution) => void;
  onClear: (rowId: string) => void;
  onAnswer: (answer: ChoiceSelection) => void;
  onError: (error: unknown) => void;
}

type Filter = 'all' | 'choose' | 'notFound' | 'leftOut';

const filters: { id: Filter; label: string; shows: (status: MatchStatus) => boolean }[] = [
  { id: 'all', label: 'All', shows: () => true },
  { id: 'choose', label: 'Needs a choice', shows: (s) => s === 'choose' },
  { id: 'notFound', label: 'Not found', shows: (s) => s === 'notFound' || s === 'noPlace' || s === 'unreadable' },
  { id: 'leftOut', label: 'Left out', shows: (s) => s === 'leftOut' },
];

/**
 * Step 3: one row per thing read, grouped by kind in real tables. Names are proposals: a row that needs a choice waits for
 * the user, and every row can be left out. Most content of a real sheet is not in the installed SRD and homebrew, so
 * "Not found" is expected; it is listed as a gap note.
 */
export function DdbMatchesStep({ preview, family, campaignId, resolutions, answers, onResolve, onClear, onAnswer, onError }: Props) {
  const [filter, setFilter] = useState<Filter>('all');
  const [names, setNames] = useState<Record<string, string>>({});

  useEffect(() => {
    client
      .listContent(family, campaignId)
      .then((options) => setNames(Object.fromEntries(options.map((o) => [o.reference.contentId, o.name]))))
      .catch(onError);
  }, [family, campaignId, onError]);

  if (!preview) return <p className="hint">Matching the sheet against your installed content…</p>;

  const shows = filters.find((f) => f.id === filter)!.shows;
  const count = (test: (s: MatchStatus) => boolean) => preview.matches.filter((r) => test(r.status)).length;

  return (
    <>
      <p>
        {count((s) => s === 'matched')} matched, {count((s) => s === 'choose')} need a choice,{' '}
        {count((s) => s === 'notFound' || s === 'noPlace' || s === 'unreadable')} not found or with no place, {count((s) => s === 'leftOut')} left out. Most
        content from books other than the SRD is not installed, so “Not found” is expected; each one is kept as a gap note.
      </p>
      <fieldset className="filters">
        <legend>Show</legend>
        {filters.map((f) => (
          <label key={f.id}>
            <input type="radio" name="ddb-filter" checked={filter === f.id} onChange={() => setFilter(f.id)} /> {f.label}
          </label>
        ))}
      </fieldset>
      {kindOrder.map((kind) => {
        const rows = preview.matches
          .filter((r) => r.kind === kind && shows(r.status))
          .sort((a, b) => Number(b.status === 'choose') - Number(a.status === 'choose'));
        if (rows.length === 0) return null;
        return (
          <table key={kind}>
            <caption>{kindLabels[kind]}</caption>
            <thead>
              <tr>
                <th scope="col">On the sheet</th>
                <th scope="col">Result</th>
                <th scope="col">TomeStack content</th>
                <th scope="col">Your choice</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <Row key={row.rowId} row={row} names={names} resolution={resolutions[row.rowId]} onResolve={onResolve} onClear={onClear} />
              ))}
            </tbody>
          </table>
        );
      })}
      {preview.openChoices.length > 0 && filter === 'all' && (
        <section aria-labelledby="ddb-open-choices">
          <h4 id="ddb-open-choices">Open choices</h4>
          <p className="hint">The sheet does not settle these. Answer them here, or later in the builder.</p>
          {preview.openChoices.map((choice) => (
            <OpenChoice key={`${choice.source.contentId}:${choice.choiceId}`} choice={choice} names={names} answers={answers} onAnswer={onAnswer} />
          ))}
        </section>
      )}
    </>
  );
}

function describe(candidate: MatchCandidate, names: Record<string, string>): string {
  const caster = candidate.placement.caster ? ` (cast as ${names[candidate.placement.caster] ?? 'a caster'})` : '';
  return `${candidate.name} — ${candidate.sourceTitle || 'installed content'}${caster}`;
}

function Row({
  row,
  names,
  resolution,
  onResolve,
  onClear,
}: {
  row: MatchRow;
  names: Record<string, string>;
  resolution?: Resolution;
  onResolve: (resolution: Resolution) => void;
  onClear: (rowId: string) => void;
}) {
  const headerId = `ddb-row-${row.rowId.replaceAll(':', '-')}`;
  const leftOut = resolution?.leaveOut === true || row.status === 'leftOut';
  const selected = row.candidates.findIndex(
    (c) =>
      (resolution?.chosen ?? row.chosen?.reference)?.revisionId === c.reference.revisionId &&
      (resolution ? resolution.caster === c.placement.caster : row.chosen?.placement.caster === c.placement.caster),
  );
  return (
    <tr>
      <th scope="row" id={headerId}>
        {row.label || '(unreadable)'}
      </th>
      <td>
        {statusLabels[row.status]}
        {row.note && <span className="hint"> {noteLabels[row.note] ?? row.note}</span>}
      </td>
      <td>{row.chosen ? `${describe(row.chosen, names)} · ${row.chosen.families.join(', ')}` : row.candidates.length > 1 ? `${row.candidates.length} candidates` : '—'}</td>
      <td>
        <div role="group" aria-labelledby={headerId}>
          {row.candidates.length > 0 && (
            <select
              aria-label={`Match for ${row.label}`}
              value={selected < 0 || leftOut ? '' : String(selected)}
              disabled={leftOut}
              onChange={(e) => {
                const candidate = row.candidates[Number(e.target.value)];
                if (e.target.value === '' || !candidate) onClear(row.rowId);
                else onResolve({ rowId: row.rowId, chosen: candidate.reference, leaveOut: false, caster: candidate.placement.caster });
              }}
            >
              <option value="">{row.status === 'choose' ? 'Choose…' : 'As proposed'}</option>
              {row.candidates.map((c, i) => (
                <option key={`${c.reference.revisionId}-${c.placement.caster ?? ''}`} value={String(i)}>
                  {describe(c, names)}
                </option>
              ))}
            </select>
          )}
          {(row.candidates.length > 0 || row.status === 'leftOut') && (
            <label>
              <input
                type="checkbox"
                checked={leftOut}
                onChange={(e) => (e.target.checked ? onResolve({ rowId: row.rowId, leaveOut: true }) : onClear(row.rowId))}
              />{' '}
              Leave out <span className="visually-hidden">{row.label}</span>
            </label>
          )}
        </div>
      </td>
    </tr>
  );
}

function OpenChoice({
  choice,
  names,
  answers,
  onAnswer,
}: {
  choice: ChoiceStatus;
  names: Record<string, string>;
  answers: ChoiceSelection[];
  onAnswer: (answer: ChoiceSelection) => void;
}) {
  const answer = answers.find((a) => a.choiceId === choice.choiceId && a.source.contentId === choice.source.contentId);
  const selected = answer?.selected ?? choice.selected;
  const room = choice.count - selected.length;
  return (
    <fieldset>
      <legend>
        {choice.sourceName}: {choice.text ?? choice.choiceId} (choose {choice.count})
      </legend>
      {choice.options.map((option) => {
        const checked = selected.some((s) => s.revisionId === option.revisionId);
        return (
          <label key={option.revisionId}>
            <input
              type="checkbox"
              checked={checked}
              disabled={!checked && room <= 0}
              onChange={() =>
                onAnswer({
                  source: choice.source,
                  choiceId: choice.choiceId,
                  selected: checked ? selected.filter((s) => s.revisionId !== option.revisionId) : [...selected, option],
                })
              }
            />{' '}
            {names[option.contentId] ?? 'Option'}
          </label>
        );
      })}
    </fieldset>
  );
}
