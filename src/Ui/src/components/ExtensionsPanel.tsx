import { useCallback, useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type {
  CharacterSummary,
  ExtensionHook,
  ExtensionInstallPreview,
  ExtensionRunPreview,
  InstalledExtension,
  RulesFamilyId,
  RulesFamilyPolicy,
  SheetPurpose,
} from '../api/types';
import { downloadBase64, readFileAsBase64 } from '../files';

interface Props {
  characters: CharacterSummary[];
  rulesFamilies: RulesFamilyPolicy[];
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

const unsupported = (error: unknown) => error instanceof TomeStackError && error.code === 'unsupported';

/** The review before installing: what it is, what it may do (unticked until you tick it), and what it adds. */
function InstallReview({ fileName, preview, onInstalled, onCancel, onError }: {
  fileName: string;
  preview: ExtensionInstallPreview;
  onInstalled: (installed: InstalledExtension) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
}) {
  const [grants, setGrants] = useState<string[]>([]);
  const manifest = preview.manifest;

  async function install() {
    try {
      onInstalled(await client.installExtension(preview.token!, grants));
    } catch (error) {
      onError(error);
    }
  }

  return (
    <section className="play-panel" aria-labelledby="extension-review-heading">
      <h3 id="extension-review-heading">Install {manifest?.name ?? fileName}?</h3>
      {preview.errors.length > 0 && (
        <div role="alert">
          <p>This extension cannot be installed:</p>
          <ul className="errors">
            {preview.errors.map((e, i) => (
              <li key={i}>{e.message}</li>
            ))}
          </ul>
        </div>
      )}
      {manifest && (
        <>
          <p>
            Version {manifest.version} by {manifest.author}, under {manifest.license}. It runs no code: TomeStack reads its
            instructions and does the work itself.
          </p>
          {manifest.description && <p className="hint">{manifest.description}</p>}
          {preview.update && (
            <p className="warn">
              This replaces version {preview.update.installedVersion} with {preview.update.newVersion}. You grant its permissions again.
              {preview.update.permissionsAdded.length > 0 && ` New permissions: ${preview.update.permissionsAdded.join(', ')}.`}
            </p>
          )}
          <h4>What it can do</h4>
          <ul>
            {manifest.hooks.map((h) => (
              <li key={h.id}>{h.label}</li>
            ))}
          </ul>
          <fieldset>
            <legend>Permissions to grant</legend>
            {preview.permissions.map((p) => (
              <label key={p.permission} className="check">
                <input
                  type="checkbox"
                  checked={grants.includes(p.permission)}
                  onChange={(e) =>
                    setGrants((current) => (e.target.checked ? [...current, p.permission] : current.filter((g) => g !== p.permission)))
                  }
                />
                {p.permission}: {p.description}
              </label>
            ))}
            <p className="hint">A hook whose permissions you do not grant cannot run. You can remove the extension at any time.</p>
          </fieldset>
        </>
      )}
      <div className="actions">
        <button type="button" disabled={!preview.canInstall || grants.length === 0} onClick={install}>
          Install with these permissions
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </section>
  );
}

/** One hook run: pick what it reads, preview what it would write, then write it. */
function HookRun({ extension, hook, characters, rulesFamilies, onError, onStatus, onDone }: {
  extension: InstalledExtension;
  hook: ExtensionHook;
  onDone: () => void;
} & Props) {
  const [characterId, setCharacterId] = useState(characters[0]?.id ?? '');
  const [purpose, setPurpose] = useState<SheetPurpose>('share');
  const [family, setFamily] = useState<RulesFamilyId>(rulesFamilies[0]?.id ?? 'srd-5.2.1');
  const [title, setTitle] = useState(`${extension.manifest.name} import`);
  const [preview, setPreview] = useState<ExtensionRunPreview>();
  const [inputName, setInputName] = useState<string>();
  const picker = useRef<HTMLInputElement>(null);

  async function previewExport() {
    try {
      setPreview(await client.previewExtensionRun({ extensionId: extension.id, hookId: hook.id, characterId, purpose }));
    } catch (error) {
      onError(error);
    }
  }

  async function previewImport(input: { inputToken?: string; inputBase64?: string }, name: string) {
    try {
      setInputName(name);
      setPreview(await client.previewExtensionRun({ extensionId: extension.id, hookId: hook.id, rulesFamily: family, sourceTitle: title, ...input }));
    } catch (error) {
      onError(error);
    }
  }

  async function chooseInput() {
    try {
      const chosen = await client.chooseExtensionInput();
      if (chosen.chosen && chosen.token) await previewImport({ inputToken: chosen.token }, chosen.fileName ?? 'the file');
    } catch (error) {
      // Browser development (DevHost) has no native Open dialog: use the browser's file picker.
      if (unsupported(error)) picker.current?.click();
      else onError(error);
    }
  }

  async function save() {
    if (!preview) return;
    try {
      const outcome = await client.saveExtensionOutputAs(preview.token);
      if (outcome.saved) onStatus(`Saved ${outcome.fileName}.`);
    } catch (error) {
      if (unsupported(error)) {
        try {
          const output = await client.runExtensionExport(preview.token);
          downloadBase64(output.fileName, output.base64, 'text/plain');
          onStatus(`Downloaded ${output.fileName}.`);
        } catch (inner) {
          onError(inner);
        }
      } else onError(error);
    }
    setPreview(undefined);
  }

  async function createDrafts() {
    if (!preview) return;
    try {
      const result = await client.runExtensionImport(preview.token);
      onStatus(`Created ${result.drafts} draft${result.drafts === 1 ? '' : 's'} in the new source ${result.sourceTitle}. Nothing is active until you publish it in the studio.`);
      onDone();
    } catch (error) {
      onError(error);
    }
  }

  return (
    <section className="play-panel" aria-label={`Run ${hook.label}`}>
      <h4>{hook.label}</h4>
      {hook.kind === 'export' ? (
        <>
          <label className="field">
            Character
            <select value={characterId} onChange={(e) => (setCharacterId(e.target.value), setPreview(undefined))}>
              {characters.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
          <fieldset>
            <legend>What the file may hold</legend>
            <label className="choice">
              <input type="radio" name={`purpose-${hook.id}`} checked={purpose === 'share'} onChange={() => (setPurpose('share'), setPreview(undefined))} />
              Share: content that may not be shared is left out; its totals stay
            </label>
            <label className="choice">
              <input type="radio" name={`purpose-${hook.id}`} checked={purpose === 'personal'} onChange={() => (setPurpose('personal'), setPreview(undefined))} />
              Personal copy: also includes your own homebrew. Do not share it.
            </label>
          </fieldset>
          <button type="button" disabled={!characterId} onClick={previewExport}>
            Preview output
          </button>
        </>
      ) : (
        <>
          <label className="field">
            Rules
            <select value={family} onChange={(e) => setFamily(e.target.value as RulesFamilyId)}>
              {rulesFamilies.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.displayName}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            New source for the drafts
            <input value={title} onChange={(e) => setTitle(e.target.value)} />
          </label>
          <button type="button" onClick={chooseInput}>
            Choose {hook.accepts?.toUpperCase()} file…
          </button>
          <input
            ref={picker}
            type="file"
            hidden
            aria-label={`${hook.label}: file`}
            accept={hook.accepts === 'csv' ? '.csv' : '.json'}
            onChange={async (e) => {
              const file = e.target.files?.[0];
              e.target.value = '';
              if (file) await previewImport({ inputBase64: await readFileAsBase64(file) }, file.name);
            }}
          />
        </>
      )}
      {preview && (
        <div role="region" aria-label={`Preview of ${hook.label}`}>
          {preview.kind === 'export' ? (
            <>
              <p>
                {preview.fileName}: {preview.bytes} bytes.
              </p>
              <pre className="excerpt">{preview.excerpt}</pre>
              <p>{preview.dropped.length === 0 ? 'Nothing was left out.' : 'Left out because it may not leave this machine for this purpose:'}</p>
              {preview.dropped.length > 0 && (
                <ul>
                  {preview.dropped.map((d) => (
                    <li key={d.source}>
                      {d.items} item{d.items === 1 ? '' : 's'} from {d.source} ({d.publisher})
                    </li>
                  ))}
                </ul>
              )}
              <div className="actions">
                <button type="button" onClick={save}>
                  Save output…
                </button>
              </div>
            </>
          ) : (
            <>
              <p>
                From {inputName}: {preview.drafts.length} draft{preview.drafts.length === 1 ? '' : 's'} in the new source {preview.sourceTitle}.
              </p>
              <ul>
                {preview.drafts.map((d, i) => (
                  <li key={i}>
                    {d.name} ({d.kind}){d.errors.length > 0 ? `: ${d.errors.length} problem(s) to fix before publishing` : ''}
                  </li>
                ))}
              </ul>
              <div className="actions">
                <button type="button" onClick={createDrafts}>
                  Create drafts
                </button>
              </div>
            </>
          )}
          {preview.warnings.length > 0 && (
            <ul className="warnings">
              {preview.warnings.map((w, i) => (
                <li key={i}>{w.message}</li>
              ))}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}

/**
 * M6 slice 3 (ADR-011, option A): extensions are declarative. Installing shows what an extension asks for and grants
 * only what you tick; every run is started here, previews its result, and writes only after you confirm.
 */
export function ExtensionsPanel(props: Props) {
  const { onError, onStatus } = props;
  const [installed, setInstalled] = useState<InstalledExtension[]>([]);
  const [review, setReview] = useState<{ fileName: string; preview: ExtensionInstallPreview }>();
  const [running, setRunning] = useState<{ extension: InstalledExtension; hook: ExtensionHook }>();
  const [removing, setRemoving] = useState<InstalledExtension>();
  const picker = useRef<HTMLInputElement>(null);

  const reload = useCallback(() => client.listExtensions().then(setInstalled).catch(onError), [onError]);
  useEffect(() => {
    client.listExtensions().then(setInstalled).catch(onError);
  }, [onError]);

  async function choose() {
    try {
      const chosen = await client.chooseExtensionInstall();
      if (chosen.chosen && chosen.preview) setReview({ fileName: chosen.fileName ?? 'extension', preview: chosen.preview });
    } catch (error) {
      if (unsupported(error)) picker.current?.click();
      else onError(error);
    }
  }

  return (
    <section className="panel" aria-labelledby="extensions-heading">
      <h2 id="extensions-heading">Extensions</h2>
      <p className="hint">
        An extension adds an import or an export. It never runs code: TomeStack reads its instructions and does the work, and
        it can use only the permissions you grant. Every run shows you the result first.
      </p>
      <div className="actions">
        <button type="button" onClick={choose}>
          Install extension…
        </button>
        <input
          ref={picker}
          type="file"
          hidden
          aria-label="Extension file"
          accept=".zip"
          onChange={async (e) => {
            const file = e.target.files?.[0];
            e.target.value = '';
            if (!file) return;
            try {
              setReview({ fileName: file.name, preview: await client.previewExtensionInstall(await readFileAsBase64(file)) });
            } catch (error) {
              onError(error);
            }
          }}
        />
      </div>
      {review && (
        <InstallReview
          fileName={review.fileName}
          preview={review.preview}
          onError={onError}
          onCancel={() => setReview(undefined)}
          onInstalled={(extension) => {
            setReview(undefined);
            onStatus(`Installed ${extension.manifest.name} with ${extension.grants.length} permission${extension.grants.length === 1 ? '' : 's'}.`);
            void reload();
          }}
        />
      )}
      {installed.length === 0 ? (
        <p>No extension is installed.</p>
      ) : (
        <ul className="resources" aria-label="Installed extensions">
          {installed.map((x) => (
            <li key={x.id} className="resource" aria-label={x.manifest.name}>
              <span className="option-name">{x.manifest.name}</span> <span className="tag">{x.manifest.version}</span>{' '}
              <span className="hint">by {x.manifest.author}; granted: {x.grants.join(', ') || 'nothing'}</span>
              <label className="check">
                <input
                  type="checkbox"
                  checked={x.enabled}
                  onChange={(e) =>
                    client
                      .setExtensionEnabled(x.id, e.target.checked)
                      .then(() => reload())
                      .catch(onError)
                  }
                />
                Turned on
              </label>
              <div className="actions">
                {x.manifest.hooks.map((h) => (
                  <button key={h.id} type="button" disabled={!x.enabled} onClick={() => setRunning({ extension: x, hook: h })}>
                    {h.label}
                  </button>
                ))}
                <button type="button" onClick={() => setRemoving(x)}>
                  Remove {x.manifest.name}…
                </button>
              </div>
              {removing?.id === x.id && (
                <div role="alertdialog" aria-label={`Remove ${x.manifest.name}?`}>
                  <p>Its permissions are revoked and its file is deleted. Drafts it created stay: they are yours.</p>
                  <button
                    type="button"
                    onClick={() =>
                      client
                        .removeExtension(x.id)
                        .then(() => {
                          setRemoving(undefined);
                          setRunning(undefined);
                          onStatus(`Removed ${x.manifest.name}.`);
                          return reload();
                        })
                        .catch(onError)
                    }
                  >
                    Remove
                  </button>
                  <button type="button" onClick={() => setRemoving(undefined)}>
                    Keep it
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {running && (
        <HookRun key={`${running.extension.id}-${running.hook.id}`} {...props} extension={running.extension} hook={running.hook} onDone={() => setRunning(undefined)} />
      )}
    </section>
  );
}
