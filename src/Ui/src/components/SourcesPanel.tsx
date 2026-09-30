import { useCallback, useEffect, useRef, useState, type ChangeEvent } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { AttachmentInfo, DetachPreview, SourcePackPreview, SourceRecord } from '../api/types';
import { downloadBase64, readFileAsBase64 } from '../files';
import { ImportPanel } from './ImportPanel';

/**
 * What the author confirms to mark a source as shareable (M6 slice 1). The same words as the service's
 * `TomeStackApp.OwnWorkStatement`, which a source pack records as the attestation.
 */
const ownWorkStatement =
  'The source is my own work, and it holds no text, tables or rules copied from a book, PDF or other material I did not write.';

/** M6 slice 1: may this source go into a source pack? The service checks again at every export. */
const inPack = (s: SourceRecord) => s.redistributable && !s.importDerived && s.origin !== 'received' && !!s.shareConfirmedAt;

/** M6 slice 1: how a homebrew source may be shared, in words. */
function sharing(source: SourceRecord): string {
  if (source.importDerived) return 'Holds material imported from a PDF: never shared, even after the PDF is removed.';
  if (source.origin === 'received') return source.redistributable ? 'Received from someone else; may travel on in character shares.' : 'Received from someone else; not shared.';
  if (inPack(source)) return 'Marked as shareable: your own work, can go in a source pack.';
  if (source.redistributable) return 'Shared in character shares, but not confirmed as your own work, so not in a source pack.';
  return 'Not shared.';
}

interface Props {
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

const size = (bytes: number) => (bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`);

const statusText: Record<AttachmentInfo['status'], string> = {
  available: 'available',
  missing: 'missing: attach it again',
  changed: 'changed since it was linked: page numbers may have moved',
};

/**
 * SPEC I-01, I-03 (owner decision 2026-09-27: no text extraction in M2): pages of the attached PDF become a draft
 * reference-only entry citing them. It is inactive until the player publishes it in the studio (ADR-004).
 */
function PageImportForm({ source, onError, onStatus }: { source: SourceRecord } & Props) {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [title, setTitle] = useState('');
  const start = Number(from);
  const end = to === '' ? undefined : Number(to);
  const valid = from !== '' && Number.isInteger(start) && start >= 1 && (end === undefined || (Number.isInteger(end) && end >= start));

  async function run(request: { start?: number; end?: number; title?: string; wholeDocument?: boolean }) {
    try {
      const draft = await client.importPages(source.id, request);
      onStatus(`Draft reference entry "${draft.name}" created. Review and publish it in the studio; until then it does nothing.`);
      setFrom('');
      setTo('');
      setTitle('');
    } catch (error) {
      onError(error);
    }
  }

  return (
    <fieldset>
      <legend>Import pages of {source.title} as reference</legend>
      <p className="hint">Nothing is read from the PDF: the entry cites the pages and opens them. Text stays in your PDF.</p>
      <div className="inline-form">
        <label className="field">
          First page
          <input type="number" min={1} value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="field">
          Last page (optional)
          <input type="number" min={1} value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <label className="field">
          Title (optional)
          <input value={title} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <button type="button" disabled={!valid} onClick={() => run({ start, end, title: title.trim() || undefined })}>
          Import pages
        </button>
        <button type="button" onClick={() => run({ wholeDocument: true, title: title.trim() || undefined })}>
          Import the whole document
        </button>
      </div>
    </fieldset>
  );
}

/**
 * M6 slice 1: share your own homebrew sources as a source pack. Only sources marked as shareable are offered; the pack
 * carries their published content and nothing else (no characters, drafts, PDFs or text read from PDFs).
 */
function SourcePackForm({ sources, onError, onStatus }: { sources: SourceRecord[] } & Props) {
  const [picked, setPicked] = useState<string[]>([]);
  const [preview, setPreview] = useState<SourcePackPreview>();
  const offered = sources.filter(inPack);
  const chosen = picked.filter((id) => offered.some((s) => s.id === id));

  function toggle(id: string, on: boolean) {
    setPreview(undefined);
    setPicked((current) => (on ? [...current, id] : current.filter((x) => x !== id)));
  }

  async function save() {
    try {
      const outcome = await client.saveSourcePackAs(chosen);
      if (outcome.saved) onStatus(`Saved the source pack ${outcome.fileName}.`);
    } catch (error) {
      if (error instanceof TomeStackError && error.code === 'unsupported') {
        // Browser development (DevHost) has no native Save dialog: download the pack instead.
        try {
          const exported = await client.exportSourcePack(chosen);
          downloadBase64(exported.fileName, exported.base64);
          onStatus(`Downloaded the source pack ${exported.fileName}.`);
        } catch (inner) {
          onError(inner);
        }
        return;
      }
      onError(error);
    }
  }

  return (
    <section className="play-panel" aria-labelledby="source-pack-heading">
      <h3 id="source-pack-heading">Share sources as a pack</h3>
      <p className="hint">
        A source pack carries the published content of sources you marked as shareable, and nothing else: no characters,
        drafts, PDFs or text read from PDFs. Whoever imports it sees your statement that the sources are your own work.
      </p>
      {offered.length === 0 ? (
        <p>No source is marked as shareable yet.</p>
      ) : (
        <fieldset>
          <legend>Sources to share</legend>
          {offered.map((s) => (
            <label key={s.id} className="check">
              <input type="checkbox" checked={chosen.includes(s.id)} onChange={(e) => toggle(s.id, e.target.checked)} />
              {s.title}
            </label>
          ))}
          <div className="actions">
            <button type="button" disabled={chosen.length === 0} onClick={() => client.sourcePackPreview(chosen).then(setPreview).catch(onError)}>
              Preview pack
            </button>
          </div>
        </fieldset>
      )}
      {preview && (
        <div role="region" aria-label="Source pack preview">
          <p>
            {preview.fileName}: {preview.sources.length} source{preview.sources.length === 1 ? '' : 's'}, {preview.revisions} published
            revision{preview.revisions === 1 ? '' : 's'}.{preview.drafts > 0 ? ` ${preview.drafts} draft${preview.drafts === 1 ? ' stays' : 's stay'} here.` : ''}
          </p>
          {preview.warnings.length > 0 && (
            <ul>
              {preview.warnings.map((w, i) => (
                <li key={i}>{w.message}</li>
              ))}
            </ul>
          )}
          <div className="actions">
            <button type="button" onClick={save}>
              Save pack…
            </button>
          </div>
        </div>
      )}
    </section>
  );
}

/**
 * M2 item 6, ADR-005, SPEC S-01, S-04: every source with its license and its PDF. A PDF is copied into TomeStack's
 * data folder by default (a managed copy survives moving or renaming the original), or linked where it is. Removing it
 * shows what breaks first and keeps all content.
 */
export function SourcesPanel({ onError, onStatus }: Props) {
  const [sources, setSources] = useState<SourceRecord[]>([]);
  const [attachments, setAttachments] = useState<Record<string, AttachmentInfo | undefined>>({});
  const [removing, setRemoving] = useState<DetachPreview>();
  const [marking, setMarking] = useState<string>();
  const [ownWork, setOwnWork] = useState(false);
  const browserTarget = useRef<string | undefined>(undefined);
  const fileInput = useRef<HTMLInputElement>(null);

  const fetchAll = useCallback(async () => {
    const all = await client.listSources();
    const entries = await Promise.all(all.map(async (s) => [s.id, await client.attachment(s.id)] as const));
    return { all, attached: Object.fromEntries(entries) };
  }, []);

  const load = async () => {
    const { all, attached } = await fetchAll();
    setSources(all);
    setAttachments(attached);
  };

  useEffect(() => {
    fetchAll()
      .then(({ all, attached }) => {
        setSources(all);
        setAttachments(attached);
      })
      .catch(onError);
  }, [fetchAll, onError]);

  async function attach(source: SourceRecord, mode: 'managed' | 'linked') {
    // Recorded before the host answers: a file chosen in the browser picker must never find no target (it was dropped
    // when the pick came before the "unsupported" reply had been handled).
    browserTarget.current = source.id;
    try {
      const outcome = await client.attachPdf(source.id, mode);
      if (outcome.attached) {
        onStatus(`Attached ${outcome.attachment?.originalFileName} to ${source.title}.`);
        await load();
      }
    } catch (error) {
      if (error instanceof TomeStackError && error.code === 'unsupported' && mode === 'managed') {
        // Browser development (DevHost) has no native dialog: pick the file here and send its bytes.
        fileInput.current?.click();
        return;
      }
      onError(error);
    }
  }

  async function attachFromBrowser(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = '';
    const target = browserTarget.current;
    if (!file || !target) return;
    try {
      const info = await client.attachPdfData(target, file.name, await readFileAsBase64(file));
      onStatus(`Attached ${info.originalFileName}.`);
      await load();
    } catch (error) {
      onError(error);
    }
  }

  /** M6 slice 1: "Mark as shareable" (with the author's confirmation) or "Stop sharing". */
  async function setShareable(source: SourceRecord, shareable: boolean) {
    try {
      await client.setShareable(source.id, shareable, shareable && ownWork);
      onStatus(shareable ? `${source.title} is marked as shareable.` : `${source.title} is no longer shared. Files you already sent are not recalled.`);
      setMarking(undefined);
      setOwnWork(false);
      await load();
    } catch (error) {
      onError(error);
    }
  }

  async function confirmRemove() {
    if (!removing) return;
    try {
      await client.detach(removing.sourceId);
      onStatus(`Removed ${removing.originalFileName}. The content stays; its page links no longer open.`);
      setRemoving(undefined);
      await load();
    } catch (error) {
      onError(error);
    }
  }

  return (
    <section className="panel" aria-labelledby="sources-page-heading">
      <h2 id="sources-page-heading">Sources</h2>
      <p className="hint">
        Attach your own PDF of a source to open cited pages from features. PDFs stay on this computer: exports never include
        them. A copy is kept in TomeStack&apos;s data folder unless you link the file where it is.
      </p>
      <input ref={fileInput} type="file" accept=".pdf,application/pdf" hidden onChange={attachFromBrowser} aria-label="PDF file" />
      <SourcePackForm sources={sources} onError={onError} onStatus={onStatus} />
      <ul className="resources">
        {sources.map((source) => {
          const pdf = attachments[source.id];
          return (
            <li key={source.id} className="resource" aria-label={source.title}>
              <h3>{source.title}</h3>
              <p className="hint">
                {source.publisher} · {source.license} · {source.redistributable ? 'may be shared' : 'not shared in exports'} ·{' '}
                {source.rulesFamilies.join(', ')}
              </p>
              {source.editionVersion === 'homebrew' && <p>{sharing(source)}</p>}
              <p>
                {pdf
                  ? `PDF: ${pdf.originalFileName} (${size(pdf.byteLength)}, ${pdf.mode === 'managed' ? 'copy in TomeStack' : 'linked'}): ${statusText[pdf.status]}`
                  : 'No PDF attached.'}
              </p>
              <div className="actions">
                <button type="button" onClick={() => attach(source, 'managed')}>
                  {pdf ? 'Replace PDF…' : 'Attach PDF…'}
                </button>
                <button type="button" onClick={() => attach(source, 'linked')}>
                  Link PDF where it is…
                </button>
                {pdf && (
                  <button
                    type="button"
                    onClick={() =>
                      client
                        .detachPreview(source.id)
                        .then(setRemoving)
                        .catch(onError)
                    }
                  >
                    Remove PDF…
                  </button>
                )}
                {source.editionVersion === 'homebrew' && !source.importDerived && source.origin !== 'received' && !inPack(source) && (
                  <button type="button" onClick={() => { setOwnWork(false); setMarking(source.id); }}>
                    Mark as shareable…
                  </button>
                )}
                {source.editionVersion === 'homebrew' && source.redistributable && (
                  <button type="button" onClick={() => setShareable(source, false)}>
                    Stop sharing
                  </button>
                )}
              </div>
              {marking === source.id && (
                <div role="alertdialog" aria-label={`Mark ${source.title} as shareable?`} className="play-panel">
                  <p>
                    A shareable source can go in a source pack and in character shares. Once a PDF is attached, it never can
                    again.
                  </p>
                  <label className="check">
                    <input type="checkbox" checked={ownWork} onChange={(e) => setOwnWork(e.target.checked)} />
                    {ownWorkStatement}
                  </label>
                  <div className="actions">
                    <button type="button" disabled={!ownWork} onClick={() => setShareable(source, true)}>
                      Mark as shareable
                    </button>
                    <button type="button" onClick={() => setMarking(undefined)}>
                      Cancel
                    </button>
                  </div>
                </div>
              )}
              {pdf && pdf.status !== 'missing' && source.editionVersion === 'homebrew' && (
                <>
                  <PageImportForm source={source} onError={onError} onStatus={onStatus} />
                  <ImportPanel source={source} onError={onError} onStatus={onStatus} />
                </>
              )}
              {removing?.sourceId === source.id && (
                <div role="alertdialog" aria-label={`Remove ${removing.originalFileName}?`} className="play-panel">
                  <p>
                    {removing.pageLinks === 0
                      ? 'No content cites pages in this source.'
                      : `${removing.pageLinks} entr${removing.pageLinks === 1 ? 'y cites' : 'ies cite'} pages in it (${removing.contentNames.slice(0, 5).join(', ')}${removing.contentNames.length > 5 ? ', …' : ''}). Their "Open page" links will stop working.`}{' '}
                    All content stays.
                  </p>
                  <div className="actions">
                    <button type="button" onClick={confirmRemove}>
                      Remove PDF
                    </button>
                    <button type="button" onClick={() => setRemoving(undefined)}>
                      Keep it
                    </button>
                  </div>
                </div>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
