// Accessibility assertion for component tests. axe is global state, so every run is awaited and callers must not use
// test.concurrent. jsdom has no layout, so colour contrast stays a manual High Contrast check (accessibility checklist).
import axe from 'axe-core';

const defaultRules: axe.RuleObject = { 'color-contrast': { enabled: false } };

export async function expectNoAxeViolations(container: Element, options: axe.RunOptions = {}): Promise<void> {
  const results = await axe.run(container, { ...options, rules: { ...defaultRules, ...options.rules } });
  if (results.violations.length === 0) return;
  const lines = results.violations.map((v) => `- ${v.id}: ${v.help} [${v.nodes.map((n) => n.target.join(' ')).join('; ')}]`);
  throw new Error(`${results.violations.length} axe violation(s):\n${lines.join('\n')}`);
}
