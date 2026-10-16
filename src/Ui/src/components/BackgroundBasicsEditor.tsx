import { useEffect, useId, useRef, useState } from 'react';
import { client } from '../api/client';
import type { ContentReference, ContentRevision, Effect } from '../api/types';
import { abilities } from '../classBasics';
import {
  grantOfKind,
  increaseChoice,
  increaseOptions,
  isIncreaseChoice,
  keysOfOptions,
  setIncreaseChoice,
  setKindGrant,
  type AbilityKey,
  type IncreaseOption,
  type Known,
} from '../entryBasics';
import { familyName, joinNames, NewFeatureForm, SkillFieldset, StalePin, type BackgroundProps } from './OriginPieces';

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
  const featLegend = useRef<HTMLLegendElement>(null);
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
  const [leftBehind, setLeftBehind] = useState(0);
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
    const covers = (r: ContentRevision) => rev.rulesFamilies.every((f) => r.rulesFamilies.includes(f));
    const newest = (contentId: string) => props.newestOf(contentId) ?? [...madeRevisions.values()].filter((r) => r.contentId === contentId).at(-1);

    // The options the choice has now, by what they raise: with the same three abilities they are kept as the same contents (R63).
    const own = new Map<string, ContentReference>();
    if (current && sameKeys) {
      for (const o of current.options) {
        const r = revisionOf(o);
        if (r) own.set(signature(r.effects), o);
      }
    }

    const save = async (contentId: string, option: IncreaseOption): Promise<ContentReference> => {
      const stored: ContentRevision = {
        contentId,
        revisionId: '00000000-0000-0000-0000-000000000000',
        kind: 'feature',
        name: option.name,
        rulesFamilies: [...rev.rulesFamilies],
        provenance: { sourceId: props.source.id },
        status: 'draft',
        effects: option.effects,
      };
      const reference = await client.saveDraft(stored);
      const made: ContentRevision = { ...stored, contentId: reference.contentId, revisionId: reference.revisionId };
      setMadeRevisions((all) => new Map(all).set(made.revisionId, made));
      return reference;
    };

    const refs: ContentReference[] = [];
    const drafts: ContentReference[] = [];
    for (const option of wanted) {
      const mine = own.get(signature(option.effects));
      if (mine) {
        // Point at the newest revision when it already says this (also moves a pin off a superseded draft); else a new revision of it.
        const latestRevision = newest(mine.contentId);
        if (latestRevision && latestRevision.name === option.name && signature(latestRevision.effects) === signature(option.effects) && covers(latestRevision)) {
          const reference = { contentId: latestRevision.contentId, revisionId: latestRevision.revisionId };
          refs.push(reference);
          if (latestRevision.status === 'draft') drafts.push(reference);
        } else {
          const reference = await save(mine.contentId, option);
          refs.push(reference);
          drafts.push(reference);
        }
        continue;
      }
      const reuse = reusable(option);
      if (reuse) {
        refs.push(reuse.reference);
        if (reuse.draft) drafts.push(reuse.reference);
        continue;
      }
      const reference = await save(crypto.randomUUID(), option);
      props.onMade({ contentId: reference.contentId, name: option.name, kind: 'feature', reference, draft: true, own: true });
      refs.push(reference);
      drafts.push(reference);
    }

    const unchangedOptions = current !== undefined && sameKeys && current.options.length === refs.length && current.options.every((o, i) => o.revisionId === refs[i]!.revisionId);
    if (unchangedOptions) return unchanged;
    const stay = current ? current.options.filter((o) => !refs.some((r) => r.contentId === o.contentId)).length : 0;
    setLeftBehind(stay);
    return { revision: { ...rev, effects: setIncreaseChoice(rev.effects, increaseChoice(rev.name, refs, current?.choiceId)) }, drafts };
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

  const nameOf = (g?: { content?: ContentReference }) => props.known.find((k) => k.contentId === g?.content?.contentId)?.name ?? 'this content';
  const featureName = nameOf(featureGrant);
  const featName = nameOf(featGrant);
  const featureNewer = featureGrant && props.newerPublished(featureGrant.content);
  const featNewer = featGrant && props.newerPublished(featGrant.content);
  /** R62: the author asked for the published version of a pinned draft; nothing re-points without this press. */
  function repointToPublished(kind: 'feature' | 'feat', newer: ContentReference) {
    onEffects((current) => setKindGrant(current, kindOf, kind, newer, kind));
    setAnnouncement(`The ${kind} ${kind === 'feature' ? featureName : featName} now points at its published version.`);
    // The pressed button unmounts: focus goes to the owning fieldset's legend (WCAG 2.4.3).
    (kind === 'feature' ? featureLegend : featLegend).current?.focus();
  }

  function pick(kind: 'feature' | 'feat', contentId: string, list: Known[]) {
    const chosen = list.find((k) => k.contentId === contentId);
    onEffects((current) => setKindGrant(current, kindOf, kind, chosen?.reference, kind));
  }

  const choiceLegend = usesIncreases ? `Ability score increases (${joinNames(increaseFamilies.map((p) => p.displayName))})` : 'Ability score increases';

  return (
    <section aria-labelledby="background-basics-heading" className="effect-editor origin-basics">
      <h4 id="background-basics-heading">Background basics</h4>
      <p aria-live="polite" className="hint">
        {announcement}
      </p>

      <SkillFieldset id="background-basics-skills" info={info} effects={effects} onEffects={onEffects} />

      {(featureFamilies.length > 0 || featureGrant) && (
        <fieldset id="background-basics-feature" tabIndex={-1}>
          <legend ref={featureLegend} tabIndex={-1}>
            {featureFamilies.length > 0 ? `Feature (${joinNames(featureFamilies.map((p) => p.displayName))})` : 'Feature'}
          </legend>
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
          {featureNewer && (
            <p>
              <StalePin name={featureName} onUse={() => repointToPublished('feature', featureNewer)} />
            </p>
          )}
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
          <legend ref={featLegend} tabIndex={-1}>
            {featFamilies.length > 0 ? `Feat (${joinNames(featFamilies.map((p) => p.displayName))})` : 'Feat'}
          </legend>
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
          {featNewer && (
            <p>
              <StalePin name={featName} onUse={() => repointToPublished('feat', featNewer)} />
            </p>
          )}
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
          {leftBehind > 0 && (
            <p className="hint">
              {leftBehind === 1
                ? '1 old option entry stays in this source; it is no longer offered by this background.'
                : `${leftBehind} old option entries stay in this source; they are no longer offered by this background.`}
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
