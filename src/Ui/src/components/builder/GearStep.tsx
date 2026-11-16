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
  /** What the character is trained in, as the guide names it (`requiresProficiency`). */
  trained: string[];
  value: GearValue;
  onChange: (value: GearValue) => void;
  onPreview: (kits: GuideKit[]) => Promise<KitView>;
}

const defaultOption = (pick: GuidePickView) => pick.options.find((o) => o.id === 'a') ?? pick.options[0];

/** The choice shown for a pick: the player's, or option `a` until they choose. */
function choiceFor(guide: GuideView, pick: GuidePickView, value: GearValue): KitChoice {
  const made = value.choices[guide.guideContentId]?.find((c) => c.pickId === pick.id);
  return made ?? { pickId: pick.id, optionId: defaultOption(pick)?.id ?? '' };
}

const allChoices = (guide: GuideView, value: GearValue): KitChoice[] => guide.picks.map((pick) => choiceFor(guide, pick, value));

const available = (option: GuideOptionView, trained: string[]) => option.requiresProficiency == null || trained.includes(option.requiresProficiency);

function PackContents({ line }: { line: GuideLineView }) {
  if (line.kind !== 'pack' || !line.contents) return null;
  return (
    <details>
      <summary>What&apos;s in it?</summary>
      <ul aria-label={`${line.name ?? 'Pack'} contents`}>
        {line.contents.map((c, i) => (
          <li key={`${c.itemContentId ?? 'x'}-${i}`}>{`${c.quantity} × ${c.name ?? 'unknown item'}`}</li>
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
  onChoose: (guide: GuideView, choice: KitChoice) => void;
}) {
  const { guide, value } = props;
  return (
    <section aria-label={props.heading}>
      <h3>{props.heading}</h3>
      {guide.picks.map((pick) => {
        const choice = choiceFor(guide, pick, value);
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
                        if (ok) props.onChoose(guide, { pickId: pick.id, optionId: option.id }); // inert, not disabled (2.4.3)
                      }}
                    />
                    <span className="option-name">{option.text}</span>
                    {!ok && <span className="option-source">{`needs ${option.requiresProficiency} proficiency`}</span>}
                  </label>
                );
              })}
            </fieldset>
            {chosen?.lines.map((line, index) =>
              line.kind === 'anyOf' && line.choices ? (
                <label key={index} className="field">
                  {`Choose: ${line.anyOf ?? line.name ?? 'an item'}`}
                  <select
                    value={choice.anyOf?.[index] ?? line.choices[0]?.reference.contentId ?? ''}
                    onChange={(e) => props.onChoose(guide, { ...choice, anyOf: { ...choice.anyOf, [index]: e.target.value } })}
                  >
                    {line.choices.map((c) => (
                      <option key={c.reference.revisionId} value={c.reference.contentId}>
                        {c.name}
                      </option>
                    ))}
                  </select>
                </label>
              ) : (
                <PackContents key={index} line={line} />
              ),
            )}
          </div>
        );
      })}
    </section>
  );
}

/** The builder's starting-equipment step: one kit per guide (class and background), a live Armor Class and a way to skip. */
export function GearStep({ draft, classGuide, backgroundGuide, className, backgroundName, trained, value, onChange, onPreview }: GearStepProps) {
  const [shown, setShown] = useState<{ key: string; view: KitView } | undefined>();
  const [whyOpen, setWhyOpen] = useState(false);
  const whyId = useId();

  const kits = useMemo<GuideKit[]>(
    () => [classGuide, backgroundGuide].filter((g): g is GuideView => g !== undefined).map((g) => ({ guideContentId: g.guideContentId, choices: allChoices(g, value) })),
    [classGuide, backgroundGuide, value],
  );
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
      () => undefined, // the step stays usable without a preview
    );
    return () => {
      current = false;
    };
  }, [key, wanted, draft]);

  function choose(guide: GuideView, choice: KitChoice) {
    const rest = allChoices(guide, value).filter((c) => c.pickId !== choice.pickId);
    const next = guide.picks.map((p) => (p.id === choice.pickId ? choice : rest.find((c) => c.pickId === p.id)!));
    onChange({ ...value, choices: { ...value.choices, [guide.guideContentId]: next } });
  }

  const view = shown?.key === key ? shown.view : shown?.view; // keep the last answer on screen while the next one loads
  const ac = view?.preview.sheet.fields.find((f) => f.field === 'armorClass');
  const labels = new Map(view?.preview.sheet.fields.map((f) => [f.field, f.label]));

  return (
    <div className="gear-step">
      <button type="button" aria-pressed={value.skipped} onClick={() => onChange({ ...value, skipped: !value.skipped })}>
        Skip: I&apos;ll add gear myself
      </button>
      {!value.skipped &&
        (
          [
            { guide: classGuide, heading: `From your class (${className})`, name: className },
            { guide: backgroundGuide, heading: `From your background (${backgroundName})`, name: backgroundName },
          ] as const
        ).map(({ guide, heading, name }) =>
          guide ? (
            <GuideSection key={heading} heading={heading} guide={guide} trained={trained} value={value} onChoose={choose} />
          ) : (
            <p key={heading}>{`No starting kit for ${name}. Add gear on the Gear tab after saving.`}</p>
          ),
        )}
      {!value.skipped && view && (
        <>
          {view.problems.length > 0 && (
            <ul className="warnings" aria-label="Problems with this kit">
              {view.problems.map((p, i) => (
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
        </>
      )}
    </div>
  );
}
