import type { SpellEntry } from '../../api/types';
import { foldName, type CastingBucket, type SpellFilter } from '../../spellSearch';

/** P09 (D70): one caster's filter choices; a tri-state is 'any', 'yes' or 'no'. Component state, not persisted. */
export type Tri = 'any' | 'yes' | 'no';
export interface SpellFilterState {
  levels: number[];
  schools: string[];
  casting: CastingBucket[];
  concentration: Tri;
  ritual: Tri;
  /** MECH-X item 1 (R39): the ticked spell tags. */
  tags: string[];
}

export const noFilters: SpellFilterState = { levels: [], schools: [], casting: [], concentration: 'any', ritual: 'any', tags: [] };

export const filtersActive = (f: SpellFilterState): boolean =>
  f.levels.length > 0 || f.schools.length > 0 || f.casting.length > 0 || f.concentration !== 'any' || f.ritual !== 'any' || f.tags.length > 0;

const tri = (t: Tri): boolean | undefined => (t === 'any' ? undefined : t === 'yes');

/** The input for `filterSpells`: the text query plus these choices. */
export const toSpellFilter = (text: string, f: SpellFilterState): SpellFilter => ({
  text, levels: f.levels, schools: f.schools, casting: f.casting, concentration: tri(f.concentration), ritual: tri(f.ritual), tags: f.tags,
});

const castingLabels: [CastingBucket, string][] = [
  ['action', 'Action'],
  ['bonusAction', 'Bonus action'],
  ['reaction', 'Reaction'],
  ['longer', 'Longer or not stated'],
];

/** The schools present, compared case-insensitively (imported spells may differ in case); the first spelling seen is shown. */
function schoolsPresent(spells: SpellEntry[]): string[] {
  const seen = new Map<string, string>();
  for (const s of spells) if (s.school && !seen.has(foldName(s.school))) seen.set(foldName(s.school), s.school);
  return [...seen.values()].sort((a, b) => a.localeCompare(b));
}

/** The tags present, compared case-insensitively; the first spelling seen is shown. The group is drawn only when this is not empty. */
function tagsPresent(spells: SpellEntry[]): string[] {
  const seen = new Map<string, string>();
  for (const s of spells) for (const t of s.tags ?? []) if (!seen.has(foldName(t))) seen.set(foldName(t), t);
  return [...seen.values()].sort((a, b) => a.localeCompare(b));
}

const toggle = <T,>(list: T[], item: T): T[] => (list.includes(item) ? list.filter((x) => x !== item) : [...list, item]);

/**
 * P09 (D70): a collapsible "Filters" group above one caster's list. The button sets `aria-expanded`; every control is a
 * native checkbox, select or button, so Tab reaches them all. The count is announced by the search box's existing polite
 * region, not here.
 */
export function SpellFilters(props: {
  id: string;
  casterName: string;
  spells: SpellEntry[];
  value: SpellFilterState;
  onChange: (value: SpellFilterState) => void;
  open: boolean;
  onToggle: () => void;
}) {
  const { value, onChange } = props;
  const schools = schoolsPresent(props.spells);
  const tags = tagsPresent(props.spells);
  const panelId = `${props.id}-panel`;
  const triSelect = (key: 'concentration' | 'ritual', label: string) => (
    <div className="spell-filter-select">
      <label htmlFor={`${props.id}-${key}`}>{label}</label>
      <select id={`${props.id}-${key}`} value={value[key]} onChange={(e) => onChange({ ...value, [key]: e.target.value as Tri })}>
        <option value="any">Any</option>
        <option value="yes">Yes</option>
        <option value="no">No</option>
      </select>
    </div>
  );
  return (
    <div className="spell-filters">
      <button type="button" aria-expanded={props.open} aria-controls={panelId} aria-label={`Filters: ${props.casterName} spells${filtersActive(value) ? ', on' : ''}`} onClick={props.onToggle}>
        Filters{filtersActive(value) ? ' (on)' : ''}
      </button>
      {props.open && (
        <fieldset id={panelId}>
          <legend>Filter {props.casterName} spells</legend>
          <fieldset>
            <legend>Level</legend>
            {Array.from({ length: 10 }, (_, level) => (
              <label key={level} className="choice">
                <input type="checkbox" checked={value.levels.includes(level)} onChange={() => onChange({ ...value, levels: toggle(value.levels, level) })} />
                {level === 0 ? 'Cantrip' : `Level ${level}`}
              </label>
            ))}
          </fieldset>
          {schools.length > 0 && (
            <fieldset>
              <legend>School</legend>
              {schools.map((school) => {
                const ticked = value.schools.some((s) => foldName(s) === foldName(school));
                return (
                  <label key={foldName(school)} className="choice">
                    <input
                      type="checkbox"
                      checked={ticked}
                      onChange={() => onChange({ ...value, schools: ticked ? value.schools.filter((s) => foldName(s) !== foldName(school)) : [...value.schools, school] })}
                    />
                    {school}
                  </label>
                );
              })}
            </fieldset>
          )}
          <fieldset>
            <legend>Casting time</legend>
            {castingLabels.map(([bucket, label]) => (
              <label key={bucket} className="choice">
                <input type="checkbox" checked={value.casting.includes(bucket)} onChange={() => onChange({ ...value, casting: toggle(value.casting, bucket) })} />
                {label}
              </label>
            ))}
          </fieldset>
          {tags.length > 0 && (
            <fieldset>
              <legend>Tags</legend>
              {tags.map((tag) => {
                const ticked = value.tags.some((t) => foldName(t) === foldName(tag));
                return (
                  <label key={foldName(tag)} className="choice">
                    <input
                      type="checkbox"
                      checked={ticked}
                      onChange={() => onChange({ ...value, tags: ticked ? value.tags.filter((t) => foldName(t) !== foldName(tag)) : [...value.tags, tag] })}
                    />
                    {tag}
                  </label>
                );
              })}
            </fieldset>
          )}
          {triSelect('concentration', 'Concentration')}
          {triSelect('ritual', 'Ritual')}
          <button type="button" onClick={() => onChange(noFilters)}>
            Clear filters
          </button>
        </fieldset>
      )}
    </div>
  );
}
