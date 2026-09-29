/**
 * M5 slice 7 (LIVING_SPECS D14): whether the studio shows design feedback. A preference of this app on this machine,
 * off by default. It lives in the page's own storage (the WebView2 profile in the data folder), so it is in no package,
 * share or library backup, and nothing else reads it.
 */
const key = 'tomestack.designFeedback';

export function designFeedbackOn(): boolean {
  try {
    return globalThis.localStorage?.getItem(key) === 'on';
  } catch {
    return false; // storage unavailable: the default, off
  }
}

export function setDesignFeedback(on: boolean): void {
  try {
    if (on) globalThis.localStorage?.setItem(key, 'on');
    else globalThis.localStorage?.removeItem(key);
  } catch {
    // storage unavailable: the choice lasts for this session only
  }
}
