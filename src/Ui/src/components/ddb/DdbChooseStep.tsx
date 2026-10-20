import { useEffect, useRef, useState, type ChangeEvent } from 'react';
import { client } from '../../api/client';
import { TomeStackError } from '../../api/transport';
import type { DdbReadResult } from '../../api/types';
import { readFileAsBase64 } from '../../files';

interface Props {
  read?: DdbReadResult;
  onRead: (read: DdbReadResult) => void;
  onError: (error: unknown) => void;
}

/** The most characters (UTF-16 code units) the service reads from text (ADR-017); a file's size is checked against it before it is read. */
const maxTextChars = 262_144;
const count = (n: number) => n.toLocaleString('en-US');

/**
 * Step 1: in the desktop app the native Open dialog picks the PDF, so its path never crosses the bridge (ADR-006). The
 * DevHost and the browser have no dialog (`unsupported`): a file input sends the bytes instead, read once and not kept.
 */
export function DdbChooseStep({ read, onRead, onError }: Props) {
  const fileInput = useRef<HTMLInputElement>(null);
  const textButton = useRef<HTMLButtonElement>(null);
  const textBox = useRef<HTMLTextAreaElement>(null);
  const [textOpen, setTextOpen] = useState(false);
  const [text, setText] = useState('');
  const [fileProblem, setFileProblem] = useState<string>();
  const [reading, setReading] = useState(false);
  // Focus goes into the box on open; closing hands it back to the button that opened it.
  const opened = useRef(false);
  useEffect(() => {
    if (textOpen) textBox.current?.focus();
    else if (opened.current) textButton.current?.focus();
    opened.current = true;
  }, [textOpen]);
  const tooLong = text.length > maxTextChars;

  async function readText() {
    if (reading || !text.trim() || tooLong) return;
    try {
      setReading(true);
      onRead(await client.ddbReadText(text));
    } catch (error) {
      onError(error);
    } finally {
      setReading(false);
    }
  }

  async function textFromFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    // The size is known before the file is read; a file over the limit is refused and never loaded. Only sizes are named.
    if (file.size > maxTextChars) {
      setFileProblem(`That file is ${count(file.size)} bytes; the limit is ${count(maxTextChars)} characters. Nothing was read.`);
      return;
    }
    setFileProblem(undefined);
    try {
      setText(await file.text());
      textBox.current?.focus();
    } catch (error) {
      onError(error);
    }
  }

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
        Choose a fillable character sheet PDF (an export from D&amp;D Beyond, or the fillable 2014 character sheet filled in on a computer), or
        use “Import a character from text…” below for pasted text or a .txt or .md file. TomeStack reads a PDF&apos;s form fields once, in the
        import worker, and keeps nothing of the file.
      </p>
      {/* Release checklist item 1 (owner, 2026-10-12): the name only describes the file; no affiliation is implied. */}
      <p className="hint">TomeStack is not affiliated with D&amp;D Beyond or Wizards of the Coast.</p>
      <button type="button" onClick={choose}>
        Choose PDF…
      </button>
      <input ref={fileInput} type="file" accept=".pdf" hidden onChange={fromBrowser} aria-label="Character sheet PDF file" />
      <div className="ddb-text-entry">
        <button ref={textButton} type="button" aria-expanded={textOpen} aria-controls="ddb-text-panel" onClick={() => setTextOpen((o) => !o)}>
          Import a character from text…
        </button>
        {textOpen && (
          <div id="ddb-text-panel">
            <p className="hint">
              Paste a character written as text, or choose a .txt or .md file. TomeStack reads labelled lines such as “Name:”, “Class:” and “STR 10”, and
              lists any line it could not read. The text itself isn&apos;t kept. Lines that can&apos;t be read become session notes if you keep the tick.
            </p>
            <label className="field">
              Character text
              <textarea ref={textBox} rows={10} value={text} onChange={(e) => setText(e.target.value)} aria-describedby="ddb-text-count" aria-invalid={tooLong} />
            </label>
            <p id="ddb-text-count" className="hint">
              {count(text.length)} of {count(maxTextChars)} characters
            </p>
            {tooLong && (
              <p role="alert" className="warn">
                The text is {count(text.length)} characters; the limit is {count(maxTextChars)}. Shorten it to read it.
              </p>
            )}
            <label className="field">
              Character text file
              <input type="file" accept=".txt,.md,text/plain,text/markdown" onChange={textFromFile} />
            </label>
            {fileProblem && (
              <p role="alert" className="warn">
                {fileProblem}
              </p>
            )}
            <button type="button" disabled={reading || !text.trim() || tooLong} onClick={readText}>
              Read the text
            </button>
          </div>
        )}
      </div>
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
          {!!read.summary.notRead && (
            <>
              <dt>Not read</dt>
              <dd>{read.summary.notRead === 1 ? '1 line' : `${read.summary.notRead} lines`}</dd>
            </>
          )}
          <dt>Layout</dt>
          <dd>{read.layoutName || read.layout}</dd>
        </dl>
      )}
    </>
  );
}
