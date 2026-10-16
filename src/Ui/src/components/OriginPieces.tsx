import { useId, useRef, useState } from 'react';
import { client } from '../api/client';
import type { AppInfo, ContentReference, ContentRevision, Effect, RulesFamilyId, SourceRecord } from '../api/types';
import { toggleSkill, skillGrants, type Known } from '../entryBasics';
import type { EffectsUpdate } from './ClassBasicsEditor';

const emptyId = '00000000-0000-0000-0000-000000000000';

/** What the species, background and spell editors share with the studio's entry editor. */
export interface BasicsProps {
  revision: ContentRevision;
  source: SourceRecord;
  info: AppInfo;
  /** Everything a picker can offer or a grant can be named from: this source's entries, the ones made here, installed feats. */
  known: Known[];
  onEffects: (update: EffectsUpdate) => void;
  /** A helper is saving drafts: the whole editor is locked until it settles. */
  onBusy: (busy: boolean) => void;
  /** A helper saved a feature draft: the studio reloads this source's content. */
  onMade: (made: Known) => void;
  /** A field that does not parse, by name; `undefined` clears it. It blocks saving while it is set. */
  onProblem: (field: string, problem: string | undefined) => void;
  onError: (error: unknown) => void;
}

/**
 * A step the entry editor runs before it saves: the background editor makes the option features its ability score choice
 * points at (saved first, so the background can pin their draft references). It returns the revision to save and the draft
 * revisions that revision now pins, which must be published together with it.
 */
export type BeforeSave = (revision: ContentRevision) => Promise<{ revision: ContentRevision; drafts: ContentReference[] }>;

export interface BackgroundProps extends BasicsProps {
  /** The stored revision a reference names (this source's entries), if it is known. */
  revisionOf: (reference: ContentReference) => ContentRevision | undefined;
  /** The editor hands the studio its save step (and `null` when it goes away). */
  registerBeforeSave: (step: BeforeSave | null) => void;
}

/** The display name of a rules family, from the app info (never from its id). */
export const familyName = (info: AppInfo, id: RulesFamilyId): string => info.rulesFamilies.find((f) => f.id === id)?.displayName ?? id;

export const joinNames = (names: string[]): string => (names.length <= 2 ? names.join(' and ') : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`);

/** Skill proficiencies of the entry: a `grant proficiency` on `skill.<x>` each (the bundled species and backgrounds). */
export function SkillFieldset(props: { id: string; info: AppInfo; effects: Effect[]; onEffects: (update: EffectsUpdate) => void }) {
  const fields = props.info.fields.filter((f) => f.id.startsWith('skill.'));
  const granted = skillGrants(props.effects);
  return (
    <fieldset id={props.id} tabIndex={-1}>
      <legend>Skill proficiencies</legend>
      <div className="origin-grid">
        {fields.map((f) => (
          <label key={f.id} className="choice">
            <input type="checkbox" checked={granted.includes(f.id)} onChange={() => props.onEffects((current) => toggleSkill(current, f.id))} />
            {f.label}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/**
 * "New feature": saves a feature draft in this source (a name and a description, so a reference-only feature, as the
 * bundled packs write size, darkvision and languages) and hands it back to be granted. The press is refused with a visible
 * reason while the name is empty (aria-disabled, never `disabled`), and focus stays put (WCAG 2.4.3).
 */
export function NewFeatureForm(props: {
  label: string;
  source: SourceRecord;
  families: RulesFamilyId[];
  onCreated: (made: Known) => void;
  onBusy: (busy: boolean) => void;
  onDone: (message: string) => void;
  onError: (error: unknown) => void;
}) {
  const [name, setName] = useState('');
  const [text, setText] = useState('');
  const submitting = useRef(false);
  const reasonId = useId();
  const why = name.trim() === '' ? 'Name the feature first.' : props.families.length === 0 ? 'Tick a rules family first.' : '';

  async function create() {
    if (why || submitting.current) return;
    submitting.current = true;
    props.onBusy(true);
    try {
      const trimmed = name.trim();
      const reference: ContentReference = await client.saveDraft({
        contentId: crypto.randomUUID(),
        revisionId: emptyId,
        kind: 'feature',
        name: trimmed,
        summary: text.trim() || undefined,
        rulesFamilies: [...props.families],
        provenance: { sourceId: props.source.id },
        status: 'draft',
        effects: [],
      });
      props.onCreated({ contentId: reference.contentId, name: trimmed, kind: 'feature', reference, draft: true, own: true });
      setName('');
      setText('');
      props.onDone(`Saved a draft of the feature ${trimmed} and added it. Publish it together with this entry.`);
    } catch (error) {
      props.onError(error);
    } finally {
      submitting.current = false;
      props.onBusy(false);
    }
  }

  return (
    <div className="origin-new">
      <label className="field">
        {props.label} name
        <input value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <label className="field">
        {props.label} description
        <textarea rows={2} value={text} onChange={(e) => setText(e.target.value)} />
      </label>
      <button type="button" aria-disabled={why !== '' || undefined} aria-describedby={reasonId} onClick={() => void create()}>
        Create {props.label.toLowerCase()} and add it
      </button>
      <p id={reasonId} className="hint">
        {why || 'This saves a draft feature in this source. Text alone is a reference-only feature.'}
      </p>
    </div>
  );
}
