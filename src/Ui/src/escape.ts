/**
 * Esc closes the open disclosure the focus is in (spec 3.11, ADR-014): an open `<details>` (focus returns to its summary),
 * or a panel that a button with `aria-expanded="true"` controls (focus returns to that button). Esc never changes the screen.
 * Returns whether something was closed. An event another handler already took (`defaultPrevented`) is left alone, and
 * a closed disclosure is reported with `preventDefault` so later handlers skip the same key press.
 */
export function closeOnEscape(event: KeyboardEvent): boolean {
  if (event.key !== 'Escape' || event.defaultPrevented || event.isComposing) return false; // Esc during IME composition cancels the composition
  const target = event.target;
  if (!(target instanceof Element)) return false;
  if (target.closest('[role="dialog"], [role="alertdialog"], dialog')) return false; // a dialog owns Esc while it is open

  const details = target.closest('details[open]');
  if (details instanceof HTMLDetailsElement) {
    details.open = false;
    details.querySelector<HTMLElement>(':scope > summary')?.focus();
    event.preventDefault();
    return true;
  }

  for (let el: Element | null = target; el; el = el.parentElement) {
    if (!el.id) continue;
    const trigger = Array.from(document.querySelectorAll<HTMLElement>('button[aria-expanded="true"][aria-controls]')).find((b) =>
      b.getAttribute('aria-controls')?.split(/\s+/).includes(el!.id),
    );
    if (trigger) {
      trigger.click();
      trigger.focus();
      event.preventDefault();
      return true;
    }
  }
  return false;
}
