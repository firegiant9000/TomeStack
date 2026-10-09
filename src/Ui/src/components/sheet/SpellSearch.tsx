import { foldName } from '../../spellSearch';

/**
 * D30: a labelled search box with a polite "N of M spells shown" status only while a query filters something (a blank or
 * marks-only query matches everything and is silent). The builder also says how many chosen spells the search hides (R23).
 */
export function SpellSearch(props: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  shown: number;
  total: number;
  hiddenChosen?: number;
}) {
  const active = foldName(props.value) !== '';
  const hidden = props.hiddenChosen ?? 0;
  const status = active ? `${props.shown} of ${props.total} spells shown${hidden > 0 ? `, ${hidden} chosen hidden by the search` : ''}` : '';
  return (
    <div className="spell-search">
      <label className="field" htmlFor={props.id}>{props.label}</label>
      <input id={props.id} type="search" value={props.value} onChange={(e) => props.onChange(e.target.value)} autoComplete="off" />
      <p className="hint" aria-live="polite">{status}</p>
    </div>
  );
}
