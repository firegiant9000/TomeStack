import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type {
  AffectedCharacter,
  AppInfo,
  ContentKind,
  ContentOption,
  ContentReference,
  ContentRevision,
  ContentSpellListResult,
  ContentTreeView,
  DebugFinding,
  DebugReport,
  DesignHint,
  Effect,
  PublishResult,
  RulesFamilyId,
  SourceRecord,
  StudioEntry,
  ValidationReport,
} from '../api/types';
import { FormulaPreview } from './FormulaPreview';
import { UpdateReviewPanel } from './UpdateReviewPanel';
import { ClassBasicsEditor, classBasicElementId, isClassBasic } from './ClassBasicsEditor';
import { BackgroundBasicsEditor } from './BackgroundBasicsEditor';
import { SpeciesBasicsEditor } from './SpeciesBasicsEditor';
import { SpellBasicsEditor } from './SpellBasicsEditor';
import type { BeforeSave } from './OriginPieces';
import { DebugFindings } from './DebugFindings';
import { SandboxPanel } from './SandboxPanel';
import { ComparePanel } from './ComparePanel';
import { PublishManyPanel, type PendingDraft } from './PublishManyPanel';
import { SpellListForm } from './SpellListForm';
import { TreeView } from './TreeView';
import { fromTemplate, templates, type TemplateId } from '../templates';
import { designFeedbackOn, setDesignFeedback } from '../designFeedback';
import { abilities, nextGroupKey, nextScaleKey, parseSlotRows, parseTwenty } from '../classBasics';
import {
  grantOfKind,
  isBackgroundBasic,
  isIncreaseChoice,
  isSkillGrant,
  isSpeciesBasic,
  isSpeciesSpeed,
  isSpellBasic,
  knownContent,
  pinnedDrafts,
  setSpell,
  spellOf,
  tidySpell,
  type Known,
} from '../entryBasics';

const emptyId = '00000000-0000-0000-0000-000000000000';
const authorable: { kind: ContentKind; label: string }[] = [
  { kind: 'class', label: 'class' },
  { kind: 'subclass', label: 'subclass' },
  { kind: 'species', label: 'species' },
  { kind: 'background', label: 'background' },
  { kind: 'feature', label: 'feature' },
  { kind: 'feat', label: 'feat' },
  { kind: 'spell', label: 'spell' },
  { kind: 'item', label: 'item' },
];

const spellListReasons: Record<string, string> = {
  'content.not-found': 'was not found',
  'spell-list.not-a-spell': 'is not a spell',
  'spell-list.already-on-list': 'is already on this list',
  'spell-list.has-unpublished-draft': 'has an unpublished draft; publish or discard it first',
};

const automationChoices = [
  { value: 'automatic', label: 'Automatic: TomeStack applies it' },
  { value: 'assisted', label: 'Assisted: the player applies it' },
  { value: 'reference', label: 'Reference only: text' },
] as const;

interface Props {
  info: AppInfo;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** Opened from the import review: show this source (if it is a homebrew one) and take focus (WCAG 2.4.3). */
  initialSourceId?: string;
}

/**
 * M2 item 5, SPEC I-04, I-06: guided authoring of classes (M5 slice 1b, ADR-010), subclasses, features, feats and items
 * for the user's own homebrew sources, over content.saveDraft / validate / publish. Nothing here runs code: every
 * control writes a declarative effect (ADR-003), and publishing validates again. No character changes until its own
 * reviewed update.
 */
export function HomebrewStudio({ info, onError, onStatus, initialSourceId }: Props) {
  const [sources, setSources] = useState<SourceRecord[]>([]);
  // An unknown id falls back to the first source below, as an empty id does.
  const [sourceId, setSourceId] = useState(initialSourceId ?? '');
  const heading = useRef<HTMLHeadingElement>(null);
  // From the sidebar the button keeps focus; arriving from another screen (its button is gone) the heading takes it.
  useEffect(() => {
    if (initialSourceId) heading.current?.focus();
  }, [initialSourceId]);
  const [entries, setEntries] = useState<StudioEntry[]>([]);
  const [editing, setEditing] = useState<ContentRevision>();
  /** R64: the open editor holds changes that are not saved; "Publish all drafts" would otherwise discard them. */
  const [editorDirty, setEditorDirty] = useState(false);
  const [published, setPublished] = useState<{ result: PublishResult; name: string }>();
  const [reviewing, setReviewing] = useState<{ affected: AffectedCharacter; to: ContentReference; name: string }>();
  // M5 slice 2: the debugger's findings for one source, and the rule a "Show" button asked the editor to focus.
  const [sourceReport, setSourceReport] = useState<{ sourceId: string; report: DebugReport }>();
  const [focus, setFocus] = useState<RuleFocus>();
  const [diagnosing, setDiagnosing] = useState(false);
  const [templateId, setTemplateId] = useState<TemplateId>(templates[0]!.id);
  // M5 slice 7: design feedback, off by default (a preference on this machine only).
  const [feedbackOn, setFeedbackOn] = useState(designFeedbackOn);

  const loadSources = useCallback(
    () =>
      client
        .listSources()
        .then((all) => setSources(all.filter((s) => s.editionVersion === 'homebrew')))
        .catch(onError),
    [onError],
  );

  useEffect(() => {
    void loadSources();
  }, [loadSources]);

  const source = sources.find((s) => s.id === sourceId) ?? sources[0];

  // Item 9: a content is pending when its newest revision is a draft; "Publish all drafts" previews them.
  const [publishingAll, setPublishingAll] = useState(false);
  const publishAllButton = useRef<HTMLButtonElement>(null);
  const pendingDrafts: PendingDraft[] = entries
    .filter((e) => e.latest.status === 'draft')
    .map((e) => ({
      contentId: e.contentId,
      name: e.name,
      kind: e.kind,
      replacesPublished: e.latestPublished !== undefined,
      draft: { contentId: e.latest.contentId, revisionId: e.latest.revisionId },
      latestPublished: e.latestPublished && { contentId: e.latestPublished.contentId, revisionId: e.latestPublished.revisionId },
    }));

  // Item 10 (R44): spell entries can be ticked and added to a spell list; the drafts that makes open in the publish panel.
  const [ticked, setTicked] = useState<string[]>([]);
  const [listOpen, setListOpen] = useState(false);
  const [restrictTo, setRestrictTo] = useState<string[]>();
  // A new run of the panel remounts it; clearing the restriction after a publish must not (it would drop the result).
  const [panelRun, setPanelRun] = useState(0);
  const panelFrom = useRef<'list' | 'all'>('all');
  const shownSource = useRef<string | undefined>(undefined);
  useEffect(() => {
    shownSource.current = source?.id;
  });
  const [notChanged, setNotChanged] = useState<{ name: string; reasons: string[] }[]>([]);
  const listOpener = useRef<HTMLButtonElement>(null);
  const tickedSpells = entries.filter((e) => e.kind === 'spell' && ticked.includes(e.contentId));
  const panelPending = restrictTo ? pendingDrafts.filter((p) => restrictTo.includes(p.contentId)) : pendingDrafts;

  async function addTickedToList(listKey: string) {
    if (!source || editing || tickedSpells.length === 0) return;
    const ids = tickedSpells.map((e) => e.contentId);
    const names = new Map(tickedSpells.map((e) => [e.contentId, e.name]));
    const sourceId = source.id;
    let result: ContentSpellListResult;
    try {
      result = await client.addToSpellList(ids, listKey);
    } catch (error) {
      onError(error);
      return;
    }
    // The service has committed. The result is shown at once, before the reload, so a slow or failed reload cannot hide it; it is
    // ignored only when another source is on screen now (its rows and ticks are not these).
    if (shownSource.current !== sourceId) return;
    const made = result.drafts.map((d) => d.contentId);
    setNotChanged(result.skipped.map((s) => ({ name: names.get(s.contentId) ?? 'A spell', reasons: s.codes.map((c) => spellListReasons[c] ?? 'could not be changed') })));
    setTicked(result.skipped.map((s) => s.contentId));
    setListOpen(false);
    onStatus(
      made.length === 0
        ? `No spell was added to the spell list ${result.listKey}; ${result.skipped.length} skipped.`
        : `Saved ${made.length} draft${made.length === 1 ? '' : 's'} with the spell list ${result.listKey}${result.skipped.length > 0 ? `; ${result.skipped.length} skipped` : ''}. ${result.note} Review the drafts below, then publish them.`,
    );
    if (made.length > 0) setRestrictTo(made);
    else listOpener.current?.focus();
    try {
      await loadEntries(sourceId);
    } catch (error) {
      onError(error); // the drafts are saved; only the list on screen is out of date
    }
    if (made.length > 0 && shownSource.current === sourceId) {
      panelFrom.current = 'list';
      setPanelRun((n) => n + 1);
      setPublishingAll(true);
    }
  }

  // Only the newest request's answer is shown: a slow reload of source A must not land under source B.
  const entriesRequest = useRef(0);
  const loadEntries = useCallback(
    (id: string) => {
      const request = ++entriesRequest.current;
      return client
        .contentBySource(id)
        .then((rows) => {
          if (request === entriesRequest.current) setEntries(rows);
        })
        .catch(onError);
    },
    [onError],
  );

  useEffect(() => {
    if (source) void loadEntries(source.id);
  }, [source, loadEntries]);

  function newEntry(kind: ContentKind) {
    if (!source) return;
    setPublished(undefined);
    setEditing({
      contentId: crypto.randomUUID(),
      revisionId: emptyId,
      kind,
      name: '',
      rulesFamilies: [...source.rulesFamilies],
      provenance: { sourceId: source.id },
      status: 'draft',
      effects: [],
    });
  }

  function edit(entry: StudioEntry) {
    setPublished(undefined);
    setFocus(undefined);
    // A published revision is never changed: editing starts a new draft of the same content.
    setEditing({ ...entry.latest, revisionId: emptyId, status: 'draft', schemaVersion: undefined });
  }

  async function diagnoseSource(id: string) {
    setDiagnosing(true); // one request at a time: a large library takes a moment (review fix)
    try {
      const report = await client.diagnoseSource(id);
      setSourceReport({ sourceId: id, report });
      onStatus(report.findings.length === 0 ? 'The debugger found no problems.' : `The debugger found ${report.findings.length} thing(s) to look at.`);
    } catch (error) {
      onError(error);
    } finally {
      setDiagnosing(false);
    }
  }

  /** Opens the finding's entry (unless it is already open, which keeps unsaved edits) and focuses its rule. */
  function show(finding: DebugFinding) {
    const entry = entries.find((e) => e.contentId === finding.content.contentId);
    if (!entry) return;
    if (editing?.contentId !== entry.contentId) edit(entry);
    setFocus((f) => ({ effectId: finding.effectId, n: (f?.n ?? 0) + 1 }));
  }

  return (
    <section className="panel" aria-labelledby="studio-heading">
      <h2 id="studio-heading" tabIndex={-1} ref={heading}>
        Homebrew studio
      </h2>
      <p className="hint">
        Write your own classes, subclasses, species, backgrounds, features, feats, spells and items. Everything starts as a draft; publishing checks it and creates a
        new revision. Characters keep their current revision until you review and apply an update.
      </p>
      <label className="choice">
        <input
          type="checkbox"
          checked={feedbackOn}
          onChange={() => {
            setDesignFeedback(!feedbackOn);
            setFeedbackOn((on) => !on);
          }}
          aria-describedby="feedback-hint"
        />
        Show design feedback
      </label>
      <p id="feedback-hint" className="hint">
        Hints that compare your classes and features with the SRD ones. Off by default; they never block publishing, never change a calculation and
        are never exported.
      </p>

      <SourcePicker
        sources={sources}
        selected={source}
        rulesFamilies={info.rulesFamilies.map((f) => ({ id: f.id, label: f.displayName }))}
        onSelect={(id) => {
          // Choosing the source already shown changes nothing (and would not reload, so the rows must stay).
          if (id !== source?.id) {
            setPublishingAll(false);
            setRestrictTo(undefined);
            setTicked([]);
            setListOpen(false);
            setNotChanged([]);
            setEntries([]); // never show another source's rows while this one loads
          }
          setSourceId(id);
        }}
        onCreated={async (created) => {
          await loadSources();
          // The publish panel must not open on, or list, another source's drafts.
          setPublishingAll(false);
          setRestrictTo(undefined);
          setTicked([]);
          setListOpen(false);
          setNotChanged([]);
          setEntries([]);
          setSourceId(created.id);
          onStatus(`Created the homebrew source ${created.title}.`);
        }}
        onError={onError}
      />

      {source && (
        <section aria-labelledby="entries-heading">
          <h3 id="entries-heading">Content in {source.title}</h3>
          {entries.length === 0 ? (
            <p className="hint">Nothing yet.</p>
          ) : (
            <ul className="resources">
              {entries.map((entry) => (
                <li key={entry.contentId} className="resource">
                  {entry.kind === 'spell' && (
                    <input
                      type="checkbox"
                      className="row-select"
                      aria-label={`Select ${entry.name}`}
                      checked={ticked.includes(entry.contentId)}
                      onChange={() => {
                        setTicked((t) => (t.includes(entry.contentId) ? t.filter((x) => x !== entry.contentId) : [...t, entry.contentId]));
                        setListOpen(false);
                      }}
                    />
                  )}
                  <span className="option-name">{entry.name}</span> <span className="tag">{entry.kind}</span>{' '}
                  <span className="hint">
                    {entry.latest.status === 'published' ? 'published' : entry.latestPublished ? 'published, with a newer draft' : 'draft'} ·{' '}
                    {entry.revisions.length} revision{entry.revisions.length === 1 ? '' : 's'}
                  </span>{' '}
                  <button type="button" onClick={() => edit(entry)}>
                    Edit {entry.name}
                  </button>
                </li>
              ))}
            </ul>
          )}
          <div className="actions">
            {authorable.map((a) => (
              <button key={a.kind} type="button" onClick={() => newEntry(a.kind)}>
                New {a.label}
              </button>
            ))}
            <label className="field">
              Start from a template
              <select value={templateId} onChange={(e) => setTemplateId(e.target.value as TemplateId)} aria-describedby="template-hint">
                {templates.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.label}
                  </option>
                ))}
              </select>
            </label>
            <button
              type="button"
              onClick={() => {
                setPublished(undefined);
                setFocus(undefined);
                setEditing(fromTemplate(templateId, source.id, source.rulesFamilies, crypto.randomUUID()));
              }}
            >
              Use template
            </button>
            <p id="template-hint" className="hint">
              {templates.find((t) => t.id === templateId)?.description} It opens as an unsaved draft; nothing is saved until you save it.
            </p>
            {entries.length > 0 && (
              <button type="button" onClick={() => void diagnoseSource(source.id)} disabled={diagnosing}>
                Find problems in {source.title}
              </button>
            )}
            {pendingDrafts.length > 0 && (
              <>
                {/* aria-disabled, never `disabled`: the press is refused in the handler and the reason is visible (2.4.3). */}
                <button
                  type="button"
                  ref={publishAllButton}
                  aria-disabled={editorDirty || undefined}
                  aria-describedby={editorDirty ? 'publish-all-held' : undefined}
                  onClick={() => {
                    if (editorDirty) return;
                    setRestrictTo(undefined);
                    panelFrom.current = 'all';
                    setPanelRun((n) => n + 1);
                    setPublishingAll(true);
                  }}
                >
                  Publish all drafts in this source…
                </button>
                {editorDirty && (
                  <span id="publish-all-held" className="hint">
                    Save or close the open editor first.
                  </span>
                )}
              </>
            )}
            {entries.some((e) => e.kind === 'spell') && (
              <>
                {/* aria-disabled, never `disabled`: the press is refused in the handler and the reason is visible (2.4.3). */}
                <button
                  type="button"
                  ref={listOpener}
                  aria-disabled={Boolean(editing) || tickedSpells.length === 0 || undefined}
                  aria-describedby={editing ? 'spell-list-held' : tickedSpells.length === 0 ? 'spell-list-none' : undefined}
                  onClick={() => !editing && tickedSpells.length > 0 && setListOpen(true)}
                >
                  {tickedSpells.length === 0 ? 'Add to a spell list…' : `Add ${tickedSpells.length} spell${tickedSpells.length === 1 ? '' : 's'} to a spell list…`}
                </button>
                {editing ? (
                  <span id="spell-list-held" className="hint">
                    Close the open editor first.
                  </span>
                ) : tickedSpells.length === 0 ? (
                  <span id="spell-list-none" className="hint">
                    Select at least one spell first.
                  </span>
                ) : null}
              </>
            )}
          </div>
          {listOpen && tickedSpells.length > 0 && (
            <SpellListForm
              subject={`${tickedSpells.length} spell${tickedSpells.length === 1 ? '' : 's'}`}
              held={Boolean(editing)}
              heldReasonId="spell-list-held"
              onSubmit={addTickedToList}
              onCancel={() => {
                setListOpen(false);
                listOpener.current?.focus();
              }}
              onError={onError}
            />
          )}
          {notChanged.length > 0 && (
            <ul className="warnings" aria-label="Not changed">
              {notChanged.map((s, i) => (
                <li key={`${s.name}-${i}`}>
                  {s.name}: {s.reasons.join('; ')}. It stays selected.
                </li>
              ))}
            </ul>
          )}
          {publishingAll && (
            <PublishManyPanel
              key={`${source.id}-${panelRun}`}
              source={source}
              pending={panelPending}
              tickAll={restrictTo !== undefined}
              onError={onError}
              onClose={() => {
                setPublishingAll(false);
                // WCAG 2.4.3: the panel is gone, so focus goes back to the button that opened it (or the heading if that is gone too).
                const back = panelFrom.current === 'list' ? listOpener.current : publishAllButton.current;
                setRestrictTo(undefined);
                setTimeout(() => (back ?? heading.current)?.focus());
              }}
              onReload={() => loadEntries(source.id)}
              onPublished={async (count) => {
                setRestrictTo(undefined); // review fix 10: the restriction ends with the publish (the key does not change, so the result stays)
                await loadEntries(source.id);
                onStatus(`Published ${count} draft${count === 1 ? '' : 's'}. Characters keep their current revisions until you review an update.`);
              }}
            />
          )}
          {sourceReport?.sourceId === source.id && (
            <DebugFindings report={sourceReport.report} label={`Debugger findings for ${source.title}`} showNames onShow={show} />
          )}
        </section>
      )}

      {editing && source && (
        <EntryEditor
          key={`${editing.contentId}-${editing.revisionId}`}
          initial={editing}
          source={source}
          entries={entries}
          info={info}
          focus={focus}
          designFeedback={feedbackOn}
          onError={onError}
          onDirty={setEditorDirty}
          onClose={() => setEditing(undefined)}
          onEntriesChanged={() => void loadEntries(source.id)}
          onPublished={async (result, name) => {
            setEditing(undefined);
            setPublished({ result, name });
            await loadEntries(source.id);
            onStatus(`Published ${name}.`);
          }}
          onSuperseded={(name) => {
            setEditing(undefined);
            onStatus(`Closed the editor of ${name}: it was published with the other drafts. Open it again to change the published version; edits made after the last save were not kept.`);
          }}
          onDraftSaved={async (name, pinned) => {
            await loadEntries(source.id);
            // Item 12: an entry that points at drafts is best published with them.
            const together = `${pinned} unpublished draft${pinned === 1 ? '' : 's'}`;
            onStatus(
              pinned === 0
                ? `Saved a draft of ${name}. Drafts never affect characters.`
                : `Saved a draft of ${name}. It points at ${together}; "Publish all drafts in this source…" publishes ${pinned === 1 ? 'it' : 'them'} together and re-points it.`,
            );
          }}
        />
      )}

      {published && (
        <section aria-labelledby="published-heading" className="play-panel">
          <h3 id="published-heading">Published {published.name}</h3>
          {published.result.affected.filter((a) => a.pinned.revisionId !== published.result.published.revisionId).length === 0 ? (
            <p>No character uses an older revision of it.</p>
          ) : (
            <>
              <p>These characters use an older revision. Nothing changes for them until you review and apply an update:</p>
              <ul>
                {published.result.affected
                  .filter((a) => a.pinned.revisionId !== published.result.published.revisionId)
                  .map((a) => (
                    <li key={`${a.characterId}-${a.pinned.revisionId}`}>
                      {a.name} ({a.role}
                      {a.via ? `, granted by ${a.via}` : ''}){' '}
                      {a.role === 'grant' ? (
                        <span className="hint">Publish a new revision of {a.via} that grants this one, then update that.</span>
                      ) : (
                        <button type="button" onClick={() => setReviewing({ affected: a, to: published.result.published, name: published.name })}>
                          Review update for {a.name}
                        </button>
                      )}
                    </li>
                  ))}
              </ul>
            </>
          )}
        </section>
      )}

      {reviewing && (
        <UpdateReviewPanel
          key={`${reviewing.affected.characterId}-${reviewing.to.revisionId}`}
          characterId={reviewing.affected.characterId}
          characterName={reviewing.affected.name}
          contentName={reviewing.name}
          from={reviewing.affected.pinned}
          to={reviewing.to}
          onError={onError}
          onCancel={() => setReviewing(undefined)}
          onApplied={(view) => {
            setReviewing(undefined);
            setPublished((p) =>
              p && { ...p, result: { ...p.result, affected: p.result.affected.filter((a) => a.characterId !== view.character.id) } },
            );
            onStatus(`Updated ${view.character.name} to the new revision of ${reviewing.name}.`);
          }}
        />
      )}
    </section>
  );
}

function SourcePicker(props: {
  sources: SourceRecord[];
  selected?: SourceRecord;
  rulesFamilies: { id: RulesFamilyId; label: string }[];
  onSelect: (id: string) => void;
  onCreated: (source: SourceRecord) => void;
  onError: (error: unknown) => void;
}) {
  const [title, setTitle] = useState('');
  const [families, setFamilies] = useState<RulesFamilyId[]>([]);

  async function create(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      const created = await client.createHomebrewSource(title, families);
      setTitle('');
      setFamilies([]);
      props.onCreated(created);
    } catch (error) {
      props.onError(error);
    }
  }

  return (
    <section aria-labelledby="sources-heading">
      <h3 id="sources-heading">Homebrew sources</h3>
      {props.sources.length > 0 && (
        <label className="field">
          Homebrew source
          <select value={props.selected?.id ?? ''} onChange={(e) => props.onSelect(e.target.value)}>
            {props.sources.map((s) => (
              <option key={s.id} value={s.id}>
                {s.title} ({s.rulesFamilies.join(', ')})
              </option>
            ))}
          </select>
        </label>
      )}
      <form className="inline-form" onSubmit={create} aria-label="New homebrew source">
        <label className="field">
          Source title
          <input value={title} onChange={(e) => setTitle(e.target.value)} required />
        </label>
        <fieldset>
          <legend>Written for</legend>
          {props.rulesFamilies.map((f) => (
            <label key={f.id} className="choice">
              <input
                type="checkbox"
                checked={families.includes(f.id)}
                onChange={() => setFamilies((s) => (s.includes(f.id) ? s.filter((x) => x !== f.id) : [...s, f.id]))}
              />
              {f.label}
            </label>
          ))}
        </fieldset>
        <button type="submit" disabled={families.length === 0}>
          Create source
        </button>
      </form>
      <p className="hint">Homebrew sources are personal: a shared export leaves them out.</p>
    </section>
  );
}

/** A request to move focus to one rule of the open editor (`n` changes on every request, so a repeat still moves it). */
interface RuleFocus {
  effectId?: string;
  n: number;
}

interface ClassChoice {
  classContentId: string;
  className: string;
  choiceId: string;
  text: string;
}

// A spell always carries its spell data (the form shows a level 1 spell on no list until a field changes), with its text trimmed.
const forServer = (r: ContentRevision): ContentRevision => ({
  ...r,
  name: r.name.trim(),
  summary: r.summary?.trim() || undefined,
  effects: r.kind === 'spell' ? tidySpell(spellOf(r.effects) ? r.effects : setSpell(r.effects, {})) : r.effects,
});

function EntryEditor(props: {
  initial: ContentRevision;
  source: SourceRecord;
  entries: StudioEntry[];
  info: AppInfo;
  focus?: RuleFocus;
  designFeedback: boolean;
  onError: (error: unknown) => void;
  onClose: () => void;
  /** Whether the form holds changes that are not saved (the studio then holds "Publish all drafts"). */
  onDirty: (dirty: boolean) => void;
  /** The class helper published option features: reload the source's content. */
  onEntriesChanged: () => void;
  /** `pinned` counts the unpublished drafts the saved entry points at. */
  onDraftSaved: (name: string, pinned: number) => void;
  /** "Publish all drafts" published the entry this editor saved: the editor shows a superseded draft and is closed. */
  onSuperseded: (name: string) => void;
  onPublished: (result: PublishResult, name: string) => void;
}) {
  const [revision, setRevision] = useState<ContentRevision>(props.initial);
  const [report, setReport] = useState<ValidationReport>();
  // The findings belong to the revision they were computed for: any edit makes a new revision object, which hides them.
  const [debugged, setDebugged] = useState<{ revision: ContentRevision; report: DebugReport }>();
  // M5 slice 5: the relationship tree, for the revision it was built from (any edit hides it).
  const [treed, setTreed] = useState<{ revision: ContentRevision; view: ContentTreeView }>();
  const [treeBusy, setTreeBusy] = useState(false);
  const treeRequest = useRef(0);
  // M5 slice 7: the design hints, for the revision they were computed for.
  const [hinted, setHinted] = useState<{ revision: ContentRevision; hints: DesignHint[] }>();
  const [hintsBusy, setHintsBusy] = useState(false);
  const hintsRequest = useRef(0);
  const [diagnosing, setDiagnosing] = useState(false);
  const [classChoices, setClassChoices] = useState<ClassChoice[]>([]);
  const [busy, setBusy] = useState(false);
  // The revision as last saved (or opened): anything else is an unsaved change.
  const [baseline, setBaseline] = useState(() => JSON.stringify(props.initial));
  // The skill-choice helper publishes its option features; nothing else is saved meanwhile (review fix).
  const [classBusy, setClassBusy] = useState(false);
  // Fields that do not parse (a slot line with a typo, a column without 20 values), by effect id: they block saving.
  const [problems, setProblems] = useState<Record<string, string>>({});
  const problemList = Object.values(problems);
  const dirty = problemList.length > 0 || JSON.stringify(revision) !== baseline;
  const { onDirty } = props;
  useEffect(() => {
    onDirty(dirty);
  }, [dirty, onDirty]);
  useEffect(() => () => onDirty(false), [onDirty]);
  const heading = useRef<HTMLHeadingElement>(null);
  const { onError } = props;
  const family = props.source.rulesFamilies[0];

  // Item 12: what the species, background and spell editors can pick from (this source, what they made, installed feats), and
  // the step a background runs before saving (it makes the ability score option features its choice pins).
  const [made, setMade] = useState<Known[]>([]);
  const [installed, setInstalled] = useState<ContentOption[]>([]);
  const beforeSave = useRef<BeforeSave | null>(null);
  const registerBeforeSave = useCallback((step: BeforeSave | null) => {
    beforeSave.current = step;
  }, []);
  const reportProblem = useCallback(
    (field: string, problem: string | undefined) =>
      setProblems((p) => {
        const rest = Object.fromEntries(Object.entries(p).filter(([key]) => key !== field));
        return problem ? { ...rest, [field]: problem } : rest;
      }),
    [],
  );
  const sourceFamilies = props.source.rulesFamilies;
  useEffect(() => {
    if (props.initial.kind !== 'background') return;
    let current = true;
    Promise.all(sourceFamilies.map((f) => client.listContent(f)))
      .then((all) => {
        if (current) setInstalled(all.flat());
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [props.initial.kind, sourceFamilies, onError]);
  // A made entry is dropped once this source's entries (reloaded after a save or a publish) list it.
  const madeLive = made.filter((m) => !props.entries.some((e) => e.contentId === m.contentId));
  const known = knownContent(props.entries, installed, madeLive);
  const kindOf = (r: ContentReference) => known.find((k) => k.contentId === r.contentId)?.kind;
  const allRevisions = props.entries.flatMap((e) => e.revisions);
  const revisionOf = (r: ContentReference) => allRevisions.find((x) => x.revisionId === r.revisionId);
  const draftIds = new Set([...allRevisions.filter((r) => r.status === 'draft').map((r) => r.revisionId), ...madeLive.filter((m) => m.draft).map((m) => m.reference.revisionId)]);
  const pins = [...new Map(pinnedDrafts(revision.effects, draftIds).map((r) => [r.revisionId, r])).values()];

  /** The newest revision of a content of this source, published or not (insertion order). */
  const newestOf = (contentId: string) => props.entries.find((e) => e.contentId === contentId)?.revisions.at(-1);
  /** R62: the newest published revision of a content, when `r` is an older draft of it; otherwise undefined. */
  const newerPublished = (r: ContentReference): ContentReference | undefined => {
    const entry = props.entries.find((e) => e.contentId === r.contentId);
    const at = entry?.revisions.findIndex((x) => x.revisionId === r.revisionId) ?? -1;
    if (!entry || at < 0 || entry.revisions[at]!.status !== 'draft' || !entry.latestPublished) return undefined;
    const published = entry.latestPublished;
    return entry.revisions.findIndex((x) => x.revisionId === published.revisionId) > at ? { contentId: published.contentId, revisionId: published.revisionId } : undefined;
  };

  // The draft this editor last saved: when "Publish all drafts" later publishes that content, this editor shows a superseded draft.
  const lastSaved = useRef<ContentReference | undefined>(undefined);
  const { onSuperseded } = props;
  useEffect(() => {
    const saved = lastSaved.current;
    const entry = props.entries.find((e) => e.contentId === revision.contentId);
    if (!saved || !entry?.latestPublished) return;
    const at = entry.revisions.findIndex((x) => x.revisionId === saved.revisionId);
    const published = entry.latestPublished;
    if (at >= 0 && entry.revisions.findIndex((x) => x.revisionId === published.revisionId) > at) onSuperseded(revision.name);
  }, [props.entries, revision.contentId, revision.name, onSuperseded]);

  useEffect(() => heading.current?.focus(), []);

  // A debugger finding's "Show" button: focus the rule's fieldset, or the editor's heading when the finding concerns the
  // whole entry or a rule that is no longer there. Runs after the heading focus above, on the first render too.
  function focusRule(effectId: string | undefined) {
    const effect = effectId === undefined ? undefined : revision.effects.find((e) => e.id === effectId);
    const id = effect && (revision.kind === 'class' && isClassBasic(effect, revision.effects) ? classBasicElementId(effect) : (originElementId(revision, effect, kindOf) ?? ruleElementId(effect.id)));
    const target = id === undefined ? undefined : document.getElementById(id);
    (target ?? heading.current)?.focus();
  }
  const onFocusRequest = useEffectEvent((effectId: string | undefined) => focusRule(effectId));
  useEffect(() => {
    if (props.focus) onFocusRequest(props.focus.effectId);
  }, [props.focus]);

  // A subclass can add itself to a class's choice (content schema v4 extendsChoice): list every choice of every class.
  useEffect(() => {
    if (props.initial.kind !== 'subclass' || !family) return;
    let current = true;
    client
      .listContent(family)
      .then(async (options: ContentOption[]) => {
        // One entry per class content: older revisions are listed too (superseded), and would repeat every choice.
        const classes = options.filter((o) => o.kind === 'class' && o.compatible && !o.superseded);
        // In parallel: one request per class, in class order.
        const histories = await Promise.all(classes.map((c) => client.contentRevisions(c.reference.contentId)));
        const found: ClassChoice[] = [];
        classes.forEach((c, i) => {
          const latest = histories[i]!.filter((r) => r.status === 'published').at(-1);
          for (const effect of latest?.effects ?? []) {
            if (effect.type === 'choice') found.push({ classContentId: c.reference.contentId, className: c.name, choiceId: effect.choiceId, text: effect.text || effect.choiceId });
          }
        });
        if (current) setClassChoices(found);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [props.initial.kind, family, onError]);

  // What the formula previews (D61) are worked out against: only the draft's scale columns (the one thing a preview reads
  // from it) with the identity fields the server requires. A preview asks again whenever this object changes, so it is
  // memoised on a key of exactly those inputs: editing a name, a summary or any other rule asks for nothing.
  const [previewId] = useState(() => crypto.randomUUID());
  const previewKey = JSON.stringify({
    kind: revision.kind,
    schemaVersion: revision.schemaVersion,
    scales: revision.effects.filter((e) => e.type === 'scale'),
  });
  const previewRevision = useMemo<ContentRevision>(() => {
    const { kind, schemaVersion, scales } = JSON.parse(previewKey) as { kind: ContentRevision['kind']; schemaVersion?: number; scales: Effect[] };
    return {
      contentId: props.initial.contentId,
      revisionId: previewId,
      schemaVersion,
      kind,
      name: 'Preview',
      rulesFamilies: [],
      provenance: props.initial.provenance,
      status: 'draft',
      effects: scales,
    };
  }, [previewKey, previewId, props.initial.contentId, props.initial.provenance]);

  const update = (change: Partial<ContentRevision>) => setRevision((r) => ({ ...r, ...change }));
  const setEffect = (index: number, effect: Effect) => update({ effects: revision.effects.map((e, i) => (i === index ? effect : e)) });
  const nextId = (type: string) => {
    let n = revision.effects.length + 1;
    while (revision.effects.some((e) => e.id === `${type}-${n}`)) n++;
    return `${type}-${n}`;
  };
  const resources = revision.effects.filter((e): e is Extract<Effect, { type: 'resource' }> => e.type === 'resource');
  const grantable = props.entries.filter((e) => e.contentId !== revision.contentId && e.latestPublished && (e.kind === 'feature' || e.kind === 'feat'));
  const isClass = revision.kind === 'class';
  const classLike = isClass || revision.kind === 'subclass';
  const hasSpellcasting = revision.effects.some((e) => e.type === 'spellcasting');
  // A class's basics (hit die, saves, prerequisites, choices) have their own editor; the rule list shows the rest.
  // Species, background and spell basics have their own editors too (item 12), and each hides exactly the effects it shows.
  const shownByEditor = (effect: Effect) =>
    isClass
      ? isClassBasic(effect, revision.effects)
      : revision.kind === 'species'
        ? isSpeciesBasic(effect, revision.effects)
        : revision.kind === 'background'
          ? isBackgroundBasic(effect, revision.effects, kindOf)
          : revision.kind === 'spell'
            ? isSpellBasic(effect, revision.effects)
            : false;
  const listed = revision.effects.map((effect, index) => ({ effect, index })).filter(({ effect }) => !shownByEditor(effect));

  function add(type: 'modifier' | 'resource' | 'recovery' | 'roll' | 'armor' | 'grant' | 'scale' | 'spellcasting' | 'toggle' | 'toggleGroup') {
    const id = nextId(type);
    const effect: Effect =
      type === 'modifier'
        ? { type, id, operation: 'bonus', target: 'initiative', value: '1' }
        : type === 'resource'
          ? // Set once, never from the label: recoveries and rolls point at it, and spent uses are kept per resource id.
            { type, id, resourceId: id, label: '', maximum: 'PB' }
          : type === 'recovery'
            ? { type, id, resourceId: resources[0]?.resourceId ?? '', on: 'longRest', amount: 'all', timing: 'onLongRest' }
            : type === 'roll'
              ? { type, id, rollId: id, label: '', dice: '1d6', timing: 'onRoll', automation: 'assisted' }
              : type === 'armor'
                ? { type, id, category: 'light', armorClass: 11 }
                : type === 'scale'
                  ? // Content v9 (ADR-010): a class-table column. Its key is what formulas read as SCALE.<key>.
                    { type, id, scaleId: nextScaleKey(revision.effects), label: '', values: Array<number>(20).fill(1) }
                  : type === 'spellcasting'
                    ? { type, id, ability: 'int', preparation: 'prepared', spellList: '', slotKind: 'spellSlots', slots: Array.from({ length: 20 }, () => [] as number[]) }
                    : type === 'toggle'
                      ? // Set once like a resource's id: modifiers, overlays and saved play state name it.
                        { type, id, toggleId: id, label: '' }
                      : type === 'toggleGroup'
                        ? // Content v11 (D43): a key like a scale's (a lowercase letter, then letters and digits), set once; the label is for the sheet.
                          { type, id, groupId: nextGroupKey(revision.effects), label: '' }
                        : { type: 'grant', id, grant: 'content', content: grantable[0]?.latestPublished && reference(grantable[0].latestPublished), level: isClass ? 1 : 3 };
    update({ effects: [...revision.effects, effect] });
  }

  async function check() {
    try {
      setReport(await client.validateRevision({ ...forServer(revision), revisionId: crypto.randomUUID() }));
    } catch (error) {
      onError(error);
    }
  }

  /** M5 slice 2: the debugger on this unsaved revision, against everything installed. */
  async function diagnose() {
    const studied = revision;
    setDiagnosing(true);
    try {
      const report = await client.diagnoseRevision({ ...forServer(studied), revisionId: crypto.randomUUID() });
      setDebugged({ revision: studied, report });
    } catch (error) {
      onError(error);
    } finally {
      setDiagnosing(false);
    }
  }

  async function showTree() {
    const studied = revision;
    const request = ++treeRequest.current;
    setTreeBusy(true);
    try {
      const view = await client.tree({ ...forServer(studied), revisionId: crypto.randomUUID() });
      // An older reply never replaces a newer one (review fix).
      if (request === treeRequest.current) setTreed({ revision: studied, view });
    } catch (error) {
      onError(error);
    } finally {
      if (request === treeRequest.current) setTreeBusy(false);
    }
  }

  async function save(publish: boolean) {
    setBusy(true);
    try {
      // A background first saves the option features its ability score choice pins (item 12, R48), then itself.
      const prepared = beforeSave.current ? await beforeSave.current(revision) : { revision, drafts: [] as ContentReference[] };
      if (prepared.revision !== revision) setRevision(prepared.revision);
      const saved = await client.saveDraft(forServer(prepared.revision));
      lastSaved.current = saved;
      setBaseline(JSON.stringify(prepared.revision));
      if (!publish) {
        // An entry that points at drafts is saved as it is; "Publish all drafts" publishes them together and re-points it (R61).
        const pinned = new Set([...prepared.drafts, ...pinnedDrafts(prepared.revision.effects, draftIds)].map((r) => r.revisionId));
        props.onDraftSaved(revision.name, pinned.size);
        return;
      }
      props.onPublished(await client.publish(saved), revision.name);
    } catch (error) {
      onError(error);
      void check(); // show what is wrong next to the form, too
    } finally {
      setBusy(false);
    }
  }

  const blocked = busy || classBusy || problemList.length > 0;
  const title = `${props.initial.name ? `Edit ${props.initial.name}` : `New ${props.initial.kind}`}`;
  return (
    <section aria-labelledby="editor-heading" className="play-panel">
      <h3 id="editor-heading" tabIndex={-1} ref={heading}>
        {title}
      </h3>
      {/* While the skill-choice helper publishes, the whole editor is disabled, Close included (review fix). */}
      <fieldset disabled={classBusy} className="bare-fieldset">
      <label className="field">
        Name
        <input value={revision.name} onChange={(e) => update({ name: e.target.value })} />
      </label>
      <fieldset>
        <legend>Rules families</legend>
        {props.source.rulesFamilies.map((f) => (
          <label key={f} className="choice">
            <input
              type="checkbox"
              checked={revision.rulesFamilies.includes(f)}
              onChange={() =>
                update({ rulesFamilies: revision.rulesFamilies.includes(f) ? revision.rulesFamilies.filter((x) => x !== f) : [...revision.rulesFamilies, f] })
              }
            />
            {f}
          </label>
        ))}
      </fieldset>
      <label className="field">
        Page (optional)
        <input
          type="number"
          min={1}
          value={revision.provenance.page?.start ?? ''}
          onChange={(e) => update({ provenance: { ...revision.provenance, page: e.target.value ? { start: Number(e.target.value) } : undefined } })}
        />
      </label>
      <label className="field">
        Description (shown on the sheet; text alone is a reference-only feature)
        <textarea rows={3} value={revision.summary ?? ''} onChange={(e) => update({ summary: e.target.value })} />
      </label>

      {revision.kind === 'subclass' && (
        <label className="field">
          Offered in the choice
          <select
            value={revision.extendsChoice ? `${revision.extendsChoice.contentId}|${revision.extendsChoice.choiceId}` : ''}
            onChange={(e) => {
              const [contentId, choiceId] = e.target.value.split('|');
              update({ extendsChoice: contentId && choiceId ? { contentId, choiceId } : undefined });
            }}
          >
            <option value="">Not offered in any choice</option>
            {classChoices.map((c) => (
              <option key={`${c.classContentId}|${c.choiceId}`} value={`${c.classContentId}|${c.choiceId}`}>
                {c.className}: {c.text.length > 60 ? `${c.text.slice(0, 60)}…` : c.text}
              </option>
            ))}
          </select>
        </label>
      )}

      {isClass && (
        <ClassBasicsEditor
          revision={revision}
          source={props.source}
          entries={props.entries}
          info={props.info}
          disabled={classBusy}
          onEffects={(change) => setRevision((r) => ({ ...r, effects: change(r.effects) }))}
          onBusy={setClassBusy}
          onOptionsPublished={props.onEntriesChanged}
          onError={onError}
        />
      )}

      {(revision.kind === 'species' || revision.kind === 'background' || revision.kind === 'spell') &&
        (() => {
          const shared = {
            revision,
            source: props.source,
            info: props.info,
            known,
            onEffects: (change: (effects: Effect[]) => Effect[]) => setRevision((r) => ({ ...r, effects: change(r.effects) })),
            onBusy: setClassBusy,
            onMade: (k: Known) => setMade((m) => [...m, k]),
            onProblem: reportProblem,
            onError,
          };
          return revision.kind === 'species' ? (
            <SpeciesBasicsEditor {...shared} newerPublished={newerPublished} />
          ) : revision.kind === 'background' ? (
            <BackgroundBasicsEditor {...shared} revisionOf={revisionOf} newestOf={newestOf} newerPublished={newerPublished} registerBeforeSave={registerBeforeSave} />
          ) : (
            <SpellBasicsEditor {...shared} />
          );
        })()}

      {listed.map(({ effect, index }, position) => (
        <EffectEditor
          key={effect.id}
          index={position}
          effect={effect}
          fields={props.info.fields}
          resources={resources}
          toggles={revision.effects.filter((e): e is Extract<Effect, { type: 'toggle' }> => e.type === 'toggle')}
          groups={revision.effects.filter((e): e is Extract<Effect, { type: 'toggleGroup' }> => e.type === 'toggleGroup')}
          grantable={grantable}
          previewRevision={previewRevision}
          onChange={(e) => setEffect(index, e)}
          onProblem={reportProblem}
          onRemove={() => {
            // Rules that named the removed one no longer do (review fix): a modifier switched by a removed toggle is always
            // on again, and a toggle that spent a removed resource spends nothing.
            const removedToggle = effect.type === 'toggle' ? effect.toggleId : undefined;
            const removedGroup = effect.type === 'toggleGroup' ? effect.groupId : undefined;
            const removedResource = effect.type === 'resource' ? effect.resourceId : undefined;
            const remaining = revision.effects.filter((_, i) => i !== index);
            const resourceStillDefined = remaining.some((e) => e.type === 'resource' && e.resourceId === removedResource);
            update({
              effects: remaining.map((e) =>
                e.type === 'modifier' && removedToggle !== undefined && e.toggle === removedToggle
                  ? { ...e, toggle: undefined, timing: undefined }
                  : e.type === 'toggle' && removedResource !== undefined && !resourceStillDefined && e.resourceId === removedResource
                    ? { ...e, resourceId: undefined }
                    : // D43: a toggle leaves a removed group (it would name a group nothing declares), and an overlay forgets a removed primary.
                      e.type === 'toggle' && removedGroup !== undefined && e.group === removedGroup && !e.groupContent
                      ? { ...e, group: undefined, layer: undefined, allowedWith: undefined, allowedInBase: undefined }
                      : e.type === 'toggle' && removedToggle !== undefined && e.allowedWith?.some((r) => !r.contentId && r.toggleId === removedToggle)
                        ? { ...e, allowedWith: e.allowedWith.filter((r) => r.contentId || r.toggleId !== removedToggle) }
                        : e,
              ),
            });
            // Field keys start with the effect id: a removed rule's problems no longer block saving.
            setProblems((p) => Object.fromEntries(Object.entries(p).filter(([key]) => !key.startsWith(`${effect.id}-`))));
          }}
        />
      ))}

      <div className="actions" role="group" aria-label="Add a rule">
        <button type="button" onClick={() => add('modifier')}>
          Add modifier
        </button>
        <button type="button" onClick={() => add('resource')}>
          Add resource
        </button>
        <button type="button" onClick={() => add('recovery')} disabled={resources.length === 0}>
          Add recovery
        </button>
        <button type="button" onClick={() => add('roll')}>
          Add roll or action
        </button>
        <button type="button" onClick={() => add('toggle')}>
          Add toggle
        </button>
        <button type="button" onClick={() => add('toggleGroup')}>
          Add form group
        </button>
        {(classLike || revision.kind === 'feat') && (
          <button type="button" onClick={() => add('grant')} disabled={grantable.length === 0}>
            Grant a feature
          </button>
        )}
        {classLike && (
          <button type="button" onClick={() => add('scale')}>
            Add class column
          </button>
        )}
        {classLike && (
          <button type="button" onClick={() => add('spellcasting')} disabled={hasSpellcasting}>
            Add spellcasting
          </button>
        )}
        {revision.kind === 'item' && (
          <button type="button" onClick={() => add('armor')}>
            Add armor
          </button>
        )}
      </div>

      {report && (
        <div role="region" aria-label="Check results">
          {report.errors.length === 0 && report.warnings.length === 0 ? (
            <p>No problems found. It can be published.</p>
          ) : (
            <ul>
              {report.errors.map((d) => (
                <li key={`e-${d.code}-${d.effectId ?? ''}`} className="error">
                  Error: {d.message}
                </li>
              ))}
              {report.warnings.map((d) => (
                <li key={`w-${d.code}-${d.effectId ?? ''}`} className="warn">
                  Warning: {d.message}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {props.designFeedback && (
        <section aria-labelledby="feedback-heading" className="effect-editor">
          <h4 id="feedback-heading">Design feedback</h4>
          <p className="hint">Opinions, not errors: nothing here blocks publishing or changes a character.</p>
          <button
            type="button"
            disabled={hintsBusy}
            onClick={async () => {
              const studied = revision;
              const request = ++hintsRequest.current;
              setHintsBusy(true);
              try {
                const hints = await client.feedback({ ...forServer(studied), revisionId: crypto.randomUUID() });
                // An older reply never replaces a newer one (review fix).
                if (request === hintsRequest.current) setHinted({ revision: studied, hints });
              } catch (error) {
                onError(error);
              } finally {
                if (request === hintsRequest.current) setHintsBusy(false);
              }
            }}
          >
            Get design hints
          </button>
          {hinted?.revision === revision && (
            <div role="region" aria-label="Design hints">
              {hinted.hints.length === 0 ? (
                <p>No hints: nothing stands out against the SRD classes.</p>
              ) : (
                <ul>
                  {hinted.hints.map((h, i) => (
                    <li key={`${h.code}-${h.effectId ?? ''}-${h.family ?? ''}-${i}`}>
                      {h.message}
                      {h.family && <span className="hint"> ({h.family})</span>}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </section>
      )}

      <section aria-labelledby="relations-heading" className="effect-editor">
        <h4 id="relations-heading">Relationships</h4>
        <p className="hint">
          What this {revision.kind} brings in, level by level: features, choices, resources and the rolls and recoveries that use them. Enter on a
          rule of this entry shows it above.
        </p>
        <button type="button" onClick={() => void showTree()} disabled={treeBusy}>
          Show relationships
        </button>
        {treed?.revision === revision && (
          <>
            <TreeView
              root={treed.view.root}
              label={`Relationships of ${revision.name || `this ${revision.kind}`}`}
              onActivate={(node) => {
                // Only a rule this entry holds (review fix: a granted content's node carries the grant, owned by this entry).
                if (node.owner?.contentId === revision.contentId && node.effectId) focusRule(node.effectId);
              }}
            />
            {treed.view.truncated && <p className="warn">This tree is too large to show completely; some branches are cut.</p>}
          </>
        )}
      </section>

      <ComparePanel
        entry={props.entries.find((e) => e.contentId === revision.contentId)}
        unsaved={revision}
        prepare={(r) => ({ ...forServer(r), revisionId: crypto.randomUUID() })}
        onError={onError}
      />

      {classLike && <SandboxPanel revision={revision} prepare={(r) => ({ ...forServer(r), revisionId: crypto.randomUUID() })} onError={onError} />}

      {debugged?.revision === revision && (
        <DebugFindings report={debugged.report} label="Debugger findings" showNames={false} onShow={(f) => focusRule(f.effectId)} />
      )}

      <div className="actions">
        <button type="button" onClick={check}>
          Check
        </button>
        <button type="button" onClick={diagnose} disabled={diagnosing}>
          Find problems
        </button>
        {/* aria-disabled with a guarded click, never native `disabled`: focus stays and the reason is linked (WCAG 2.4.3, 4.1.2). */}
        <button type="button" onClick={() => !blocked && void save(false)} aria-disabled={blocked || undefined} aria-describedby="editor-blocked">
          Save draft
        </button>
        <button
          type="button"
          onClick={() => !blocked && void save(true)}
          aria-disabled={blocked || undefined}
          aria-describedby={pins.length > 0 ? 'editor-blocked editor-pins' : 'editor-blocked'}
        >
          Publish
        </button>
        <button type="button" onClick={props.onClose}>
          Close editor
        </button>
      </div>
      </fieldset>
      <p id="editor-pins" className="hint">
        {pins.length > 0
          ? `Points at ${pins.length} unpublished draft${pins.length === 1 ? '' : 's'}. Publish all drafts publishes ${pins.length === 1 ? 'it' : 'them'} together and re-points this entry.`
          : ''}
      </p>
      <p id="editor-blocked" className="hint" aria-live="polite">
        {classBusy
          ? 'Publishing the skill options…'
          : busy
            ? 'Saving…'
            : problemList.length > 0
            ? `Fix ${problemList.length === 1 ? 'the marked field' : `the ${problemList.length} marked fields`} before saving: ${problemList.join(' ')}`
            : ''}
      </p>
    </section>
  );
}

const reference = (r: ContentRevision): ContentReference => ({ contentId: r.contentId, revisionId: r.revisionId });

/** The id of a rule's fieldset in the editor, which a debugger finding focuses. */
const ruleElementId = (effectId: string) => `rule-${effectId}`;

/** The group of a species, background or spell editor that holds this effect, for the same focus; undefined for any other effect. */
function originElementId(revision: ContentRevision, effect: Effect, kindOf: (r: ContentReference) => ContentKind | undefined): string | undefined {
  const effects = revision.effects;
  if (revision.kind === 'species' && isSpeciesBasic(effect, effects)) {
    return effect.type === 'modifier' ? (isSpeciesSpeed(effect, effects) ? 'species-basics-speed' : 'species-basics-increases') : isSkillGrant(effect, effects) ? 'species-basics-skills' : 'species-basics-traits';
  }
  if (revision.kind === 'background' && isBackgroundBasic(effect, effects, kindOf)) {
    if (isSkillGrant(effect, effects)) return 'background-basics-skills';
    if (isIncreaseChoice(effect)) return 'background-basics-increases';
    return (grantOfKind(effects, kindOf, 'feat') as Effect | undefined) === effect ? 'background-basics-feat' : 'background-basics-feature';
  }
  if (revision.kind === 'spell' && isSpellBasic(effect, effects)) return 'spell-basics-core';
  return undefined;
}

/** Reports a field that does not parse, by name within its effect; `undefined` clears it. */
type ReportProblem = (field: string, problem: string | undefined) => void;

/**
 * Exactly 20 whole numbers, such as a class column's values. It keeps its own text while typing (so "2," is not
 * rewritten to "2"). Nothing is dropped: a typo or a wrong count is shown next to the field, linked to it, and blocks
 * saving until fixed (review fix). Only a valid list is handed up.
 */
function TwentyNumbersField(props: { id: string; label: string; max: number; values: number[]; onChange: (values: number[]) => void; onProblem: ReportProblem }) {
  const [text, setText] = useState(props.values.join(', '));
  const parsed = parseTwenty(text, props.max);
  const errorId = `${props.id}-error`;
  return (
    <div className="field">
      <label>
        {props.label}
        <input
          value={text}
          aria-invalid={parsed.problem ? true : undefined}
          aria-describedby={parsed.problem ? errorId : undefined}
          onChange={(e) => {
            setText(e.target.value);
            const next = parseTwenty(e.target.value, props.max);
            props.onProblem(props.id, next.problem && `${props.label}: ${next.problem}.`);
            if (!next.problem) props.onChange(next.values);
          }}
        />
      </label>
      {parsed.problem && (
        <p id={errorId} className="error">
          {parsed.problem}
        </p>
      )}
    </div>
  );
}

/** Content v9 (ADR-010): a class-table column. Formulas of the class read it as SCALE.<key> at the class level. */
function ScaleFields(props: { effect: Extract<Effect, { type: 'scale' }>; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
  const { effect, onChange } = props;
  return (
    <>
      <label className="field">
        Column name
        <input value={effect.label} onChange={(e) => onChange({ ...effect, label: e.target.value })} />
      </label>
      <label className="field">
        Key (formulas read it as SCALE.key; a lowercase letter, then letters or digits)
        <input value={effect.scaleId} onChange={(e) => onChange({ ...effect, scaleId: e.target.value.trim() })} />
      </label>
      <TwentyNumbersField
        id={`${effect.id}-values`}
        label="Values at class levels 1 to 20, separated by commas"
        max={10000}
        values={effect.values}
        onChange={(values) => onChange({ ...effect, values })}
        onProblem={props.onProblem}
      />
    </>
  );
}

/**
 * Content v5 spellcasting, with the v9 multiclass table (ADR-010). The slot table is one line per class level, each the
 * slots of spell levels 1, 2, … separated by commas (an empty line: no slots at that level).
 */
function SpellcastingFields(props: {
  effect: Extract<Effect, { type: 'spellcasting' }>;
  previewRevision?: ContentRevision;
  onChange: (effect: Effect) => void;
  onProblem: ReportProblem;
}) {
  const { effect, onChange } = props;
  // A new caster's 20 empty rows start as an empty box, not 19 blank lines that typed rows would follow.
  const [slotsText, setSlotsText] = useState(effect.slots.every((row) => row.length === 0) ? '' : effect.slots.map((row) => row.join(', ')).join('\n'));
  const slotsProblem = parseSlotRows(slotsText).problem;
  const slotsErrorId = `${effect.id}-slots-error`;
  const share = effect.multiclassCasterTable ? 'table' : (effect.multiclassCaster ?? 'none');
  return (
    <>
      <label className="field">
        Spellcasting ability
        <select value={effect.ability} onChange={(e) => onChange({ ...effect, ability: e.target.value as typeof effect.ability })}>
          {abilities.map(([key, label]) => (
            <option key={key} value={key}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        Spell list key (spells name the lists they are on)
        <input value={effect.spellList} onChange={(e) => onChange({ ...effect, spellList: e.target.value.trim() })} />
      </label>
      <label className="field">
        Spells
        <select value={effect.preparation ?? 'prepared'} onChange={(e) => onChange({ ...effect, preparation: e.target.value as 'prepared' | 'known' })}>
          <option value="prepared">Prepared (the list can change)</option>
          <option value="known">Known</option>
        </select>
      </label>
      <label className="field">
        Prepared or known spells (optional formula, such as max(1, INT.MOD + CLASS_LEVEL))
        <input
          value={effect.spellsFormula ?? ''}
          aria-describedby={`rule-${effect.id}-spells-preview`}
          onChange={(e) => onChange({ ...effect, spellsFormula: e.target.value || undefined })}
        />
      </label>
      <FormulaPreview id={`rule-${effect.id}-spells-preview`} formula={effect.spellsFormula ?? ''} revision={props.previewRevision} floorAtZero />
      <div className="field">
        <label>
          Spell slots: one line per class level 1 to 20, the slots of spell levels 1, 2, … separated by commas
          <textarea
            rows={6}
            value={slotsText}
            aria-invalid={slotsProblem ? true : undefined}
            aria-describedby={slotsProblem ? slotsErrorId : undefined}
            onChange={(e) => {
              setSlotsText(e.target.value);
              // Positions matter: a typo is shown and blocks saving; it never moves a slot to another spell level.
              const parsed = parseSlotRows(e.target.value);
              props.onProblem(`${effect.id}-slots`, parsed.problem && `Spell slots: ${parsed.problem}.`);
              if (!parsed.problem) onChange({ ...effect, slots: parsed.rows });
            }}
          />
        </label>
        {slotsProblem && (
          <p id={slotsErrorId} className="error">
            {slotsProblem}
          </p>
        )}
      </div>
      <label className="field">
        With other casters (the Multiclass Spellcaster table)
        <select
          value={share}
          onChange={(e) => {
            const value = e.target.value;
            if (value !== 'table') props.onProblem(`${effect.id}-table`, undefined); // the table field goes away
            onChange({
              ...effect,
              multiclassCaster: value === 'full' || value === 'half' || value === 'third' ? value : undefined,
              multiclassCasterTable: value === 'table' ? (effect.multiclassCasterTable ?? Array<number>(20).fill(0)) : undefined,
            });
          }}
        >
          <option value="none">Not combined (a manual step)</option>
          <option value="full">All its levels count</option>
          <option value="half">Half its levels count</option>
          <option value="third">A third of its levels count</option>
          <option value="table">Its own table of caster levels</option>
        </select>
      </label>
      {effect.multiclassCasterTable && (
        <TwentyNumbersField
          id={`${effect.id}-table`}
          label="Caster levels it adds at class levels 1 to 20, separated by commas"
          max={20}
          values={effect.multiclassCasterTable}
          onChange={(values) => onChange({ ...effect, multiclassCasterTable: values })}
          onProblem={props.onProblem}
        />
      )}
    </>
  );
}

function EffectEditor(props: {
  index: number;
  effect: Effect;
  fields: { id: string; label: string }[];
  resources: Extract<Effect, { type: 'resource' }>[];
  toggles: Extract<Effect, { type: 'toggle' }>[];
  /** Content v11 (D43): the form groups this revision declares. */
  groups: Extract<Effect, { type: 'toggleGroup' }>[];
  grantable: StudioEntry[];
  /** The draft the formula previews are worked out against (D61). */
  previewRevision?: ContentRevision;
  onChange: (effect: Effect) => void;
  onProblem: ReportProblem;
  onRemove: () => void;
}) {
  const { effect, onChange, onProblem } = props;
  const n = props.index + 1;
  const names: Record<Effect['type'], string> = {
    modifier: 'Modifier',
    grant: 'Granted feature',
    resource: 'Resource',
    recovery: 'Recovery',
    roll: 'Roll or action',
    armor: 'Armor',
    choice: 'Choice',
    hitDie: 'Hit die',
    restriction: 'Prerequisite',
    scale: 'Class column',
    spellcasting: 'Spellcasting',
    spell: 'Spell data',
    toggle: 'Toggle',
    toggleGroup: 'Form group',
    item: 'Item rules',
  };
  const toggles = props.toggles;
  const legend = `Rule ${n}: ${names[effect.type]}`;

  return (
    <fieldset className="effect-editor" id={ruleElementId(effect.id)} tabIndex={-1}>
      <legend>{legend}</legend>
      {effect.type === 'modifier' && (
        <>
          <label className="field">
            Changes
            <select value={effect.target} onChange={(e) => onChange({ ...effect, target: e.target.value })}>
              {props.fields.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.label}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            How
            <select value={effect.operation} onChange={(e) => onChange({ ...effect, operation: e.target.value as typeof effect.operation })}>
              <option value="bonus">Add (bonus)</option>
              <option value="replace">Replace the base</option>
              <option value="set">Set to</option>
            </select>
          </label>
          <label className="field">
            Value (a number or a formula such as PB + CON.MOD)
            <input value={effect.value} aria-describedby={`rule-${effect.id}-value-preview`} onChange={(e) => onChange({ ...effect, value: e.target.value })} />
          </label>
          <FormulaPreview id={`rule-${effect.id}-value-preview`} formula={effect.value} revision={props.previewRevision} />
          {/* Only for a modifier that is always on or switched by a toggle: another timing (such as onRoll, from imported
              content) is left as it is, never turned into "always" (review fix). */}
          {(effect.toggle || ((effect.timing ?? 'always') === 'always' && toggles.length > 0)) && (
            <label className="field">
              Applies
              <select
                value={effect.toggle ?? ''}
                onChange={(e) =>
                  // Content v6: a toggled modifier applies only while its toggle is on, so its timing is whileActive; without
                  // the toggle it is always on again (its timing was whileActive only because of the toggle).
                  onChange(e.target.value ? { ...effect, toggle: e.target.value, timing: 'whileActive' } : { ...effect, toggle: undefined, timing: undefined })
                }
              >
                <option value="">Always</option>
                {toggles.map((t) => (
                  <option key={t.id} value={t.toggleId}>
                    While {t.label || t.toggleId} is on
                  </option>
                ))}
              </select>
            </label>
          )}
        </>
      )}
      {effect.type === 'toggle' && (
        <>
          <label className="field">
            Toggle name
            <input value={effect.label} onChange={(e) => onChange({ ...effect, label: e.target.value })} />
          </label>
          <label className="field">
            Turning it on spends
            <select value={effect.resourceId ?? ''} onChange={(e) => onChange({ ...effect, resourceId: e.target.value || undefined })}>
              <option value="">Nothing</option>
              {props.resources.map((r) => (
                <option key={r.id} value={r.resourceId}>
                  One use of {r.label || r.resourceId}
                </option>
              ))}
            </select>
          </label>
          {/* Content v11 (D43). The version is not chosen here: the check and publish pick it from what the rules use. */}
          {effect.resourceId && (
            <>
              <label className="field">
                Cost (a number or a formula; empty is one use)
                <input value={effect.cost ?? ''} aria-describedby={`rule-${effect.id}-cost-preview`} onChange={(e) => onChange({ ...effect, cost: e.target.value || undefined })} />
              </label>
              <FormulaPreview id={`rule-${effect.id}-cost-preview`} formula={effect.cost ?? ''} revision={props.previewRevision} floorAtZero />
            </>
          )}
          <label className="field">
            Form group
            <select
              value={effect.group ?? ''}
              onChange={(e) =>
                // Leaving the group drops what only means something inside one; a group of this revision is named without a content.
                onChange(
                  e.target.value
                    ? { ...effect, group: e.target.value, groupContent: undefined }
                    : { ...effect, group: undefined, groupContent: undefined, layer: undefined, allowedWith: undefined, allowedInBase: undefined },
                )
              }
            >
              <option value="">Not in a group</option>
              {props.groups.map((g) => (
                <option key={g.id} value={g.groupId}>
                  {g.label || g.groupId}
                </option>
              ))}
              {/* A group declared by other content is kept as it is, not silently moved. */}
              {effect.group && (effect.groupContent || !props.groups.some((g) => g.groupId === effect.group)) && <option value={effect.group}>{effect.group} (other content)</option>}
            </select>
          </label>
          {effect.group && (
            <>
              <label className="field">
                Kind
                <select
                  value={effect.layer ?? 'primary'}
                  onChange={(e) =>
                    onChange(e.target.value === 'overlay' ? { ...effect, layer: 'overlay' } : { ...effect, layer: undefined, allowedWith: undefined, allowedInBase: undefined })
                  }
                >
                  <option value="primary">Form (one at a time)</option>
                  <option value="overlay">Overlay (on top of a form)</option>
                </select>
              </label>
              {effect.layer === 'overlay' && (
                <>
                  <label className="choice">
                    <input type="checkbox" checked={effect.allowedInBase ?? false} onChange={(e) => onChange({ ...effect, allowedInBase: e.target.checked || undefined })} />
                    Can be on with no form chosen (Base)
                  </label>
                  <fieldset>
                    <legend>Can be on with these forms</legend>
                    {toggles
                      .filter((t) => t.id !== effect.id && t.group === effect.group && t.layer !== 'overlay')
                      .map((t) => {
                        const on = (effect.allowedWith ?? []).some((r) => !r.contentId && r.toggleId === t.toggleId);
                        return (
                          <label key={t.id} className="choice">
                            <input
                              type="checkbox"
                              checked={on}
                              onChange={(e) => {
                                const others = (effect.allowedWith ?? []).filter((r) => r.contentId || r.toggleId !== t.toggleId);
                                const next = e.target.checked ? [...others, { toggleId: t.toggleId }] : others;
                                onChange({ ...effect, allowedWith: next.length > 0 ? next : undefined });
                              }}
                            />
                            {t.label || t.toggleId}
                          </label>
                        );
                      })}
                  </fieldset>
                </>
              )}
            </>
          )}
          <label className="field">
            Available from character level
            <input
              type="number"
              min={1}
              max={20}
              value={effect.level ?? ''}
              onChange={(e) => onChange({ ...effect, level: e.target.value ? Number(e.target.value) : undefined })}
            />
          </label>
        </>
      )}
      {effect.type === 'toggleGroup' && (
        <>
          <label className="field">
            Group name (shown on the sheet)
            <input value={effect.label} onChange={(e) => onChange({ ...effect, label: e.target.value })} />
          </label>
          <p className="hint">Key: {effect.groupId}. Toggles join this group from their own Form group choice.</p>
        </>
      )}
      {effect.type === 'resource' && (
        <>
          <label className="field">
            Resource name
            <input value={effect.label} onChange={(e) => onChange({ ...effect, label: e.target.value })} />
          </label>
          <label className="field">
            Uses (a number or a formula such as PB, CLASS_LEVEL or SCALE.column1)
            <input value={effect.maximum} aria-describedby={`rule-${effect.id}-maximum-preview`} onChange={(e) => onChange({ ...effect, maximum: e.target.value })} />
          </label>
          <FormulaPreview id={`rule-${effect.id}-maximum-preview`} formula={effect.maximum} revision={props.previewRevision} floorAtZero />
        </>
      )}
      {effect.type === 'recovery' && (
        <>
          <label className="field">
            Recovers
            <select value={effect.resourceId} onChange={(e) => onChange({ ...effect, resourceId: e.target.value })}>
              {props.resources.map((r) => (
                <option key={r.id} value={r.resourceId}>
                  {r.label || r.resourceId}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            When
            <select
              value={effect.on}
              onChange={(e) => {
                const on = e.target.value as typeof effect.on;
                onChange({ ...effect, on, timing: on === 'longRest' ? 'onLongRest' : 'onShortRest' });
              }}
            >
              <option value="longRest">Long rest</option>
              <option value="shortRest">Short rest</option>
            </select>
          </label>
          <label className="field">
            How many (all, a number or a formula)
            <input value={effect.amount} aria-describedby={`rule-${effect.id}-amount-preview`} onChange={(e) => onChange({ ...effect, amount: e.target.value })} />
          </label>
          <FormulaPreview id={`rule-${effect.id}-amount-preview`} formula={effect.amount} revision={props.previewRevision} floorAtZero />
        </>
      )}
      {effect.type === 'roll' && (
        <>
          <label className="field">
            Roll name
            <input value={effect.label} onChange={(e) => onChange({ ...effect, label: e.target.value })} />
          </label>
          <label className="field">
            Dice (such as 1d8 + 2)
            <input value={effect.dice} onChange={(e) => onChange({ ...effect, dice: e.target.value })} />
          </label>
          <label className="field">
            Uses a resource (a limited-use action)
            <select value={effect.resourceId ?? ''} onChange={(e) => onChange({ ...effect, resourceId: e.target.value || undefined })}>
              <option value="">No resource</option>
              {props.resources.map((r) => (
                <option key={r.id} value={r.resourceId}>
                  {r.label || r.resourceId}
                </option>
              ))}
            </select>
          </label>
        </>
      )}
      {effect.type === 'grant' && effect.grant === 'content' && (
        <>
          <label className="field">
            Feature
            <select
              value={effect.content?.revisionId ?? ''}
              onChange={(e) => {
                const entry = props.grantable.find((g) => g.latestPublished?.revisionId === e.target.value);
                onChange({ ...effect, content: entry?.latestPublished && reference(entry.latestPublished) });
              }}
            >
              {/* An empty slot (a template's, or before any feature is published) shows as empty, so picking the first
                  feature is a change too. */}
              {!effect.content && <option value="">Choose a published feature</option>}
              {props.grantable.map((g) => (
                <option key={g.contentId} value={g.latestPublished!.revisionId}>
                  {g.name} (published)
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            From class level
            <input
              type="number"
              min={1}
              max={20}
              value={effect.level ?? ''}
              onChange={(e) => onChange({ ...effect, level: e.target.value ? Number(e.target.value) : undefined })}
            />
          </label>
        </>
      )}
      {effect.type === 'armor' && (
        <>
          <label className="field">
            Armor type
            <select value={effect.category} onChange={(e) => onChange({ ...effect, category: e.target.value as typeof effect.category })}>
              <option value="light">Light (AC + Dex)</option>
              <option value="medium">Medium (AC + Dex, capped)</option>
              <option value="heavy">Heavy (AC only)</option>
              <option value="shield">Shield (+AC)</option>
            </select>
          </label>
          <label className="field">
            Armor Class
            <input type="number" min={0} max={30} value={effect.armorClass} onChange={(e) => onChange({ ...effect, armorClass: Number(e.target.value) })} />
          </label>
        </>
      )}
      {effect.type === 'scale' && <ScaleFields effect={effect} onChange={onChange} onProblem={onProblem} />}
      {effect.type === 'spellcasting' && <SpellcastingFields effect={effect} previewRevision={props.previewRevision} onChange={onChange} onProblem={onProblem} />}
      {effect.type !== 'grant' && (
        <label className="field">
          Automation
          <select
            value={effect.automation ?? 'automatic'}
            onChange={(e) => onChange({ ...effect, automation: e.target.value as (typeof automationChoices)[number]['value'] })}
          >
            {automationChoices.map((a) => (
              <option key={a.value} value={a.value}>
                {a.label}
              </option>
            ))}
          </select>
        </label>
      )}
      <label className="field">
        Rule text (shown on the sheet)
        <textarea rows={2} value={effect.text ?? ''} onChange={(e) => onChange({ ...effect, text: e.target.value || undefined })} />
      </label>
      <button type="button" onClick={props.onRemove}>
        Remove rule {n}
      </button>
    </fieldset>
  );
}
