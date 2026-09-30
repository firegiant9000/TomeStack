import { useState } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { ExportTarget, SheetPurpose, VttExportPreview } from '../api/types';
import { downloadBase64 } from '../files';

interface Props {
  characterId: string;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

/**
 * M6 slice 4 (ADR-012): export the character for a virtual tabletop, or as TomeStack's own sheet JSON. The file is saved
 * where you choose and imported by hand; nothing is uploaded. A share export keeps only totals from content that may not
 * be shared (ADR-007 item 11); the preview says what is left out and where Foundry may calculate differently.
 */
export function VttExportPanel({ characterId, onError, onStatus }: Props) {
  const [target, setTarget] = useState<ExportTarget>('foundry-dnd5e');
  const [purpose, setPurpose] = useState<SheetPurpose>('share');
  const [preview, setPreview] = useState<VttExportPreview>();

  async function show() {
    try {
      setPreview(await client.previewVttExport(characterId, target, purpose));
    } catch (error) {
      onError(error);
    }
  }

  async function save() {
    if (!preview) return;
    try {
      const outcome = await client.saveVttExportAs(preview.token);
      if (outcome.saved) {
        onStatus(`Saved ${outcome.fileName}.`);
        setPreview(undefined);
      }
    } catch (error) {
      if (!(error instanceof TomeStackError && error.code === 'unsupported')) {
        onError(error);
        return;
      }
      // Browser development (DevHost) has no native dialog: fall back to a download.
      try {
        const file = await client.downloadVttExport(preview.token);
        downloadBase64(file.fileName, file.base64, 'application/json');
        onStatus(`Downloaded ${file.fileName}.`);
        setPreview(undefined);
      } catch (inner) {
        onError(inner);
      }
    }
  }

  return (
    <section aria-labelledby="vtt-export-heading" className="export-panel">
      <h3 id="vtt-export-heading">Export for a virtual tabletop</h3>
      <fieldset>
        <legend>Format</legend>
        <label>
          <input type="radio" name="vtt-target" checked={target === 'foundry-dnd5e'} onChange={() => (setTarget('foundry-dnd5e'), setPreview(undefined))} />
          Foundry VTT (dnd5e system) <span className="tag">Experimental</span>
        </label>
        <label>
          <input type="radio" name="vtt-target" checked={target === 'sheet-json'} onChange={() => (setTarget('sheet-json'), setPreview(undefined))} />
          TomeStack sheet (JSON), for any tool that reads it
        </label>
      </fieldset>
      <fieldset>
        <legend>What the file may hold</legend>
        <label>
          <input type="radio" name="vtt-purpose" checked={purpose === 'share'} onChange={() => (setPurpose('share'), setPreview(undefined))} />
          For a shared game: content that may not be shared is left out; its totals stay
        </label>
        <label>
          <input type="radio" name="vtt-purpose" checked={purpose === 'personal'} onChange={() => (setPurpose('personal'), setPreview(undefined))} />
          Personal copy: also includes your own homebrew. Do not share it.
        </label>
      </fieldset>
      <button type="button" onClick={show}>
        Preview export
      </button>
      {preview && (
        <div role="region" aria-label="Export preview">
          <p>
            {preview.fileName}, {preview.bytes} bytes; adapter {preview.adapterVersion}
            {preview.targetVersion ? `, checked against ${preview.targetVersion}` : ''}.
          </p>
          <p>{preview.dropped.length === 0 ? 'Nothing is left out.' : 'Left out, because it may not be shared:'}</p>
          {preview.dropped.length > 0 && (
            <ul>
              {preview.dropped.map((d) => (
                <li key={d.source}>
                  {d.items} item{d.items === 1 ? '' : 's'} from {d.source} ({d.publisher})
                </li>
              ))}
            </ul>
          )}
          {preview.differences.length > 0 && (
            <>
              <p>Foundry works these out itself and may show something else:</p>
              <ul>
                {preview.differences.map((d, i) => (
                  <li key={i}>{d}</li>
                ))}
              </ul>
            </>
          )}
          {preview.warnings.length > 0 && (
            <ul className="warnings">
              {preview.warnings.map((w, i) => (
                <li key={i}>{w.message}</li>
              ))}
            </ul>
          )}
          <p className="hint">TomeStack is not affiliated with Foundry Gaming LLC, Roll20 or Wizards of the Coast.</p>
          <button type="button" onClick={save}>
            Save export file…
          </button>
        </div>
      )}
    </section>
  );
}
