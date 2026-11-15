import type { ReactNode } from 'react';

/** A standing explanation kept out of the way: a closed "?" disclosure. Plain details/summary, so it opens from the keyboard. */
export function HelpNote({ topic, className, children }: { topic: string; className?: string; children: ReactNode }) {
  return (
    <details className={className ? `help-note ${className}` : 'help-note'}>
      <summary aria-label={`Help: ${topic}`}>?</summary>
      <div className="hint">{children}</div>
    </details>
  );
}
