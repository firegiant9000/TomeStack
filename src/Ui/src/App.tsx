import { useCallback, useEffect, useRef, useState, type ChangeEvent } from 'react';
import { client } from './api/client';
import { TomeStackError } from './api/transport';
import type { AppInfo, CharacterSummary, CharacterView, PackagePreview } from './api/types';
import { CharacterSheet } from './components/CharacterSheet';
import { CreateCharacterForm } from './components/CreateCharacterForm';
import { ImportPreview } from './components/ImportPreview';
import { readFileAsBase64 } from './files';

type Screen =
  | { kind: 'empty' }
  | { kind: 'create' }
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
          <button type="button" onClick={() => setScreen({ kind: 'create' })}>
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
        {message && (
          <p role={message.tone === 'error' ? 'alert' : 'status'} className={message.tone}>
            {message.text}
          </p>
        )}
        {screen.kind === 'empty' && <p className="hint">Create a character or open one from the list.</p>}
        {screen.kind === 'create' && info && (
          <CreateCharacterForm
            rulesFamilies={info.rulesFamilies}
            onError={onError}
            onCreated={async (id) => {
              await refresh();
              await open(id);
            }}
          />
        )}
        {screen.kind === 'sheet' && (
          <CharacterSheet
            key={screen.view.character.id}
            view={screen.view}
            onError={onError}
            onChanged={async (view) => {
              setScreen({ kind: 'sheet', view });
              await refresh();
            }}
          />
        )}
        {screen.kind === 'import' && (
          <ImportPreview
            {...screen}
            onError={onError}
            onCancel={() => setScreen({ kind: 'empty' })}
            onApplied={async (result) => {
              await refresh();
              setMessage({
                tone: 'status',
                text: `Imported: ${result.added} added, ${result.replaced} replaced, ${result.unchanged} unchanged.`,
              });
              const first = result.characters[0];
              if (first) await open(first);
              else setScreen({ kind: 'empty' });
            }}
          />
        )}
      </main>
    </div>
  );
}
