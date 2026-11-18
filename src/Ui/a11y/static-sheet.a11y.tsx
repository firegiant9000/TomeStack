// 4.8: axe in the installed Edge, with colour contrast on, over the generated static session sheet (the golden export).
// Only the page's own <style> and <main> go into the test document, never src/styles.css or the CSP meta, so the page
// is judged on its own CSS. Runs once per colour scheme (see vitest.a11y.config.ts).
import { afterEach, describe, expect, it } from 'vitest';
import html from '../../../tests/AppService.Tests/Golden/multiclass-caster.tomestack-sheet.html?raw';
import { expectNoSeriousAxe } from './axe';

const added: Element[] = [];

afterEach(() => {
  for (const element of added.splice(0)) element.remove();
});

function mount(): HTMLElement {
  const parsed = new DOMParser().parseFromString(html, 'text/html');
  document.documentElement.lang = 'en';
  document.title = parsed.title;
  const style = parsed.querySelector('style');
  const main = parsed.querySelector('main');
  if (!style || !main) throw new Error('The golden sheet has no <style> or <main>.');
  const styleCopy = document.importNode(style, true);
  document.head.appendChild(styleCopy);
  const container = document.createElement('div');
  container.appendChild(document.importNode(main, true));
  document.body.appendChild(container);
  added.push(styleCopy, container);
  return container;
}

describe('static session sheet', () => {
  it('has no script', () => {
    const parsed = new DOMParser().parseFromString(html, 'text/html');
    expect(parsed.querySelectorAll('script').length).toBe(0);
  });

  it('has no serious axe violations', async () => {
    await expectNoSeriousAxe(mount(), 'static sheet');
  });

  it('has no serious axe violations with every details open', async () => {
    const container = mount();
    container.querySelectorAll('details').forEach((d) => {
      d.open = true;
    });
    await expectNoSeriousAxe(container, 'static sheet, details open');
  });
});
