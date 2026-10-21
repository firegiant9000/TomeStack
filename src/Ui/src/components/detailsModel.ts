import type { CharacterDetails } from '../api/types';

/** The service's bounds (character schema v9, D42). The form blocks over-limit input first; the service still refuses it. */
export const maxDetailText = 2000;
export const maxListEntries = 30;
export const maxListEntry = 80;

export type TextKey =
  | 'alignment'
  | 'personalityTraits'
  | 'ideals'
  | 'bonds'
  | 'flaws'
  | 'age'
  | 'height'
  | 'weight'
  | 'eyes'
  | 'skin'
  | 'hair'
  | 'backstory'
  | 'alliesAndOrganisations';
export type ListKey = 'languages' | 'toolsAndOther';
export type DraftKey = TextKey | ListKey;
export type Draft = Record<DraftKey, string>;

export interface TextField {
  key: TextKey;
  label: string;
  /** A text box of several lines; otherwise a single line. */
  multiline: boolean;
}

export const personalityFields: readonly TextField[] = [
  { key: 'alignment', label: 'Alignment', multiline: false },
  { key: 'personalityTraits', label: 'Personality traits', multiline: true },
  { key: 'ideals', label: 'Ideals', multiline: true },
  { key: 'bonds', label: 'Bonds', multiline: true },
  { key: 'flaws', label: 'Flaws', multiline: true },
];
export const appearanceFields: readonly TextField[] = [
  { key: 'age', label: 'Age', multiline: false },
  { key: 'height', label: 'Height', multiline: false },
  { key: 'weight', label: 'Weight', multiline: false },
  { key: 'eyes', label: 'Eyes', multiline: false },
  { key: 'skin', label: 'Skin', multiline: false },
  { key: 'hair', label: 'Hair', multiline: false },
];
export const backgroundFields: readonly TextField[] = [
  { key: 'backstory', label: 'Backstory', multiline: true },
  { key: 'alliesAndOrganisations', label: 'Allies and organisations', multiline: true },
];
export const listFields: readonly { key: ListKey; label: string }[] = [
  { key: 'languages', label: 'Languages' },
  { key: 'toolsAndOther', label: 'Tools and other' },
];

const allTextFields = [...personalityFields, ...appearanceFields, ...backgroundFields];

function textOf(details: CharacterDetails | undefined, key: TextKey): string {
  if (!details) return '';
  if (['age', 'height', 'weight', 'eyes', 'skin', 'hair'].includes(key)) return details.appearance?.[key as keyof NonNullable<CharacterDetails['appearance']>] ?? '';
  return (details as Record<string, unknown>)[key] as string ?? '';
}

/** The form's starting text: one line per list entry. */
export function toDraft(details: CharacterDetails | undefined): Draft {
  const draft = { languages: (details?.languages ?? []).join('\n'), toolsAndOther: (details?.toolsAndOther ?? []).join('\n') } as Draft;
  for (const field of allTextFields) draft[field.key] = textOf(details, field.key);
  return draft;
}

export interface ListReading {
  /** Non-blank lines, trimmed. */
  entries: string[];
  /** 1-based line numbers (counting blank lines) of entries over the entry limit. */
  tooLong: number[];
}

/** Blank lines are ignored; nothing else is dropped, so an entry that is too long is reported, never cut. */
export function readList(text: string): ListReading {
  const entries: string[] = [];
  const tooLong: number[] = [];
  text.split(/\r?\n/).forEach((line, index) => {
    const entry = line.trim();
    if (entry === '') return;
    entries.push(entry);
    if (entry.length > maxListEntry) tooLong.push(index + 1);
  });
  return { entries, tooLong };
}

/** The first problem for a text, or undefined. */
export function textProblem(text: string): string | undefined {
  const over = text.trim().length - maxDetailText;
  return over > 0 ? `This is ${over.toLocaleString('en-US')} character${over === 1 ? '' : 's'} over the ${maxDetailText.toLocaleString('en-US')} limit. Shorten it to save.` : undefined;
}

export function listProblem(text: string): string | undefined {
  const { entries, tooLong } = readList(text);
  const problems: string[] = [];
  if (entries.length > maxListEntries) problems.push(`There are ${entries.length} entries; ${maxListEntries} is the most.`);
  if (tooLong.length > 0)
    problems.push(`${tooLong.length === 1 ? `Line ${tooLong[0]} is` : `Lines ${tooLong.join(', ')} are`} longer than ${maxListEntry} characters.`);
  return problems.length > 0 ? `${problems.join(' ')} Shorten or remove ${tooLong.length > 0 ? 'them' : 'some'} to save.` : undefined;
}

/** True when any field would be refused by the service. */
export function hasProblem(draft: Draft): boolean {
  return allTextFields.some((f) => textProblem(draft[f.key]) !== undefined) || listFields.some((f) => listProblem(draft[f.key]) !== undefined);
}

/** The details the draft stands for (text trimmed; empty text and lists absent), or undefined when nothing is set. */
export function buildDetails(draft: Draft): CharacterDetails | undefined {
  const text = (key: TextKey) => {
    const value = draft[key].trim();
    return value === '' ? undefined : value;
  };
  const list = (key: ListKey) => {
    const entries = readList(draft[key]).entries;
    return entries.length === 0 ? undefined : entries;
  };
  const appearance = {
    age: text('age'),
    height: text('height'),
    weight: text('weight'),
    eyes: text('eyes'),
    skin: text('skin'),
    hair: text('hair'),
  };
  const result: CharacterDetails = {
    alignment: text('alignment'),
    personalityTraits: text('personalityTraits'),
    ideals: text('ideals'),
    bonds: text('bonds'),
    flaws: text('flaws'),
    appearance: Object.values(appearance).some((v) => v !== undefined) ? appearance : undefined,
    backstory: text('backstory'),
    alliesAndOrganisations: text('alliesAndOrganisations'),
    languages: list('languages'),
    toolsAndOther: list('toolsAndOther'),
  };
  // JSON drops the undefined members, so the saved shape has only what is set.
  const clean = JSON.parse(JSON.stringify(result)) as CharacterDetails;
  return Object.keys(clean).length === 0 ? undefined : clean;
}

/** Label and text of each detail that is set, for the printed sheet. Lists read as comma-separated entries. */
export function detailRows(details: CharacterDetails | undefined): [string, string][] {
  const draft = toDraft(details);
  const rows: [string, string][] = [];
  for (const field of [...personalityFields, ...appearanceFields, ...backgroundFields]) {
    const value = draft[field.key].trim();
    if (value) rows.push([field.label, value]);
  }
  for (const field of listFields) {
    const entries = readList(draft[field.key]).entries;
    if (entries.length > 0) rows.push([field.label, entries.join(', ')]);
  }
  return rows;
}
