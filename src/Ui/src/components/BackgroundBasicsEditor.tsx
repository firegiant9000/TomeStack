import { useEffect, useId, useRef, useState } from 'react';
import { client } from '../api/client';
import type { ContentReference, ContentRevision, Effect } from '../api/types';
import { abilities } from '../classBasics';
import {
  grantOfKind,
  increaseChoice,
  increaseChoiceId,
  increaseOptions,
  isIncreaseChoice,
  keysOfOptions,
  setIncreaseChoice,
  setKindGrant,
  type AbilityKey,
  type IncreaseOption,
  type Known,
} from '../entryBasics';
import { familyName, joinNames, NewFeatureForm, SkillFieldset, type BackgroundProps } from './OriginPieces';

const problemField = 'background-increases';
const words = ['no abilities', 'one', 'two', 'three', 'four', 'five', 'six'];

/** The modifiers of an option, as a comparable string: what makes a feature "the same option" when it is reused. */
const signature = (effects: Effect[]): string =>
  effects
    .flatMap((e) => (e.type === 'modifier' ? [`${e.target}=${e.value}`] : []))
    .sort()
    .join(',');

/**
 * Tester feedback item 12: the parts of a background that need no formula, written as the bundled packs write them.
 * Skills are `grant proficiency` effects. Where the family's policy says a background grants a feat
 * (`backgroundGrantsFeat`), the grant is a feat; where it does not, a feature. Where the policy says ability increases come
 * from the background (`abilityIncreaseSource`), the author ticks three abilities and, when the entry is saved, this
 * editor makes the same options the bundled 5.2.1 backgrounds offer (+2/+1 for each ordered pair, then +1 each) as
 * feature drafts in this source, and a `choice` that pins them (ruling R48). They publish together with the background
 * through "Publish all drafts", which re-points the choice at the published options. The policy is read from the app
 * info, never from a family's name.
 */
export function BackgroundBasicsEditor(props: BackgroundProps) {
  const { revision, info, onEffects } = props;
  const effects = revision.effects;
  const policies = revision.rulesFamilies.flatMap((id) => info.rulesFamilies.filter((p) => p.id === id));
  const featureFamilies = policies.filter((p) => !p.backgroundGrantsFeat);
  const featFamilies = policies.filter((p) => p.backgroundGrantsFeat);
  const increaseFamilies = policies.filter((p) => p.abilityIncreaseSource === 'background');
  const otherFamilies = policies.filter((p) => p.abilityIncreaseSource !== 'background');

  const kindOf = (r: ContentReference) => props.known.find((k) => k.contentId === r.contentId)?.kind;
  const featureGrant = grantOfKind(effects, kindOf, 'feature');
  const featGrant = grantOfKind(effects, kindOf, 'feat');
  const [announcement, setAnnouncement] = useState('');
  const featureLegend = useRef<HTMLLegendElement>(null);
  const increaseId = useId();
  const errorId = useId();

  // ---- ability increases ----
  /** The option features this session saved, by revision id: the entries list is only reloaded after the save. */
  const [madeRevisions, setMadeRevisions] = useState<ReadonlyMap<string, ContentRevision>>(() => new Map());
  const revisionOf = (r: ContentReference) => madeRevisions.get(r.revisionId) ?? props.revisionOf(r);
  const choice = effects.find(isIncreaseChoice);

  /** The abilities the choice's options raise; `null` when any option cannot be read (so nothing is assumed about it). */
  function readKeys(c: Extract<Effect, { type: 'choice' }> | undefined): AbilityKey[] | null {
    if (!c) return [];
    const found = c.options.map((o) => revisionOf(o)?.effects);
    return found.every((f) => f !== undefined) ? keysOfOptions(found as Effect[][]) : null;
  }
  const read = readKeys(choice);
  const [picked, setPicked] = useState<AbilityKey[] | null>(null);
  const shown = picked ?? read ?? [];
  const usesIncreases = increaseFamilies.length > 0;
  const showIncreases = usesIncreases || choice !== undefined;

  const nameMissing = revision.name.trim() === '';
  let problem: { visible: string; blocking: string } | undefined;
  if (usesIncreases && shown.length > 0 && shown.length !== 3) {
    const count = words[shown.length] ?? String(shown.length);
    problem = {
      visible: `Pick exactly three abilities, or none. ${count[0]!.toUpperCase()}${count.slice(1)} ${shown.length === 1 ? 'is' : 'are'} ticked.`,
      blocking: `pick exactly three abilities, or none (${count} ${shown.length === 1 ? 'is' : 'are'} ticked).`,
    };
  } else if (usesIncreases && shown.length === 3 && nameMissing) {
    problem = { visible: 'Name the background first: the options are named after it.', blocking: 'name the background first, the options are named after it.' };
  }
  const blocking = problem && `Ability score increases: ${problem.blocking}`;
  const { onProblem } = props;
  useEffect(() => {
    onProblem(problemField, blocking);
    return () => onProblem(problemField, undefined);
  }, [blocking, onProblem]);

  /** An option feature this source already has with this name and these modifiers, covering the background's families. */
  function reusable(option: IncreaseOption): Known | undefined {
    return props.known.find((k) => {
      if (!k.own || k.kind !== 'feature' || k.name !== option.name) return false;
      const r = revisionOf(k.reference);
      return r !== undefined && signature(r.effects) === signature(option.effects) && revision.rulesFamilies.every((f) => r.rulesFamilies.includes(f));
    });
  }

  const beforeSave = async (rev: ContentRevision) => {
    const unchanged = { revision: rev, drafts: [] as ContentReference[] };
    if (!usesIncreases) return unchanged;
    const current = rev.effects.find(isIncreaseChoice);
    const currentKeys = readKeys(current);
    const keys = picked ?? currentKeys ?? [];
    if (keys.length === 0) return picked !== null && current ? { revision: { ...rev, effects: setIncreaseChoice(rev.effects, null) }, drafts: [] } : unchanged;
    if (keys.length !== 3 || rev.name.trim() === '') return unchanged; // the marked field blocks saving before this point
    const wanted = increaseOptions(rev.name, keys);
    const sameKeys = currentKeys !== null && currentKeys.length === keys.length && currentKeys.every((k) => keys.includes(k));
    if (current && current.choiceId === increaseChoiceId(rev.name) && current.options.length === wanted.length && sameKeys) return unchanged;

    const refs: ContentReference[] = [];
    const drafts: ContentReference[] = [];
    for (const option of wanted) {
      const reuse = reusable(option);
      if (reuse) {
        refs.push(reuse.reference);
        if (reuse.draft) drafts.push(reuse.reference);
        continue;
      }
      const reference = await client.saveDraft({
        contentId: crypto.randomUUID(),
        revisionId: '00000000-0000-0000-0000-000000000000',
        kind: 'feature',
        name: option.name,
        rulesFamilies: [...rev.rulesFamilies],
        provenance: { sourceId: props.source.id },
        status: 'draft',
        effects: option.effects,
      });
      const made: ContentRevision = {
        contentId: reference.contentId,
        revisionId: reference.revisionId,
        kind: 'feature',
        name: option.name,
        rulesFamilies: [...rev.rulesFamilies],
        provenance: { sourceId: props.source.id },
        status: 'draft',
        effects: option.effects,
      };
      setMadeRevisions((all) => new Map(all).set(made.revisionId, made));
      props.onMade({ contentId: reference.contentId, name: option.name, kind: 'feature', reference, draft: true, own: true });
      refs.push(reference);
      drafts.push(reference);
    }
    return { revision: { ...rev, effects: setIncreaseChoice(rev.effects, increaseChoice(rev.name, refs)) }, drafts };
  };
  const { registerBeforeSave } = props;
  useEffect(() => {
    registerBeforeSave(beforeSave);
    return () => registerBeforeSave(null);
  });

  // ---- pickers ----
  const features = props.known.filter((k) => k.own && k.kind === 'feature' && k.contentId !== revision.contentId);
  const feats = props.known.filter((k) => k.kind === 'feat' && k.contentId !== revision.contentId && (k.own || k.pickable));
  const withCurrent = (list: Known[], granted?: { content?: ContentReference }) => {
    const id = granted?.content?.contentId;
    if (!id || list.some((k) => k.contentId === id)) return list;
    const k = props.known.find((x) => x.contentId === id);
    return k ? [...list, k] : list;
  };
  const label = (k: Known) => `${k.name}${k.draft ? ' (draft)' : ''}`;

  function pick(kind: 'feature' | 'feat', contentId: string, list: Known[]) {
    const chosen = list.find((k) => k.contentId === contentId);
    onEffects((current) => setKindGrant(current, kindOf, kind, chosen?.reference, kind));
  }

  const choiceLegend = usesIncreases ? `Ability score increases (${joinNames(increaseFamilies.map((p) => p.displayName))})` : 'Ability score increases';

  return (
    <section aria-labelledby="background-basics-heading" className="effect-editor origin-basics">
      <h4 id="background-basics-heading">Background basics</h4>

      <SkillFieldset id="background-basics-skills" info={info} effects={effects} onEffects={onEffects} />

      {(featureFamilies.length > 0 || featureGrant) && (
        <fieldset id="background-basics-feature" tabIndex={-1}>
          <legend ref={featureLegend} tabIndex={-1}>
            {featureFamilies.length > 0 ? `Feature (${joinNames(featureFamilies.map((p) => p.displayName))})` : 'Feature'}
          </legend>
          <p aria-live="polite" className="hint">
            {announcement}
          </p>
          <label className="field">
            Feature granted by the background
            <select value={featureGrant?.content.contentId ?? ''} onChange={(e) => pick('feature', e.target.value, withCurrent(features, featureGrant))}>
              <option value="">None</option>
              {withCurrent(features, featureGrant).map((k) => (
                <option key={k.contentId} value={k.contentId}>
                  {label(k)}
                </option>
              ))}
            </select>
          </label>
          <NewFeatureForm
            label="Feature"
            source={props.source}
            families={revision.rulesFamilies}
            onBusy={props.onBusy}
            onError={props.onError}
            onCreated={(made) => {
              props.onMade(made);
              onEffects((current) => setKindGrant(current, (r) => (r.contentId === made.contentId ? 'feature' : kindOf(r)), 'feature', made.reference, 'feature'));
            }}
            onDone={(message) => {
              setAnnouncement(message);
              featureLegend.current?.focus();
            }}
          />
        </fieldset>
      )}
      {featureFamilies.map((p) => (
        <p key={p.id} className="hint">
          In {familyName(info, p.id)}, a background cannot grant a feat.
        </p>
      ))}

      {(featFamilies.length > 0 || featGrant) && (
        <fieldset id="background-basics-feat" tabIndex={-1}>
          <legend>{featFamilies.length > 0 ? `Feat (${joinNames(featFamilies.map((p) => p.displayName))})` : 'Feat'}</legend>
          <label className="field">
            Feat granted by the background
            <select value={featGrant?.content.contentId ?? ''} onChange={(e) => pick('feat', e.target.value, withCurrent(feats, featGrant))}>
              <option value="">None</option>
              {withCurrent(feats, featGrant).map((k) => (
                <option key={k.contentId} value={k.contentId}>
                  {label(k)}
                </option>
              ))}
            </select>
          </label>
          <p className="hint">The feats of this source and the published feats installed for these rules. Make a new feat with New feat.</p>
        </fieldset>
      )}

      {showIncreases && (
        <fieldset id="background-basics-increases" tabIndex={-1}>
          <legend>{choiceLegend}</legend>
          <p id={increaseId} className="hint">
            Tick the three abilities this background lists. A character then chooses +2 to one and +1 to another, or +1 to all three. When you save, this
            makes those seven options as feature drafts in this source; publish them together with the background.
          </p>
          <div className="origin-grid">
            {abilities.map(([key, name]) => (
              <label key={key} className="choice">
                <input
                  type="checkbox"
                  checked={shown.includes(key)}
                  aria-invalid={problem ? true : undefined}
                  aria-describedby={problem ? `${increaseId} ${errorId}` : increaseId}
                  onChange={() => setPicked(shown.includes(key) ? shown.filter((k) => k !== key) : [...shown, key])}
                />
                {name}
              </label>
            ))}
          </div>
          {problem && (
            <p id={errorId} className="error">
              {problem.visible}
            </p>
          )}
          {!usesIncreases && <p className="warn">These increases are ignored in {joinNames(otherFamilies.map((p) => p.displayName))}.</p>}
        </fieldset>
      )}
      {otherFamilies.map((p) => (
        <p key={p.id} className="hint">
          In {familyName(info, p.id)}, ability increases come from the species.
        </p>
      ))}
    </section>
  );
}
