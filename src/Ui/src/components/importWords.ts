// Import Lab Task 13 (D67): one place for the words of a Markdown import's failures and page warnings. A code with no entry
// here falls back to what the server said (a failure) or to the code itself (a page warning, which has no server text).

const failures: Record<string, string> = {
  'markdown.missing': 'The Markdown file could not be found.',
  'markdown.unreadable': 'The Markdown file could not be opened. Close it in other programs and check you can read it, then try again.',
  'markdown.encoding': 'The file is not valid UTF-8 text. Save it as UTF-8, then import it again.',
  'markdown.too-large': 'The Markdown file is too large to import: over 4 MB, or too many lines, pages or table columns.',
  'import.markdown-read-again': 'Import the Markdown file again.',
};

const pageWarnings: Record<string, string> = {
  'markdown.page-order': 'Page markers must go up; one was ignored.',
  'markdown.front-matter-ignored': 'Front matter at the top was ignored.',
  'page.text-truncated': 'This page was longer than TomeStack keeps, so the end of it was left out.',
  'page.no-text': 'This page has no text.',
};

/** What went wrong with an import, in words; `serverMessage` is used for any code this module does not know. */
export function failureWords(code: string | undefined, serverMessage?: string): string | undefined {
  if (code && failures[code]) return failures[code];
  return serverMessage;
}

/** One warning on a stored page, in words; an unknown code is shown as it is. */
export function pageWarningWords(code: string): string {
  return pageWarnings[code] ?? code;
}
