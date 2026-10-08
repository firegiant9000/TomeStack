/** D30: a labelled search box with a polite "N of M spells shown" status only while a query is typed. */
export function SpellSearch(props: { id: string; label: string; value: string; onChange: (value: string) => void; shown: number; total: number }) {
  const active = props.value.trim() !== '';
  return (
    <div className="spell-search">
      <label className="field" htmlFor={props.id}>{props.label}</label>
      <input id={props.id} type="search" value={props.value} onChange={(e) => props.onChange(e.target.value)} autoComplete="off" />
      <p className="hint" aria-live="polite">{active ? `${props.shown} of ${props.total} spells shown` : ''}</p>
    </div>
  );
}
