import { useId, useRef, useState } from 'react';
import { abilities } from '../classBasics';
import { abilityIncreases, addContentGrant, contentGrants, removeContentGrant, setAbilityIncrease } from '../entryBasics';
import { familyName, joinNames, NewFeatureForm, SkillFieldset, type BasicsProps } from './OriginPieces';

/**
 * Tester feedback item 12: the parts of a species that need no formula, written as the effects the bundled packs use
 * (`entryBasics.ts`): a bonus `modifier` on `ability.<x>.score` per increase, a `grant proficiency` on `skill.<x>` per skill,
 * and a `grant` of a feature per trait. Size, darkvision and languages are text, as in the bundled packs. Which family counts
 * ability increases comes from the family's policy in the app info, never from its name. Every other effect of the species
 * stays in the studio's rule list.
 */
export function SpeciesBasicsEditor(props: BasicsProps) {
  const { revision, info, onEffects } = props;
  const effects = revision.effects;
  const policies = revision.rulesFamilies.flatMap((id) => info.rulesFamilies.filter((p) => p.id === id));
  const withIncreases = policies.filter((p) => p.abilityIncreaseSource === 'species');
  const without = policies.filter((p) => p.abilityIncreaseSource !== 'species');
  const increases = abilityIncreases(effects);
  const hasIncreases = Object.keys(increases).length > 0;
  const traits = contentGrants(effects);
  const [announcement, setAnnouncement] = useState('');
  const [pick, setPick] = useState('');
  const legend = useRef<HTMLLegendElement>(null);
  const pickReason = useId();

  const granted = new Set(traits.map((t) => t.content.contentId));
  const features = props.known.filter((k) => k.own && k.kind === 'feature' && k.contentId !== revision.contentId && !granted.has(k.contentId));
  const chosen = features.find((k) => k.contentId === pick);

  return (
    <section aria-labelledby="species-basics-heading" className="effect-editor origin-basics">
      <h4 id="species-basics-heading">Species basics</h4>

      {(withIncreases.length > 0 || hasIncreases) && (
        <fieldset id="species-basics-increases" tabIndex={-1}>
          <legend>{withIncreases.length > 0 ? `Ability score increases (${joinNames(withIncreases.map((p) => p.displayName))})` : 'Ability score increases'}</legend>
          <div className="origin-grid">
            {abilities.map(([key, label]) => (
              <label key={key} className="field">
                {label}
                <input
                  type="number"
                  min={0}
                  max={4}
                  value={increases[key] ?? ''}
                  onChange={(e) => {
                    const amount = e.target.value === '' ? undefined : Math.min(4, Math.max(0, Math.trunc(Number(e.target.value)) || 0));
                    onEffects((current) => setAbilityIncrease(current, key, amount));
                  }}
                />
              </label>
            ))}
          </div>
          {withIncreases.length === 0 && (
            <p className="warn">These increases are ignored in {joinNames(without.map((p) => p.displayName))}; tick a family that takes them from the species.</p>
          )}
        </fieldset>
      )}
      {without.map((p) => (
        <p key={p.id} className="hint">
          In {familyName(info, p.id)}, ability increases come from the background.
        </p>
      ))}

      <SkillFieldset id="species-basics-skills" info={info} effects={effects} onEffects={onEffects} />

      <fieldset id="species-basics-traits" tabIndex={-1}>
        <legend ref={legend} tabIndex={-1}>
          Traits
        </legend>
        <p className="hint">
          A trait is a feature this species grants. Darkvision, size and languages are text: give them a feature with a description, or write them in the
          description above.
        </p>
        <p aria-live="polite" className="hint">
          {announcement}
        </p>
        {traits.length === 0 ? (
          <p className="hint">No traits yet.</p>
        ) : (
          <ul aria-label="Traits" className="origin-list">
            {traits.map((t) => {
              const known = props.known.find((k) => k.contentId === t.content.contentId);
              const name = known?.name ?? 'Content that is not installed';
              return (
                <li key={t.id}>
                  <span className="option-name">{name}</span>
                  {known?.draft && <span className="hint"> (draft)</span>}{' '}
                  <button
                    type="button"
                    onClick={() => {
                      onEffects((current) => removeContentGrant(current, t.content.contentId));
                      setAnnouncement(`Removed the trait ${name}.`);
                      legend.current?.focus();
                    }}
                  >
                    Remove trait {name}
                  </button>
                </li>
              );
            })}
          </ul>
        )}
        <label className="field">
          Add an existing feature as a trait
          <select value={pick} onChange={(e) => setPick(e.target.value)}>
            <option value="">Choose a feature</option>
            {features.map((k) => (
              <option key={k.contentId} value={k.contentId}>
                {k.name}
                {k.draft ? ' (draft)' : ''}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          aria-disabled={chosen === undefined || undefined}
          aria-describedby={chosen === undefined ? pickReason : undefined}
          onClick={() => {
            if (!chosen) return;
            onEffects((current) => addContentGrant(current, chosen.reference, 'trait'));
            setAnnouncement(`Added the trait ${chosen.name}.`);
            setPick('');
            legend.current?.focus();
          }}
        >
          Add trait
        </button>
        {chosen === undefined && (
          <p id={pickReason} className="hint">
            Choose a feature first.
          </p>
        )}
        <NewFeatureForm
          label="Trait"
          source={props.source}
          families={revision.rulesFamilies}
          onBusy={props.onBusy}
          onError={props.onError}
          onCreated={(made) => {
            props.onMade(made);
            onEffects((current) => addContentGrant(current, made.reference, 'trait'));
          }}
          onDone={(message) => {
            setAnnouncement(message);
            legend.current?.focus();
          }}
        />
      </fieldset>

      <p className="hint">Speed: set it on the character&apos;s Stats until species speed arrives.</p>
    </section>
  );
}
