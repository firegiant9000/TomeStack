import { useCallback, useEffect, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { ImportJob, ImportSearchHit, SourceRecord } from '../api/types';
import { CandidateReviewPanel } from './CandidateReviewPanel';

interface Props {
  source: SourceRecord;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
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
export function ImportPanel({ source, onError, onStatus }: Props) {
  const [jobs, setJobs] = useState<ImportJob[]>([]);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [reviewing, setReviewing] = useState<string>();
  const [query, setQuery] = useState('');
  const [hits, setHits] = useState<ImportSearchHit[]>();

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

  async function start(request: { firstPage?: number; lastPage?: number; wholeDocument?: boolean }) {
    try {
      await client.startImport(source.id, request);
      onStatus(`Reading ${source.title}. Nothing changes until you review what is found.`);
      await refresh();
    } catch (error) {
      onError(error);
    }
  }

  async function act(action: () => Promise<unknown>, done: string) {
    try {
      await action();
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

  return (
    <section aria-label={`Read the text of ${source.title}`} className="play-panel">
      <h4>Read the text and find candidates</h4>
      <p className="hint">TomeStack reads the PDF on this computer and proposes entries for you to review. The text is never exported.</p>
      <div className="inline-form">
        <label className="field">
          From page
          <input type="number" min={1} value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="field">
          To page (optional)
          <input type="number" min={1} value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <button type="button" disabled={!valid || running} onClick={() => start({ firstPage: first, lastPage: last ?? first })}>
          Read these pages
        </button>
        <button type="button" disabled={running} onClick={() => start({ wholeDocument: true })}>
          Read the whole document
        </button>
      </div>

      {jobs.length > 0 && (
        <ul className="resources" aria-label={`Imports of ${source.title}`}>
          {jobs.map((job) => (
            <li key={job.id} className="resource">
              <p role={active(job) ? 'status' : undefined}>{progress(job)}</p>
              <div className="actions">
                {active(job) && (
                  <button type="button" onClick={() => act(() => client.cancelImport(job.id), 'Import cancelled. The pages read so far are kept.')}>
                    Cancel
                  </button>
                )}
                {resumable(job) && (
                  <button type="button" disabled={running} onClick={() => act(() => client.resumeImport(job.id), 'Import resumed.')}>
                    Resume
                  </button>
                )}
                {job.status === 'completed' && (
                  <button type="button" aria-expanded={reviewing === job.id} onClick={() => setReviewing(reviewing === job.id ? undefined : job.id)}>
                    Review {job.candidates} candidate{job.candidates === 1 ? '' : 's'}
                  </button>
                )}
              </div>
              {reviewing === job.id && <CandidateReviewPanel job={job} source={source} onError={onError} onStatus={onStatus} onOpenPage={openPage} />}
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
