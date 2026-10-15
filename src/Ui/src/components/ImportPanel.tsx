import { useCallback, useEffect, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { ImportJob, ImportSearchHit, RulesFamilyId, SourceRecord } from '../api/types';
import { CandidateReviewPanel } from './CandidateReviewPanel';
import { familyLabel, familyShortName } from './familyLabels';

interface Props {
  source: SourceRecord;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  onOpenStudio: (sourceId: string) => void;
}

const active = (job: ImportJob) => job.status === 'queued' || job.status === 'running';
const resumable = (job: ImportJob) => job.status === 'cancelled' || job.status === 'failed' || job.status === 'interrupted';

function progress(job: ImportJob): string {
  const scope = job.wholeDocument ? 'whole document' : `pages ${job.firstPage}–${job.lastPage ?? job.pageCount ?? '…'}`;
  const counts = `${job.pagesDone} page${job.pagesDone === 1 ? '' : 's'} read${job.pagesFromOcr ? `, ${job.pagesFromOcr} by OCR` : ''}${job.pagesWithoutText ? `, ${job.pagesWithoutText} without text` : ''}${job.pagesFailed ? `, ${job.pagesFailed} unreadable` : ''}`;
  const failure = job.failureMessage ? ` ${job.failureMessage}` : '';
  return `${scope}: ${job.status}, ${counts}${job.status === 'completed' ? `, ${job.candidates} candidate${job.candidates === 1 ? '' : 's'}` : ''}.${failure}`;
}

/**
 * M4 D2, D5 (SPEC I-01, I-03): extract a source's PDF as a cancellable, resumable job, search its text, and review
 * what was found. Nothing here changes content: accepting a candidate creates a draft, which the studio publishes.
 */
export function ImportPanel({ source, onError, onStatus, onOpenStudio }: Props) {
  const [jobs, setJobs] = useState<ImportJob[]>([]);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [reviewing, setReviewing] = useState<string>();
  const [query, setQuery] = useState('');
  const [hits, setHits] = useState<ImportSearchHit[]>();
  const [picked, setPicked] = useState<{ sourceId: string; value: RulesFamilyId | 'both' }>();

  const refresh = useCallback(() => client.listImports(source.id).then(setJobs), [source.id]);

  useEffect(() => {
    refresh().catch(onError);
  }, [refresh, onError]);

  // Progress: poll while a job runs (the protocol is request/response, ADR-006).
  const running = jobs.some(active);
  useEffect(() => {
    if (!running) return;
    const timer = setInterval(() => {
      refresh().catch(onError);
    }, 400);
    return () => clearInterval(timer);
  }, [running, refresh, onError]);

  // 2026-10-14 item 3: a book written for both rules families is asked what it is written for; no default, so an armour row
  // or a class table is never silently placed in one of them. A one-family source needs no answer.
  const askFamily = source.rulesFamilies.length > 1;
  const answer = picked?.sourceId === source.id ? picked.value : undefined;
  const needsAnswer = askFamily && answer === undefined;
  const hintId = `written-for-hint-${source.id}`; // one panel per source on the Sources screen: ids must be unique (4.1.2)
  const writtenFor = askFamily && answer ? (answer === 'both' ? [...source.rulesFamilies] : [answer]) : undefined;

  const busyId = `read-busy-${source.id}`;
  const pagesHintId = `pages-hint-${source.id}`;

  /** Put focus on a job's line: the button pressed is about to go away or change, and focus must not fall to the page (2.4.3). */
  function focusJobLine(jobId: string) {
    document.getElementById(`import-progress-${jobId}`)?.focus();
  }

  async function start(request: { firstPage?: number; lastPage?: number; wholeDocument?: boolean }, blocked: boolean) {
    if (blocked) return; // aria-disabled with its reason shown, so this press is refused openly
    try {
      await client.startImport(source.id, writtenFor ? { ...request, writtenFor } : request);
      onStatus(`Reading ${source.title}. Nothing changes until you review what is found.`);
      await refresh();
    } catch (error) {
      onError(error);
    }
  }

  async function act(action: () => Promise<unknown>, done: string, jobId: string) {
    try {
      await action();
      focusJobLine(jobId); // before the refresh removes the pressed button
      onStatus(done);
      await refresh();
    } catch (error) {
      onError(error);
    }
  }

  async function search(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      setHits(await client.searchImport(source.id, query));
    } catch (error) {
      onError(error);
    }
  }

  async function openPage(page: number) {
    try {
      await client.openPage(source.id, page);
    } catch (error) {
      onError(error instanceof TomeStackError && error.code === 'unsupported' ? new Error('Opening a PDF page needs the TomeStack desktop app.') : error);
    }
  }

  const first = Number(from);
  const last = to === '' ? undefined : Number(to);
  const valid = from !== '' && Number.isInteger(first) && first >= 1 && (last === undefined || (Number.isInteger(last) && last >= first));
  const wholeBlocked = running || needsAnswer;
  const pagesBlocked = wholeBlocked || !valid;
  // One reason per button, the most pressing first; each is visible text, so aria-describedby never points at nothing.
  const wholeReason = running ? busyId : needsAnswer ? hintId : undefined;
  const pagesReason = running ? busyId : needsAnswer ? hintId : !valid ? pagesHintId : undefined;
  const busyText = 'Reading… — cancel it first or wait.';

  return (
    <section aria-label={`Read the text of ${source.title}`} className="play-panel">
      <h4>
        Read the text and find candidates <span className="tag">Experimental</span>
      </h4>
      <p className="hint">
        TomeStack reads the PDF on this computer and proposes entries for you to review. The text is never exported. Experimental: it has not been
        proven on a real third-party book yet, so check every candidate against its page. Nothing becomes a rule until you publish it.
      </p>
      {askFamily ? (
        <fieldset>
          <legend>This book is written for</legend>
          <p className="hint">Armour tables and class tables do not say which rules they use, so your answer decides what TomeStack proposes.</p>
          {[...source.rulesFamilies, 'both' as const].map((value) => (
            <label key={value} className="choice">
              <input
                type="radio"
                name={`written-for-${source.id}`}
                checked={answer === value}
                onChange={() => setPicked({ sourceId: source.id, value })}
              />
              {value === 'both' ? 'Both' : familyLabel(value)}
            </label>
          ))}
        </fieldset>
      ) : (
        <p className="hint">Written for {source.rulesFamilies.map(familyShortName).join(' and ')} (from the source).</p>
      )}
      {needsAnswer && (
        <p id={hintId} className="hint">
          Say what the book is written for.
        </p>
      )}
      {running && (
        <p id={busyId} className="hint">
          {busyText}
        </p>
      )}
      {!valid && (
        <p id={pagesHintId} className="hint">
          {from === '' ? 'Enter a first page.' : 'Check the pages: the last page cannot be before the first.'}
        </p>
      )}
      <div className="inline-form">
        <label className="field">
          From page
          <input type="number" min={1} value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="field">
          To page (optional)
          <input type="number" min={1} value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        {/* aria-disabled, never `disabled`: the pressed button stays focused when the job starts (2.4.3, 4.1.2). */}
        <button
          type="button"
          aria-disabled={pagesBlocked || undefined}
          aria-describedby={pagesReason}
          onClick={() => start({ firstPage: first, lastPage: last ?? first }, pagesBlocked)}
        >
          Read these pages
        </button>
        <button
          type="button"
          aria-disabled={wholeBlocked || undefined}
          aria-describedby={wholeReason}
          onClick={() => start({ wholeDocument: true }, wholeBlocked)}
        >
          Read the whole document
        </button>
      </div>

      {jobs.length > 0 && (
        <ul className="resources" aria-label={`Imports of ${source.title}`}>
          {jobs.map((job) => (
            <li key={job.id} className="resource">
              {/* Live whatever the state: a region added with the last change ("completed") is often not announced. */}
              <p id={`import-progress-${job.id}`} tabIndex={-1} aria-live="polite" aria-atomic="true">
                {progress(job)}
              </p>
              <div className="actions">
                {active(job) && (
                  <button type="button" onClick={() => act(() => client.cancelImport(job.id), 'Import cancelled. The pages read so far are kept.', job.id)}>
                    Cancel
                  </button>
                )}
                {resumable(job) && (
                  <button
                    type="button"
                    aria-disabled={running || undefined}
                    aria-describedby={running ? busyId : undefined}
                    onClick={() => {
                      if (!running) void act(() => client.resumeImport(job.id), 'Import resumed.', job.id);
                    }}
                  >
                    Resume
                  </button>
                )}
                {job.status === 'completed' && (
                  <button type="button" aria-expanded={reviewing === job.id} onClick={() => setReviewing(reviewing === job.id ? undefined : job.id)}>
                    Review {job.candidates} candidate{job.candidates === 1 ? '' : 's'}
                  </button>
                )}
                {/* While any job is under review only the panel's own button shows: it knows about an unsaved edit (ruling R26). */}
                {job.status === 'completed' && job.candidates > 0 && reviewing === undefined && (
                  <button type="button" onClick={() => onOpenStudio(source.id)}>
                    Open {source.title} in the studio
                  </button>
                )}
              </div>
              {reviewing === job.id && (
                <CandidateReviewPanel job={job} source={source} onError={onError} onStatus={onStatus} onOpenPage={openPage} onOpenStudio={onOpenStudio} />
              )}
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={search} aria-label={`Search ${source.title}`} className="inline-form">
        <label className="field">
          Search the text of {source.title}
          <input value={query} onChange={(e) => setQuery(e.target.value)} minLength={2} maxLength={100} />
        </label>
        <button type="submit" disabled={query.trim().length < 2}>
          Search
        </button>
      </form>
      {hits && (
        <ul aria-label="Search results" className="resources">
          {hits.length === 0 && <li className="hint">No page of the read text contains it.</li>}
          {hits.map((hit) => (
            <li key={hit.page} className="resource">
              <span className="tag">p. {hit.page}</span> {hit.snippet}{' '}
              <button type="button" onClick={() => openPage(hit.page)}>
                Open page {hit.page}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
