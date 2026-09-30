import type { ContentKind, ContentRevision, Effect, RulesFamilyId } from './api/types';
import { setChoice, setHitDie, toggleSave } from './classBasics';

/**
 * M5 slice 6 (B15; owner decision LIVING_SPECS D14): four starting points for homebrew. A template only fills the
 * studio editor with an unsaved draft; nothing is stored until the author saves it, and it is published only through
 * the usual checks. Every text here is original to TomeStack (no SRD or third-party text; SPEC Q-03). The set is
 * provisional: the M3 gap notes revise it (LIVING_SPECS D13).
 */
export type TemplateId = 'resource-recovery' | 'toggled-stance' | 'subclass-skeleton' | 'class-skeleton';

export interface Template {
  id: TemplateId;
  kind: ContentKind;
  label: string;
  description: string;
}

export const templates: readonly Template[] = [
  {
    id: 'resource-recovery',
    kind: 'feature',
    label: 'A feature with limited uses',
    description: 'A resource (uses equal to your proficiency bonus), an action that spends one use, and a long rest that restores them all.',
  },
  {
    id: 'toggled-stance',
    kind: 'feature',
    label: 'A stance you switch on and off',
    description: 'A toggle that spends one use of a resource when switched on, and a bonus that applies only while it is on.',
  },
  {
    id: 'subclass-skeleton',
    kind: 'subclass',
    label: 'A subclass skeleton',
    description: 'Empty feature slots at class levels 3, 6, 10 and 14. Choose the class choice it joins, then a published feature for each slot.',
  },
  {
    id: 'class-skeleton',
    kind: 'class',
    label: 'A class skeleton',
    description:
      'A d8 hit die, two starting saving throws, a subclass choice at level 3, and reference-only Ability Score Improvement slots at 4, 8, 12, 16 and 19.',
  },
];

/** The class levels the subclass skeleton leaves a feature slot at. */
export const subclassFeatureLevels = [3, 6, 10, 14] as const;

/** The class levels the class skeleton leaves an Ability Score Improvement slot at. */
export const improvementLevels = [4, 8, 12, 16, 19] as const;

/** A grant slot at a level that names no feature yet: validation reports it until the author picks one. */
function slot(id: string, level: number, text: string): Effect {
  return { type: 'grant', id, grant: 'content', level, text };
}

/** The effects of a template (pure; the same input always gives the same effects). */
export function templateEffects(id: TemplateId): Effect[] {
  switch (id) {
    case 'resource-recovery':
      return [
        { type: 'resource', id: 'uses', resourceId: 'uses', label: 'Uses', maximum: 'PB' },
        { type: 'recovery', id: 'uses-long-rest', resourceId: 'uses', on: 'longRest', amount: 'all', timing: 'onLongRest' },
        {
          type: 'roll',
          id: 'use',
          rollId: 'use',
          label: 'Use it',
          dice: '1d6',
          resourceId: 'uses',
          timing: 'onRoll',
          automation: 'assisted',
          text: 'Spend one use. Replace the dice and this text with what your feature does.',
        },
      ];
    case 'toggled-stance':
      return [
        { type: 'resource', id: 'stance-uses', resourceId: 'stance-uses', label: 'Stance uses', maximum: '2' },
        { type: 'recovery', id: 'stance-uses-long-rest', resourceId: 'stance-uses', on: 'longRest', amount: 'all', timing: 'onLongRest' },
        { type: 'toggle', id: 'stance', toggleId: 'stance', label: 'Stance', resourceId: 'stance-uses', text: 'Switch it on as you take the stance, and off when it ends.' },
        { type: 'modifier', id: 'stance-armor', operation: 'bonus', target: 'armorClass', value: '1', toggle: 'stance', timing: 'whileActive' },
      ];
    case 'subclass-skeleton':
      return subclassFeatureLevels.map((level) =>
        slot(`level-${level}-feature`, level, `The feature this subclass gives at class level ${level}. Publish it first, then pick it here.`),
      );
    case 'class-skeleton': {
      let effects: Effect[] = setHitDie([], 8);
      effects = toggleSave(toggleSave(effects, 'con'), 'wis');
      effects = setChoice(effects, 'subclass', { type: 'choice', id: 'subclass', choiceId: 'subclass', count: 1, options: [], level: 3, text: 'Choose a subclass' });
      return [
        ...effects,
        ...improvementLevels.map((level) =>
          slot(
            `improvement-${level}`,
            level,
            `Ability Score Improvement at class level ${level} (reference only: the player applies it). Grant one published, reference-only feature for it here.`,
          ),
        ),
      ];
    }
  }
}

/** An unsaved draft of the template for a source: a new content id, no revision id, no name yet. */
export function fromTemplate(id: TemplateId, sourceId: string, rulesFamilies: RulesFamilyId[], contentId: string): ContentRevision {
  const template = templates.find((t) => t.id === id)!;
  return {
    contentId,
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: template.kind,
    name: '',
    rulesFamilies: [...rulesFamilies],
    provenance: { sourceId },
    status: 'draft',
    // No description: it is shown on the sheet, so a template's instructions must never end up there (review fix).
    effects: templateEffects(id),
  };
}
