import { useEffect, useEffectEvent, useId, useMemo, useState } from 'react';
import type { Character, GuideKit, GuideLineView, GuideOptionView, GuidePickView, GuideView, KitChoice, KitView } from '../../api/types';
import { FieldWhy } from '../sheet/FieldWhy';

/** What the step keeps: whether gear is skipped, and the choices per guide (keyed by the guide's content id). */
export interface GearValue {
  skipped: boolean;
  choices: Record<string, KitChoice[]>;
}

export interface GearStepProps {
  draft: Character;
  classGuide?: GuideView;
  backgroundGuide?: GuideView;
  className: string;
  backgroundName: string;
  /** What the character is trained in, as guide category ids ("weapon.martial", "armor.heavy", ...): the vocabulary of `requiresProficiency`. */
  trained: string[];
  value: GearValue;
  onChange: (value: GearValue) => void;
  onPreview: (kits: GuideKit[]) => Promise<KitView>;
}

/** Player wording for a guide category (the service words them the same way); never the id. */
const categoryWords: Record<string, string> = {
  'weapon.simple': 'simple weapon',
  'weapon.martial': 'martial weapon',
  'armor.light': 'light armor',
  'armor.medium': 'medium armor',
  'armor.heavy': 'heavy armor',
  'armor.shield': 'shield',
};

/** Why an option is inert, in player words. */
export function proficiencyReason(id: string): string {
  const words = categoryWords[id];
  return words ? `needs ${words} proficiency` : "needs a proficiency you don't have";
}

const available = (option: GuideOptionView, trained: string[]) => option.requiresProficiency == null || trained.includes(option.requiresProficiency);

/** Option `a` while the character can take it, else the first option they can. */
function defaultOption(pick: GuidePickView, trained: string[]): GuideOptionView | undefined {
  const a = pick.options.find((o) => o.id === 'a');
  if (a && available(a, trained)) return a;
  return pick.options.find((o) => available(o, trained)) ?? a ?? pick.options[0];
}

/** A choice with every anyOf line of its option filled: the item the player took, or the first on offer (what the select shows). */
function complete(pick: GuidePickView, optionId: string, made?: Record<number, string>): KitChoice {
  const anyOf: Record<number, string> = {};
  pick.options
    .find((o) => o.id === optionId)
    ?.lines.forEach((line, index) => {
      if (line.kind !== 'anyOf') return;
      const item = made?.[index] ?? line.choices?.[0]?.reference.contentId;
      if (item) anyOf[index] = item;
    });
  return Object.keys(anyOf).length > 0 ? { pickId: pick.id, optionId, anyOf } : { pickId: pick.id, optionId };
}

/** The choice shown for a pick, and so the one sent: the player's, or the default option, with every anyOf line filled. */
function choiceFor(guide: GuideView, pick: GuidePickView, value: GearValue, trained: string[]): KitChoice {
  const made = value.choices[guide.guideContentId]?.find((c) => c.pickId === pick.id);
  return complete(pick, made?.optionId ?? defaultOption(pick, trained)?.id ?? '', made?.anyOf);
}

const allChoices = (guide: GuideView, value: GearValue, trained: string[]): KitChoice[] => guide.picks.map((pick) => choiceFor(guide, pick, value, trained));

/** What the step previews and what the builder must save: every pick answered, defaults and anyOf items included. */
export function kitsFor(guides: (GuideView | undefined)[], value: GearValue, trained: string[]): GuideKit[] {
  return guides.filter((g): g is GuideView => g !== undefined).map((g) => ({ guideContentId: g.guideContentId, choices: allChoices(g, value, trained) }));
}

const couldntAdd = (line: GuideLineView) => (line.name ? `Couldn't add: ${line.name}` : "Couldn't add an item");

function PackContents({ line }: { line: GuideLineView }) {
  if (line.kind !== 'pack' || !line.contents) return null;
  return (
    <details>
      <summary>What&apos;s in it?</summary>
      <ul aria-label={`${line.name ?? 'Pack'} contents`}>
        {line.contents.map((c, i) => (
          <li key={`${c.itemContentId ?? 'x'}-${i}`}>{c.kind === 'missing' ? couldntAdd(c) : `${c.quantity} × ${c.name ?? 'unknown item'}`}</li>
        ))}
      </ul>
    </details>
  );
}

function GuideSection(props: {
  heading: string;
  guide: GuideView;
  trained: string[];
  value: GearValue;
  onChoose: (guide: GuideView, pick: GuidePickView, optionId: string, anyOf?: Record<number, string>) => void;
}) {
  const { guide, value } = props;
  return (
    <section aria-label={props.heading}>
      <h3>{props.heading}</h3>
      {guide.picks.map((pick) => {
        const choice = choiceFor(guide, pick, value, props.trained);
        const chosen = pick.options.find((o) => o.id === choice.optionId);
        const name = `gear-${guide.guideContentId}-${pick.id}`;
        if (pick.options.length === 1) {
          return (
            <p key={pick.id}>
              <strong>Always:</strong> {pick.options[0]?.text}
            </p>
          );
        }
        return (
          <div key={pick.id}>
            <fieldset>
              <legend>{pick.label}</legend>
              {pick.options.map((option) => {
                const ok = available(option, props.trained);
                return (
                  <label key={option.id} className={`choice${ok ? '' : ' incompatible'}`}>
                    <input
                      type="radio"
                      name={name}
                      aria-disabled={!ok || undefined}
                      checked={choice.optionId === option.id}
                      onChange={() => {
                        if (ok) props.onChoose(guide, pick, option.id); // inert, not disabled (2.4.3)
                      }}
                    />
                    <span className="option-name">{option.text}</span>
                    {!ok && <span className="option-source">{proficiencyReason(option.requiresProficiency ?? '')}</span>}
                  </label>
                );
              })}
            </fieldset>
            {chosen?.lines.map((line, index) => {
              if (line.kind === 'missing') {
                // The service's option text already says "(unavailable: Name)"; say it again only where nothing else tells the player.
                if (line.name && chosen.text.includes(`unavailable: ${line.name}`)) return null;
                return <p key={index}>{couldntAdd(line)}</p>;
              }
              if (line.kind === 'anyOf' && line.choices) {
                const kind = categoryWords[line.anyOf ?? ''] ?? line.name ?? 'item';
                return (
                  <label key={index} className="field">
                    {`Choose: ${kind}`}
                    <select
                      value={choice.anyOf?.[index] ?? ''}
                      onChange={(e) => props.onChoose(guide, pick, choice.optionId, { ...choice.anyOf, [index]: e.target.value })}
                    >
                      {line.choices.map((c) => (
                        <option key={c.reference.revisionId} value={c.reference.contentId}>
                          {c.name}
                        </option>
                      ))}
                    </select>
                  </label>
                );
              }
              return <PackContents key={index} line={line} />;
            })}
          </div>
        );
      })}
    </section>
  );
}

/** The builder's starting-equipment step: one kit per guide (class and background), a live Armor Class and a way to skip. */
export function GearStep({ draft, classGuide, backgroundGuide, className, backgroundName, trained, value, onChange, onPreview }: GearStepProps) {
  const [shown, setShown] = useState<{ key: string; view: KitView } | undefined>();
  const [failedKey, setFailedKey] = useState<string | undefined>();
  const [whyOpen, setWhyOpen] = useState(false);
  const whyId = useId();

  const kits = useMemo<GuideKit[]>(() => kitsFor([classGuide, backgroundGuide], value, trained), [classGuide, backgroundGuide, value, trained]);
  const key = JSON.stringify(kits);
  const wanted = !value.skipped && kits.length > 0;

  const requestPreview = useEffectEvent((k: GuideKit[]) => onPreview(k));
  useEffect(() => {
    if (!wanted) return;
    let current = true;
    requestPreview(JSON.parse(key) as GuideKit[]).then(
      (view) => {
        if (current) setShown({ key, view });
      },
      () => {
        if (current) setFailedKey(key); // the step stays usable without a preview
      },
    );
    return () => {
      current = false;
    };
  }, [key, wanted, draft]);

  function choose(guide: GuideView, pick: GuidePickView, optionId: string, anyOf?: Record<number, string>) {
    // Write every pick of the guide, defaults and anyOf items included, so what is shown is what is stored.
    const next = allChoices(guide, value, trained).map((c) => (c.pickId === pick.id ? complete(pick, optionId, anyOf) : c));
    onChange({ ...value, choices: { ...value.choices, [guide.guideContentId]: next } });
  }

  const current = shown?.key === key ? shown.view : undefined;
  const ac = current?.preview.sheet.fields.find((f) => f.field === 'armorClass');
  const labels = new Map(current?.preview.sheet.fields.map((f) => [f.field, f.label]));
  const failed = !current && failedKey === key;

  return (
    <div className="gear-step">
      <button type="button" aria-pressed={value.skipped} onClick={() => onChange({ ...value, skipped: !value.skipped })}>
        Skip: I&apos;ll add gear myself
      </button>
      {!value.skipped &&
        (
          [
            { guide: classGuide, heading: `From your class (${className})`, name: className, whose: 'your class' },
            { guide: backgroundGuide, heading: `From your background (${backgroundName})`, name: backgroundName, whose: 'your background' },
          ] as const
        ).map(({ guide, heading, name, whose }) =>
          guide ? (
            <GuideSection key={heading} heading={heading} guide={guide} trained={trained} value={value} onChoose={choose} />
          ) : (
            <p key={heading}>{name ? `No starting kit for ${name}. Add gear on the Gear tab after saving.` : `No starting kit from ${whose}. Add gear on the Gear tab after saving.`}</p>
          ),
        )}
      {wanted && (
        <div aria-live="polite">
          {current && current.problems.length > 0 && (
            <ul className="warnings" aria-label="Problems with this kit">
              {current.problems.map((p, i) => (
                <li key={`${p.code}-${i}`}>{p.message}</li>
              ))}
            </ul>
          )}
          {ac && (
            <p>
              <span>{`AC with this gear: ${ac.value}`}</span>{' '}
              <button type="button" aria-expanded={whyOpen} aria-controls={whyId} onClick={() => setWhyOpen(!whyOpen)}>
                Why?
              </button>
              <span id={whyId} hidden={!whyOpen}>
                {whyOpen && <FieldWhy field={ac} label={ac.label} labels={labels} />}
              </span>
            </p>
          )}
          {!current && <p>{failed ? "Couldn't work out Armor Class for this gear." : 'Updating…'}</p>}
        </div>
      )}
    </div>
  );
}
