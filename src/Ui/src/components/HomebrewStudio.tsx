import { useCallback, useEffect, useRef, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type {
  AffectedCharacter,
  AppInfo,
  ContentKind,
  ContentOption,
  ContentReference,
  ContentRevision,
  Effect,
  PublishResult,
  RulesFamilyId,
  SourceRecord,
  StudioEntry,
  ValidationReport,
} from '../api/types';
import { UpdateReviewPanel } from './UpdateReviewPanel';

const emptyId = '00000000-0000-0000-0000-000000000000';
const authorable: { kind: ContentKind; label: string }[] = [
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

const slug = (text: string) =>
  text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '') || 'resource';

interface Props {
  info: AppInfo;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

/**
 * M2 item 5, SPEC I-04, I-06: guided authoring of subclasses, features, feats and items for the user's own homebrew
 * sources, over content.saveDraft / validate / publish. Nothing here runs code: every control writes a declarative
 * effect (ADR-003), and publishing validates again. No character changes until its own reviewed update.
 */
export function HomebrewStudio({ info, onError, onStatus }: Props) {
  const [sources, setSources] = useState<SourceRecord[]>([]);
  const [sourceId, setSourceId] = useState('');
  const [entries, setEntries] = useState<StudioEntry[]>([]);
  const [editing, setEditing] = useState<ContentRevision>();
  const [published, setPublished] = useState<{ result: PublishResult; name: string }>();
  const [reviewing, setReviewing] = useState<{ affected: AffectedCharacter; to: ContentReference; name: string }>();

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
    // A published revision is never changed: editing starts a new draft of the same content.
    setEditing({ ...entry.latest, revisionId: emptyId, status: 'draft', schemaVersion: undefined });
  }

  return (
    <section className="panel" aria-labelledby="studio-heading">
      <h2 id="studio-heading">Homebrew studio</h2>
      <p className="hint">
        Write your own subclasses, features, feats and items. Everything starts as a draft; publishing checks it and creates a
        new revision. Characters keep their current revision until you review and apply an update.
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
          </div>
        </section>
      )}

      {editing && source && (
        <EntryEditor
          key={`${editing.contentId}-${editing.revisionId}`}
          initial={editing}
          source={source}
          entries={entries}
          info={info}
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
  onError: (error: unknown) => void;
  onClose: () => void;
  onDraftSaved: (name: string) => void;
  onPublished: (result: PublishResult, name: string) => void;
}) {
  const [revision, setRevision] = useState<ContentRevision>(props.initial);
  const [report, setReport] = useState<ValidationReport>();
  const [classChoices, setClassChoices] = useState<ClassChoice[]>([]);
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const { onError } = props;
  const family = props.source.rulesFamilies[0];

  useEffect(() => heading.current?.focus(), []);

  // A subclass can add itself to a class's choice (content schema v4 extendsChoice): list every choice of every class.
  useEffect(() => {
    if (props.initial.kind !== 'subclass' || !family) return;
    let current = true;
    client
      .listContent(family)
      .then(async (options: ContentOption[]) => {
        const classes = options.filter((o) => o.kind === 'class' && o.compatible);
        const found: ClassChoice[] = [];
        for (const c of classes) {
          const revisions = await client.contentRevisions(c.reference.contentId);
          const latest = revisions.filter((r) => r.status === 'published').at(-1);
          for (const effect of latest?.effects ?? []) {
            if (effect.type === 'choice') found.push({ classContentId: c.reference.contentId, className: c.name, choiceId: effect.choiceId, text: effect.text ?? effect.choiceId });
          }
        }
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

  function add(type: Effect['type']) {
    const id = nextId(type);
    const effect: Effect =
      type === 'modifier'
        ? { type, id, operation: 'bonus', target: 'initiative', value: '1' }
        : type === 'resource'
          ? { type, id, resourceId: `resource-${revision.effects.length + 1}`, label: '', maximum: 'PB' }
          : type === 'recovery'
            ? { type, id, resourceId: resources[0]?.resourceId ?? '', on: 'longRest', amount: 'all', timing: 'onLongRest' }
            : type === 'roll'
              ? { type, id, rollId: id, label: '', dice: '1d6', timing: 'onRoll', automation: 'assisted' }
              : type === 'armor'
                ? { type, id, category: 'light', armorClass: 11 }
                : { type: 'grant', id, grant: 'content', content: grantable[0]?.latestPublished && reference(grantable[0].latestPublished), level: 3 };
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

      {revision.effects.map((effect, index) => (
        <EffectEditor
          key={effect.id}
          index={index}
          effect={effect}
          fields={props.info.fields}
          resources={resources}
          grantable={grantable}
          onChange={(e) => setEffect(index, e)}
          onRemove={() => update({ effects: revision.effects.filter((_, i) => i !== index) })}
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
        {(revision.kind === 'subclass' || revision.kind === 'feat') && (
          <button type="button" onClick={() => add('grant')} disabled={grantable.length === 0}>
            Grant a feature
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

      <div className="actions">
        <button type="button" onClick={check}>
          Check
        </button>
        <button type="button" onClick={() => save(false)} disabled={busy}>
          Save draft
        </button>
        <button type="button" onClick={() => save(true)} disabled={busy}>
          Publish
        </button>
        <button type="button" onClick={props.onClose}>
          Close editor
        </button>
      </div>
    </section>
  );
}

const reference = (r: ContentRevision): ContentReference => ({ contentId: r.contentId, revisionId: r.revisionId });

function EffectEditor(props: {
  index: number;
  effect: Effect;
  fields: { id: string; label: string }[];
  resources: Extract<Effect, { type: 'resource' }>[];
  grantable: StudioEntry[];
  onChange: (effect: Effect) => void;
  onRemove: () => void;
}) {
  const { effect, onChange } = props;
  const n = props.index + 1;
  const names: Record<Effect['type'], string> = {
    modifier: 'Modifier',
    grant: 'Granted feature',
    resource: 'Resource',
    recovery: 'Recovery',
    roll: 'Roll or action',
    armor: 'Armor',
    choice: 'Choice',
  };
  const legend = `Rule ${n}: ${names[effect.type]}`;

  return (
    <fieldset className="effect-editor">
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
        </>
      )}
      {effect.type === 'resource' && (
        <>
          <label className="field">
            Resource name
            <input value={effect.label} onChange={(e) => onChange({ ...effect, label: e.target.value, resourceId: slug(e.target.value) })} />
          </label>
          <label className="field">
            Uses (a number or a formula such as PB or CLASS_LEVEL)
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
