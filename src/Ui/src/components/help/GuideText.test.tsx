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
      expect(text, `${name}: table`).not.toMatch(/\|/);
      expect(text, `${name}: image`).not.toMatch(/!\[/);
      expect(text, `${name}: html`).not.toMatch(/<[a-zA-Z/]/);
      expect(text, `${name}: heading level`).not.toMatch(/^#{1}\s|^#{4,}\s/m);
    }
  });
});
