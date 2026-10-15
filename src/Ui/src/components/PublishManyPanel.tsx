import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { ContentComparison, ContentKind, ContentReference, PublishManyEntry, PublishManyPreview, SourceRecord } from '../api/types';

/** One content of the source whose newest revision is a draft (the studio's "pending"). */
export interface PendingDraft {
  contentId: string;
  name: string;
  kind: ContentKind;
  /** The content already has a published revision, so publishing replaces what characters get. Starts unticked. */
  replacesPublished: boolean;
  draft: ContentReference;
  latestPublished?: ContentReference;
}

interface Props {
  source: SourceRecord;
  pending: PendingDraft[];
  onError: (error: unknown) => void;
  onClose: () => void;
  /** Called once with how many of the ticked drafts were published, so the studio can reload and say so. */
  onPublished: (drafts: number) => void;
}

const reasonId = 'publish-many-reason';

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** What a content.compare answer says, as plain lines (the same words the compare panel uses for effects). */
function describeChanges(comparison: ContentComparison): string[] {
  return [
    ...comparison.mechanics.properties.map((p) => `${p.property}: ${p.before ?? 'none'} to ${p.after ?? 'none'}`),
    ...comparison.mechanics.effects.map((e) => `${e.change} ${e.effectId}${e.before !== undefined && e.after !== undefined ? `: ${e.before} to ${e.after}` : ''}`),
    ...comparison.text.map((t) => `text changed in ${t.where}`),
  ];
}

/**
 * Tester feedback item 9, D33 (ADR-004: a confirmed preview of the exact drafts is review): publishes the ticked pending
 * drafts of one source in one all-or-nothing transaction. The preview is read-only and re-runs for the ticked set; the
 * button sends the ticked ids and the pending revision ids the preview showed (`expected`), so a change in between is
 * refused rather than published. Drafts of content that already has a published revision start unticked, with their
 * differences on request. Grants that pin a published draft are re-pointed by the service; this lists them. Nothing here
 * changes a character.
 */
export function PublishManyPanel({ source, pending, onError, onClose, onPublished }: Props) {
  const [ticks, setTicks] = useState<string[]>(() => pending.filter((p) => !p.replacesPublished).map((p) => p.contentId));
  // The preview is of one ticked set; an answer for another set is stale and is not shown.
  const [answer, setAnswer] = useState<{ key: string; preview?: PublishManyPreview }>();
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<{ drafts: number; republished: number }>();
  const [changes, setChanges] = useState<Record<string, string[]>>({});
  const heading = useRef<HTMLHeadingElement>(null);
  const result = useRef<HTMLParagraphElement>(null);
  const latest = useRef(0);
  const publishing = useRef(false);

  // The ticked drafts, in the order the table shows them.
  const ticked = pending.filter((p) => ticks.includes(p.contentId));
  const tickedIds = ticked.map((p) => p.contentId);
  const key = tickedIds.join(',');
  const preview = answer?.key === key ? answer.preview : undefined;
  const loading = key !== '' && answer?.key !== key;

  // WCAG 2.4.3: opening the panel moves focus to its heading.
  useEffect(() => heading.current?.focus(), []);

  useEffect(() => {
    const request = ++latest.current; // a newer tick supersedes any answer still on its way
    if (done || key === '') return;
    client
      .publishManyPreview(key.split(','))
      .then((next) => {
        if (request === latest.current) setAnswer({ key, preview: next });
      })
      .catch((error: unknown) => {
        if (request !== latest.current) return;
        setAnswer({ key, preview: undefined });
        onError(error);
      });
  }, [key, done, onError]);

  // Focus follows the result (2.4.3): the form it replaced is gone.
  useEffect(() => {
    if (done) result.current?.focus();
  }, [done]);

  const shown: PublishManyEntry[] = preview ? [...preview.entries, ...preview.dependents] : [];
  const errors = shown.flatMap((e) => e.report.errors.map((d) => ({ name: e.name, message: d.message })));
  const findings = shown.flatMap((e) => e.findings.filter((f) => f.severity !== 'error'));
  const rePointed = shown.flatMap((e) => e.rePointings);
  const byDraft = new Map(preview?.entries.map((e) => [e.draft.contentId, e]));

  const reason = loading
    ? 'Checking the drafts…'
    : ticked.length === 0
      ? 'Tick at least one draft.'
      : !preview
        ? 'The drafts could not be checked. Untick one and tick it again to check them again.'
        : errors.length > 0 || !preview.canPublish
          ? 'Fix the problems above first, or untick the drafts that have them.'
          : undefined;

  function toggle(id: string) {
    if (busy) return;
    setTicks((t) => (t.includes(id) ? t.filter((x) => x !== id) : [...t, id]));
  }

  async function publish() {
    if (reason || !preview || publishing.current) return;
    publishing.current = true; // a second press while the first is in flight does nothing
    setBusy(true);
    try {
      const entries = preview.entries;
      const outcome = await client.publishMany(
        entries.map((e) => e.draft.contentId),
        entries.map((e) => e.draft.revisionId),
      );
      setDone({ drafts: entries.length, republished: Math.max(0, outcome.published.length - entries.length) });
      onPublished(entries.length);
    } catch (error) {
      onError(error);
    } finally {
      publishing.current = false;
      setBusy(false);
    }
  }

  async function showChanges(p: PendingDraft) {
    if (!p.latestPublished) return;
    try {
      const comparison = await client.compare({ from: p.latestPublished, to: p.draft });
      const lines = describeChanges(comparison);
      setChanges((c) => ({ ...c, [p.contentId]: lines.length > 0 ? lines : ['nothing differs'] }));
    } catch (error) {
      onError(error);
    }
  }

  const headingId = 'publish-many-heading';
  return (
    <section aria-labelledby={headingId} className="play-panel">
      <h3 id={headingId} tabIndex={-1} ref={heading}>
        Publish drafts of {source.title}
      </h3>
      {done ? (
        <>
          <p ref={result} tabIndex={-1}>
            {`Published ${plural(done.drafts, 'draft', 'drafts')}.`}
          </p>
          {done.republished > 0 && <p>{plural(done.republished, 'other entry was', 'other entries were')} re-pointed and published again.</p>}
          <p className="hint">Characters keep their current revisions until you review an update.</p>
          <div className="actions">
            <button type="button" onClick={onClose}>
              Close
            </button>
          </div>
        </>
      ) : (
        <>
          <p className="hint">
            Publishing creates permanent revisions, all or none. Review what will be published, then confirm. Entries that already have a published version
            start unticked.
          </p>
          <table className="publish-table">
            <caption>Pending drafts in {source.title}</caption>
            <thead>
              <tr>
                <th scope="col">Publish</th>
                <th scope="col">Name</th>
                <th scope="col">Kind</th>
                <th scope="col">Problems</th>
                <th scope="col">Replaces</th>
              </tr>
            </thead>
            <tbody>
              {pending.map((p) => {
                const entry = byDraft.get(p.contentId);
                const isTicked = ticks.includes(p.contentId);
                return (
                  <tr key={p.contentId}>
                    <td>
                      <input type="checkbox" aria-label={`Publish ${p.name}`} checked={isTicked} onChange={() => toggle(p.contentId)} />
                    </td>
                    <th scope="row">{p.name}</th>
                    <td>{p.kind}</td>
                    <td>
                      {!isTicked
                        ? 'not checked'
                        : !entry
                          ? 'checking…'
                          : entry.report.errors.length === 0 && entry.report.warnings.length === 0
                            ? 'none'
                            : [
                                entry.report.errors.length > 0 ? plural(entry.report.errors.length, 'error', 'errors') : '',
                                entry.report.warnings.length > 0 ? plural(entry.report.warnings.length, 'warning', 'warnings') : '',
                              ]
                                .filter(Boolean)
                                .join(', ')}
                    </td>
                    <td>
                      {p.replacesPublished ? (
                        <>
                          replaces the published version{' '}
                          <button type="button" onClick={() => void showChanges(p)}>
                            Show what changes in {p.name}
                          </button>
                          {changes[p.contentId] && (
                            <ul aria-label={`Changes in ${p.name}`}>
                              {changes[p.contentId]!.map((line) => (
                                <li key={line}>{line}</li>
                              ))}
                            </ul>
                          )}
                        </>
                      ) : (
                        'new'
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {errors.length > 0 && (
            <ul className="errors" aria-label="Problems that block publishing">
              {errors.map((e, i) => (
                <li key={`${e.name}-${i}`}>
                  {e.name}: {e.message}
                </li>
              ))}
            </ul>
          )}
          {rePointed.length > 0 && (
            <>
              <p>Grants and choices that name something published here are pointed at the published version, and these are published again as new revisions:</p>
              <ul aria-label="Re-pointed and republished">
                {rePointed.map((r) => (
                  <li key={r.dependent.revisionId}>
                    {r.dependentName}:{' '}
                    {[
                      r.grantsMoved > 0 ? plural(r.grantsMoved, 'grant', 'grants') : '',
                      r.optionsMoved > 0 ? plural(r.optionsMoved, 'choice option', 'choice options') : '',
                    ]
                      .filter(Boolean)
                      .join(' and ')}{' '}
                    now point at the published versions.
                  </li>
                ))}
              </ul>
            </>
          )}
          {preview && preview.dependents.length > 0 && (
            <p className="hint">
              Also published again because they use something in this batch: {preview.dependents.map((d) => d.name).join(', ')}.
            </p>
          )}
          {findings.length > 0 && (
            <ul aria-label="What the debugger found">
              {findings.map((f, i) => (
                <li key={`${f.content.revisionId}-${f.code}-${i}`} className={f.severity === 'warning' ? 'warn' : undefined}>
                  {f.severity === 'warning' ? 'Warning' : 'Note'} in {f.contentName}: {f.message}
                </li>
              ))}
            </ul>
          )}
          <div className="actions">
            {/* aria-disabled, never `disabled`: the press is refused in the handler and the reason is visible (2.4.3). */}
            <button
              type="button"
              aria-disabled={Boolean(reason) || busy || undefined}
              aria-describedby={reason ? reasonId : undefined}
              onClick={() => void publish()}
            >
              Publish {plural(ticked.length, 'draft', 'drafts')}
            </button>
            <button type="button" onClick={onClose}>
              Cancel
            </button>
            {reason && (
              <span id={reasonId} className="hint">
                {reason}
              </span>
            )}
          </div>
        </>
      )}
    </section>
  );
}
