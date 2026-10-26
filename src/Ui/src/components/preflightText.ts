import type { PreflightReport } from '../api/types';

/** Import Lab L5: the reason codes of `package.preflight`, in words. */
const reasonText: Record<string, string> = {
  'preflight.import-derived': 'holds material imported from a PDF, so it is never shared',
  'preflight.not-redistributable': 'not marked as your own shareable work, so it is not yours to share',
  'preflight.draft': 'a draft; only published content is shared',
  'preflight.attachment': 'a PDF and the text read from it never travel in a share',
  'preflight.notice-missing': 'its license notice is missing, so it cannot be shared',
  'preflight.empty': 'nothing is published yet',
  'preflight.spans-sources': 'also has revisions in another source, so it cannot leave with this one',
};

export const reasonWords = (code: string | null | undefined) => (code ? (reasonText[code] ?? 'not shared') : '');

/** Save stays off until the preflight has answered, and while anything blocks the export. */
export const canSaveAfter = (report: PreflightReport | undefined) => report !== undefined && report.blocking.length === 0;
