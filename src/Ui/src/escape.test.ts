// @vitest-environment jsdom
import { afterEach, expect, it } from 'vitest';
import { closeOnEscape } from './escape';

afterEach(() => {
  document.body.innerHTML = '';
});

function esc(target: Element, init: KeyboardEventInit = {}) {
  const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

it('closes an open details and returns focus to its summary', () => {
  document.body.innerHTML = '<details open><summary>More</summary><button id="inner">Inner</button></details>';
  const inner = document.getElementById('inner')!;
  inner.focus();
  const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
  Object.defineProperty(event, 'target', { value: inner });
  expect(closeOnEscape(event)).toBe(true);
  expect(document.querySelector('details')!.open).toBe(false);
  expect(document.activeElement).toBe(document.querySelector('summary'));
  expect(event.defaultPrevented).toBe(true);
});

it('closes a panel its aria-expanded button controls and focuses that button', () => {
  document.body.innerHTML =
    '<button id="t" aria-expanded="true" aria-controls="p">Toggle</button><div id="p"><button id="inner">Inner</button></div>';
  const toggle = document.getElementById('t')!;
  let clicks = 0;
  toggle.addEventListener('click', () => {
    clicks += 1;
    toggle.setAttribute('aria-expanded', 'false');
  });
  const inner = document.getElementById('inner')!;
  inner.focus();
  const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
  Object.defineProperty(event, 'target', { value: inner });
  expect(closeOnEscape(event)).toBe(true);
  expect(clicks).toBe(1);
  expect(document.activeElement).toBe(toggle);
});

it('does nothing when nothing is open, when the key is not Esc, or when already handled', () => {
  document.body.innerHTML =
    '<details><summary>More</summary><button id="a">A</button></details><button id="t" aria-expanded="false" aria-controls="p">T</button><div id="p"><button id="b">B</button></div>';
  const a = document.getElementById('a')!;
  const b = document.getElementById('b')!;
  expect(closeOnEscape(esc(a))).toBe(false);
  expect(closeOnEscape(esc(b))).toBe(false);
  document.body.innerHTML = '<details open><button id="c">C</button></details>';
  const c = document.getElementById('c')!;
  expect(closeOnEscape(esc(c, { key: 'Enter' }))).toBe(false);
  const handled = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
  Object.defineProperty(handled, 'target', { value: c });
  handled.preventDefault();
  expect(closeOnEscape(handled)).toBe(false);
  expect(document.querySelector('details')!.open).toBe(true);
});

it('leaves an open dialog to its own handler', () => {
  document.body.innerHTML = '<details open><div role="alertdialog"><button id="d">D</button></div></details>';
  const d = document.getElementById('d')!;
  const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
  Object.defineProperty(event, 'target', { value: d });
  expect(closeOnEscape(event)).toBe(false);
  expect(document.querySelector('details')!.open).toBe(true);
});
