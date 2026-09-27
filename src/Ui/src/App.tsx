import { useCallback, useEffect, useRef, useState, type ChangeEvent } from 'react';
import { client } from './api/client';
import { TomeStackError } from './api/transport';
import type { AppInfo, CharacterSummary, CharacterView, PackagePreview } from './api/types';
import { CharacterBuilder, type BuilderMode } from './components/CharacterBuilder';
import { CharacterSheet } from './components/CharacterSheet';
import { ImportPreview } from './components/ImportPreview';
import { readFileAsBase64 } from './files';

type Screen =
  | { kind: 'empty' }
  | { kind: 'builder'; mode: BuilderMode }
  | { kind: 'sheet'; view: CharacterView }
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
  const [screen, setScreen] = useState<Screen>({ kind: 'empty' });
  const [message, setMessage] = useState<{ tone: 'error' | 'status'; text: string }>();
  const fileInput = useRef<HTMLInputElement>(null);

  const onError = useCallback((error: unknown) => setMessage({ tone: 'error', text: describeError(error) }), []);

  const refresh = useCallback(async () => setCharacters(await client.listCharacters()), []);

  useEffect(() => {
    Promise.all([client.info().then(setInfo), client.listCharacters().then(setCharacters)]).catch(onError);
  }, [onError]);

  async function open(id: string) {
    try {
      setMessage(undefined);
      setScreen({ kind: 'sheet', view: await client.getCharacter(id) });
    } catch (error) {
      onError(error);
    }
  }

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
    <div className="app">
      <header className="app-header">
        <h1>TomeStack</h1>
        <span className="hint">
          Offline · local data{info ? ` · v${info.version} · schema ${info.schemaVersion}` : ''}
        </span>
      </header>

      <nav className="sidebar" aria-label="Characters">
        <div className="actions">
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
          <button type="button" onClick={() => fileInput.current?.click()}>
            Import package…
          </button>
          <input ref={fileInput} type="file" accept=".zip" hidden onChange={chooseImport} aria-label="Package file" />
        </div>
        <ul className="character-list">
          {characters.map((c) => (
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
          ))}
          {characters.length === 0 && <li className="hint">No characters yet.</li>}
        </ul>
      </nav>

      <main className="content">
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
        {screen.kind === 'empty' && <p className="hint">Create a character or open one from the list.</p>}
        {screen.kind === 'builder' && info && (
          <CharacterBuilder
            key={screen.mode.kind === 'create' ? 'create' : `${screen.mode.kind}-${screen.mode.view.character.id}`}
            mode={screen.mode}
            rulesFamilies={info.rulesFamilies}
            onError={onError}
            onCancel={() => {
              const mode = screen.mode;
              if (mode.kind === 'create') setScreen({ kind: 'empty' });
              else setScreen({ kind: 'sheet', view: mode.view });
              setMessage({ tone: 'status', text: 'Draft discarded. Nothing was changed.' });
            }}
            onCommitted={async (view) => {
              setMessage(undefined);
              await refresh();
              setScreen({ kind: 'sheet', view });
            }}
          />
        )}
        {screen.kind === 'sheet' && (
          <CharacterSheet
            key={screen.view.character.id}
            view={screen.view}
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
              setScreen({ kind: 'sheet', view });
              await refresh();
            }}
          />
        )}
        {screen.kind === 'import' && (
          <ImportPreview
            key={`${screen.fileName}-${screen.base64.length}`}
            {...screen}
            onError={onError}
            onCancel={() => setScreen({ kind: 'empty' })}
            onApplied={async (result) => {
              await refresh();
              // Open first: open() clears the message, which used to hide this summary and the backup location.
              const first = result.characters[0];
              if (first) await open(first);
              else setScreen({ kind: 'empty' });
              setMessage({
                tone: 'status',
                text:
                  `Imported: ${result.added} added, ${result.replaced} replaced, ${result.unchanged} unchanged.` +
                  (result.backupFile
                    ? ` The replaced copy was backed up to ${result.backupFile} in your data folder; import that file to restore it.`
                    : ''),
              });
            }}
          />
        )}
      </main>
    </div>
  );
}
