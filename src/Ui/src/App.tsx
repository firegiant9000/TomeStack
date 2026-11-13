import { lazy, Suspense, useCallback, useEffect, useRef, useState, type ChangeEvent } from 'react';
import { client } from './api/client';
import { TomeStackError } from './api/transport';
import type { AppInfo, CharacterSummary, CharacterView, PackagePreview } from './api/types';
import { AddCharacterPanel } from './components/AddCharacterPanel';
import { AllGapNotesPanel } from './components/AllGapNotesPanel';
import { SettingsPanel } from './components/SettingsPanel';
import type { BuilderMode } from './components/CharacterBuilder';
import { CharacterSheet } from './components/CharacterSheet';
import { HomePanel } from './components/HomePanel';
import { companionPrunedText } from './companionNotice';
import { readFileAsBase64 } from './files';
import { isTypingTarget } from './keys';
import { diffLevelUp, dismissChecklist, writeChecklist } from './levelUpChecklist';
import { windowTitle } from './placeTitle';
import { noticeText } from './replaceable';
import { RulesLabelProvider, useRulesLabel } from './rulesLabel';
import { applyPreferences, setSidebarCollapsed, sidebarCollapsed } from './settings';
import type { SheetTabId } from './sheetTab';

// Code-split (R161 d): the screens that are not the first paint load when they are opened, from the same dist folder
// (relative asset paths, no network, no inline script). The home list, the sheet and settings stay in the main chunk.
import { ScreenBoundary } from './components/ScreenBoundary';

const BackupsPanel = lazy(() => import('./components/BackupsPanel').then((m) => ({ default: m.BackupsPanel })));
const CampaignsPanel = lazy(() => import('./components/CampaignsPanel').then((m) => ({ default: m.CampaignsPanel })));
const CharacterBuilder = lazy(() => import('./components/CharacterBuilder').then((m) => ({ default: m.CharacterBuilder })));
const DdbImportPanel = lazy(() => import('./components/ddb/DdbImportPanel').then((m) => ({ default: m.DdbImportPanel })));
const HomebrewStudio = lazy(() => import('./components/HomebrewStudio').then((m) => ({ default: m.HomebrewStudio })));
const ImportPreview = lazy(() => import('./components/ImportPreview').then((m) => ({ default: m.ImportPreview })));
const SourcesPanel = lazy(() => import('./components/SourcesPanel').then((m) => ({ default: m.SourcesPanel })));

export type Screen =
  | { kind: 'home'; focus?: boolean } // focus: false only for the start screen (the first Tab must reach the header)
  | { kind: 'builder'; mode: BuilderMode }
  | { kind: 'studio'; sourceId?: string }
  | { kind: 'sources' }
  | { kind: 'campaigns' }
  | { kind: 'gaps' }
  | { kind: 'backups' }
  | { kind: 'add'; start?: 'import' } // start: 'import' opens the chooser on its import choices
  | { kind: 'settings' }
  | { kind: 'ddb-import' }
  | { kind: 'sheet'; view: CharacterView; tab?: SheetTabId; levelledUp?: true }
  | { kind: 'import'; fileName: string; base64: string; preview: PackagePreview };

function describeError(error: unknown): string {
  if (error instanceof TomeStackError) {
    return error.diagnostics.length > 0 ? error.diagnostics.map((d) => d.message).join(' ') : error.message;
  }
  return error instanceof Error ? error.message : String(error);
}

export function App() {
  const [info, setInfo] = useState<AppInfo>();
  return (
    <RulesLabelProvider families={info?.rulesFamilies}>
      <AppContent info={info} setInfo={setInfo} />
    </RulesLabelProvider>
  );
}

function AppContent({ info, setInfo }: { info: AppInfo | undefined; setInfo: (info: AppInfo) => void }) {
  const [characters, setCharacters] = useState<CharacterSummary[]>([]);
  const [screen, setScreen] = useState<Screen>({ kind: 'home', focus: false });
  const [message, setMessage] = useState<{ tone: 'error' | 'status'; text: string }>();
  const fileInput = useRef<HTMLInputElement>(null);
  const [collapsed, setCollapsed] = useState(sidebarCollapsed);
  const sidebar = useRef<HTMLElement>(null);
  const sidebarToggle = useRef<HTMLButtonElement>(null);

  // ADR-015: the sidebar can be hidden. WCAG 2.4.3 / 2.4.11: hiding it while focus is inside moves focus to the toggle.
  // F7: no side effects inside a state updater (StrictMode runs updaters twice); the nav's own `hidden` is the current state.
  const toggleSidebar = useCallback(() => {
    const hiding = !(sidebar.current?.hidden ?? false);
    if (hiding && sidebar.current?.contains(document.activeElement)) sidebarToggle.current?.focus();
    setSidebarCollapsed(hiding);
    setCollapsed(hiding);
  }, []);

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (!event.ctrlKey || event.altKey || event.metaKey || (event.code !== 'KeyB' && event.key.toLowerCase() !== 'b')) return; // physical B too: other layouts give another character
      if (isTypingTarget(event.target)) return; // text entry keeps Ctrl+B
      event.preventDefault();
      if (event.repeat) return; // holding the keys must not flicker the sidebar
      toggleSidebar();
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [toggleSidebar]);

  const onError = useCallback((error: unknown) => setMessage({ tone: 'error', text: describeError(error) }), []);

  const refresh = useCallback(async () => setCharacters(await client.listCharacters()), []);

  useEffect(() => {
    applyPreferences(); // ADR-015 §4: every preference is an html[data-*] attribute the CSS keys on
    Promise.all([client.info().then(setInfo), client.listCharacters().then(setCharacters)]).catch(onError);
  }, [onError, setInfo]);

  // Only the latest open applies: two quick opens can be answered out of order.
  // R40: every "New character" mounts a fresh builder, so a draft that is being saved is never offered again.
  const [builderKey, setBuilderKey] = useState(0);
  function startCreate() {
    setMessage(undefined);
    setBuilderKey((k) => k + 1);
    setScreen({ kind: 'builder', mode: { kind: 'create' } });
  }

  // R37: any change of screen kind also withdraws a pending open, so a slow open never replaces a screen chosen after it.
  const openSeq = useRef({
    n: 0,
    next() {
      this.n += 1;
      return this.n;
    },
  });
  const screenNow = useRef(screen);
  useEffect(() => {
    screenNow.current = screen;
  }, [screen]);
  useEffect(() => {
    openSeq.current.next();
  }, [screen.kind]);
  useEffect(() => {
    document.title = windowTitle(screen); // the shell mirrors this to the window title
  }, [screen]);
  async function open(id: string, tab?: SheetTabId) {
    const request = openSeq.current.next();
    try {
      setMessage(undefined);
      const view = await client.getCharacter(id);
      if (request !== openSeq.current.n) return;
      setScreen({ kind: 'sheet', view, tab });
    } catch (error) {
      if (request === openSeq.current.n) onError(error);
    }
  }

  const active = characters.filter((c) => !c.archivedAt);
  const archived = characters.filter((c) => c.archivedAt);
  const rulesLabel = useRulesLabel();
  const characterLink = (c: CharacterSummary) => (
    <li key={c.id}>
      <button
        type="button"
        className="link"
        aria-current={screen.kind === 'sheet' && screen.view.character.id === c.id ? 'page' : undefined}
        onClick={() => open(c.id)}
      >
        {c.name} <span className="tag">{rulesLabel(c.rulesFamily)}</span>
      </button>
    </li>
  );

  function go(next: Screen) {
    setMessage(undefined);
    setScreen(next);
  }
  const navItem = (label: string, isCurrent: boolean, onClick: () => void, disabled?: boolean) => (
    <li key={label}>
      <button type="button" className="link" disabled={disabled} aria-current={isCurrent ? 'page' : undefined} onClick={onClick}>
        {label}
      </button>
    </li>
  );

  async function chooseImport(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    try {
      const base64 = await readFileAsBase64(file);
      setMessage(undefined);
      setScreen({ kind: 'import', fileName: file.name, base64, preview: await client.previewImport(base64) });
    } catch (error) {
      onError(error);
    }
  }

  return (
    <div className="app" data-sidebar={collapsed ? 'collapsed' : undefined}>
      <header className="app-header">
        <button
          type="button"
          className="sidebar-toggle"
          ref={sidebarToggle}
          aria-expanded={!collapsed}
          aria-controls="sidebar"
          aria-keyshortcuts="Control+B"
          title={collapsed ? 'Show sidebar (Ctrl+B)' : 'Hide sidebar (Ctrl+B)'}
          onClick={toggleSidebar}
        >
          {collapsed ? 'Show sidebar' : 'Hide sidebar'}
        </button>
        {/* Spec 3.1: the window title area is a way home when the sidebar has no "All characters" (fewer than two). */}
        <h1>
          <button type="button" className="brand" aria-label="TomeStack, your characters" onClick={() => go({ kind: 'home', focus: true })}>
            TomeStack
          </button>
        </h1>
        <span className="tag">Offline</span>
      </header>

      {/* Owner (2026-10-12): the header's "Hide sidebar" is the only close control; the sidebar has none of its own. */}
      <nav id="sidebar" className="sidebar" aria-label="Main" ref={sidebar} hidden={collapsed}>
        {/* Always available: the file import needs no rules families; the chooser disables only what does. */}
        <button type="button" className="add-character" aria-current={screen.kind === 'add' ? 'page' : undefined} onClick={() => go({ kind: 'add' })}>
          + Add a character
        </button>
        <h2 id="characters-heading" className="sidebar-heading">
          Characters
        </h2>
        <ul className="character-list" aria-labelledby="characters-heading">
          {characters.length > 1 && navItem('All characters', screen.kind === 'home', () => go({ kind: 'home', focus: true }))}
          {active.map(characterLink)}
          {active.length === 0 && <li className="hint">{archived.length === 0 ? 'No characters yet.' : 'No active characters.'}</li>}
        </ul>
        {/* SPEC C-08: archived characters are kept, listed apart and collapsed. */}
        {archived.length > 0 && (
          <details className="archived-characters">
            <summary>Archived ({archived.length})</summary>
            <ul className="character-list" aria-label="Archived characters">
              {archived.map(characterLink)}
            </ul>
          </details>
        )}
        <h2 id="library-heading" className="sidebar-heading">
          Library
        </h2>
        <ul className="character-list" aria-labelledby="library-heading">
          {navItem('Books and PDFs', screen.kind === 'sources', () => go({ kind: 'sources' }))}
          {navItem('Homebrew', screen.kind === 'studio', () => go({ kind: 'studio' }), !info)}
          {navItem('Campaigns', screen.kind === 'campaigns', () => go({ kind: 'campaigns' }), !info)}
        </ul>
        <h2 id="app-heading" className="sidebar-heading">
          App
        </h2>
        <ul className="character-list" aria-labelledby="app-heading">
          {navItem('Backups', screen.kind === 'backups', () => go({ kind: 'backups' }))}
          {/* Stays here until the Help screen (Group 2, G11) takes it. */}
          {navItem('Gap notes', screen.kind === 'gaps', () => go({ kind: 'gaps' }))}
          {navItem('Settings', screen.kind === 'settings', () => go({ kind: 'settings' }))}
        </ul>
      </nav>

      <main className="content">
        <div className="messages">
          {info?.warnings.map((w) => (
            <p key={w.code} role="note" className="warn">
              {w.message}
            </p>
          ))}
          {message && (
            <p role={message.tone === 'error' ? 'alert' : 'status'} className={message.tone}>
              {message.text}
            </p>
          )}
        </div>
        <ScreenBoundary key={screen.kind}>
        <Suspense fallback={<p role="status">Loading…</p>}>
          {screen.kind === 'home' && (
            <HomePanel
              characters={characters}
              onOpen={(id) => void open(id)}
              onNew={startCreate}
              canCreate={!!info}
              focusOnMount={screen.focus !== false}
            />
          )}
          {screen.kind === 'builder' && info && (
            <CharacterBuilder
              key={screen.mode.kind === 'create' ? `create-${builderKey}` : `${screen.mode.kind}-${screen.mode.view.character.id}`}
              mode={screen.mode}
              rulesFamilies={info.rulesFamilies}
              onError={onError}
              onCancel={() => {
                const mode = screen.mode;
                if (mode.kind === 'create') setScreen({ kind: 'home', focus: true });
                else {
                  // The sheet shown at once is the one the builder started from; a fresh read replaces it, so a change made
                  // meanwhile (a rest, a save) is never shown as it was.
                  const id = mode.view.character.id;
                  setScreen({ kind: 'sheet', view: mode.view });
                  client
                    .getCharacter(id)
                    .then((view) => setScreen((current) => (current.kind === 'sheet' && current.view.character.id === id ? { ...current, view } : current)))
                    .catch((error) => {
                      // Only while that sheet is still the screen (R37).
                      const now = screenNow.current;
                      if (now.kind === 'sheet' && now.view.character.id === id) onError(error);
                    });
                }
                setMessage({ tone: 'status', text: 'Draft discarded. Nothing was changed.' });
              }}
              onCommitted={async (view) => {
                // R36: a save that lands is never lost silently. The list is always refreshed; the sheet opens only while this
                // builder is still the screen, otherwise the status says what happened and the screen stays where it is.
                // R39: the screen is decided and switched first, synchronously, so nothing later (a slow or failed refresh) can
                // leave the builder stuck or overwrite a newer view.
                const builder = screen.mode;
                const now = screenNow.current;
                const stillHere = now.kind === 'builder' && now.mode === builder;
                if (builder.kind === 'levelUp') {
                  // D74: the list is written before the screen switches (the sheet reads it on mount) and on both branches, so a
                  // save that lands after the user left still keeps it. A failure here never touches the save.
                  try {
                    writeChecklist(view.character.id, diffLevelUp(builder.view, view));
                  } catch {
                    // the save landed; the checklist is a convenience. An older list for this level must not stand in for the missing one.
                    dismissChecklist(view.character.id);
                  }
                }
                // MECH-X item 2: the save's advisory notice (picks replaced outside their window), worded from the sheets. It never blocks.
                let notice: string | undefined;
                try {
                  notice = builder.kind === 'create' ? undefined : noticeText(builder.view, view);
                } catch {
                  notice = view.notices?.some((n) => n.code === 'choice.replaced-outside-window') ? 'A choice had picks replaced outside its window.' : undefined; // the save landed; the wording must not block the screen (R39)
                }
                // MECH-X R69: the save reset content companions' hit points; the sheet this opens leaves its first view to us.
                const pruned = companionPrunedText(view);
                if (pruned) notice = notice ? `${notice} ${pruned}` : pruned;
                if (stillHere) {
                  setMessage(undefined);
                  if (notice) setMessage({ tone: 'status', text: notice }); // after the clear above, or it is wiped
                  setScreen(builder.kind === 'levelUp' ? { kind: 'sheet', view, tab: 'features', levelledUp: true } : { kind: 'sheet', view });
                } else {
                  setMessage({ tone: 'status', text: notice ? `Saved ${view.character.name}. ${notice}` : `Saved ${view.character.name}.` });
                  // A sheet of this character opened while the save was in flight shows the saved version, never the older read
                  // (a stale sheet could later be saved over it). Its notices are already in the message above, so the sheet gets none
                  // to repeat (R69: one reporter per path; the sheet would otherwise replace "Saved …" with the bare notice).
                  setScreen((cur) => (cur.kind === 'sheet' && cur.view.character.id === view.character.id ? { ...cur, view: { ...view, notices: undefined } } : cur));
                }
                try {
                  await refresh();
                } catch (error) {
                  // The save succeeded; the player must not read a list error as a failed save (R40).
                  if (stillHere) onError(error);
                  else setMessage({ tone: 'error', text: `Saved ${view.character.name}. The character list could not be refreshed: ${describeError(error)}` });
                }
              }}
            />
          )}
          {screen.kind === 'studio' && info && (
            <HomebrewStudio
              info={info}
              initialSourceId={screen.sourceId}
              onError={onError}
              onStatus={(text) => {
                setMessage({ tone: 'status', text });
                void refresh();
              }}
            />
          )}
          {screen.kind === 'campaigns' && info && (
            <CampaignsPanel rulesFamilies={info.rulesFamilies} onError={onError} onStatus={(text) => setMessage({ tone: 'status', text })} />
          )}
          {screen.kind === 'sources' && (
            <SourcesPanel
              onError={onError}
              onStatus={(text) => setMessage({ tone: 'status', text })}
              onOpenStudio={(sourceId) => {
                setMessage(undefined);
                setScreen({ kind: 'studio', sourceId });
              }}
            />
          )}
          {screen.kind === 'gaps' && <AllGapNotesPanel onError={onError} onOpenCharacter={(id) => open(id, 'notes')} />}
          {screen.kind === 'backups' && (
            <BackupsPanel onError={onError} onStatus={(text) => setMessage({ tone: 'status', text })} onRestored={() => void refresh()} />
          )}
          {screen.kind === 'add' && (
            <AddCharacterPanel
              canCreate={!!info}
              start={screen.start}
              onNew={startCreate}
              onSheetImport={() => go({ kind: 'ddb-import' })}
              onFileImport={() => fileInput.current?.click()}
            />
          )}
          {screen.kind === 'settings' && <SettingsPanel info={info} characters={characters} onError={onError} onStatus={(text) => setMessage({ tone: 'status', text })} />}
          {screen.kind === 'ddb-import' && info && (
            <DdbImportPanel
              rulesFamilies={info.rulesFamilies}
              onError={onError}
              onCancel={() => {
                setScreen({ kind: 'home', focus: true });
                setMessage({ tone: 'status', text: 'Import cancelled. Nothing was saved.' });
              }}
              onCreated={async (characterId, result) => {
                await refresh();
                await open(characterId);
                setMessage({
                  tone: 'status',
                  text:
                    `Character created from the character sheet: ${result.overrides} ${result.overrides === 1 ? 'number' : 'numbers'} set by hand, ${result.gapNotes} gap note(s).` +
                    (result.gapNotesNotStored > 0 ? ` ${result.gapNotesNotStored} more items were not noted: the character's note limit was reached.` : ''),
                });
              }}
            />
          )}
          {/* Not gated on info: if app.info fails, the sheet still opens and only the inspiration label falls back. */}
          {screen.kind === 'sheet' && (
            <CharacterSheet
              key={screen.view.character.id}
              view={screen.view}
              initialTab={screen.tab}
              focusChecklist={screen.levelledUp}
              rulesFamilies={info?.rulesFamilies}
              onError={onError}
              onLevelUp={() => {
                setMessage(undefined);
                setScreen({ kind: 'builder', mode: { kind: 'levelUp', view: screen.view } });
              }}
              onMakeChoices={() => {
                setMessage(undefined);
                setScreen({ kind: 'builder', mode: { kind: 'choices', view: screen.view } });
              }}
              onStatus={(text) => setMessage({ tone: 'status', text })}
              onChanged={async (view) => {
                // A late reply (a rest, a save) must not pull the user back from another screen or another character.
                setScreen((cur) => (cur.kind === 'sheet' && cur.view.character.id === view.character.id ? { ...cur, view } : cur));
                await refresh();
              }}
              onArchiveChanged={async () => {
                const id = screen.view.character.id;
                await refresh();
                const view = await client.getCharacter(id);
                // The user may have moved on while this ran; only a sheet still showing this character is refreshed.
                setScreen((current) => (current.kind === 'sheet' && current.view.character.id === id ? { kind: 'sheet', view } : current));
              }}
            />
          )}
          {screen.kind === 'import' && (
            <ImportPreview
              key={`${screen.fileName}-${screen.base64.length}`}
              {...screen}
              onError={onError}
              onCancel={() => setScreen({ kind: 'home', focus: true })}
              onApplied={async (result) => {
                await refresh();
                // Open first: open() clears the message, which used to hide this summary and the backup location.
                const first = result.characters[0];
                if (first) await open(first);
                else setScreen({ kind: 'home', focus: true });
                setMessage({
                  tone: 'status',
                  text:
                    `Imported: ${result.added} added, ${result.replaced} replaced, ${result.unchanged} unchanged.` +
                    (result.backupFile?.endsWith('.db')
                      ? // M6 slices 1 and 2: a source or campaign pack, or a replaced campaign, copies the whole database first.
                        ` Your library as it was before is copied to ${result.backupFile} in your data folder. To go back, close TomeStack and put that file in place of tomestack.db.`
                      : result.backupFile
                        ? ` The replaced copy was backed up to ${result.backupFile} in your data folder; import that file to restore it.`
                        : '') +
                    // M6 slice 2: a replaced campaign also gets a copy of the whole database.
                    (result.databaseCopy ? ` The replaced campaign is in the library copy ${result.databaseCopy}.` : ''),
                });
              }}
            />
          )}
        </Suspense>
        </ScreenBoundary>
      </main>
      <input ref={fileInput} type="file" accept=".zip" hidden onChange={chooseImport} aria-label="TomeStack file" />
    </div>
  );
}
