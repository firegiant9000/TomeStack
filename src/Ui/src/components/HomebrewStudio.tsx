import { useCallback, useEffect, useEffectEvent, useRef, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type {
  AffectedCharacter,
  AppInfo,
  ContentKind,
  ContentOption,
  ContentReference,
  ContentRevision,
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
import { UpdateReviewPanel } from './UpdateReviewPanel';
import { ClassBasicsEditor, classBasicElementId, isClassBasic } from './ClassBasicsEditor';
import { DebugFindings } from './DebugFindings';
import { SandboxPanel } from './SandboxPanel';
import { ComparePanel } from './ComparePanel';
import { TreeView } from './TreeView';
import { fromTemplate, templates, type TemplateId } from '../templates';
import { designFeedbackOn, setDesignFeedback } from '../designFeedback';
import { abilities, nextScaleKey, parseSlotRows, parseTwenty } from '../classBasics';

const emptyId = '00000000-0000-0000-0000-000000000000';
const authorable: { kind: ContentKind; label: string }[] = [
  { kind: 'class', label: 'class' },
  { kind: 'subclass', label: 'subclass' },
  { kind: 'feature', label: 'feature' },
  { kind: 'feat', label: 'feat' },
  { kind: 'item', label: 'item' },
];

const automationChoices = [
  { value: 'automatic', label: 'Automatic: TomeStack applies it' },
  { value: 'assisted', label: 'Assisted: the player applies it' },
  { value: 'reference', label: 'Reference only: text' },
] as const;

interface Props {
  info: AppInfo;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

/**
 * M2 item 5, SPEC I-04, I-06: guided authoring of classes (M5 slice 1b, ADR-010), subclasses, features, feats and items
 * for the user's own homebrew sources, over content.saveDraft / validate / publish. Nothing here runs code: every
 * control writes a declarative effect (ADR-003), and publishing validates again. No character changes until its own
 * reviewed update.
 */
export function HomebrewStudio({ info, onError, onStatus }: Props) {
  const [sources, setSources] = useState<SourceRecord[]>([]);
  const [sourceId, setSourceId] = useState('');
  const [entries, setEntries] = useState<StudioEntry[]>([]);
  const [editing, setEditing] = useState<ContentRevision>();
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

  const loadEntries = useCallback(
    (id: string) => client.contentBySource(id).then(setEntries).catch(onError),
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
      <h2 id="studio-heading">Homebrew studio</h2>
      <p className="hint">
        Write your own classes, subclasses, features, feats and items. Everything starts as a draft; publishing checks it and creates a
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
        onSelect={setSourceId}
        onCreated={async (created) => {
          await loadSources();
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
          </div>
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
          onClose={() => setEditing(undefined)}
          onPublished={async (result, name) => {
            setEditing(undefined);
            setPublished({ result, name });
            await loadEntries(source.id);
            onStatus(`Published ${name}.`);
          }}
          onDraftSaved={async (name) => {
            await loadEntries(source.id);
            onStatus(`Saved a draft of ${name}. Drafts never affect characters.`);
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

function EntryEditor(props: {
  initial: ContentRevision;
  source: SourceRecord;
  entries: StudioEntry[];
  info: AppInfo;
  focus?: RuleFocus;
  designFeedback: boolean;
  onError: (error: unknown) => void;
  onClose: () => void;
  onDraftSaved: (name: string) => void;
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
  // The skill-choice helper publishes its option features; nothing else is saved meanwhile (review fix).
  const [classBusy, setClassBusy] = useState(false);
  // Fields that do not parse (a slot line with a typo, a column without 20 values), by effect id: they block saving.
  const [problems, setProblems] = useState<Record<string, string>>({});
  const problemList = Object.values(problems);
  const heading = useRef<HTMLHeadingElement>(null);
  const { onError } = props;
  const family = props.source.rulesFamilies[0];

  useEffect(() => heading.current?.focus(), []);

  // A debugger finding's "Show" button: focus the rule's fieldset, or the editor's heading when the finding concerns the
  // whole entry or a rule that is no longer there. Runs after the heading focus above, on the first render too.
  function focusRule(effectId: string | undefined) {
    const effect = effectId === undefined ? undefined : revision.effects.find((e) => e.id === effectId);
    const target = effect && document.getElementById(revision.kind === 'class' && isClassBasic(effect) ? classBasicElementId(effect) : ruleElementId(effect.id));
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
            if (effect.type === 'choice') found.push({ classContentId: c.reference.contentId, className: c.name, choiceId: effect.choiceId, text: effect.text ?? effect.choiceId });
          }
        });
        if (current) setClassChoices(found);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [props.initial.kind, family, onError]);

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
  const listed = revision.effects.map((effect, index) => ({ effect, index })).filter(({ effect }) => !(isClass && isClassBasic(effect)));

  function add(type: 'modifier' | 'resource' | 'recovery' | 'roll' | 'armor' | 'grant' | 'scale' | 'spellcasting') {
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
                    : { type: 'grant', id, grant: 'content', content: grantable[0]?.latestPublished && reference(grantable[0].latestPublished), level: isClass ? 1 : 3 };
    update({ effects: [...revision.effects, effect] });
  }

  const forServer = (r: ContentRevision): ContentRevision => ({ ...r, name: r.name.trim(), summary: r.summary?.trim() || undefined });

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
      const saved = await client.saveDraft(forServer(revision));
      if (!publish) {
        props.onDraftSaved(revision.name);
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

  const title = `${props.initial.name ? `Edit ${props.initial.name}` : `New ${props.initial.kind}`}`;
  return (
    <section aria-labelledby="editor-heading" className="play-panel">
      <h3 id="editor-heading" tabIndex={-1} ref={heading}>
        {title}
      </h3>
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
          onError={onError}
        />
      )}

      {listed.map(({ effect, index }, position) => (
        <EffectEditor
          key={effect.id}
          index={position}
          effect={effect}
          fields={props.info.fields}
          resources={resources}
          toggles={revision.effects.filter((e): e is Extract<Effect, { type: 'toggle' }> => e.type === 'toggle')}
          grantable={grantable}
          onChange={(e) => setEffect(index, e)}
          onProblem={(field, problem) =>
            setProblems((p) => {
              const rest = Object.fromEntries(Object.entries(p).filter(([key]) => key !== field));
              return problem ? { ...rest, [field]: problem } : rest;
            })
          }
          onRemove={() => {
            // Rules that named the removed one no longer do (review fix): a modifier switched by a removed toggle is always
            // on again, and a toggle that spent a removed resource spends nothing.
            const removedToggle = effect.type === 'toggle' ? effect.toggleId : undefined;
            const removedResource = effect.type === 'resource' ? effect.resourceId : undefined;
            const remaining = revision.effects.filter((_, i) => i !== index);
            const resourceStillDefined = remaining.some((e) => e.type === 'resource' && e.resourceId === removedResource);
            update({
              effects: remaining.map((e) =>
                e.type === 'modifier' && removedToggle !== undefined && e.toggle === removedToggle
                  ? { ...e, toggle: undefined, timing: undefined }
                  : e.type === 'toggle' && removedResource !== undefined && !resourceStillDefined && e.resourceId === removedResource
                    ? { ...e, resourceId: undefined }
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
        <button type="button" onClick={() => save(false)} disabled={busy || classBusy || problemList.length > 0} aria-describedby="editor-blocked">
          Save draft
        </button>
        <button type="button" onClick={() => save(true)} disabled={busy || classBusy || problemList.length > 0} aria-describedby="editor-blocked">
          Publish
        </button>
        <button type="button" onClick={props.onClose}>
          Close editor
        </button>
      </div>
      <p id="editor-blocked" className="hint">
        {classBusy
          ? 'Publishing the skill options…'
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
function SpellcastingFields(props: { effect: Extract<Effect, { type: 'spellcasting' }>; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
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
        <input value={effect.spellsFormula ?? ''} onChange={(e) => onChange({ ...effect, spellsFormula: e.target.value || undefined })} />
      </label>
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
  grantable: StudioEntry[];
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
    toggle: 'Toggle',
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
            <input value={effect.value} onChange={(e) => onChange({ ...effect, value: e.target.value })} />
          </label>
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
            <input value={effect.maximum} onChange={(e) => onChange({ ...effect, maximum: e.target.value })} />
          </label>
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
            <input value={effect.amount} onChange={(e) => onChange({ ...effect, amount: e.target.value })} />
          </label>
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
      {effect.type === 'spellcasting' && <SpellcastingFields effect={effect} onChange={onChange} onProblem={onProblem} />}
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
