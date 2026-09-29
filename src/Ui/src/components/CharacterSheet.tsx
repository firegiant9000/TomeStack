import { useEffect, useRef, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type {
  CharacterView,
  DerivedValue,
  ExportPreview,
  ExportPurpose,
  FieldOverride,
  PlayAction,
  RestPeriod,
  RollMode,
  RollRecord,
  RollTarget,
} from '../api/types';
import { downloadBase64 } from '../files';
import { ArchivePanel } from './ArchivePanel';
import { ActionsPanel, ConditionsPanel, DeathSavesPanel, FeaturesPanel, HitPointsPanel, ResourcesPanel, RollModePicker, RollResult } from './PlayPanels';
import { EquipmentPanel } from './EquipmentPanel';
import { GapNotesPanel, gapAboutFeature, gapAboutField } from './GapNotesPanel';
import { PrintView } from './PrintView';
import { RestPanel } from './RestPanel';
import { SpellsPanel } from './SpellsPanel';
import { TraceTable } from './TraceTable';
import { UpdatesPanel } from './UpdatesPanel';

const signed = (n: number) => (n >= 0 ? `+${n}` : `${n}`);
const display = (value: DerivedValue, n: number) => (value.units === 'score' ? `${n}` : signed(n));

/** Display groups for the calculated fields; the rules core decides what exists, this only orders it. */
const groups: { title: string; match: (field: string) => boolean }[] = [
  { title: 'Abilities', match: (f) => f.startsWith('ability.') },
  { title: 'Proficiency', match: (f) => f === 'proficiencyBonus' },
  { title: 'Saving throws', match: (f) => f.startsWith('save.') },
  { title: 'Skills', match: (f) => f.startsWith('skill.') },
  { title: 'Combat', match: (f) => f === 'initiative' || f === 'armorClass' || f === 'hitPoints' || f === 'attacks' || f === 'criticalRange' },
  // D04: the primary caster's numbers, with traces and overrides (the manual step for combined multiclass slots).
  { title: 'Spellcasting', match: (f) => f === 'spellAttack' || f === 'spellSaveDc' || f === 'pactSlots' || f.startsWith('spellSlots.') },
];

const isSpellField = (f: string) => f === 'spellAttack' || f === 'spellSaveDc' || f === 'pactSlots' || f.startsWith('spellSlots.');

/** Fields the `roll` command accepts as a d20 test: ability modifiers, saves, skills and initiative. */
const isD20 = (field: string) =>
  field === 'initiative' || field.startsWith('save.') || field.startsWith('skill.') || (field.startsWith('ability.') && field.endsWith('.mod'));

interface FieldProps {
  value: DerivedValue;
  labels: Map<string, string>;
  onOverride: (field: string, change: FieldOverride | undefined) => void;
  onRoll: (field: string) => void;
  onReportGap: (field: string) => void;
}

/** One field: its own override form state, so fields never share input values. */
function FieldCard({ value, labels, onOverride, onRoll, onReportGap }: FieldProps) {
  const [overrideValue, setOverrideValue] = useState('');
  const [overrideReason, setOverrideReason] = useState('');
  const headingId = `field-${value.field}`;

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const n = Number(overrideValue);
    if (!Number.isInteger(n)) return;
    onOverride(value.field, { field: value.field, value: n, reason: overrideReason || undefined });
    setOverrideValue('');
    setOverrideReason('');
  }

  return (
    <section aria-labelledby={headingId} className="field-card">
      <details>
        <summary>
          <h4 id={headingId}>
            {value.label}: <span className="derived">{display(value, value.value)}</span>
            {value.override && <span className="override-label"> overridden (calculated {display(value, value.computedValue)})</span>}
            {value.warnings.length > 0 && <span className="warning-count"> · {value.warnings.length} warning{value.warnings.length === 1 ? '' : 's'}</span>}
          </h4>
        </summary>
        {isD20(value.field) && (
          <button type="button" onClick={() => onRoll(value.field)}>
            Roll {value.label}
          </button>
        )}
        <TraceTable label={value.label} trace={value.trace} labels={labels} />
        {value.warnings.length > 0 && (
          <ul className="warnings" aria-label={`${value.label} warnings`}>
            {value.warnings.map((w) => (
              <li key={`${w.code}-${w.content?.revisionId ?? ''}-${w.effectId ?? ''}`}>{w.message}</li>
            ))}
          </ul>
        )}
        <form className="override" onSubmit={submit}>
          <label className="field">
            Override value
            <input type="number" value={overrideValue} onChange={(e) => setOverrideValue(e.target.value)} required />
          </label>
          <label className="field">
            Reason (optional)
            <input value={overrideReason} onChange={(e) => setOverrideReason(e.target.value)} />
          </label>
          <button type="submit">Apply override</button>
          {value.override && (
            <button type="button" onClick={() => onOverride(value.field, undefined)}>
              Remove override
            </button>
          )}
        </form>
        <button type="button" onClick={() => onReportGap(value.field)}>
          Report a gap: {value.label}
        </button>
      </details>
    </section>
  );
}

interface ExportProps {
  characterId: string;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

/** ADR-007: choose backup or share; a share first lists everything it will leave out. */
function ExportPanel({ characterId, onError, onStatus }: ExportProps) {
  const [purpose, setPurpose] = useState<ExportPurpose>('backup');
  const [preview, setPreview] = useState<ExportPreview>();

  async function choose(next: ExportPurpose) {
    setPurpose(next);
    setPreview(undefined);
    if (next !== 'share') return;
    try {
      setPreview(await client.previewExport([characterId], next));
    } catch (error) {
      onError(error);
    }
  }

  async function exportCharacter() {
    try {
      const outcome = await client.saveExportAs([characterId], purpose);
      if (outcome.saved) onStatus(`Saved ${outcome.fileName}.`);
    } catch (error) {
      if (!(error instanceof TomeStackError && error.code === 'unsupported')) {
        onError(error);
        return;
      }
      // Browser development (DevHost) has no native dialog: fall back to a download.
      try {
        const exported = await client.exportCharacters([characterId], purpose);
        downloadBase64(exported.fileName, exported.base64);
      } catch (fallbackError) {
        onError(fallbackError);
      }
    }
  }

  return (
    <section aria-labelledby="export-heading" className="export-panel">
      <h3 id="export-heading">Export</h3>
      <fieldset>
        <legend>What is this package for?</legend>
        <label>
          <input type="radio" name="export-purpose" checked={purpose === 'backup'} onChange={() => choose('backup')} />
          Personal backup of this character: everything it uses, your gap notes too, but no PDFs. Do not share it. (To back up
          your whole library, PDFs included, use Backups.)
        </label>
        <label>
          <input type="radio" name="export-purpose" checked={purpose === 'share'} onChange={() => choose('share')} />
          Share with someone: leaves out content you may not share, and never includes gap notes or PDFs
        </label>
      </fieldset>
      {purpose === 'share' && preview && (
        <div role="region" aria-label="Left out of the shared package">
          {preview.omitted.length === 0 ? (
            <p>Nothing is left out: every source this character uses may be shared.</p>
          ) : (
            <>
              <p>These are left out. The receiver sees them as missing until they install the source themselves:</p>
              <ul>
                {preview.omitted.map((source) => (
                  <li key={source.sourceId}>
                    {source.title} ({source.publisher}, {source.license}): {source.revisions.map((r) => r.name).join(', ')}
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}
      <button type="button" onClick={exportCharacter} disabled={purpose === 'share' && !preview}>
        Export package
      </button>
    </section>
  );
}

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** Opens the builder on a level-up draft of this character (SPEC C-07). */
  onLevelUp: () => void;
  /** Opens the builder on this character's open choices, as a draft. */
  onMakeChoices: () => void;
  /** After the character was archived or unarchived (SPEC C-08). */
  onArchiveChanged: () => Promise<void> | void;
}

export function CharacterSheet({ view, onChanged, onError, onStatus, onLevelUp, onMakeChoices, onArchiveChanged }: Props) {
  const { character, sheet } = view;
  const labels = new Map(sheet.fields.map((f) => [f.field, f.label]));
  const heading = useRef<HTMLHeadingElement>(null);
  const printButton = useRef<HTMLButtonElement>(null);

  // WCAG 2.4.3: opening a sheet (after create, import or picking from the list) moves focus to its heading instead of
  // leaving it on <body>. The sheet is keyed by character, so this runs once per opened character, not on every save.
  useEffect(() => heading.current?.focus(), []);

  const [rollMode, setRollMode] = useState<RollMode>('normal');
  const [lastRoll, setLastRoll] = useState<RollRecord>();
  const [resting, setResting] = useState<RestPeriod>();
  const [printing, setPrinting] = useState(false);
  const [gapAbout, setGapAbout] = useState('');
  const gapText = useRef<HTMLTextAreaElement>(null);

  /** M3 C5: "Report a gap" pre-fills the gap note form and moves focus to its text box. */
  function reportGap(about: string) {
    setGapAbout(about);
    gapText.current?.focus();
  }
  const [pdfSources, setPdfSources] = useState<ReadonlySet<string>>(new Set());

  // ADR-005: which cited sources have an available PDF, so features can offer "Open page".
  const citedSources = [...new Set((sheet.features ?? []).filter((f) => f.origin.page && f.origin.sourceId).map((f) => f.origin.sourceId!))].sort().join(',');
  useEffect(() => {
    if (!citedSources) return;
    let current = true;
    Promise.all(citedSources.split(',').map(async (id) => [id, await client.attachment(id)] as const))
      .then((found) => {
        if (current) setPdfSources(new Set(found.filter(([, a]) => a?.status === 'available' || a?.status === 'changed').map(([id]) => id)));
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [citedSources, onError]);

  async function openPage(sourceId: string, page: number) {
    try {
      const outcome = await client.openPage(sourceId, page);
      onStatus(outcome.warnings.length > 0 ? outcome.warnings.map((w) => w.message).join(' ') : `Opened page ${outcome.page}.`);
    } catch (error) {
      onError(error instanceof TomeStackError && error.code === 'unsupported' ? new Error('Opening a PDF page needs the TomeStack desktop app.') : error);
    }
  }

  async function changeOverride(field: string, change: FieldOverride | undefined) {
    const overrides = [...character.overrides.filter((o) => o.field !== field), ...(change ? [change] : [])];
    try {
      onChanged(await client.saveCharacter({ ...character, overrides }));
    } catch (error) {
      onError(error);
    }
  }

  async function act(action: PlayAction) {
    try {
      onChanged(await client.play(character.id, action));
    } catch (error) {
      onError(error);
    }
  }

  async function roll(target: RollTarget) {
    try {
      setLastRoll(await client.roll(character.id, target));
    } catch (error) {
      onError(error);
    }
  }

  return (
    <article className="panel" aria-labelledby="sheet-heading">
      <header className="sheet-header">
        <h2 id="sheet-heading" tabIndex={-1} ref={heading}>
          {character.name}
        </h2>
        <span className="tag">{character.rulesFamily}</span>
        <span className="tag">Level {character.level}</span>
        {view.campaign && <span className="tag">Campaign: {view.campaign.name}</span>}
        <button type="button" onClick={onLevelUp} disabled={character.level >= 20}>
          Level up
        </button>
        <button type="button" ref={printButton} onClick={() => setPrinting(true)} aria-expanded={printing}>
          Print…
        </button>
      </header>

      {printing && (
        <PrintView
          view={view}
          onError={onError}
          onClose={() => {
            setPrinting(false);
            printButton.current?.focus();
          }}
        />
      )}

      {view.campaign && view.campaign.warnings.length > 0 && (
        <section aria-labelledby="campaign-heading">
          <h3 id="campaign-heading">Campaign: {view.campaign.name}</h3>
          <ul className="warnings">
            {view.campaign.warnings.map((w) => (
              <li key={`${w.code}-${w.content?.revisionId ?? ''}`}>{w.message}</li>
            ))}
          </ul>
        </section>
      )}

      <UpdatesPanel view={view} onChanged={onChanged} onError={onError} onStatus={onStatus} />

      <ExportPanel characterId={character.id} onError={onError} onStatus={onStatus} />

      <ArchivePanel character={character} onError={onError} onStatus={onStatus} onChanged={onArchiveChanged} />

      <HitPointsPanel view={view} act={act} />
      <DeathSavesPanel view={view} act={act} roll={roll} lastRoll={lastRoll} />
      {resting ? (
        <RestPanel
          key={resting}
          characterId={character.id}
          kind={resting}
          hitDice={sheet.hitDice ?? []}
          onError={onError}
          onCancel={() => setResting(undefined)}
          onRested={(rested, applied) => {
            setResting(undefined);
            onChanged(rested);
            onStatus(`${resting === 'shortRest' ? 'Short' : 'Long'} rest finished: ${applied} change${applied === 1 ? '' : 's'} applied.`);
          }}
        />
      ) : (
        <div className="actions">
          <button type="button" onClick={() => setResting('shortRest')}>
            Short rest…
          </button>
          <button type="button" onClick={() => setResting('longRest')}>
            Long rest…
          </button>
        </div>
      )}
      <ConditionsPanel view={view} act={act} />
      <ResourcesPanel view={view} act={act} />
      <EquipmentPanel view={view} onChanged={onChanged} onError={onError} />
      <SpellsPanel
        view={view}
        act={act}
        roll={roll}
        onSave={(changed) =>
          client
            .saveCharacter(changed)
            .then(onChanged)
            .catch(onError)
        }
      />
      <section aria-labelledby="rolls-heading" className="play-panel">
        <h3 id="rolls-heading">Rolls</h3>
        <RollModePicker mode={rollMode} onChange={setRollMode} />
        <p className="hint">Rolling never spends anything. Roll a check, save or skill from its field below, or a feature's roll.</p>
        <RollResult record={lastRoll} resources={sheet.resources ?? []} features={sheet.features ?? []} act={act} />
      </section>
      <ActionsPanel view={view} roll={roll} act={act} />
      <FeaturesPanel view={view} pdfSources={pdfSources} openPage={openPage} reportGap={(id) => reportGap(gapAboutFeature(id))} />
      <GapNotesPanel view={view} onError={onError} onStatus={onStatus} about={gapAbout} onAboutChange={setGapAbout} textRef={gapText} />

      {groups.map((group) => {
        const caster = (sheet.spellcasting ?? []).length > 0;
        // Spell fields of a non-caster are all 0; they are shown only for a caster, or when a value or override exists.
        const fields = sheet.fields.filter((f) => group.match(f.field) && (!isSpellField(f.field) || caster || f.value !== 0 || !!f.override));
        if (fields.length === 0) return null;
        return (
          <section key={group.title} aria-label={group.title} className="field-group">
            <h3>{group.title}</h3>
            {fields.map((field) => (
              <FieldCard
                key={field.field}
                value={field}
                labels={labels}
                onOverride={changeOverride}
                onRoll={(f) => roll({ field: f, mode: rollMode })}
                onReportGap={(f) => reportGap(gapAboutField(f))}
              />
            ))}
          </section>
        );
      })}

      {sheet.choices?.some((c) => !c.resolved) && (
        <section aria-labelledby="choices-heading">
          <h3 id="choices-heading">Choices to make</h3>
          <ul className="warnings">
            {sheet.choices
              .filter((c) => !c.resolved)
              .map((c) => (
                <li key={`${c.source.revisionId}-${c.choiceId}`}>
                  {c.sourceName}: choose {c.count} ({c.selected.length} chosen){c.text ? `. ${c.text}` : ''}
                </li>
              ))}
          </ul>
          <button type="button" onClick={onMakeChoices}>
            Make choices
          </button>
        </section>
      )}

      {sheet.diagnostics.length > 0 && (
        <section aria-labelledby="diagnostics-heading">
          <h3 id="diagnostics-heading">Content not applied</h3>
          <ul className="warnings">
            {sheet.diagnostics.map((d) => (
              <li key={`${d.code}-${d.content?.revisionId ?? ''}-${d.effectId ?? ''}`}>{d.message}</li>
            ))}
          </ul>
        </section>
      )}
    </article>
  );
}
