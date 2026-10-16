import { useCallback, useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { CandidateCheck, CandidateFilter, CandidatePage, CandidateStatus, ContentKind, ImportJob, RulesFamilyId, SourceRecord, StoredCandidate } from '../api/types';
import { familyLabel } from './familyLabels';

interface Props {
  job: ImportJob;
  source: SourceRecord;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  onOpenPage: (page: number) => void;
  onOpenStudio: (sourceId: string) => void;
}

/** How long the page-range fields wait after a keystroke before the list reloads. */
const pageRangeDelayMs = 300;

/** Item 13: candidates shown at a time (the buttons say "Previous 50" and "Next 50"). */
const pageSize = 50;

const kinds: ContentKind[] = ['spell', 'feat', 'feature', 'item', 'class', 'subclass', 'species', 'background'];

const statusLabels: Record<CandidateStatus, string> = {
  pending: 'to review',
  accepted: 'accepted as a draft',
  acceptedAsReference: 'accepted as reference',
  ignored: 'ignored',
};

/** Item 9: why a bulk accept skipped a candidate, by the service's codes (never text from the document). */
const blockedReasons: Record<string, string> = {
  'candidate.low-confidence': 'has fields the detector is unsure of',
  'candidate.unresolved-reference': 'names something that is neither installed nor imported',
  'import.family-needed': 'needs a rules family',
  'import.already-accepted': 'was already accepted from another reading of this document',
  'candidate.validation-failed': 'has problems the check reports',
  'candidate.already-reviewed': 'was already reviewed',
  'candidate.not-found': 'no longer exists',
};

const pageText = (page: { start: number; end?: number }) => (page.end && page.end !== page.start ? `pp. ${page.start}–${page.end}` : `p. ${page.start}`);

const percent = (confidence: number) => `${Math.round(confidence * 100)} %`;

/**
 * M4 D5 (SPEC I-02): the candidates of one import, filtered by page, kind, confidence and status. Each shows its
 * excerpt beside "Open page", what was read, its uncertainties and unresolved references, and editable fields and
 * effects. Accept makes a draft (validated first, ADR-004); Accept as reference keeps the text and page only; Ignore sets
 * it aside. Publishing is the homebrew studio's, with its own validation. Confidence is a hint, never permission.
 */
export function CandidateReviewPanel({ job, source, onError, onStatus, onOpenPage, onOpenStudio }: Props) {
  const [filter, setFilter] = useState<CandidateFilter>({ status: 'pending' });
  const [offset, setOffset] = useState(0); // item 13: which of the filtered candidates the list starts at
  const [result, setResult] = useState<CandidatePage>();
  // The one polite paragraph (WCAG 4.1.3): "Showing 51–100 of 340" on a page turn, "N selected", "Selection cleared".
  const [announcement, setAnnouncement] = useState('');
  const [selected, setSelected] = useState<string>();
  // Item 9: the rows ticked for a bulk accept (the open row, `selected`, is a different thing).
  const [ticked, setTicked] = useState<string[]>([]);
  const clearedByPage = useRef(false); // a page turn dropped ticks: say so in the same message as the new range
  const [skipped, setSkipped] = useState<{ id: string; name: string; reasons: string[] }[]>([]);
  const [bulkBusy, setBulkBusy] = useState(false);
  const bulkRunning = useRef(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const announceNext = useRef(false);
  const latestRequest = useRef(0);
  const shownOffset = useRef(0); // the offset the rows on screen were fetched for
  const [shown, setShown] = useState(0); // the same, for rendering
  const [dirty, setDirty] = useState(false); // the open candidate's form differs from what is stored
  const dirtyRef = useRef(false);
  dirtyRef.current = dirty;
  const [rangeFields, setRangeFields] = useState({ from: '', to: '' }); // what the page-range fields show; the filter follows after a pause
  const pageTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const dirtyId = `unsaved-${job.id}`;
  const rangeId = `range-${job.id}`;
  const noneId = `none-selected-${job.id}`;
  const bulkId = `accepting-${job.id}`;
  const candidates = result?.items;
  const total = result?.total ?? 0;
  const pendingShown = candidates?.filter((c) => c.status === 'pending') ?? [];

  const load = useCallback(() => {
    const request = ++latestRequest.current;
    return client
      .candidates(job.id, filter, { offset, limit: pageSize })
      .then((page) => {
        if (request !== latestRequest.current) return page.items; // a newer request is on its way
        if (page.items.length === 0 && page.total > 0 && offset > 0) {
          // The last candidate of the last page left the list: go to the page that is now last.
          setOffset(Math.floor((page.total - 1) / pageSize) * pageSize);
          return page.items;
        }
        setResult(page);
        shownOffset.current = offset;
        setShown(offset);
        // WCAG 2.4.3: a detail whose candidate is no longer listed goes away, so it cannot return later and take focus.
        setSelected((s) => (s !== undefined && page.items.some((c) => c.id === s) ? s : undefined));
        // Only pending rows still shown stay ticked (an accepted row leaves; a blocked one stays).
        setTicked((t) => {
          const keep = t.filter((id) => page.items.some((c) => c.id === id && c.status === 'pending'));
          return keep.length === t.length ? t : keep;
        });
        if (announceNext.current) {
          announceNext.current = false;
          const range = `Showing ${offset + 1}–${offset + page.items.length} of ${page.total}`;
          setAnnouncement(clearedByPage.current ? `${range}. Selection cleared.` : range);
          clearedByPage.current = false;
        }
        return page.items;
      })
      .catch((error: unknown) => {
        if (request === latestRequest.current && announceNext.current) {
          // A page turn failed: stay on the page that is shown, and announce nothing later.
          announceNext.current = false;
          setOffset(shownOffset.current);
          // The ticks are gone either way; the range is not announced, so say that alone.
          if (clearedByPage.current) setAnnouncement('Selection cleared');
          clearedByPage.current = false;
        }
        throw error;
      });
  }, [job.id, filter, offset]);

  useEffect(() => {
    load().catch(onError);
  }, [load, onError]);

  useEffect(() => () => clearTimeout(pageTimer.current), []);

  const current = candidates?.find((c) => c.id === selected);

  // Ruling R35: paging, the filters and the other rows would silently drop an unsaved edit, so they are held while there is one.
  // A bulk accept in flight holds them too: a page turn or filter change would move the rows it is about to reload.
  const held = dirty || bulkBusy;
  const heldReason = dirty ? dirtyId : bulkBusy ? bulkId : undefined;
  const heldProps = { 'aria-disabled': held || undefined, 'aria-describedby': heldReason };

  function changeFilter(next: CandidateFilter) {
    if (held) return;
    setFilter(next);
    resetToFirstPage();
  }

  function resetToFirstPage() {
    setOffset(0);
    setAnnouncement('');
    setSelected(undefined);
    clearTicks();
  }

  /** The ticks never outlive the rows they were made on; say so once (WCAG 4.1.3), and only when there was something to lose. */
  function clearTicks() {
    setSkipped([]);
    if (ticked.length === 0) return;
    setTicked([]);
    setAnnouncement('Selection cleared');
  }

  function tick(next: string[]) {
    setTicked(next);
    setSkipped([]);
    setAnnouncement(`${next.length} selected`);
  }

  async function acceptTicked(asReference: boolean) {
    if (dirty || bulkRunning.current || ticked.length === 0) return;
    bulkRunning.current = true; // a second press while the first is in flight does nothing
    // A page-range pause still running would change the filter and drop the ticks mid-flight: cancel it, and show the applied range.
    clearTimeout(pageTimer.current);
    setRangeFields({ from: String(filter.firstPage ?? ''), to: String(filter.lastPage ?? '') });
    setBulkBusy(true);
    try {
      const outcome = await client.acceptCandidates(ticked, asReference);
      const nameOf = (id: string) => {
        const c = candidates?.find((x) => x.id === id);
        return c ? (c.edited ?? c.candidate).proposedName : 'A candidate';
      };
      const blocked = outcome.blocked.length;
      onStatus(
        `Accepted ${outcome.accepted.length}${blocked > 0 ? `; ${blocked} need review` : ''}.${
          outcome.accepted.length > 0 ? ' They are drafts in the studio and do nothing until you publish them there.' : ''
        }`,
      );
      setSkipped(outcome.blocked.map((b) => ({ id: b.candidateId, name: nameOf(b.candidateId), reasons: b.codes.map((code) => blockedReasons[code] ?? code) })));
      const stay = outcome.blocked.map((b) => b.candidateId);
      setTicked(stay);
      setAnnouncement(`${stay.length} selected`);
      await load();
    } catch (error) {
      onError(error);
    } finally {
      bulkRunning.current = false;
      setBulkBusy(false);
    }
  }

  const pageNumber = (text: string) => (text === '' ? undefined : Number(text));

  /** The page-range fields show every keystroke at once but reload the list only after a pause (the service reads the whole job each time). */
  function changePageRange(which: 'from' | 'to', text: string) {
    if (held) return;
    const next = { ...rangeFields, [which]: text };
    setRangeFields(next);
    clearTimeout(pageTimer.current);
    pageTimer.current = setTimeout(() => {
      if (dirtyRef.current || bulkRunning.current) {
        setRangeFields({ from: String(filter.firstPage ?? ''), to: String(filter.lastPage ?? '') });
        return;
      }
      setFilter((f) => ({ ...f, firstPage: pageNumber(next.from), lastPage: pageNumber(next.to) }));
      resetToFirstPage();
    }, pageRangeDelayMs);
  }

  function turnPage(direction: -1 | 1) {
    const target = shown + direction * pageSize;
    if (held || target < 0 || target >= total) return;
    announceNext.current = true;
    setSelected(undefined);
    setSkipped([]);
    if (ticked.length > 0) {
      // Said with the new range, in one message, once the page has loaded.
      setTicked([]);
      clearedByPage.current = true;
    }
    setOffset(target);
  }

  function select(id: string) {
    if (dirty && id !== selected) return;
    setSelected(id);
  }

  const atStart = shown === 0;
  const atEnd = shown + pageSize >= total;
  // A button held at an end points at the range, which says why ("1–50 of 340"); a held edit points at its own reason.
  const pagingReason = heldReason ?? (atStart || atEnd ? rangeId : undefined);

  return (
    <section aria-label={`Candidates from ${source.title}`} className="play-panel">
      <h5 ref={heading} tabIndex={-1}>
        Candidates from {source.title}
      </h5>
      <p className="hint">Accepted entries are drafts in the Homebrew studio under {source.title}. Publish them there to use them in the builder.</p>
      <div className="actions">
        {/* aria-disabled, never `disabled`; leaving would silently drop an unsaved edit (ruling R26). */}
        <button
          type="button"
          aria-disabled={dirty || undefined}
          aria-describedby={dirty ? dirtyId : undefined}
          onClick={() => {
            if (!dirty) onOpenStudio(source.id);
          }}
        >
          Open {source.title} in the studio
        </button>
      </div>
      {dirty && (
        <p id={dirtyId} className="hint">
          Save or undo your edit first.
        </p>
      )}
      <fieldset>
        <legend>Show</legend>
        <div className="inline-form">
          <label className="field">
            From page
            <input
              type="number"
              min={1}
              {...heldProps}
              value={rangeFields.from}
              onChange={(e) => changePageRange('from', e.target.value)}
            />
          </label>
          <label className="field">
            To page
            <input
              type="number"
              min={1}
              {...heldProps}
              value={rangeFields.to}
              onChange={(e) => changePageRange('to', e.target.value)}
            />
          </label>
          <label className="field">
            Kind
            <select {...heldProps} value={filter.kind ?? ''} onChange={(e) => changeFilter({ ...filter, kind: (e.target.value || undefined) as ContentKind | undefined })}>
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
              {...heldProps}
              value={filter.maxConfidence !== undefined ? 'low' : filter.minConfidence !== undefined ? 'high' : ''}
              onChange={(e) =>
                changeFilter({
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
            <select {...heldProps} value={filter.status ?? ''} onChange={(e) => changeFilter({ ...filter, status: (e.target.value || undefined) as CandidateStatus | undefined })}>
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
      {job.truncatedCandidates && <p className="hint">Showing the first 5,000 candidates; read fewer pages at a time to see the rest.</p>}
      {job.detectionLimitedFromPage !== undefined && (
        <p className="hint">
          Detection stopped before page {job.detectionLimitedFromPage}; read pages {job.detectionLimitedFromPage} on as a range to detect them.
        </p>
      )}
      <p className="visually-hidden selection-note" aria-live="polite">
        {announcement}
      </p>
      {bulkBusy && (
        <p id={bulkId} className="hint">
          Accepting the selected candidates…
        </p>
      )}
      {candidates && candidates.length === 0 && <p className="hint">No candidates match.</p>}
      {candidates && candidates.length > 0 && (
        <div className="actions">
          {/* aria-disabled, never `disabled`: a held button keeps focus and its place in the tab order. */}
          <button
            type="button"
            aria-disabled={atStart || held || undefined}
            aria-describedby={atStart || held ? pagingReason : undefined}
            onClick={() => turnPage(-1)}
          >
            Previous {pageSize}
          </button>
          <span id={rangeId} className="hint">
            {shown + 1}–{shown + candidates.length} of {total}
          </span>
          <button
            type="button"
            aria-disabled={atEnd || held || undefined}
            aria-describedby={atEnd || held ? pagingReason : undefined}
            onClick={() => turnPage(1)}
          >
            Next {pageSize}
          </button>
        </div>
      )}
      {pendingShown.length > 0 && (
        <div className="actions" role="group" aria-label="Accept several at once">
          {/* Item 9, D33: the same checks as accepting one at a time; blocked ones are listed, never forced. aria-disabled, as everywhere here. */}
          <button type="button" aria-disabled={held || undefined} aria-describedby={heldReason} onClick={() => !held && tick(pendingShown.map((c) => c.id))}>
            Select the {pendingShown.length} shown
          </button>
          {ticked.length > 0 && (
            <button type="button" aria-disabled={held || undefined} aria-describedby={heldReason} onClick={() => !held && clearTicks()}>
              Clear selection
            </button>
          )}
          <button
            type="button"
            aria-disabled={held || ticked.length === 0 || undefined}
            aria-describedby={heldReason ?? (ticked.length === 0 ? noneId : undefined)}
            onClick={() => void acceptTicked(false)}
          >
            Accept selected as drafts
          </button>
          <button
            type="button"
            aria-disabled={held || ticked.length === 0 || undefined}
            aria-describedby={heldReason ?? (ticked.length === 0 ? noneId : undefined)}
            onClick={() => void acceptTicked(true)}
          >
            Accept selected as reference
          </button>
          {ticked.length > 0 ? (
            <span className="hint">{ticked.length} selected</span>
          ) : (
            <span id={noneId} className="hint">
              Select at least one candidate first.
            </span>
          )}
        </div>
      )}
      {skipped.length > 0 && (
        <ul className="warnings" aria-label="Not accepted">
          {skipped.map((s) => (
            <li key={s.id}>
              {s.name}: {s.reasons.join('; ')}. It stays selected; edit it, or accept it as reference only.
            </li>
          ))}
        </ul>
      )}
      {candidates && candidates.length > 0 && (
        <ul className="resources" aria-label="Candidates">
          {candidates.map((c) => (
            <li key={c.id} className="resource">
              {c.status === 'pending' && (
                <input
                  type="checkbox"
                  className="row-select"
                  aria-label={`Select ${(c.edited ?? c.candidate).proposedName}`}
                  checked={ticked.includes(c.id)}
                  aria-disabled={held || undefined}
                  aria-describedby={heldReason}
                  onChange={() => !held && tick(ticked.includes(c.id) ? ticked.filter((id) => id !== c.id) : [...ticked, c.id])}
                />
              )}{' '}
              <button
                type="button"
                className="link"
                aria-current={selected === c.id ? 'true' : undefined}
                aria-disabled={(dirty && selected !== c.id) || undefined}
                aria-describedby={dirty && selected !== c.id ? dirtyId : undefined}
                onClick={() => select(c.id)}
              >
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
          sourceFamilies={source.rulesFamilies}
          onOpenPage={onOpenPage}
          onError={onError}
          onDirtyChange={setDirty}
          onChanged={async (text) => {
            onStatus(text);
            const list = await load();
            // WCAG 2.4.3: a reviewed candidate can leave the filtered list, and its details with it; focus goes back to the list.
            if (!list.some((c) => c.id === current.id)) {
              setSelected(undefined);
              heading.current?.focus();
            }
          }}
        />
      )}
    </section>
  );
}

interface DetailProps {
  stored: StoredCandidate;
  /** Items 3 and 4: a candidate can only be written for the families its source declares. */
  sourceFamilies: RulesFamilyId[];
  onOpenPage: (page: number) => void;
  onError: (error: unknown) => void;
  onChanged: (status: string) => Promise<void>;
  /** Whether the form differs from what is stored, so the panel can keep the player from leaving with an unsaved edit. */
  onDirtyChange: (dirty: boolean) => void;
}

function CandidateDetail({ stored, sourceFamilies, onOpenPage, onError, onChanged, onDirtyChange }: DetailProps) {
  const candidate = stored.edited ?? stored.candidate;
  const heading = useRef<HTMLHeadingElement>(null);
  const [name, setName] = useState(candidate.proposedName);
  const [families, setFamilies] = useState<RulesFamilyId[]>(candidate.rulesFamilies.filter((f) => sourceFamilies.includes(f)));
  const [kind, setKind] = useState<ContentKind>(candidate.proposedKind);
  const [summary, setSummary] = useState(candidate.summary ?? '');
  const [effects, setEffects] = useState(JSON.stringify(candidate.proposedEffects, null, 2));
  const [dismissed, setDismissed] = useState<string[]>([]);
  const [effectsError, setEffectsError] = useState<string>();
  const [check, setCheck] = useState<CandidateCheck>();
  const pending = stored.status === 'pending';

  const storedFamilies = candidate.rulesFamilies.filter((f) => sourceFamilies.includes(f));
  const sameSet = (a: string[], b: string[]) => a.length === b.length && a.every((x) => b.includes(x));
  const dirty =
    pending &&
    (name !== candidate.proposedName ||
      kind !== candidate.proposedKind ||
      summary !== (candidate.summary ?? '') ||
      effects !== JSON.stringify(candidate.proposedEffects, null, 2) ||
      !sameSet(families, storedFamilies) ||
      dismissed.length > 0);
  useEffect(() => {
    onDirtyChange(dirty);
    return () => onDirtyChange(false); // the detail goes away (another candidate, a save): nothing is unsaved any more
  }, [dirty, onDirtyChange]);

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
      await client.editCandidate(stored.id, { name, kind, rulesFamilies: families, summary, effects: parsed, dismissReferences: dismissed });
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
  const blocksId = `blocks-${stored.id}`;
  const problemsId = `problems-${stored.id}`;
  // What a blocked Accept points at: the blockers, else the validator's problems; nothing while the check is loading.
  const reasonId = check ? (check.blockers.length > 0 ? blocksId : check.report.errors.length > 0 ? problemsId : undefined) : undefined;
  return (
    <section aria-labelledby={headingId} className="play-panel">
      <h5 id={headingId} tabIndex={-1} ref={heading}>
        Candidate: {candidate.proposedName}
      </h5>
      <p className="hint">
        {candidate.proposedKind} · {pageText(candidate.page)} · {candidate.rulesFamilies.length > 0 ? candidate.rulesFamilies.join(', ') : 'no rules family yet'} · confidence {percent(candidate.confidence)} (a hint, not a
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
          <fieldset>
            <legend>Rules families</legend>
            {sourceFamilies.map((f) => (
              <label key={f} className="choice">
                <input
                  type="checkbox"
                  checked={families.includes(f)}
                  onChange={() => setFamilies(families.includes(f) ? families.filter((x) => x !== f) : [...families, f])}
                />
                {familyLabel(f)}
              </label>
            ))}
          </fieldset>
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
            <ul id={blocksId} className="warnings" aria-label="Blocks accepting">
              {check.blockers.map((b, i) => (
                <li key={`${b.code}-${i}`}>{b.message}</li>
              ))}
            </ul>
          )}
          {check.report.errors.length > 0 && (
            <ul id={problemsId} className="errors" aria-label="Problems">
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
          {/* aria-disabled, never `disabled`: a re-check can block a button that has focus (2.4.3); the reason is the visible list. */}
          <button
            type="button"
            aria-disabled={!check?.canAccept || undefined}
            aria-describedby={!check?.canAccept ? reasonId : undefined}
            onClick={() => check?.canAccept && accept(false)}
          >
            Accept as a draft
          </button>
          <button
            type="button"
            aria-disabled={!check?.canAcceptAsReference || undefined}
            aria-describedby={!check?.canAcceptAsReference ? reasonId : undefined}
            onClick={() => check?.canAcceptAsReference && accept(true)}
          >
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
