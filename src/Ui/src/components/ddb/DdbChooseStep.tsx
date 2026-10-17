import { useRef, type ChangeEvent } from 'react';
import { client } from '../../api/client';
import { TomeStackError } from '../../api/transport';
import type { DdbReadResult } from '../../api/types';
import { readFileAsBase64 } from '../../files';

interface Props {
  read?: DdbReadResult;
  onRead: (read: DdbReadResult) => void;
  onError: (error: unknown) => void;
}

/**
 * Step 1: in the desktop app the native Open dialog picks the PDF, so its path never crosses the bridge (ADR-006). The
 * DevHost and the browser have no dialog (`unsupported`): a file input sends the bytes instead, read once and not kept.
 */
export function DdbChooseStep({ read, onRead, onError }: Props) {
  const fileInput = useRef<HTMLInputElement>(null);

  async function choose() {
    try {
      const result = await client.ddbRead();
      if (result.chosen !== false) onRead(result);
    } catch (error) {
      if (error instanceof TomeStackError && error.code === 'unsupported') {
        fileInput.current?.click();
        return;
      }
      onError(error);
    }
  }

  async function fromBrowser(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    try {
      onRead(await client.ddbReadData(file.name, await readFileAsBase64(file)));
    } catch (error) {
      onError(error);
    }
  }

  return (
    <>
      <p>
        Choose a character sheet PDF: an export from D&amp;D Beyond, or the fillable 2014 character sheet filled in on a computer.
        TomeStack reads its form fields once, in the import worker, and keeps nothing of the file.
      </p>
      {/* Release checklist item 1 (owner, 2026-10-12): the name only describes the file; no affiliation is implied. */}
      <p className="hint">TomeStack is not affiliated with D&amp;D Beyond or Wizards of the Coast.</p>
      <button type="button" onClick={choose}>
        Choose PDF…
      </button>
      <input ref={fileInput} type="file" accept=".pdf" hidden onChange={fromBrowser} aria-label="Character sheet PDF file" />
      {read && (
        <dl className="ddb-read">
          <dt>Character</dt>
          <dd>{read.summary.name || '(no name on the sheet)'}</dd>
          <dt>Classes</dt>
          <dd>{read.summary.classText || '(could not be read)'}</dd>
          <dt>Read</dt>
          <dd>
            {read.summary.features} features, {read.summary.spells} spells, {read.summary.items} items
          </dd>
          <dt>Layout</dt>
          <dd>{read.layoutName || read.layout}</dd>
        </dl>
      )}
    </>
  );
}
