import { describe, expect, it } from 'vitest';
import { fromTemplate, improvementLevels, subclassFeatureLevels, templateEffects, templates } from './templates';

describe('templates (M5 slice 6)', () => {
  it('make an unsaved draft of the right kind for the source, with no name and no revision id', () => {
    for (const t of templates) {
      const draft = fromTemplate(t.id, 'source-1', ['srd-5.2.1'], 'content-1');
      expect(draft).toMatchObject({ kind: t.kind, status: 'draft', name: '', provenance: { sourceId: 'source-1' }, rulesFamilies: ['srd-5.2.1'] });
      expect(draft.revisionId).toBe('00000000-0000-0000-0000-000000000000');
      // Effect ids are unique, so the editor and validation can tell rules apart.
      expect(new Set(draft.effects.map((e) => e.id)).size).toBe(draft.effects.length);
    }
  });

  it('link the resource template: the action and the recovery name the resource', () => {
    const effects = templateEffects('resource-recovery');
    const resource = effects.find((e) => e.type === 'resource');
    expect(effects.filter((e) => (e.type === 'roll' || e.type === 'recovery') && e.resourceId === resource?.resourceId)).toHaveLength(2);
  });

  it('link the stance: the toggle spends the resource, and the bonus applies only while it is on', () => {
    const effects = templateEffects('toggled-stance');
    const toggle = effects.find((e) => e.type === 'toggle');
    const modifier = effects.find((e) => e.type === 'modifier');
    expect(toggle && modifier && modifier.type === 'modifier' && modifier.toggle === toggle.toggleId && modifier.timing === 'whileActive').toBe(true);
    expect(effects.some((e) => e.type === 'resource' && toggle?.type === 'toggle' && e.resourceId === toggle.resourceId)).toBe(true);
  });

  it('leave empty grant slots at the skeleton levels', () => {
    expect(templateEffects('subclass-skeleton').map((e) => (e.type === 'grant' ? [e.level, e.content] : null))).toEqual(subclassFeatureLevels.map((l) => [l, undefined]));
    const cls = templateEffects('class-skeleton');
    expect(cls.filter((e) => e.type === 'grant' && e.grant === 'content').map((e) => (e.type === 'grant' ? e.level : 0))).toEqual([...improvementLevels]);
    expect(cls.some((e) => e.type === 'hitDie' && e.die === 8)).toBe(true);
    expect(cls.some((e) => e.type === 'choice' && e.choiceId === 'subclass' && e.level === 3 && e.options.length === 0)).toBe(true);
    expect(cls.filter((e) => e.type === 'grant' && e.grant === 'proficiency').map((e) => (e.type === 'grant' ? e.target : ''))).toEqual(['save.con', 'save.wis']);
  });
});
