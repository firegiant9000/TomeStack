import type { ReactNode } from 'react';

/**
 * Renders the guide text under docs/guides. Only these constructs are supported: `##` and `###` headings, paragraphs,
 * `-` lists, `**bold**` and `` `code` ``. Everything else is shown as plain text. Elements are built, never injected
 * as HTML. The Help screen puts this under its own section h2, so `##` renders as h3 and `###` as h4.
 */
export function GuideText({ markdown }: { markdown: string }) {
  const blocks: ReactNode[] = [];
  let paragraph: string[] = [];
  let items: string[] = [];

  const flush = () => {
    if (paragraph.length > 0) {
      blocks.push(<p key={blocks.length}>{inline(paragraph.join(' '))}</p>);
      paragraph = [];
    }
    if (items.length > 0) {
      blocks.push(
        <ul key={blocks.length}>
          {items.map((item, i) => (
            <li key={i}>{inline(item)}</li>
          ))}
        </ul>,
      );
      items = [];
    }
  };

  for (const raw of markdown.split(/\r?\n/)) {
    const line = raw.trim();
    if (line === '') {
      flush();
    } else if (line.startsWith('### ')) {
      flush();
      blocks.push(<h4 key={blocks.length}>{inline(line.slice(4))}</h4>);
    } else if (line.startsWith('## ')) {
      flush();
      blocks.push(<h3 key={blocks.length}>{inline(line.slice(3))}</h3>);
    } else if (line.startsWith('- ')) {
      if (paragraph.length > 0) flush();
      items.push(line.slice(2));
    } else {
      if (items.length > 0) flush();
      paragraph.push(line);
    }
  }
  flush();

  return <div className="guide-text">{blocks}</div>;
}

/** `**bold**` and `` `code` `` spans; anything else, including angle brackets, stays text. */
function inline(text: string): ReactNode[] {
  return text
    .split(/(\*\*[^*]+\*\*|`[^`]+`)/)
    .filter((part) => part !== '')
    .map((part, i) => {
      if (part.startsWith('**') && part.endsWith('**') && part.length > 4) return <strong key={i}>{part.slice(2, -2)}</strong>;
      if (part.startsWith('`') && part.endsWith('`') && part.length > 2) return <code key={i}>{part.slice(1, -1)}</code>;
      return part;
    });
}
