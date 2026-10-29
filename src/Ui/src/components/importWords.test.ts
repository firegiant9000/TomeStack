import { expect, it } from 'vitest';
import { failureWords, pageWarningWords } from './importWords';

it('puts every Markdown failure code in plain words', () => {
  expect(failureWords('markdown.missing', 'server')).toBe('The Markdown file could not be found.');
  expect(failureWords('markdown.unreadable', 'server')).toContain('could not be opened');
  expect(failureWords('markdown.encoding', 'server')).toContain('not valid UTF-8');
  expect(failureWords('markdown.too-large', 'server')).toContain('too large');
  expect(failureWords('import.markdown-read-again', 'server')).toBe('Import the Markdown file again.');
});

it('falls back to the server message for any other code, or none', () => {
  expect(failureWords('pdf.encrypted', 'The PDF is protected.')).toBe('The PDF is protected.');
  expect(failureWords(undefined, 'Some message.')).toBe('Some message.');
  expect(failureWords('pdf.encrypted')).toBeUndefined();
});

it('puts every Markdown page warning in plain words, and shows an unknown one as it is', () => {
  expect(pageWarningWords('markdown.page-order')).toBe('Page markers must go up; one was ignored.');
  expect(pageWarningWords('markdown.front-matter-ignored')).toBe('Front matter at the top was ignored.');
  expect(pageWarningWords('page.text-truncated')).toContain('longer than TomeStack keeps');
  expect(pageWarningWords('page.no-text')).toBe('This page has no text.');
  expect(pageWarningWords('page.blocks-truncated')).toContain('more lines than TomeStack keeps');
  expect(pageWarningWords('page.something-new')).toBe('page.something-new');
});
