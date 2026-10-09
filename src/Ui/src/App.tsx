import { useCallback, useEffect, useRef, useState, type ChangeEvent } from 'react';
import { client } from './api/client';
import { TomeStackError } from './api/transport';
import type { AppInfo, CharacterSummary, CharacterView, PackagePreview } from './api/types';
import { AllGapNotesPanel } from './components/AllGapNotesPanel';
import { BackupsPanel } from './components/BackupsPanel';
import { CampaignsPanel } from './components/CampaignsPanel';
import { DdbImportPanel } from './components/ddb/DdbImportPanel';
import { ExtensionsPanel } from './components/ExtensionsPanel';
import { SettingsPanel } from './components/SettingsPanel';
import { CharacterBuilder, type BuilderMode } from './components/CharacterBuilder';
import { CharacterSheet } from './components/CharacterSheet';
import { HomebrewStudio } from './components/HomebrewStudio';
import { HomePanel } from './components/HomePanel';
import { ImportPreview } from './components/ImportPreview';
import { SourcesPanel } from './components/SourcesPanel';
import { readFileAsBase64 } from './files';
import { applyPreferences, setSidebarCollapsed, sidebarCollapsed } from './settings';
import type { SheetTabId } from './sheetTab';

type Screen =
  | { kind: 'home'; focus?: boolean } // focus: false only for the start screen (the first Tab must reach the header)
  | { kind: 'builder'; mode: BuilderMode }
  | { kind: 'studio' }
  | { kind: 'sources' }
  | { kind: 'campaigns' }
  | { kind: 'gaps' }
  | { kind: 'backups' }
  | { kind: 'extensions' }
  | { kind: 'settings' }
  | { kind: 'ddb-import' }
  | { kind: 'sheet'; view: CharacterView; tab?: SheetTabId }
  | { kind: 'import'; fileName: string; base64: string; preview: PackagePreview };

function describeError(error: unknown): string {
  if (error instanceof TomeStackError) {
    return error.diagnostics.length > 0 ? error.diagnostics.map((d) => d.message).join(' ') : error.message;
  }
  return error instanceof Error ? error.message : String(error);
}

export function App() {
  const [info, setInfo] = useState<AppInfo>();
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
      const target = event.target as HTMLElement | null;
      if (target && (target.isContentEditable || /^(TEXTAREA|SELECT)$/.test(target.tagName))) return;
      // Only text-entry inputs keep Ctrl+B; a focused checkbox, radio or button must not block the shortcut (WCAG 2.1.1).
      if (target?.tagName === 'INPUT' && !/^(checkbox|radio|button|submit|reset|range|color|file)$/.test((target as HTMLInputElement).type)) return;
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
  }, [onError]);

  // Only the latest open applies: two quick opens can be answered out of order.
  const openSeq = useRef(0);
  async function open(id: string, tab?: SheetTabId) {
    const request = ++openSeq.current;
    try {
      setMessage(undefined);
      const view = await client.getCharacter(id);
      if (request !== openSeq.current) return;
      setScreen({ kind: 'sheet', view, tab });
    } catch (error) {
      if (request === openSeq.current) onError(error);
    }
  }

  const active = characters.filter((c) => !c.archivedAt);
  const archived = characters.filter((c) => c.archivedAt);
  const characterLink = (c: CharacterSummary) => (
    <li key={c.id}>
      <button
        type="button"
        className="link"
        aria-current={screen.kind === 'sheet' && screen.view.character.id === c.id ? 'page' : undefined}
        onClick={() => open(c.id)}
      >
        {c.name} <span className="tag">{c.rulesFamily}</span>
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
        <h1>TomeStack</h1>
        <span className="tag">Offline</span>
      </header>

      <nav id="sidebar" className="sidebar" aria-labelledby="characters-heading" ref={sidebar} hidden={collapsed}>
        {/* Owner (2026-10-06): a visible close control inside the sidebar. A distinct name from the header's "Hide sidebar"
            (two buttons with one name would make getByRole throw). Never shown collapsed, so no aria-expanded. */}
        <button type="button" className="sidebar-close" aria-controls="sidebar" onClick={toggleSidebar}>
          Close sidebar
        </button>
        <h2 id="characters-heading" className="sidebar-heading">
          Characters
        </h2>
        <ul className="character-list" aria-labelledby="characters-heading">
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
        <h2 id="tools-heading" className="sidebar-heading">
          Tools
        </h2>
        <div className="actions" role="group" aria-labelledby="tools-heading">
          <button
            type="button"
            aria-current={screen.kind === 'home' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'home', focus: true });
            }}
          >
            Characters
          </button>
          {/* Disabled until app.info has loaded: the form needs the rules families, and a click must never do nothing. */}
          <button
            type="button"
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'builder', mode: { kind: 'create' } });
            }}
            disabled={!info}
          >
            New character
          </button>
          <button
            type="button"
            title="Package import is for TomeStack .zip files. For a D&D Beyond character, use Import from D&D Beyond PDF."
            onClick={() => fileInput.current?.click()}
          >
            Import package…
          </button>
          <input ref={fileInput} type="file" accept=".zip" hidden onChange={chooseImport} aria-label="Package file" />
          <button
            type="button"
            disabled={!info}
            aria-current={screen.kind === 'ddb-import' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'ddb-import' });
            }}
          >
            Import from D&amp;D Beyond PDF…
          </button>
          <button
            type="button"
            disabled={!info}
            aria-current={screen.kind === 'studio' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'studio' });
            }}
          >
            Homebrew studio
          </button>
          <button
            type="button"
            aria-current={screen.kind === 'sources' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'sources' });
            }}
          >
            Sources
          </button>
          <button
            type="button"
            disabled={!info}
            aria-current={screen.kind === 'campaigns' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'campaigns' });
            }}
          >
            Campaigns
          </button>
          <button
            type="button"
            aria-current={screen.kind === 'gaps' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'gaps' });
            }}
          >
            Gap notes
          </button>
          <button
            type="button"
            aria-current={screen.kind === 'backups' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'backups' });
            }}
          >
            Backups
          </button>
          <button
            type="button"
            aria-current={screen.kind === 'extensions' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'extensions' });
            }}
          >
            Extensions
          </button>
          <button
            type="button"
            aria-current={screen.kind === 'settings' ? 'page' : undefined}
            onClick={() => {
              setMessage(undefined);
              setScreen({ kind: 'settings' });
            }}
          >
            Settings
          </button>
        </div>
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
        {screen.kind === 'home' && (
          <HomePanel
            characters={characters}
            onOpen={(id) => void open(id)}
            onNew={() => {
              setMessage(undefined);
              setScreen({ kind: 'builder', mode: { kind: 'create' } });
            }}
            canCreate={!!info}
            focusOnMount={screen.focus !== false}
          />
        )}
        {screen.kind === 'builder' && info && (
          <CharacterBuilder
            key={screen.mode.kind === 'create' ? 'create' : `${screen.mode.kind}-${screen.mode.view.character.id}`}
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
                  .catch(onError);
              }
              setMessage({ tone: 'status', text: 'Draft discarded. Nothing was changed.' });
            }}
            onCommitted={async (view) => {
              setMessage(undefined);
              await refresh();
              // Only while this builder is still the screen: a Cancel (or another screen) in the meantime wins.
              setScreen((current) => (current.kind === 'builder' ? { kind: 'sheet', view } : current));
            }}
          />
        )}
        {screen.kind === 'studio' && info && (
          <HomebrewStudio
            info={info}
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
        {screen.kind === 'sources' &&<SourcesPanel onError={onError} onStatus={(text) => setMessage({ tone: 'status', text })} />}
        {screen.kind === 'gaps' && <AllGapNotesPanel onError={onError} onOpenCharacter={(id) => open(id, 'notes')} />}
        {screen.kind === 'backups' && (
          <BackupsPanel onError={onError} onStatus={(text) => setMessage({ tone: 'status', text })} onRestored={() => void refresh()} />
        )}
        {screen.kind === 'extensions' && info && (
          <ExtensionsPanel
            characters={characters}
            rulesFamilies={info.rulesFamilies}
            onError={onError}
            onStatus={(text) => setMessage({ tone: 'status', text })}
          />
        )}
        {screen.kind === 'settings' && <SettingsPanel info={info} />}
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
                  `Character created from the D&D Beyond sheet: ${result.overrides} override(s), ${result.gapNotes} gap note(s).` +
                  (result.gapNotesNotStored > 0 ? ` ${result.gapNotesNotStored} more items were not noted: the character's note limit was reached.` : ''),
              });
            }}
          />
        )}
        {screen.kind === 'sheet' && (
          <CharacterSheet
            key={screen.view.character.id}
            view={screen.view}
            initialTab={screen.tab}
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
      </main>
    </div>
  );
}
