import { useState } from 'react';
import { client } from '../api/client';
import type { ImportResult, PackagePreview } from '../api/types';

interface Props {
  fileName: string;
  base64: string;
  preview: PackagePreview;
  onApplied: (result: ImportResult) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
}

/** SPEC P-02: the importer shows dependencies and conflicts; nothing is written until the user applies. */
export function ImportPreview({ fileName, base64, preview, onApplied, onCancel, onError }: Props) {
  const [applying, setApplying] = useState(false);

  async function apply() {
    setApplying(true);
    try {
      onApplied(await client.applyImport(base64));
    } catch (error) {
      onError(error);
    } finally {
      setApplying(false);
    }
  }

  return (
    <section className="panel" aria-labelledby="import-heading">
      <h2 id="import-heading">Import {fileName}</h2>

      {preview.errors.length > 0 && (
        <div role="alert">
          <h3>This package cannot be imported</h3>
          <ul className="errors">
            {preview.errors.map((e, i) => (
              <li key={i}>{e.message}</li>
            ))}
          </ul>
        </div>
      )}

      {preview.items.length > 0 && (
        <table>
          <caption>Package contents</caption>
          <thead>
            <tr>
              <th scope="col">Type</th>
              <th scope="col">Name</th>
              <th scope="col">Action</th>
              <th scope="col">Details</th>
            </tr>
          </thead>
          <tbody>
            {preview.items.map((item) => (
              <tr key={`${item.kind}-${item.id}`} className={`action-${item.action}`}>
                <td>{item.kind}</td>
                <td>{item.name}</td>
                <td>{item.action}</td>
                <td>{item.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {preview.warnings.length > 0 && (
        <ul className="warnings">
          {preview.warnings.map((w, i) => (
            <li key={i}>{w.message}</li>
          ))}
        </ul>
      )}

      {preview.manifest && (
        <>
          <h3>Licenses</h3>
          <ul>
            {preview.manifest.notices.map((n) => (
              <li key={n.sourceId}>
                {n.title} ({n.publisher}): {n.license}
                {n.redistributable ? '' : ' · not for redistribution'}
              </li>
            ))}
          </ul>
          <p className="hint">{preview.manifest.attachmentPolicy}</p>
        </>
      )}

      <div className="actions">
        <button type="button" onClick={apply} disabled={!preview.canApply || applying}>
          {applying ? 'Importing…' : 'Apply import'}
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </section>
  );
}
