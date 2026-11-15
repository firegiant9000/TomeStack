// @vitest-environment jsdom
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import gettingStarted from '../../../../../docs/guides/getting-started.md?raw';
import knownGaps from '../../../../../docs/guides/known-gaps.md?raw';
import { GuideText } from './GuideText';

describe('GuideText', () => {
  it('renders ## as h3 and ### as h4', () => {
    render(<GuideText markdown={'## Big\n\n### Small'} />);
    expect(screen.getByRole('heading', { level: 3, name: 'Big' })).toBeTruthy();
    expect(screen.getByRole('heading', { level: 4, name: 'Small' })).toBeTruthy();
  });

  it('renders paragraphs, list items, bold and code', () => {
    const { container } = render(<GuideText markdown={'Some **bold** and `code` text.\nSecond line.\n\n- one\n- **two**'} />);
    expect(container.querySelectorAll('p')).toHaveLength(1);
    expect(container.querySelector('p strong')?.textContent).toBe('bold');
    expect(container.querySelector('p code')?.textContent).toBe('code');
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
    expect(container.querySelector('li strong')?.textContent).toBe('two');
  });

  it('ends a list at the next line of text, which becomes its own paragraph (CRLF input)', () => {
    const { container } = render(<GuideText markdown={'a\r\n\r\n- x\r\nafter **open'} />);
    expect(container.querySelectorAll('ul')).toHaveLength(1);
    expect(Array.from(container.querySelectorAll('li')).map((li) => li.textContent)).toEqual(['x']);
    expect(Array.from(container.querySelectorAll('p')).map((p) => p.textContent)).toEqual(['a', 'after **open']);
    expect(container.querySelector('strong')).toBeNull();
  });

  it('shows unsupported-looking text as plain text, not HTML', () => {
    const { container } = render(<GuideText markdown={'<b>hi</b> [x](http://e.com) <script>alert(1)</script>'} />);
    expect(container.querySelector('b')).toBeNull();
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('a')).toBeNull();
    expect(container.textContent).toContain('<b>hi</b>');
  });
});

describe('guides', () => {
  it('Guides_use_only_supported_markdown', () => {
    for (const [name, text] of [['getting-started', gettingStarted], ['known-gaps', knownGaps]] as const) {
      expect(text, `${name}: link`).not.toMatch(/\[[^\]]*\]\([^)]*\)/);
      expect(text, `${name}: table`).not.toMatch(/^\s*\|/m);
      expect(text, `${name}: blockquote`).not.toMatch(/^\s*>/m);
      expect(text, `${name}: ordered list`).not.toMatch(/^\s*\d+[.)]\s/m);
      expect(text, `${name}: code fence`).not.toMatch(/^\s*```/m);
      expect(text, `${name}: rule`).not.toMatch(/^[ \t]*(---+|\*\*\*+|___+)[ \t]*$/m);
      expect(text, `${name}: nested list`).not.toMatch(/^[ \t]+-[ \t]/m);
      expect(text, `${name}: image`).not.toMatch(/!\[/);
      expect(text, `${name}: html`).not.toMatch(/<[a-zA-Z/]/);
      expect(text, `${name}: heading level`).not.toMatch(/^#{1}\s|^#{4,}\s/m);
    }
  });
});
