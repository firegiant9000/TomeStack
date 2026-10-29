import type { CharacterSheet, CharacterView, ResourceValue } from './api/types';

/** D74: one thing that is new on the sheet since the level-up. Ids and numbers only; the wording is made from the current sheet when shown. */
export type LevelUpChange =
  | { kind: 'feature'; contentId: string }
  | { kind: 'choice'; contentId: string; choiceId: string }
  | { kind: 'hitPoints'; before: number; after: number }
  | { kind: 'slots'; level: number; before: number; after: number }
  | { kind: 'pact'; before?: { maximum: number; level: number }; after?: { maximum: number; level: number } }
  | { kind: 'pool'; contentId: string; resourceId: string; before?: number; after?: number } // before absent = new pool
  | { kind: 'toggle'; contentId: string; toggleId: string };
export type LevelUpList = { level: number; changes: LevelUpChange[] };
export type ChecklistRow = { key: string; text: string; target?: 'play' | 'spells' | 'choices' };

const maxChanges = 64;

/** D74: what a level-up added, from the builder's starting view and the saved view (R16, R18). Matches by contentId, never revisionId. */
export function diffLevelUp(before: CharacterView, after: CharacterView): LevelUpList {
  const b = before.sheet;
  const a = after.sheet;
  const changes: LevelUpChange[] = [];

  const knownFeatures = new Set((b.features ?? []).map((f) => f.content.contentId));
  for (const f of a.features ?? []) {
    if (!knownFeatures.has(f.content.contentId)) changes.push({ kind: 'feature', contentId: f.content.contentId });
  }

  const knownChoices = new Set((b.choices ?? []).map((c) => `${c.source.contentId}\n${c.choiceId}`));
  for (const c of a.choices ?? []) {
    if (!knownChoices.has(`${c.source.contentId}\n${c.choiceId}`)) changes.push({ kind: 'choice', contentId: c.source.contentId, choiceId: c.choiceId });
  }

  if (b.hitPoints && a.hitPoints && b.hitPoints.maximum !== a.hitPoints.maximum) {
    changes.push({ kind: 'hitPoints', before: b.hitPoints.maximum, after: a.hitPoints.maximum });
  }

  // A slot level missing on either side counts as 0, so a new level and one that disappears are both listed.
  const slotMax = (sheet: CharacterSheet) => new Map((sheet.spellSlots ?? []).map((s) => [s.level, s.maximum]));
  const slotsBefore = slotMax(b);
  const slotsAfter = slotMax(a);
  const levels = [...new Set([...slotsBefore.keys(), ...slotsAfter.keys()])].sort((x, y) => x - y);
  for (const level of levels) {
    const was = slotsBefore.get(level) ?? 0;
    const now = slotsAfter.get(level) ?? 0;
    if (was !== now) changes.push({ kind: 'slots', level, before: was, after: now });
  }

  const pactBefore = b.pactSlots ? { maximum: b.pactSlots.maximum, level: b.pactSlots.level } : undefined;
  const pactAfter = a.pactSlots ? { maximum: a.pactSlots.maximum, level: a.pactSlots.level } : undefined;
  if ((pactBefore || pactAfter) && (pactBefore?.maximum !== pactAfter?.maximum || pactBefore?.level !== pactAfter?.level)) {
    const change: LevelUpChange = { kind: 'pact' };
    if (pactBefore) change.before = pactBefore;
    if (pactAfter) change.after = pactAfter;
    changes.push(change);
  }

  const poolKey = (r: ResourceValue) => `${r.content.contentId}\n${r.resourceId}`;
  const poolsBefore = new Map((b.resources ?? []).map((r) => [poolKey(r), r]));
  for (const r of a.resources ?? []) {
    const was = poolsBefore.get(poolKey(r));
    if (!was) {
      const change: LevelUpChange = { kind: 'pool', contentId: r.content.contentId, resourceId: r.resourceId };
      if (r.maximum !== undefined) change.after = r.maximum;
      changes.push(change);
    } else if (was.maximum !== undefined && r.maximum !== undefined && was.maximum !== r.maximum) {
      changes.push({ kind: 'pool', contentId: r.content.contentId, resourceId: r.resourceId, before: was.maximum, after: r.maximum });
    }
  }

  const toggleKey = (contentId: string, toggleId: string) => `${contentId}\n${toggleId}`;
  const togglesBefore = new Set((b.toggles ?? []).map((t) => toggleKey(t.content.contentId, t.toggleId)));
  for (const t of a.toggles ?? []) {
    if (!togglesBefore.has(toggleKey(t.content.contentId, t.toggleId))) changes.push({ kind: 'toggle', contentId: t.content.contentId, toggleId: t.toggleId });
  }

  return { level: after.character.level, changes };
}

const pactText = (p: { maximum: number; level: number }) => `${p.maximum} at level ${p.level}`;

function refresh(resource: ResourceValue): string {
  const recoveries = resource.recoveries ?? [];
  if (resource.allowance) return ', refreshes each round';
  if (recoveries.some((r) => r.on === 'shortRest')) return ', refreshes on a short rest';
  if (recoveries.some((r) => r.on === 'longRest')) return ', refreshes on a long rest';
  return '';
}

/** D74: the rows to show, worded from the current sheet. A change whose content is gone drops silently (R17). */
export function checklistRows(checklist: LevelUpList, sheet: CharacterSheet): ChecklistRow[] {
  const features = sheet.features ?? [];
  const choices = sheet.choices ?? [];
  const resources = sheet.resources ?? [];
  const toggles = sheet.toggles ?? [];
  const rows: ChecklistRow[] = [];
  const add = (row: ChecklistRow) => {
    // A9: two grouped toggles of one content give one form row; other rows with the same wording are distinct changes.
    if (!(row.key.startsWith('toggle:') && rows.some((r) => r.key.startsWith('toggle:') && r.text === row.text))) rows.push(row);
  };
  const ofKind = <K extends LevelUpChange['kind']>(kind: K) => checklist.changes.filter((c): c is Extract<LevelUpChange, { kind: K }> => c.kind === kind);

  for (const c of ofKind('feature')) {
    const feature = features.find((f) => f.content.contentId === c.contentId);
    if (feature) add({ key: `feature:${c.contentId}`, text: `New feature: ${feature.name}` });
  }
  for (const c of ofKind('choice')) {
    const choice = choices.find((x) => x.source.contentId === c.contentId && x.choiceId === c.choiceId);
    if (!choice) continue;
    const state = choice.resolved ? 'made' : 'still to make';
    add({ key: `choice:${c.contentId}:${c.choiceId}`, text: `New choice: ${choice.sourceName}: choose ${choice.count} (${state})`, target: choice.resolved ? undefined : 'choices' });
  }
  for (const c of ofKind('hitPoints')) add({ key: 'hitPoints', text: `Hit point maximum: ${c.before} → ${c.after}`, target: 'play' });
  for (const c of ofKind('slots')) add({ key: `slots:${c.level}`, text: `Level ${c.level} spell slots: ${c.before} → ${c.after}`, target: 'spells' });
  for (const c of ofKind('pact')) {
    if (c.after) {
      const text = c.before ? `Pact slots: ${pactText(c.before)} → ${pactText(c.after)}` : `New pact slots: ${pactText(c.after)}`;
      add({ key: 'pact', text, target: 'spells' });
    } else if (c.before) {
      add({ key: 'pact', text: `Pact slots: ${pactText(c.before)} → none`, target: 'spells' });
    }
  }
  for (const c of ofKind('pool')) {
    const resource = resources.find((r) => r.content.contentId === c.contentId && r.resourceId === c.resourceId);
    if (!resource) continue;
    const key = `pool:${c.contentId}:${c.resourceId}`;
    if (c.before === undefined) {
      const maximum = resource.maximum !== undefined ? `, ${resource.maximum}` : '';
      add({ key, text: `New pool: ${resource.label}${maximum}${refresh(resource)}`, target: 'play' });
    } else if (c.after !== undefined) {
      add({ key, text: `${resource.label}: ${c.before} → ${c.after}`, target: 'play' });
    }
  }
  for (const c of ofKind('toggle')) {
    const toggle = toggles.find((t) => t.content.contentId === c.contentId && t.toggleId === c.toggleId);
    if (!toggle) continue;
    add({
      key: `toggle:${c.contentId}:${c.toggleId}`,
      text: toggle.group ? `New form available: ${toggle.contentName}` : `New toggle: ${toggle.label}`,
      target: 'play',
    });
  }

  const byHand = ofKind('feature')
    .map((c) => features.find((f) => f.content.contentId === c.contentId))
    .filter((f) => f !== undefined && f.automation !== 'automatic')
    .map((f) => f!.name);
  if (byHand.length > 0) add({ key: 'byHand', text: `By hand from now on: ${byHand.join(', ')}` });

  return rows;
}

/** True when no row except hit points remains (drives "Nothing new besides hit points"). */
export function onlyHitPoints(rows: ChecklistRow[]): boolean {
  return rows.length > 0 && rows.every((r) => r.key.startsWith('hitPoints'));
}

/**
 * D74: the level-up list for a character, a preference of this app on this machine. It lives in the page's own storage
 * (the WebView2 profile in the data folder), so it is in no package, share or library backup, and nothing else reads it.
 * Same pattern as `sheetTab.ts`. It holds ids and numbers only and stays until Dismiss removes the key.
 */
const key = (characterId: string) => `tomestack.levelUpChecklist.${characterId}`;

const isText = (v: unknown): v is string => typeof v === 'string';
const isNum = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
const isPact = (v: unknown): v is { maximum: number; level: number } =>
  typeof v === 'object' && v !== null && isNum((v as { maximum?: unknown }).maximum) && isNum((v as { level?: unknown }).level);

/** Keeps only the known fields of a well-formed change; anything else is undefined. */
function normaliseChange(raw: unknown): LevelUpChange | undefined {
  if (typeof raw !== 'object' || raw === null) return undefined;
  const c = raw as Record<string, unknown>;
  switch (c.kind) {
    case 'feature':
      return isText(c.contentId) ? { kind: 'feature', contentId: c.contentId } : undefined;
    case 'choice':
      return isText(c.contentId) && isText(c.choiceId) ? { kind: 'choice', contentId: c.contentId, choiceId: c.choiceId } : undefined;
    case 'hitPoints':
      return isNum(c.before) && isNum(c.after) ? { kind: 'hitPoints', before: c.before, after: c.after } : undefined;
    case 'slots':
      return isNum(c.level) && isNum(c.before) && isNum(c.after) ? { kind: 'slots', level: c.level, before: c.before, after: c.after } : undefined;
    case 'pact': {
      if (c.before !== undefined && !isPact(c.before)) return undefined;
      if (c.after !== undefined && !isPact(c.after)) return undefined;
      if (!c.before && !c.after) return undefined;
      const change: LevelUpChange = { kind: 'pact' };
      if (c.before) change.before = { maximum: c.before.maximum, level: c.before.level };
      if (c.after) change.after = { maximum: c.after.maximum, level: c.after.level };
      return change;
    }
    case 'pool': {
      if (!isText(c.contentId) || !isText(c.resourceId)) return undefined;
      if ((c.before !== undefined && !isNum(c.before)) || (c.after !== undefined && !isNum(c.after))) return undefined;
      if (c.before !== undefined && c.after === undefined) return undefined; // a grown pool needs both numbers
      const change: LevelUpChange = { kind: 'pool', contentId: c.contentId, resourceId: c.resourceId };
      if (c.before !== undefined) change.before = c.before;
      if (c.after !== undefined) change.after = c.after;
      return change;
    }
    case 'toggle':
      return isText(c.contentId) && isText(c.toggleId) ? { kind: 'toggle', contentId: c.contentId, toggleId: c.toggleId } : undefined;
    default:
      return undefined;
  }
}

export function readChecklist(characterId: string, level: number): LevelUpList | undefined {
  try {
    const stored = globalThis.localStorage?.getItem(key(characterId));
    if (!stored) return undefined;
    const parsed: unknown = JSON.parse(stored);
    if (typeof parsed !== 'object' || parsed === null) return undefined;
    const { level: storedLevel, changes } = parsed as { level?: unknown; changes?: unknown };
    if (!Number.isInteger(storedLevel) || (storedLevel as number) < 1 || (storedLevel as number) > 20 || storedLevel !== level) return undefined;
    if (!Array.isArray(changes)) return undefined;
    const kept = changes.map(normaliseChange).filter((c): c is LevelUpChange => c !== undefined).slice(0, maxChanges);
    return { level, changes: kept };
  } catch {
    return undefined; // storage unavailable or unreadable: no list
  }
}

export function writeChecklist(characterId: string, checklist: LevelUpList): void {
  try {
    globalThis.localStorage?.setItem(key(characterId), JSON.stringify({ level: checklist.level, changes: checklist.changes.slice(0, maxChanges) }));
  } catch {
    // storage unavailable: the list is not kept
  }
}

export function dismissChecklist(characterId: string): void {
  try {
    globalThis.localStorage?.removeItem(key(characterId));
  } catch {
    // storage unavailable: nothing to remove
  }
}
