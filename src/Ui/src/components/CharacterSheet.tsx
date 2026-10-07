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
import { SnapshotsPanel } from './SnapshotsPanel';
import { ActionsPanel, ClassColumnsPanel, ConcentrationPanel, ConditionsPanel, DeathSavesPanel, FeaturesPanel, HitPointsPanel, ResourcesPanel } from './PlayPanels';
import { CurrencyPanel, EquipmentPanel } from './EquipmentPanel';
import { SessionNotesPanel } from './SessionNotesPanel';
import { GapNotesPanel, gapAboutFeature, gapAboutField } from './GapNotesPanel';
import { PrintView } from './PrintView';
import { RestPanel } from './RestPanel';
import { SheetSummary } from './SheetSummary';
import { SpellsPanel } from './SpellsPanel';
import { TraceTable } from './TraceTable';
import { UpdatesPanel } from './UpdatesPanel';
import { VttExportPanel } from './VttExportPanel';
import { TabList, TabPanel, tabId, type TabSpec } from './sheet/TabList';
import { rememberSheetTab, rememberedSheetTab, type SheetTabId } from '../sheetTab';
import { inverseOf, type UndoEntry } from '../undo';
import { applyPreferences, compactPlay, setCompactPlay } from '../settings';

const signed = (n: number) => (n >= 0 ? `+${n}` : `${n}`);
const display = (value: DerivedValue, n: number) =>
  value.units === 'modifier' || value.units === 'bonus' ? signed(n) : value.units === 'feet' ? `${n} ft.` : `${n}`;

/** Display groups for the calculated fields on the Stats tab; the rules core decides what exists, this only orders it. */
const statGroups: { title: string; match: (field: string) => boolean }[] = [
  { title: 'Abilities', match: (f) => f.startsWith('ability.') },
  { title: 'Proficiency', match: (f) => f === 'proficiencyBonus' },
  { title: 'Saving throws', match: (f) => f.startsWith('save.') },
  { title: 'Skills', match: (f) => f.startsWith('skill.') || f.startsWith('passive.') },
  { title: 'Combat', match: (f) => f === 'initiative' || f === 'speed' || f === 'armorClass' || f === 'hitPoints' || f === 'attacks' || f === 'criticalRange' },
];
// D04: the caster numbers, with traces and overrides, on the Spells tab (ADR-014). Slots combine on the multiclass table
// (M3 C3); an override is the manual step only for class revisions that do not say how they combine.
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
  const marked = Boolean(value.mark && value.mark !== 'none');

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const n = Number(overrideValue);
    if (!Number.isInteger(n)) return;
    onOverride(value.field, { field: value.field, value: n, reason: overrideReason || undefined });
    setOverrideValue('');
    setOverrideReason('');
  }

  return (
    <section aria-labelledby={headingId} className={`field-card${marked ? ' marked' : ''}`}>
      <details>
        <summary>
          <h4 id={headingId}>
            {value.label}: <span className="derived">{display(value, value.value)}</span>
            {marked && (
              <>
                {' '}
                <span className="mark">· {value.mark}</span>
              </>
            )}
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
  /** The tab to open on (a deep link, for example from the Gap notes screen); Play when absent (ADR-014). */
  initialTab?: SheetTabId;
}

export function CharacterSheet({ view, onChanged, onError, onStatus, onLevelUp, onMakeChoices, onArchiveChanged, initialTab }: Props) {
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
  const [compact, setCompact] = useState(compactPlay());
  const [printing, setPrinting] = useState(false);
  const [undo, setUndo] = useState<UndoEntry>();
  const compactToggle = useRef<HTMLInputElement>(null);
  const [gapAbout, setGapAbout] = useState('');
  const gapText = useRef<HTMLTextAreaElement>(null);

  // ADR-014: the tabs. Spells is offered only when there is something to show (a caster, or a spell field with a value
  // or override). A requested or remembered tab that is not offered falls back to Play.
  const caster = (sheet.spellcasting ?? []).length > 0;
  const showsSpellField = (f: DerivedValue) => caster || f.value !== 0 || !!f.override;
  const spellFields = sheet.fields.filter((f) => isSpellField(f.field) && showsSpellField(f));
  const showSpells = caster || spellFields.length > 0;
  const tabs: readonly TabSpec<SheetTabId>[] = [
    { id: 'play', label: 'Play' },
    ...(showSpells ? ([{ id: 'spells', label: 'Spells' }] as const) : []),
    { id: 'inventory', label: 'Inventory' },
    { id: 'features', label: 'Features' },
    { id: 'stats', label: 'Stats' },
    { id: 'notes', label: 'Notes' },
    { id: 'manage', label: 'Manage' },
  ];
  // The sheet is keyed by character, so this runs once per opened character. A deep link wins over the memory.
  const [tab, setTabState] = useState<SheetTabId>(() => initialTab ?? rememberedSheetTab(character.id) ?? 'play');
  function setTab(next: SheetTabId) {
    setTabState(next);
    rememberSheetTab(character.id, next);
  }
  const active: SheetTabId = tabs.some((t) => t.id === tab) ? tab : 'play';
  // A tab that is no longer offered becomes Play (React's adjust-state-while-rendering). The stored memory is not touched:
  // it only changes when the user picks a tab, so a remembered Spells comes back if the character becomes a caster again.
  if (tab !== active) setTabState('play');

  // The open tab stopping being offered (the last spell override was removed on a non-caster) unmounts its panel with focus
  // inside, which would drop focus to <body> (WCAG 2.4.3): put it on the Play tab. Focus is never moved otherwise, so a
  // remembered tab that is not offered falls back silently while the heading holds focus on mount.
  // It goes to the selected tab (Play when the open tab vanished). `active` is read through a ref so only showSpells triggers it.
  const activeRef = useRef(active);
  useEffect(() => {
    activeRef.current = active;
  });
  useEffect(() => {
    const focused = document.activeElement;
    if (!focused || focused === document.body) document.getElementById(tabId('sheet', activeRef.current))?.focus();
  }, [showSpells]);

  // M3 C5: "Report a gap" pre-fills the gap note form, opens Notes and moves focus to its text box. A counter, not a flag:
  // setting the tab to the value it already has causes no render, so the focus must not depend on a tab change.
  const [gapFocus, setGapFocus] = useState(0);
  function reportGap(about: string) {
    setGapAbout(about);
    setTab('notes');
    setGapFocus((n) => n + 1);
  }
  useEffect(() => {
    if (gapFocus > 0) gapText.current?.focus();
  }, [gapFocus]);
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

  // D23: an entry is offered only for a change whose "before" is the state it actually changed. The shell runs commands one at
  // a time and answers them in order, so quick repeated presses all apply. A change gets no entry when it overlapped another
  // play change or an Undo (started after it, or still running), or when another view (a rest, a restore, a save) was shown
  // while it was in flight: its "before" is then not what the service changed.
  const started = useRef(0);
  const running = useRef(0);
  const shownView = useRef(view);
  useEffect(() => {
    shownView.current = view;
  }, [view]);
  async function act(action: PlayAction) {
    setUndo(undefined); // an offered Undo is withdrawn at once, so it cannot run in between this change and its reply
    const mine = ++started.current;
    const startedWhileRunning = running.current > 0;
    running.current += 1;
    try {
      const after = await client.play(character.id, action);
      const overlapped = startedWhileRunning || started.current !== mine || running.current > 1 || shownView.current !== view;
      setUndo(overlapped ? undefined : inverseOf(action, view, after)); // D23: one level, session-only
      onChanged(after);
    } catch (error) {
      onError(error);
    } finally {
      running.current -= 1;
    }
  }

  // D23: the entry is offered only while the sheet still shows the state its change produced. A rest, a save, a level-up
  // or a reload shows another view, and undoing then would overwrite that change.
  const undoable = undo && undo.after === view ? undo : undefined;

  async function undoLast() {
    if (!undoable) return;
    const steps = undoable.inverse;
    setUndo(undefined);
    let current: CharacterView | undefined;
    // WCAG 2.4.3: the button is disabled now, so focus goes to what changed (hit points, concentration) or the Play tools.
    const refocus = () => {
      const concentrationOnly = steps.every((s) => s.action === 'endConcentration' || s.action === 'startConcentration');
      const target =
        steps[0]?.action === 'setHitPoints' || steps[0]?.action === 'setTemporaryHitPoints'
          ? document.getElementById('hp-heading')
          : concentrationOnly && current?.character.play?.concentration
            ? document.getElementById('concentration-heading')
            : null;
      (target ?? compactToggle.current)?.focus();
    };
    // An Undo in flight counts as a running change, so a play change started meanwhile gets no entry.
    started.current += 1;
    running.current += 1;
    try {
      for (const step of steps) current = await client.play(character.id, step);
      if (current) onChanged(current);
    } catch (error) {
      // A step that succeeded before the failure has changed the character: show it, so the sheet is not stale.
      if (current) onChanged(current);
      onError(error);
    } finally {
      running.current -= 1;
    }
    refocus();
  }

  async function roll(target: RollTarget) {
    try {
      setLastRoll(await client.roll(character.id, target));
    } catch (error) {
      onError(error);
    }
  }

  const fieldGroup = (title: string, fields: DerivedValue[]) =>
    fields.length === 0 ? null : (
      <section key={title} aria-label={title} className="field-group">
        <h3>{title}</h3>
        {fields.map((field) => (
          <FieldCard key={field.field} value={field} labels={labels} onOverride={changeOverride} onRoll={(f) => roll({ field: f, mode: rollMode })} onReportGap={(f) => reportGap(gapAboutField(f))} />
        ))}
      </section>
    );

  return (
    <article className="panel sheet" aria-labelledby="sheet-heading">
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
        <button type="button" ref={printButton} onClick={() => setPrinting(true)} aria-expanded={printing} aria-controls="print-preview">
          Print…
        </button>
      </header>

      <SheetSummary view={view} rollMode={rollMode} onRollMode={setRollMode} lastRoll={lastRoll} act={act} spellsTab={showSpells} onRoll={(f) => roll({ field: f, mode: rollMode })} />

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
      <div className="sheet-body">

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

      {/* Open choices and content problems stay visible whatever tab is open (SPEC C-03). */}
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

      <TabList label="Sheet sections" idPrefix="sheet" tabs={tabs} active={active} onActivate={setTab} />

      <TabPanel idPrefix="sheet" id="play" active={active === 'play'}>
        {/* D20 (owner, 2026-10-06): a second layout of the Play tab for combat; CSS hides traces, hints and class columns. */}
        <div className="play-tools">
          <label className="choice compact-toggle">
            <input
              type="checkbox"
              ref={compactToggle}
              checked={compact}
              onChange={(e) => {
                setCompactPlay(e.target.checked);
                setCompact(e.target.checked);
                applyPreferences();
              }}
            />{' '}
            Compact view: hit points, attacks, conditions and resources only
          </label>
          <button type="button" disabled={!undoable} onClick={undoLast}>
            Undo last change{undoable ? `: ${undoable.label}` : ''}
          </button>
        </div>
        {/* Investigation 2026-10-06 item 8: two plain wrappers, side by side at 60rem and up, stacked below it. Visual order is DOM order. */}
        <div className="play-column">
          <HitPointsPanel view={view} act={act} />
          <DeathSavesPanel view={view} act={act} roll={roll} lastRoll={lastRoll} />
          <ConcentrationPanel view={view} act={act} roll={roll} />
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
          <ClassColumnsPanel view={view} />
        </div>
        <div className="play-column">
          <ActionsPanel view={view} roll={roll} act={act} />
        </div>
      </TabPanel>

      {showSpells && (
        <TabPanel idPrefix="sheet" id="spells" active={active === 'spells'}>
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
          {fieldGroup('Spellcasting', spellFields)}
        </TabPanel>
      )}

      <TabPanel idPrefix="sheet" id="inventory" active={active === 'inventory'}>
        <EquipmentPanel view={view} onChanged={onChanged} onError={onError} />
        <CurrencyPanel view={view} onChanged={onChanged} onError={onError} />
      </TabPanel>

      <TabPanel idPrefix="sheet" id="features" active={active === 'features'}>
        {(sheet.features ?? []).length === 0 && <p className="hint">No features yet.</p>}
        <FeaturesPanel view={view} pdfSources={pdfSources} openPage={openPage} reportGap={(id) => reportGap(gapAboutFeature(id))} />
      </TabPanel>

      <TabPanel idPrefix="sheet" id="stats" active={active === 'stats'}>
        {statGroups.map((group) =>
          fieldGroup(
            group.title,
            sheet.fields.filter((f) => group.match(f.field)),
          ),
        )}
      </TabPanel>

      <TabPanel idPrefix="sheet" id="notes" active={active === 'notes'}>
        <SessionNotesPanel view={view} onChanged={onChanged} onError={onError} onStatus={onStatus} />
        <GapNotesPanel view={view} onError={onError} onStatus={onStatus} about={gapAbout} onAboutChange={setGapAbout} textRef={gapText} />
      </TabPanel>

      <TabPanel idPrefix="sheet" id="manage" active={active === 'manage'}>
        <UpdatesPanel view={view} onChanged={onChanged} onError={onError} onStatus={onStatus} />
        <ExportPanel characterId={character.id} onError={onError} onStatus={onStatus} />
        <VttExportPanel characterId={character.id} onError={onError} onStatus={onStatus} />
        <ArchivePanel character={character} onError={onError} onStatus={onStatus} onChanged={onArchiveChanged} />
        <SnapshotsPanel character={character} onChanged={onChanged} onError={onError} onStatus={onStatus} />
      </TabPanel>
      </div>
    </article>
  );
}
