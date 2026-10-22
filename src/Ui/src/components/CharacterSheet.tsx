import { useCallback, useEffect, useRef, useState, type SubmitEvent } from 'react';
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
  TurnMoment,
} from '../api/types';
import { downloadBase64 } from '../files';
import { ArchivePanel } from './ArchivePanel';
import { CombatStrip } from './CombatStrip';
import { TurnPanel } from './TurnPanel';
import { SnapshotsPanel } from './SnapshotsPanel';
import { ActionsPanel, ClassColumnsPanel, ConcentrationPanel, ConditionsPanel, DeathSavesPanel, FeaturesPanel, HitPointsPanel, ResourcesPanel } from './PlayPanels';
import { CompanionsPanel } from './CompanionsPanel';
import { CustomAttacksPanel, CustomItemsPanel } from './CustomEntries';
import { CurrencyPanel, EquipmentPanel } from './EquipmentPanel';
import { SessionNotesPanel } from './SessionNotesPanel';
import { EntryGone, useSheetSave } from './sheetSave';
import { DefencesPanel } from './DefencesPanel';
import { DetailsPanel } from './DetailsPanel';
import { ExperiencePanel } from './ExperiencePanel';
import { GapNotesPanel, gapAboutFeature, gapAboutField } from './GapNotesPanel';
import { PrintView } from './PrintView';
import { RestPanel } from './RestPanel';
import { logged, rollLogLimit, type LoggedRoll } from './RollLog';
import { SheetSummary } from './SheetSummary';
import { SpellsPanel } from './SpellsPanel';
import { TraceTable } from './TraceTable';
import { UpdatesPanel } from './UpdatesPanel';
import { VttExportPanel } from './VttExportPanel';
import { TabList, TabPanel, tabId, type TabSpec } from './sheet/TabList';
import { rememberSheetTab, rememberedSheetTab, type SheetTabId } from '../sheetTab';
import { inverseOf, isCompound, type UndoEntry } from '../undo';
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

export function CharacterSheet({ view, onChanged, onError: reportError, onStatus, onLevelUp, onMakeChoices, onArchiveChanged, initialTab }: Props) {
  const { character, sheet } = view;
  // D43: `toggle.group-absent` is information (the toggle works as a group of one), not content that failed to apply.
  const notApplied = sheet.diagnostics.filter((d) => d.code !== 'toggle.group-absent');
  // R161/R182: one handler for every save path. A save refused as stale (the character changed since this sheet's copy was read)
  // shows its words in the usual alert and brings in the current copy, so the next edit starts from it. Focus is not touched: whatever
  // the user was on stays, and what they typed stays in its field for them to apply again.
  // R132: every sheet save goes through this one queue, each as an updater applied to the latest saved character.
  // R188: play actions, Undo and a rest finish run in the same queue (`run`), so a save never starts from a stamp they replaced.
  const { save: saveCharacter, adopt, run: runQueued } = useSheetSave(view);
  const characterId = character.id;
  // The wrapper's identity must not change when the app passes a new inline `onChanged`: panels list `onError` in their effect
  // dependencies, so a new identity on every render would refetch (and loop on a failing fetch). It reads the props through a ref.
  const latestProps = useRef({ reportError, onChanged, adopt });
  useEffect(() => {
    latestProps.current = { reportError, onChanged, adopt };
  });
  const onError = useCallback(
    (error: unknown) => {
      latestProps.current.reportError(error);
      // D44: a refused service undo (the slot is gone or was written for an earlier stamp) reloads the stored copy too, so its label drops.
      // D43 S3: a turn confirmed against a preview the character has since left (`turn.preview-stale`) reloads the stored copy too.
      if (error instanceof TomeStackError && ['character.stale', 'undo.stale', 'undo.nothing', 'turn.preview-stale'].includes(error.code)) {
        client
          .getCharacter(characterId)
          .then((fresh) => {
            latestProps.current.adopt(fresh); // unconditionally: the stored copy is the truth, whatever its stamp sorts like
            return latestProps.current.onChanged(fresh);
          })
          .catch((failure: unknown) => latestProps.current.reportError(failure));
      }
    },
    [characterId],
  );
  const labels = new Map(sheet.fields.map((f) => [f.field, f.label]));
  const heading = useRef<HTMLHeadingElement>(null);
  const printButton = useRef<HTMLButtonElement>(null);
  const shortRestButton = useRef<HTMLButtonElement>(null);
  const longRestButton = useRef<HTMLButtonElement>(null);

  // WCAG 2.4.3: opening a sheet (after create, import or picking from the list) moves focus to its heading instead of
  // leaving it on <body>. The sheet is keyed by character, so this runs once per opened character, not on every save.
  useEffect(() => heading.current?.focus(), []);

  const [rollMode, setRollMode] = useState<RollMode>('normal');
  const [lastRoll, setLastRoll] = useState<RollRecord>();
  // D28: the previous rolls of this session. The sheet is keyed by character id, so a switch remounts and clears both.
  const [rollLog, setRollLog] = useState<LoggedRoll[]>([]);
  const lastLogged = useRef<LoggedRoll | undefined>(undefined);
  const [resting, setResting] = useState<RestPeriod>();
  const [compact, setCompact] = useState(compactPlay());
  const [printing, setPrinting] = useState(false);
  // D43 S3: the open turn panel (start or end of the turn) and the opener to return focus to when it closes (WCAG 2.4.3).
  const [turning, setTurning] = useState<TurnMoment>();
  const turnOpener = useRef<TurnMoment>(undefined);
  const encounterRunning = character.play?.encounter !== undefined;
  // The encounter ended (here or by a long rest): a turn needs one, so the panel closes (adjusting state during render, guarded).
  if (turning && !encounterRunning) setTurning(undefined);
  useEffect(() => {
    if (turning || !turnOpener.current) return;
    (document.getElementById(`turn-open-${turnOpener.current}`) ?? document.getElementById('combat-heading'))?.focus();
    turnOpener.current = undefined;
  }, [turning]);
  const [undo, setUndo] = useState<UndoEntry>();
  // Wave 3 item 21: the Hit points damage type; a weapon's damage roll pre-fills it. '' is untyped.
  const [damageType, setDamageType] = useState('');
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
  // Item 17: Stats lists languages and tools read-only; its link opens Notes and focuses the Details heading. A counter, like
  // the gap focus, so a second press moves focus again.
  const [detailsFocus, setDetailsFocus] = useState(0);
  const detailsHeading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (detailsFocus > 0) detailsHeading.current?.focus();
  }, [detailsFocus]);
  function editDetails() {
    setTab('notes');
    setDetailsFocus((n) => n + 1);
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
      .catch((error) => {
        if (current) onError(error);
      });
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
    try {
      onChanged(
        await saveCharacter((latest) => ({
          ...latest,
          overrides: [...latest.overrides.filter((o) => o.field !== field), ...(change ? [change] : [])],
        })),
      );
    } catch (error) {
      onError(error);
    }
  }

  // D23: an entry is offered only for a change whose "before" is the state it actually changed. The shell runs commands one at
  // a time and answers them in order, so quick repeated presses all apply. A change gets no entry when it overlapped another
  // play change or an Undo (started after it, or still running), or when another view (a rest, a restore, a save) was shown
  // while it was in flight: its "before" is then not what the service changed.
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  const started = useRef(0);
  const running = useRef(0);
  const [inFlight, setInFlight] = useState(false); // `running > 0`, for the render: the service's Undo waits for in-flight changes
  const shownView = useRef(view);
  useEffect(() => {
    shownView.current = view;
  }, [view]);
  /** D43 S3: what the reply noticed (an encounter's durations to track by hand, a pool that filled) goes to the sheet's one status line. */
  function say(reply: CharacterView) {
    const message = reply.notices?.map((n) => n.message).join(' ');
    if (message && mounted.current) onStatus(message);
  }
  async function act(action: PlayAction) {
    setUndo(undefined); // an offered Undo is withdrawn at once, so it cannot run in between this change and its reply
    const mine = ++started.current;
    const startedWhileRunning = running.current > 0;
    running.current += 1;
    setInFlight(true);
    try {
      // D44: a compound change names the stamp of the character as the queue holds it when the change starts (after every earlier
      // save or play change finished), not the one this render captured.
      // D43 S3: starting or ending an encounter is decided from what the player saw (like Undo), so it names the stamp of the shown
      // view: a change in between is refused `character.stale` and the sheet reloads, instead of the encounter being applied to it.
      const shownStamp = view.character.updatedAt;
      const encounter = action.action === 'startEncounter' || action.action === 'endEncounter';
      const after = await runQueued(
        (latest) =>
          client.play(character.id, encounter ? { ...action, expectedUpdatedAt: shownStamp } : isCompound(action) ? { ...action, expectedUpdatedAt: latest.updatedAt } : action),
        (reply) => reply.character,
      );
      const overlapped = startedWhileRunning || started.current !== mine || running.current > 1 || shownView.current !== view;
      setUndo(overlapped ? undefined : inverseOf(action, view, after)); // D23: one level, session-only
      onChanged(after);
      say(after);
    } catch (error) {
      onError(error);
    } finally {
      running.current -= 1;
      setInFlight(running.current > 0);
    }
  }

  // D23: the entry is offered only while the sheet still shows the state its change produced. A rest, a save, a level-up
  // or a reload shows another view, and undoing then would overwrite that change.
  // D44: while the service offers an undo of a compound change (`undoLabel`), that is the one Undo; a client entry from before the
  // switch is never offered beside it.
  const serverUndo = view.undoLabel;
  const undoable = !serverUndo && undo && undo.after === view ? undo : undefined;

  /** D44: the service puts the play state back to what the last form switch started from; the reply is adopted like a play result. */
  async function undoServerChange() {
    if (!serverUndo || running.current > 0) return; // nothing is on offer, or a change is in flight (the button is disabled meanwhile)
    setUndo(undefined);
    // The stamp of the view that showed the label, not the queue's latest: the service then undoes exactly the change the label names,
    // or refuses (character.stale) and the sheet reloads.
    const shownStamp = view.character.updatedAt ?? '';
    started.current += 1; // an Undo in flight counts as a running change, as for the client undo
    running.current += 1;
    setInFlight(true);
    try {
      const after = await runQueued(() => client.undoPlay(characterId, shownStamp), (reply) => reply.character);
      onChanged(after);
    } catch (error) {
      onError(error); // character.stale, undo.stale and undo.nothing show the service's words and reload the stored copy
    } finally {
      running.current -= 1;
      setInFlight(running.current > 0);
    }
    compactToggle.current?.focus(); // the button changes under the cursor; focus stays in the Play tools (WCAG 2.4.3)
  }

  async function undoLast() {
    if (!undoable) return;
    const steps = undoable.inverse;
    setUndo(undefined);
    let current: CharacterView | undefined;
    // WCAG 2.4.3: the button is disabled now, so focus goes to what changed (hit points, concentration) or the Play tools.
    const refocus = () => {
      const concentrationOnly = steps.every((s) => s.action === 'endConcentration' || s.action === 'startConcentration');
      const target =
        steps[0]?.action === 'companionSetHitPoints'
          ? document.getElementById(`companion-${steps[0].companionId}-heading`) // Wave 3 item 23: the companion's card
          : steps[0]?.action === 'setHitPoints' || steps[0]?.action === 'setTemporaryHitPoints'
            ? document.getElementById('hp-heading')
            : concentrationOnly && current?.character.play?.concentration
              ? document.getElementById('concentration-heading')
              : null;
      (target ?? compactToggle.current)?.focus();
    };
    // An Undo in flight counts as a running change, so a play change started meanwhile gets no entry.
    started.current += 1;
    running.current += 1;
    setInFlight(true);
    try {
      // One queue entry for all the steps, so no save slips in between them. A step that fails after another succeeded still
      // leaves that step's stamp as the latest.
      await runQueued(
        async () => {
          try {
            for (const step of steps) current = await client.play(character.id, step);
          } catch (error) {
            if (current) adopt(current);
            throw error;
          }
          return current;
        },
        (reply) => reply?.character,
      );
      if (current) onChanged(current);
    } catch (error) {
      // A step that succeeded before the failure has changed the character: show it, so the sheet is not stale.
      if (current) onChanged(current);
      onError(error);
    } finally {
      running.current -= 1;
      setInFlight(running.current > 0);
    }
    refocus();
  }

  /** A roll shown in Last roll; the one it replaces moves into the log, in `at` order (newest first), so a hit die logged since stays above it. */
  function showRoll(record: RollRecord) {
    const replaced = lastLogged.current;
    if (replaced) {
      setRollLog((log) => {
        const at = log.findIndex((entry) => entry.at < replaced.at);
        const next = at < 0 ? [...log, replaced] : [...log.slice(0, at), replaced, ...log.slice(at)];
        return next.slice(0, rollLogLimit);
      });
    }
    lastLogged.current = logged(record);
    setLastRoll(record);
  }

  /** A roll the strip does not show (hit dice in a rest, dice-engine.md "Display"): logged only. */
  function logRoll(record: RollRecord) {
    const entry = logged(record);
    setRollLog((log) => [entry, ...log].slice(0, rollLogLimit));
  }

  async function roll(target: RollTarget) {
    try {
      showRoll(await client.roll(character.id, target));
    } catch (error) {
      onError(error);
    }
  }

  /** Typed dice from the summary (item 8). The label is fixed, never the typed text; a refusal goes back to the box, not the sheet banner. */
  async function rollDice(formula: string) {
    showRoll(await client.rollDice(formula, undefined, 'Dice'));
  }

  /** The rest that is open right now, readable from a callback that outlives its render (a finish still in flight). */
  const openRest = useRef<RestPeriod>(undefined);
  function showRest(kind: RestPeriod | undefined) {
    openRest.current = kind;
    setResting(kind);
  }

  /** The opener button toggles its own panel; the other one switches kind. Focus stays on the pressed button. */
  function toggleRest(kind: RestPeriod) {
    showRest(openRest.current === kind ? undefined : kind);
  }

  /**
   * D29: closing a rest returns focus to the button that opened it (WCAG 2.4.3), but only when `kind` is still the open
   * one. A finish that lands after the player cancelled or switched kind must not close the newer panel or take focus.
   */
  function closeRest(kind: RestPeriod) {
    if (openRest.current !== kind) return;
    showRest(undefined);
    (kind === 'shortRest' ? shortRestButton : longRestButton).current?.focus();
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

  // Item 17: a read-only list of the details' languages or tools; nothing when it is empty.
  const detailList = (id: string, title: string, entries: readonly string[] = []) =>
    entries.length === 0 ? null : (
      <div>
        <h4 id={`${id}-heading`}>{title}</h4>
        <ul aria-labelledby={`${id}-heading`}>
          {entries.map((entry, index) => (
            <li key={`${index}-${entry}`}>{entry}</li>
          ))}
        </ul>
      </div>
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
        {/* D29 (owner, 2026-10-07): rests at the top of the sheet, not in Play. */}
        <button type="button" ref={shortRestButton} onClick={() => toggleRest('shortRest')} aria-expanded={resting === 'shortRest'} aria-controls={resting ? 'rest-panel' : undefined}>
          Short rest…
        </button>
        <button type="button" ref={longRestButton} onClick={() => toggleRest('longRest')} aria-expanded={resting === 'longRest'} aria-controls={resting ? 'rest-panel' : undefined}>
          Long rest…
        </button>
        {/* aria-disabled with a visible reason, not native disabled: a disabled button leaves the tab order and says nothing (3.3.2, 4.1.2). */}
        <button
          type="button"
          onClick={() => {
            if (character.level < 20) onLevelUp();
          }}
          aria-disabled={character.level >= 20 || undefined}
          aria-describedby={character.level >= 20 ? 'level-up-hint' : undefined}
        >
          Level up
        </button>
        {character.level >= 20 && (
          <span id="level-up-hint" className="hint">
            Already at level 20, the highest level.
          </span>
        )}
        <button type="button" ref={printButton} onClick={() => setPrinting((open) => !open)} aria-expanded={printing} aria-controls={printing ? 'print-preview' : undefined}>
          Print…
        </button>
      </header>

      <SheetSummary view={view} rollMode={rollMode} onRollMode={setRollMode} lastRoll={lastRoll} rollLog={rollLog} act={act} spellsTab={showSpells} onRoll={(f) => roll({ field: f, mode: rollMode })} onRollDice={rollDice} />

      {resting && (
        <div className="rest-sheet" id="rest-panel">
          <RestPanel
            key={resting}
            characterId={character.id}
            kind={resting}
            hitDice={sheet.hitDice ?? []}
            version={view}
            onError={onError}
            run={(command) => runQueued(command, (reply) => reply.character)}
            onRoll={logRoll}
            onCancel={() => closeRest(resting)}
            onRested={(rested, applied) => {
              const kind = resting;
              closeRest(kind);
              onChanged(rested);
              // A reply that lands after this sheet is gone (another character opened, Level up pressed) says nothing: its
              // rest is saved, but the status would describe a screen the user has left.
              if (!mounted.current) return;
              onStatus(`${kind === 'shortRest' ? 'Short' : 'Long'} rest finished: ${applied} change${applied === 1 ? '' : 's'} applied.`);
            }}
          />
        </div>
      )}

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

      {notApplied.length > 0 && (
        <section aria-labelledby="diagnostics-heading">
          <h3 id="diagnostics-heading">Content not applied</h3>
          <ul className="warnings">
            {notApplied.map((d) => (
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
          {serverUndo ? (
            <button type="button" disabled={inFlight} onClick={undoServerChange}>
              Undo {serverUndo}
            </button>
          ) : (
            <button type="button" disabled={!undoable} onClick={undoLast}>
              Undo last change{undoable ? `: ${undoable.label}` : ''}
            </button>
          )}
        </div>
        {/* D43 slice 3: forms, the round, encounter and turn buttons, durations and pools; the turn panel opens under it. */}
        <CombatStrip
          view={view}
          act={act}
          busy={inFlight}
          onTurn={(at) => {
            turnOpener.current = at;
            setTurning(at);
          }}
        />
        {turning && encounterRunning && (
          <TurnPanel
            key={turning}
            characterId={character.id}
            at={turning}
            version={view}
            toggles={sheet.toggles}
            onError={onError}
            run={(command) => runQueued(command, (reply) => reply.character)}
            onCancel={() => setTurning(undefined)}
            onDone={(done) => {
              setUndo(undefined); // the service holds the one undo of a turn (`undoLabel`); a client entry from before it is stale
              setTurning(undefined);
              onChanged(done);
              say(done);
            }}
          />
        )}
        {/* Investigation 2026-10-06 item 8: two plain wrappers, side by side at 60rem and up, stacked below it. Visual order is DOM order. */}
        <div className="play-column">
          <HitPointsPanel view={view} act={act} damageType={damageType} onDamageType={setDamageType} />
          <DeathSavesPanel view={view} act={act} roll={roll} lastRoll={lastRoll} />
          <ConcentrationPanel view={view} act={act} roll={roll} />
          <ConditionsPanel view={view} act={act} />
          <ResourcesPanel view={view} act={act} />
          <ClassColumnsPanel view={view} />
        </div>
        <div className="play-column">
          <ActionsPanel view={view} roll={roll} act={act} rollMode={rollMode} onDamageType={setDamageType} />
          <CompanionsPanel view={view} act={act} roll={roll} rollMode={rollMode} onChanged={onChanged} onError={onError} onStatus={onStatus} saveCharacter={saveCharacter} />
        </div>
      </TabPanel>

      {showSpells && (
        <TabPanel idPrefix="sheet" id="spells" active={active === 'spells'}>
          <SpellsPanel
            view={view}
            act={act}
            roll={roll}
            rollMode={rollMode}
            onSave={(update) =>
              saveCharacter(update)
                .then(onChanged)
                .catch((error) => (error instanceof EntryGone ? onStatus(error.message) : onError(error)))
            }
          />
          {fieldGroup('Spellcasting', spellFields)}
        </TabPanel>
      )}

      <TabPanel idPrefix="sheet" id="inventory" active={active === 'inventory'}>
        <EquipmentPanel view={view} onChanged={onChanged} onError={onError} onStatus={onStatus} saveCharacter={saveCharacter} act={act} />
        <CustomItemsPanel view={view} onChanged={onChanged} onError={onError} onStatus={onStatus} saveCharacter={saveCharacter} />
        <CustomAttacksPanel view={view} onChanged={onChanged} onError={onError} onStatus={onStatus} saveCharacter={saveCharacter} />
        <CurrencyPanel view={view} onChanged={onChanged} onError={onError} saveCharacter={saveCharacter} />
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
        <DefencesPanel view={view} onChanged={onChanged} onError={onError} saveCharacter={saveCharacter} />
        <ExperiencePanel view={view} onChanged={onChanged} onError={onError} saveCharacter={saveCharacter} />
        {((character.details?.languages ?? []).length > 0 || (character.details?.toolsAndOther ?? []).length > 0) && (
          <section aria-labelledby="stats-details-heading" className="field-group">
            <h3 id="stats-details-heading">Languages and tools</h3>
            {detailList('stats-languages', 'Languages', character.details?.languages)}
            {detailList('stats-tools', 'Tools and other', character.details?.toolsAndOther)}
            <button type="button" onClick={editDetails}>
              Edit in Notes → Details
            </button>
          </section>
        )}
      </TabPanel>

      <TabPanel idPrefix="sheet" id="notes" active={active === 'notes'}>
        <SessionNotesPanel view={view} onChanged={onChanged} onError={onError} onStatus={onStatus} saveCharacter={saveCharacter} />
        <GapNotesPanel view={view} onError={onError} onStatus={onStatus} about={gapAbout} onAboutChange={setGapAbout} textRef={gapText} />
        <DetailsPanel view={view} onChanged={onChanged} onError={onError} onStatus={onStatus} headingRef={detailsHeading} shown={active === 'notes'} saveCharacter={saveCharacter} />
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
