// Real-browser axe run (4.1): every rule on, including color-contrast, which jsdom cannot compute. `serious` and
// `critical` violations fail; `moderate` and `minor` are only warned. axe is global state, so runs are awaited one by one.
import axe from 'axe-core';

type Result = axe.Result;

function describeNode(violation: Result, node: Result['nodes'][number]): string {
  const target = `${node.target.join(' ')} ${node.html.slice(0, 140).replace(/\s+/g, ' ')}`;
  if (violation.id !== 'color-contrast') return target;
  const data = node.any.find((c) => c.id === 'color-contrast')?.data as
    | { fgColor?: string; bgColor?: string; contrastRatio?: number; expectedContrastRatio?: string }
    | undefined;
  return data ? `${target} (fg ${data.fgColor}, bg ${data.bgColor}, ratio ${data.contrastRatio}, needs ${data.expectedContrastRatio})` : target;
}

function lines(violations: Result[]): string[] {
  return violations.map((v) => `- [${v.impact}] ${v.id}: ${v.help}\n    ${v.nodes.map((n) => describeNode(v, n)).join('\n    ')}`);
}

export async function expectNoSeriousAxe(root: Element, label: string): Promise<void> {
  const results = await axe.run(root);
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  const minor = results.violations.filter((v) => v.impact !== 'serious' && v.impact !== 'critical');
  if (minor.length > 0) console.warn(`axe (${label}): ${minor.length} moderate/minor violation(s)\n${lines(minor).join('\n')}`);
  if (blocking.length > 0) throw new Error(`axe (${label}): ${blocking.length} serious/critical violation(s)\n${lines(blocking).join('\n')}`);
}
