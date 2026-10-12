/** At most this many pips are drawn; the text beside them always says the real numbers. */
const drawnPips = 12;

/**
 * Investigation 2026-10-06 (items 8, 12): pips beside "N of M" for spell slots and resources. Decoration only (aria-hidden,
 * no text), so the heading's text stays the one thing read and asserted. Filled pips are the remaining uses.
 */
export function Pips({ filled, total }: { filled: number; total: number }) {
  if (total <= 0) return null;
  const drawn = Math.min(total, drawnPips);
  return (
    <span className="pips" aria-hidden="true">
      {Array.from({ length: drawn }, (_, i) => (
        <span key={i} className="pip" data-filled={i < filled ? 'true' : 'false'} />
      ))}
      {total > drawn && <span className="pip-more" data-more={total - drawn} />}
    </span>
  );
}
