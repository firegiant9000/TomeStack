import { useState } from 'react';
import { client } from '../api/client';
import type { AppInfo, ContentReference, ContentRevision, Effect, SourceRecord } from '../api/types';

/** The six abilities: the key used in field ids (save.int, ability.int.score) and the label. */
export const abilities = [
  ['str', 'Strength'],
  ['dex', 'Dexterity'],
  ['con', 'Constitution'],
  ['int', 'Intelligence'],
  ['wis', 'Wisdom'],
  ['cha', 'Charisma'],
] as const;

const hitDice = [6, 8, 10, 12];
const emptyId = '00000000-0000-0000-0000-000000000000';

/**
 * The effects this editor owns on a class: its hit die, starting-class saving throws, multiclass prerequisites and its
 * two choices (skills and subclass). The studio's rule list shows everything else (M5 slice 1b, ADR-010).
 */
export function isClassBasic(effect: Effect): boolean {
  return (
    effect.type === 'hitDie' ||
    effect.type === 'restriction' ||
    effect.type === 'choice' ||
    (effect.type === 'grant' && effect.grant === 'proficiency')
  );
}

/**
 * M5 slice 1b (ADR-010): the parts of a class that need no formula. Every control writes a declarative effect of an
 * existing type (content v3/v5): `hitDie`, `grant proficiency save.*` only as the starting class, `restriction` with
 * `multiclass` (and one `group` for "any one of these"), and `choice` effects. The skill choice's options are features,
 * as in the SRD packs: "Create skill choice" publishes one feature per skill (each granting that proficiency) in this
 * source, then offers them. Nothing here runs code.
 */
export function ClassBasicsEditor(props: {
  revision: ContentRevision;
  source: SourceRecord;
  info: AppInfo;
  onEffects: (effects: Effect[]) => void;
  onError: (error: unknown) => void;
}) {
  const { revision, onEffects } = props;
  const others = revision.effects.filter((e) => !isClassBasic(e));
  const basics = revision.effects.filter(isClassBasic);
  const hitDie = basics.find((e): e is Extract<Effect, { type: 'hitDie' }> => e.type === 'hitDie');
  const saves = basics.filter((e): e is Extract<Effect, { type: 'grant' }> => e.type === 'grant' && (e.target ?? '').startsWith('save.'));
  const otherProficiencies = basics.filter((e) => e.type === 'grant' && !(e.target ?? '').startsWith('save.')); // kept as they are
  const prerequisites = basics.filter((e): e is Extract<Effect, { type: 'restriction' }> => e.type === 'restriction');
  const choices = basics.filter((e): e is Extract<Effect, { type: 'choice' }> => e.type === 'choice');
  const skills = choices.find((c) => c.choiceId === 'skills');
  const subclass = choices.find((c) => c.choiceId === 'subclass');
  const otherChoices = choices.filter((c) => c !== skills && c !== subclass);
  const anyOne = prerequisites.length > 1 && prerequisites.every((p) => p.group === 'multiclass');

  // Rebuilds the list in a stable order: the basics first, then the author's rules as they were.
  function write(next: {
    hitDie?: Effect | null;
    saves?: Effect[];
    prerequisites?: Effect[];
    skills?: Effect | null;
    subclass?: Effect | null;
  }) {
    const pick = <T,>(value: T | null | undefined, current: T | undefined) => (value === undefined ? current : (value ?? undefined));
    onEffects(
      [
        pick(next.hitDie, hitDie),
        ...(next.saves ?? saves),
        ...otherProficiencies,
        ...(next.prerequisites ?? prerequisites),
        pick(next.skills, skills),
        pick(next.subclass, subclass),
        ...otherChoices,
        ...others,
      ].filter((e): e is Effect => e !== undefined),
    );
  }

  function toggleSave(key: string) {
    const target = `save.${key}`;
    const added: Extract<Effect, { type: 'grant' }> = { type: 'grant', id: `save-${key}`, grant: 'proficiency', target, onlyAs: 'startingClass' };
    write({
      saves: saves.some((s) => s.target === target)
        ? saves.filter((s) => s.target !== target)
        : [...saves, added].sort((a, b) => (a.target ?? '').localeCompare(b.target ?? '')),
    });
  }

  function setPrerequisite(key: string, minimum: number | undefined, group: boolean) {
    const field = `ability.${key}.score`;
    const rest = prerequisites.filter((p) => p.field !== field);
    const all = minimum === undefined ? rest : [...rest, { type: 'restriction' as const, id: `multiclass-${key}`, field, minimum, multiclass: true }];
    write({ prerequisites: regroup(all, group) });
  }

  // "Any one of these" puts every prerequisite in one group (content v5 restriction.group); otherwise all apply.
  function regroup(all: Extract<Effect, { type: 'restriction' }>[], group: boolean): Effect[] {
    return abilities
      .map(([key]) => all.find((p) => p.field === `ability.${key}.score`))
      .filter((p): p is Extract<Effect, { type: 'restriction' }> => p !== undefined)
      .map((p) => ({ ...p, group: group && all.length > 1 ? 'multiclass' : undefined }));
  }

  return (
    <section aria-labelledby="class-basics-heading" className="effect-editor">
      <h4 id="class-basics-heading">Class basics</h4>
      <label className="field">
        Hit die
        <select
          value={hitDie?.type === 'hitDie' ? String(hitDie.die) : ''}
          onChange={(e) => write({ hitDie: e.target.value ? { type: 'hitDie', id: 'hit-die', die: Number(e.target.value) } : null })}
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
            <input type="checkbox" checked={saves.some((s) => s.target === `save.${key}`)} onChange={() => toggleSave(key)} />
            {label}
          </label>
        ))}
      </fieldset>

      <fieldset>
        <legend>Multiclass prerequisites (leave blank for none)</legend>
        {abilities.map(([key, label]) => (
          <label key={key} className="field">
            {label} at least
            <input
              type="number"
              min={1}
              max={30}
              value={prerequisites.find((p) => p.field === `ability.${key}.score`)?.minimum ?? ''}
              onChange={(e) => setPrerequisite(key, e.target.value ? Number(e.target.value) : undefined, anyOne)}
            />
          </label>
        ))}
        <label className="choice">
          <input type="checkbox" checked={anyOne} disabled={prerequisites.length < 2} onChange={() => write({ prerequisites: regroup(prerequisites, !anyOne) })} />
          Any one of these is enough
        </label>
      </fieldset>

      <SkillChoice
        className={revision.name}
        revision={revision}
        source={props.source}
        info={props.info}
        current={skills?.type === 'choice' ? skills : undefined}
        onChoice={(choice) => write({ skills: choice })}
        onError={props.onError}
      />

      <fieldset>
        <legend>Subclass</legend>
        <label className="choice">
          <input
            type="checkbox"
            checked={subclass !== undefined}
            onChange={() =>
              write({
                subclass: subclass
                  ? null
                  : { type: 'choice', id: 'subclass', choiceId: 'subclass', count: 1, options: [], level: 3, text: 'Choose a subclass' },
              })
            }
          />
          This class has subclasses (they join its choice from the studio)
        </label>
        {subclass?.type === 'choice' && (
          <label className="field">
            Subclass chosen at class level
            <input
              type="number"
              min={1}
              max={20}
              value={subclass.level ?? ''}
              onChange={(e) => write({ subclass: { ...subclass, level: e.target.value ? Number(e.target.value) : undefined } })}
            />
          </label>
        )}
      </fieldset>
    </section>
  );
}

/** The class's skill choice: its options are published features that each grant one skill proficiency. */
function SkillChoice(props: {
  className: string;
  revision: ContentRevision;
  source: SourceRecord;
  info: AppInfo;
  current?: Extract<Effect, { type: 'choice' }>;
  onChoice: (choice: Effect | null) => void;
  onError: (error: unknown) => void;
}) {
  const skillFields = props.info.fields.filter((f) => f.id.startsWith('skill.'));
  const [selected, setSelected] = useState<string[]>([]);
  const [count, setCount] = useState(2);
  const [busy, setBusy] = useState(false);

  async function create() {
    setBusy(true);
    try {
      const options: ContentReference[] = [];
      for (const field of skillFields.filter((f) => selected.includes(f.id))) {
        const draft = await client.saveDraft({
          contentId: crypto.randomUUID(),
          revisionId: emptyId,
          kind: 'feature',
          name: `${props.className.trim()}: ${field.label}`,
          rulesFamilies: [...props.revision.rulesFamilies],
          provenance: { sourceId: props.source.id },
          status: 'draft',
          effects: [{ type: 'grant', id: 'skill', grant: 'proficiency', target: field.id }],
        });
        options.push((await client.publish(draft)).published);
      }
      props.onChoice({
        type: 'choice',
        id: 'skills',
        choiceId: 'skills',
        count,
        options,
        onlyAs: 'startingClass',
        text: `Choose ${count} skill${count === 1 ? '' : 's'}`,
      });
      setSelected([]);
    } catch (error) {
      props.onError(error);
    } finally {
      setBusy(false);
    }
  }

  return (
    <fieldset>
      <legend>Skill choice (starting class only)</legend>
      {props.current ? (
        <p>
          Choose {props.current.count} of {props.current.options.length} skills.{' '}
          <button type="button" onClick={() => props.onChoice(null)}>
            Remove the skill choice
          </button>
        </p>
      ) : (
        <>
          <label className="field">
            How many to choose
            <input type="number" min={1} max={18} value={count} onChange={(e) => setCount(Number(e.target.value) || 1)} />
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
          <button type="button" onClick={create} disabled={busy || !props.className.trim() || selected.length < count}>
            Create skill choice
          </button>
          <p className="hint">This publishes one feature per skill in this source, named after the class, and offers them.</p>
        </>
      )}
    </fieldset>
  );
}
