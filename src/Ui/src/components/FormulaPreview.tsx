import { useEffect, useState } from 'react';
import { client } from '../api/client';
import type { ContentRevision, FormulaPreviewResult } from '../api/types';

const DEBOUNCE_MS = 300;

/** Formulas with nothing to compute: nothing to preview. */
function previewable(formula: string): boolean {
  const f = formula.trim();
  return f !== '' && f !== 'all' && !/^-?\d+$/.test(f);
}

/**
 * A formula's value at sample class levels (D61), asked 300 ms after the last keystroke. `id` is what the input's
 * aria-describedby points to; the text is read on focus, so it is not a live region.
 */
export function FormulaPreview({ id, formula, revision }: { id: string; formula: string; revision?: ContentRevision }) {
  // The answer is stored with the formula it answers, so a formula that stops being previewable (or changes) never
  // shows an older result.
  const [answer, setAnswer] = useState<{ formula: string; outcome: FormulaPreviewResult | 'failed' } | null>(null);

  useEffect(() => {
    if (!previewable(formula)) return;
    // Each run of this effect owns one request; the cleanup (formula change or unmount) makes its reply stale.
    let stale = false;
    const timer = setTimeout(() => {
      client.previewFormula(formula, revision).then(
        (r) => {
          if (!stale) setAnswer({ formula, outcome: r });
        },
        () => {
          if (!stale) setAnswer({ formula, outcome: 'failed' });
        },
      );
    }, DEBOUNCE_MS);
    return () => {
      stale = true;
      clearTimeout(timer);
    };
  }, [formula, revision]);

  // The container stays (so the input's aria-describedby always resolves) and, for a formula that gets a preview, keeps
  // its room while the answer is on its way, so the form below does not jump on each keystroke.
  const ready = previewable(formula);
  const outcome = ready && answer !== null && answer.formula === formula ? answer.outcome : null;

  let body: string | null = null;
  if (outcome === 'failed') body = 'Preview unavailable.';
  else if (outcome !== null) {
    if (outcome.error != null) body = outcome.error.message;
    else if (outcome.unavailable != null) body = outcome.unavailable;
    else body = outcome.points.map((p) => `Level ${p.level}: ${p.problem ?? p.value ?? '—'}`).join(' · ');
  }

  return (
    <div id={id} className={ready ? 'formula-preview reserved' : 'formula-preview'}>
      {body !== null && (
        <>
          <div>{body}</div>
          <small>Preview, not a character: ability scores 10, proficiency bonus by level.</small>
        </>
      )}
    </div>
  );
}
