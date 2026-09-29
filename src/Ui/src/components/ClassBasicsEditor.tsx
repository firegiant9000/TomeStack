import { useRef, useState } from 'react';
import { client } from '../api/client';
import type { AppInfo, ContentReference, ContentRevision, Effect, SourceRecord, StudioEntry } from '../api/types';
import { abilities, anyOne, isPrerequisite, isSave, setAnyOne, setChoice, setHitDie, setPrerequisite, toggleSave } from '../classBasics';

export { isClassBasic } from '../classBasics';

const hitDice = [6, 8, 10, 12];
const emptyId = '00000000-0000-0000-0000-000000000000';

/**
 * The element of this editor that holds a class-basic effect, so a debugger finding can move focus to it (M5 slice 2).
 * Each group is a fieldset with `tabIndex={-1}`, named by its legend.
 */
export function classBasicElementId(effect: Effect): string {
  if (effect.type === 'choice') return effect.choiceId === 'skills' ? 'class-basics-skills' : 'class-basics-subclass';
  return effect.type === 'restriction' ? 'class-basics-prerequisites' : 'class-basics-die';
}

/** A change to the latest effect list; never a snapshot, so awaited work cannot undo other edits (review fix). */
export type EffectsUpdate = (effects: Effect[]) => Effect[];

/**
 * M5 slice 1b (ADR-010): the parts of a class that need no formula. Every control writes a declarative effect of an
 * existing type (content v3/v5; `classBasics.ts`): `hitDie`, `grant proficiency save.*` only as the starting class,
 * `restriction` with `multiclass` (one `group` for "any one of these"), and the `skills` and `subclass` choices. The
 * skill choice's options are features, as in the SRD packs. Every other effect of the class stays in the studio's rule
 * list, where it can be seen, changed and removed. Nothing here runs code.
 */
export function ClassBasicsEditor(props: {
  revision: ContentRevision;
  source: SourceRecord;
  entries: StudioEntry[];
  info: AppInfo;
  disabled: boolean;
  onEffects: (update: EffectsUpdate) => void;
  onBusy: (busy: boolean) => void;
  onError: (error: unknown) => void;
}) {
  const { revision, onEffects } = props;
  const effects = revision.effects;
  const hitDie = effects.find((e): e is Extract<Effect, { type: 'hitDie' }> => e.type === 'hitDie');
  const prerequisites = effects.filter(isPrerequisite);
  const skills = effects.find((e): e is Extract<Effect, { type: 'choice' }> => e.type === 'choice' && e.choiceId === 'skills');
  const subclass = effects.find((e): e is Extract<Effect, { type: 'choice' }> => e.type === 'choice' && e.choiceId === 'subclass');
  const any = anyOne(effects);

  return (
    <section aria-labelledby="class-basics-heading" className="effect-editor">
      <h4 id="class-basics-heading">Class basics</h4>
      <fieldset disabled={props.disabled} id="class-basics-die" tabIndex={-1}>
        <legend>Hit die and saving throws</legend>
        <label className="field">
          Hit die
          <select
            value={hitDie ? String(hitDie.die) : ''}
            onChange={(e) => {
              const die = e.target.value ? Number(e.target.value) : undefined;
              onEffects((current) => setHitDie(current, die));
            }}
          >
            <option value="">None (no hit points)</option>
            {hitDice.map((d) => (
              <option key={d} value={d}>
                d{d}
              </option>
            ))}
          </select>
        </label>
        <fieldset>
          <legend>Saving throw proficiencies (starting class only)</legend>
          {abilities.map(([key, label]) => (
            <label key={key} className="choice">
              <input
                type="checkbox"
                checked={effects.some((s) => isSave(s) && s.target === `save.${key}`)}
                onChange={() => onEffects((current) => toggleSave(current, key))}
              />
              {label}
            </label>
          ))}
        </fieldset>
      </fieldset>

      <fieldset disabled={props.disabled} id="class-basics-prerequisites" tabIndex={-1}>
        <legend>Multiclass prerequisites (leave blank for none)</legend>
        {abilities.map(([key, label]) => (
          <label key={key} className="field">
            {label} at least
            <input
              type="number"
              min={1}
              max={30}
              value={prerequisites.find((p) => p.field === `ability.${key}.score`)?.minimum ?? ''}
              onChange={(e) => {
                const minimum = e.target.value ? Math.trunc(Number(e.target.value)) : undefined;
                onEffects((current) => setPrerequisite(current, key, minimum));
              }}
            />
          </label>
        ))}
        <label className="choice">
          <input type="checkbox" checked={any} onChange={() => onEffects((current) => setAnyOne(current, !any))} />
          Any one of these is enough
        </label>
      </fieldset>

      <SkillChoice
        revision={revision}
        source={props.source}
        entries={props.entries}
        info={props.info}
        current={skills}
        disabled={props.disabled}
        onChoice={(choice) => onEffects((current) => setChoice(current, 'skills', choice))}
        onBusy={props.onBusy}
        onError={props.onError}
      />

      <fieldset disabled={props.disabled} id="class-basics-subclass" tabIndex={-1}>
        <legend>Subclass</legend>
        <label className="choice">
          <input
            type="checkbox"
            checked={subclass !== undefined}
            onChange={() =>
              onEffects((current) =>
                setChoice(
                  current,
                  'subclass',
                  subclass ? null : { type: 'choice', id: 'subclass', choiceId: 'subclass', count: 1, options: [], level: 3, text: 'Choose a subclass' },
                ),
              )
            }
          />
          This class has subclasses (they join its choice from the studio)
        </label>
        {subclass && (
          <label className="field">
            Subclass chosen at class level
            <input
              type="number"
              min={1}
              max={20}
              value={subclass.level ?? ''}
              onChange={(e) => {
                const level = e.target.value ? Math.trunc(Number(e.target.value)) : undefined;
                onEffects((current) => setChoice(current, 'subclass', { ...subclass, level }));
              }}
            />
          </label>
        )}
      </fieldset>
    </section>
  );
}

/**
 * The class's skill choice: its options are published features that each grant one skill proficiency. A feature this
 * source already has for that class and skill (same name and grant, covering the class's rules families) is reused,
 * so a retry, or removing and recreating the choice, never publishes duplicates. The choice is written once every
 * option exists, and a failure part-way leaves nothing half-written; the next try reuses what was published (review fix).
 */
function SkillChoice(props: {
  revision: ContentRevision;
  source: SourceRecord;
  entries: StudioEntry[];
  info: AppInfo;
  current?: Extract<Effect, { type: 'choice' }>;
  disabled: boolean;
  onChoice: (choice: Extract<Effect, { type: 'choice' }> | null) => void;
  onBusy: (busy: boolean) => void;
  onError: (error: unknown) => void;
}) {
  const skillFields = props.info.fields.filter((f) => f.id.startsWith('skill.'));
  const [selected, setSelected] = useState<string[]>([]);
  const [count, setCount] = useState(2);
  const [busy, setBusy] = useState(false);
  const [announcement, setAnnouncement] = useState('');
  const legend = useRef<HTMLLegendElement>(null);
  const className = props.revision.name.trim();
  const families = props.revision.rulesFamilies;
  const why = !className
    ? 'Name the class first: the options are named after it.'
    : selected.length < count
      ? `Tick at least ${count} skill${count === 1 ? '' : 's'}.`
      : '';

  // The existing published feature for this class and skill, if it covers the class's rules families.
  function existing(field: { id: string; label: string }): ContentReference | undefined {
    const found = props.entries.find(
      (e) =>
        e.kind === 'feature' &&
        e.name === `${className}: ${field.label}` &&
        e.latestPublished?.effects.some((x) => x.type === 'grant' && x.grant === 'proficiency' && x.target === field.id) &&
        families.every((f) => e.latestPublished!.rulesFamilies.includes(f)),
    );
    return found?.latestPublished && { contentId: found.latestPublished.contentId, revisionId: found.latestPublished.revisionId };
  }

  function done(message: string) {
    setAnnouncement(message);
    legend.current?.focus(); // the pressed button is gone; focus stays in this group (WCAG 2.4.3)
  }

  async function create() {
    setBusy(true);
    props.onBusy(true);
    try {
      const options: ContentReference[] = [];
      let created = 0;
      for (const field of skillFields.filter((f) => selected.includes(f.id))) {
        const reuse = existing(field);
        if (reuse) {
          options.push(reuse);
          continue;
        }
        const draft = await client.saveDraft({
          contentId: crypto.randomUUID(),
          revisionId: emptyId,
          kind: 'feature',
          name: `${className}: ${field.label}`,
          rulesFamilies: [...families],
          provenance: { sourceId: props.source.id },
          status: 'draft',
          effects: [{ type: 'grant', id: 'skill', grant: 'proficiency', target: field.id }],
        });
        options.push((await client.publish(draft)).published);
        created++;
      }
      props.onChoice({ type: 'choice', id: 'skills', choiceId: 'skills', count, options, onlyAs: 'startingClass', text: `Choose ${count} skill${count === 1 ? '' : 's'}` });
      setSelected([]);
      done(`Skill choice added: choose ${count} of ${options.length}. ${created} new option feature${created === 1 ? '' : 's'} published, ${options.length - created} reused.`);
    } catch (error) {
      props.onError(error);
    } finally {
      setBusy(false);
      props.onBusy(false);
    }
  }

  // Options made for other rules families cannot be picked by a character of a family they do not support.
  const optionFamilies = (props.current?.options ?? []).map(
    (o) => props.entries.find((e) => e.contentId === o.contentId)?.revisions.find((r) => r.revisionId === o.revisionId)?.rulesFamilies,
  );
  const uncovered = families.filter((f) => optionFamilies.some((of) => of !== undefined && !of.includes(f)));

  return (
    <fieldset disabled={props.disabled} id="class-basics-skills" tabIndex={-1}>
      <legend ref={legend} tabIndex={-1}>
        Skill choice (starting class only)
      </legend>
      <p aria-live="polite" className="hint">
        {announcement}
      </p>
      {props.current ? (
        <>
          <p>
            Choose {props.current.count} of {props.current.options.length} skills.{' '}
            <button
              type="button"
              onClick={() => {
                props.onChoice(null);
                done('Skill choice removed. Its option features stay published and are reused if you add it again.');
              }}
            >
              Remove the skill choice
            </button>
          </p>
          {uncovered.length > 0 && (
            <p className="warn">
              Some skill options are not written for {uncovered.join(', ')}, so characters of that family cannot pick them. Remove the skill choice and
              create it again to add options for every family of the class.
            </p>
          )}
        </>
      ) : (
        <>
          <label className="field">
            How many to choose
            <input
              type="number"
              min={1}
              max={18}
              value={count}
              onChange={(e) => setCount(Math.min(18, Math.max(1, Math.trunc(Number(e.target.value)) || 1)))}
            />
          </label>
          <div className="choice-grid">
            {skillFields.map((f) => (
              <label key={f.id} className="choice">
                <input
                  type="checkbox"
                  checked={selected.includes(f.id)}
                  onChange={() => setSelected((s) => (s.includes(f.id) ? s.filter((x) => x !== f.id) : [...s, f.id]))}
                />
                {f.label}
              </label>
            ))}
          </div>
          <button type="button" onClick={create} disabled={busy || why !== ''} aria-describedby="skill-choice-why">
            Create skill choice
          </button>
          <p id="skill-choice-why" className="hint">
            {why || 'This publishes one feature per skill in this source, named after the class (existing ones are reused), and offers them.'}
          </p>
        </>
      )}
    </fieldset>
  );
}
