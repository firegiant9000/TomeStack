import { useEffect, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { Ability, AbilityScores, ContentOption, ContentReference, RulesFamilyPolicy, RulesFamilyId } from '../api/types';

const abilities: { key: Ability; label: string }[] = [
  { key: 'str', label: 'Strength' },
  { key: 'dex', label: 'Dexterity' },
  { key: 'con', label: 'Constitution' },
  { key: 'int', label: 'Intelligence' },
  { key: 'wis', label: 'Wisdom' },
  { key: 'cha', label: 'Charisma' },
];

const sameRef = (a: ContentReference, b: ContentReference) => a.contentId === b.contentId && a.revisionId === b.revisionId;

interface Props {
  rulesFamilies: RulesFamilyPolicy[];
  onCreated: (id: string) => void;
  onError: (error: unknown) => void;
}

export function CreateCharacterForm({ rulesFamilies, onCreated, onError }: Props) {
  const [name, setName] = useState('');
  const [rulesFamily, setRulesFamily] = useState<RulesFamilyId>('srd-5.1');
  const [scores, setScores] = useState<AbilityScores>({ str: 10, dex: 14, con: 12, int: 10, wis: 13, cha: 8 });
  const [options, setOptions] = useState<ContentOption[]>([]);
  const [pins, setPins] = useState<ContentReference[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let current = true;
    client
      .listContent(rulesFamily)
      .then((result) => {
        if (!current) return;
        setOptions(result);
        setPins((selected) => selected.filter((pin) => result.some((o) => o.compatible && sameRef(o.reference, pin))));
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [rulesFamily, onError]);

  const policy = rulesFamilies.find((f) => f.id === rulesFamily);

  async function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    try {
      const view = await client.createCharacter({ name, rulesFamily, baseAbilities: scores, pins });
      onCreated(view.character.id);
    } catch (error) {
      onError(error);
    } finally {
      setSaving(false);
    }
  }

  function togglePin(option: ContentOption) {
    setPins((selected) =>
      selected.some((p) => sameRef(p, option.reference))
        ? selected.filter((p) => !sameRef(p, option.reference))
        : [...selected, option.reference],
    );
  }

  return (
    <form className="panel" onSubmit={submit} aria-labelledby="create-heading">
      <h2 id="create-heading">New character</h2>

      <label className="field">
        Name
        <input required value={name} onChange={(e) => setName(e.target.value)} autoFocus />
      </label>

      <fieldset>
        <legend>Rules family</legend>
        {rulesFamilies.map((family) => (
          <label key={family.id} className="choice">
            <input
              type="radio"
              name="rulesFamily"
              value={family.id}
              checked={rulesFamily === family.id}
              onChange={() => setRulesFamily(family.id)}
            />
            {family.displayName} <code>{family.id}</code>
          </label>
        ))}
        {policy && (
          <p className="hint">
            Ability score increases under this family come from <strong>{policy.abilityIncreaseSource}</strong> content.
          </p>
        )}
      </fieldset>

      <fieldset className="abilities">
        <legend>Base ability scores</legend>
        {abilities.map(({ key, label }) => (
          <label key={key} className="field">
            {label}
            <input
              type="number"
              min={1}
              max={30}
              required
              value={scores[key]}
              onChange={(e) => setScores({ ...scores, [key]: Number(e.target.value) })}
            />
          </label>
        ))}
      </fieldset>

      <fieldset>
        <legend>Content</legend>
        <p className="hint">Every option shows its source and rules family. Incompatible options cannot be selected.</p>
        <ul className="options">
          {options.map((option) => {
            const id = `opt-${option.reference.revisionId}`;
            return (
              <li key={option.reference.revisionId} className={option.compatible ? '' : 'incompatible'}>
                <input
                  id={id}
                  type="checkbox"
                  disabled={!option.compatible}
                  checked={pins.some((p) => sameRef(p, option.reference))}
                  onChange={() => togglePin(option)}
                />
                <label htmlFor={id}>
                  <span className="option-name">{option.name}</span> <span className="tag">{option.kind}</span>
                  <span className="option-source">
                    {option.sourceTitle}
                    {option.page ? `, ${option.page}` : ''} · {option.rulesFamilies.join(', ')}
                    {!option.compatible && ' · not available for this rules family'}
                  </span>
                </label>
              </li>
            );
          })}
        </ul>
      </fieldset>

      <button type="submit" disabled={saving}>
        {saving ? 'Saving…' : 'Create and save'}
      </button>
    </form>
  );
}
