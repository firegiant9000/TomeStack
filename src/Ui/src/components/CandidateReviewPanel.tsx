import { useCallback, useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { CandidateCheck, CandidateFilter, CandidateStatus, ContentKind, ImportJob, SourceRecord, StoredCandidate } from '../api/types';

interface Props {
  job: ImportJob;
  source: SourceRecord;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  onOpenPage: (page: number) => void;
}

const kinds: ContentKind[] = ['spell', 'feat', 'feature', 'item', 'class', 'subclass', 'species', 'background'];

const statusLabels: Record<CandidateStatus, string> = {
  pending: 'to review',
  accepted: 'accepted as a draft',
  acceptedAsReference: 'accepted as reference',
  ignored: 'ignored',
};

const pageText = (page: { start: number; end?: number }) => (page.end && page.end !== page.start ? `pp. ${page.start}–${page.end}` : `p. ${page.start}`);

const percent = (confidence: number) => `${Math.round(confidence * 100)} %`;

/**
 * M4 D5 (SPEC I-02): the candidates of one import, filtered by page, kind, confidence and status. Each shows its
 * excerpt beside "Open page", what was read, its uncertainties and unresolved references, and editable fields and
 * effects. Accept makes a draft (validated first, ADR-004); Accept as reference keeps the text and page only; Ignore sets
 * it aside. Publishing is the homebrew studio's, with its own validation. Confidence is a hint, never permission.
 */
export function CandidateReviewPanel({ job, source, onError, onStatus, onOpenPage }: Props) {
  const [filter, setFilter] = useState<CandidateFilter>({ status: 'pending' });
  const [candidates, setCandidates] = useState<StoredCandidate[]>();
  const [selected, setSelected] = useState<string>();

  const load = useCallback(() => client.candidates(job.id, filter).then(setCandidates), [job.id, filter]);

  useEffect(() => {
    load().catch(onError);
  }, [load, onError]);

  const current = candidates?.find((c) => c.id === selected);

  return (
    <section aria-label={`Candidates from ${source.title}`} className="play-panel">
      <h5>Candidates from {source.title}</h5>
      <fieldset>
        <legend>Show</legend>
        <div className="inline-form">
          <label className="field">
            Page
            <input
              type="number"
              min={1}
              value={filter.page ?? ''}
              onChange={(e) => setFilter({ ...filter, page: e.target.value === '' ? undefined : Number(e.target.value) })}
            />
          </label>
          <label className="field">
            Kind
            <select value={filter.kind ?? ''} onChange={(e) => setFilter({ ...filter, kind: (e.target.value || undefined) as ContentKind | undefined })}>
              <option value="">Every kind</option>
              {kinds.map((k) => (
                <option key={k} value={k}>
                  {k}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            Confidence
            <select
              value={filter.maxConfidence !== undefined ? 'low' : filter.minConfidence !== undefined ? 'high' : ''}
              onChange={(e) =>
                setFilter({
                  ...filter,
                  minConfidence: e.target.value === 'high' ? 0.8 : undefined,
                  maxConfidence: e.target.value === 'low' ? 0.79 : undefined,
                })
              }
            >
              <option value="">Any confidence</option>
              <option value="low">Below 80 % (unsure first)</option>
              <option value="high">80 % or more</option>
            </select>
          </label>
          <label className="field">
            Status
            <select value={filter.status ?? ''} onChange={(e) => setFilter({ ...filter, status: (e.target.value || undefined) as CandidateStatus | undefined })}>
              <option value="">Any status</option>
              {(Object.keys(statusLabels) as CandidateStatus[]).map((s) => (
                <option key={s} value={s}>
                  {statusLabels[s]}
                </option>
              ))}
            </select>
          </label>
        </div>
      </fieldset>
      {candidates && candidates.length === 0 && <p className="hint">No candidates match.</p>}
      {candidates && candidates.length > 0 && (
        <ul className="resources" aria-label="Candidates">
          {candidates.map((c) => (
            <li key={c.id} className="resource">
              <button type="button" className="link" aria-current={selected === c.id ? 'true' : undefined} onClick={() => setSelected(c.id)}>
                {(c.edited ?? c.candidate).proposedName}
              </button>{' '}
              <span className="tag">{(c.edited ?? c.candidate).proposedKind}</span> <span className="hint">{pageText((c.edited ?? c.candidate).page)}</span>{' '}
              <span className={(c.edited ?? c.candidate).confidence < 0.8 ? 'warn' : 'hint'}>{percent((c.edited ?? c.candidate).confidence)}</span>{' '}
              <span className="tag">{statusLabels[c.status]}</span>
            </li>
          ))}
        </ul>
      )}
      {current && (
        <CandidateDetail
          key={`${current.id}-${current.updatedAt}`}
          stored={current}
          onOpenPage={onOpenPage}
          onError={onError}
          onChanged={async (text) => {
            onStatus(text);
            await load();
          }}
        />
      )}
    </section>
  );
}

interface DetailProps {
  stored: StoredCandidate;
  onOpenPage: (page: number) => void;
  onError: (error: unknown) => void;
  onChanged: (status: string) => Promise<void>;
}

function CandidateDetail({ stored, onOpenPage, onError, onChanged }: DetailProps) {
  const candidate = stored.edited ?? stored.candidate;
  const heading = useRef<HTMLHeadingElement>(null);
  const [name, setName] = useState(candidate.proposedName);
  const [kind, setKind] = useState<ContentKind>(candidate.proposedKind);
  const [summary, setSummary] = useState(candidate.summary ?? '');
  const [effects, setEffects] = useState(JSON.stringify(candidate.proposedEffects, null, 2));
  const [dismissed, setDismissed] = useState<string[]>([]);
  const [effectsError, setEffectsError] = useState<string>();
  const [check, setCheck] = useState<CandidateCheck>();
  const pending = stored.status === 'pending';

  // WCAG 2.4.3: choosing a candidate moves focus to its details.
  useEffect(() => heading.current?.focus(), []);

  useEffect(() => {
    let current = true;
    client
      .checkCandidate(stored.id)
      .then((result) => {
        if (current) setCheck(result);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [stored.id, onError]);

  async function save() {
    let parsed: unknown[];
    try {
      const value: unknown = JSON.parse(effects);
      if (!Array.isArray(value)) throw new Error('not a list');
      parsed = value;
    } catch {
      setEffectsError('The effects must be a JSON list, for example [].');
      return;
    }
    setEffectsError(undefined);
    try {
      await client.editCandidate(stored.id, { name, kind, summary, effects: parsed, dismissReferences: dismissed });
      await onChanged(`Saved your edit of ${name}. Check it again before accepting.`);
    } catch (error) {
      onError(error);
    }
  }

  async function accept(asReference: boolean) {
    try {
      await client.acceptCandidate(stored.id, asReference);
      await onChanged(
        `${candidate.proposedName} is now a draft${asReference ? ' reference entry' : ''} in the studio. It does nothing until you publish it there.`,
      );
    } catch (error) {
      onError(error);
    }
  }

  async function ignore() {
    try {
      await client.ignoreCandidate(stored.id);
      await onChanged(`Ignored ${candidate.proposedName}. Nothing was created.`);
    } catch (error) {
      onError(error);
    }
  }

  const headingId = `candidate-${stored.id}`;
  return (
    <section aria-labelledby={headingId} className="play-panel">
      <h5 id={headingId} tabIndex={-1} ref={heading}>
        Candidate: {candidate.proposedName}
      </h5>
      <p className="hint">
        {candidate.proposedKind} · {pageText(candidate.page)} · {candidate.rulesFamilies.join(', ')} · confidence {percent(candidate.confidence)} (a hint, not a
        check) · {statusLabels[stored.status]}
      </p>
      <div className="actions">
        <button type="button" onClick={() => onOpenPage(candidate.page.start)}>
          Open page {candidate.page.start}
        </button>
      </div>
      <figure>
        <figcaption>Excerpt from {pageText(candidate.page)}</figcaption>
        <pre className="excerpt">{candidate.excerpt}</pre>
      </figure>

      {Object.keys(candidate.fields).length > 0 && (
        <dl aria-label="What was read">
          {Object.entries(candidate.fields).map(([field, value]) => (
            <div key={field}>
              <dt>
                {field}
                {candidate.lowConfidenceFields.includes(field) ? ' (unsure)' : ''}
              </dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
      {candidate.uncertainties.length > 0 && (
        <ul className="warnings" aria-label="Uncertainties">
          {candidate.uncertainties.map((u) => (
            <li key={u}>{u}</li>
          ))}
        </ul>
      )}
      {candidate.unresolvedReferences.length > 0 && (
        <fieldset>
          <legend>Unresolved references</legend>
          <p className="hint">These names are neither installed nor found in this import. Dismiss one if the entry does not need it.</p>
          {candidate.unresolvedReferences.map((r) => (
            <label key={r} className="choice">
              <input
                type="checkbox"
                disabled={!pending}
                checked={dismissed.includes(r)}
                onChange={(e) => setDismissed(e.target.checked ? [...dismissed, r] : dismissed.filter((d) => d !== r))}
              />{' '}
              Dismiss {r}
            </label>
          ))}
        </fieldset>
      )}

      {pending && (
        <fieldset>
          <legend>Edit before accepting</legend>
          <label className="field">
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </label>
          <label className="field">
            Kind
            <select value={kind} onChange={(e) => setKind(e.target.value as ContentKind)}>
              {kinds.map((k) => (
                <option key={k} value={k}>
                  {k}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            Text
            <textarea value={summary} onChange={(e) => setSummary(e.target.value)} rows={5} maxLength={20000} />
          </label>
          <label className="field">
            Proposed effects (JSON; they stay reference-only until you change them in the studio)
            <textarea value={effects} onChange={(e) => setEffects(e.target.value)} rows={6} spellCheck={false} />
          </label>
          {effectsError && <p className="error">{effectsError}</p>}
          <button type="button" onClick={save}>
            Save edit
          </button>
        </fieldset>
      )}

      {check && (
        <div role="region" aria-label="Check before accepting">
          {check.blockers.length > 0 && (
            <ul className="warnings" aria-label="Blocks accepting">
              {check.blockers.map((b, i) => (
                <li key={`${b.code}-${i}`}>{b.message}</li>
              ))}
            </ul>
          )}
          {check.report.errors.length > 0 && (
            <ul className="errors" aria-label="Problems">
              {check.report.errors.map((e, i) => (
                <li key={`${e.code}-${i}`}>{e.message}</li>
              ))}
            </ul>
          )}
          <p className="hint">Depends on: {check.dependencies.map((d) => `${d.name}${d.kind === 'missing-content' || d.kind === 'unresolved-name' ? ' (missing)' : ''}`).join(', ')}</p>
          {check.canAccept && <p>No problems found: it can be accepted as a draft.</p>}
        </div>
      )}

      {pending && (
        <div className="actions">
          <button type="button" disabled={!check?.canAccept} onClick={() => accept(false)}>
            Accept as a draft
          </button>
          <button type="button" disabled={!check?.canAcceptAsReference} onClick={() => accept(true)}>
            Accept as reference
          </button>
          <button type="button" onClick={ignore}>
            Ignore
          </button>
        </div>
      )}
    </section>
  );
}
