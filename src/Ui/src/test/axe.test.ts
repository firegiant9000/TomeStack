// @vitest-environment jsdom
// The axe helper: a clean fragment passes, a missing label fails naming the rule, and two runs in a row both settle
// (axe is global, so each run is awaited and no axe test uses test.concurrent).
import { afterEach, expect, it } from 'vitest';
import { expectNoAxeViolations } from './axe';

afterEach(() => {
  document.body.innerHTML = '';
});

function mount(html: string): HTMLElement {
  const host = document.createElement('div');
  host.innerHTML = html;
  document.body.appendChild(host);
  return host;
}

it('passes a clean fragment', async () => {
  await expectNoAxeViolations(mount('<button>Go</button>'));
});

it('fails an input with no label, naming the label rule', async () => {
  await expect(expectNoAxeViolations(mount('<input type="text" />'))).rejects.toThrow(/label/);
});

it('Runs_twice_in_a_row', async () => {
  const host = mount('<button>Go</button>');
  await expectNoAxeViolations(host);
  await expectNoAxeViolations(host);
});

it('lets the caller switch a rule off', async () => {
  await expectNoAxeViolations(mount('<input type="text" />'), { rules: { label: { enabled: false } } });
});
