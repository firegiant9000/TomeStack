// Keyboard helpers shared by the app-wide shortcuts (Ctrl+B in App.tsx, the quickbar's Alt+1 to Alt+8).

/** True when the key press came from a field the player is typing in, where a shortcut must pass through (WCAG 2.1.1 keeps checkboxes, radios and buttons out of this). */
export function isTypingTarget(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  if (element && (element.isContentEditable || /^(TEXTAREA|SELECT)$/.test(element.tagName))) return true;
  // Only text-entry inputs keep the shortcut; a focused checkbox, radio or button must not block it (WCAG 2.1.1).
  return element?.tagName === 'INPUT' && !/^(checkbox|radio|button|submit|reset|range|color|file)$/.test((element as HTMLInputElement).type);
}

/** D73: the quickbar slot (0 to 7) for Alt+1 to Alt+8 on the digit row, else undefined. Physical keys: the numpad and other layouts' characters are ignored. */
export function quickbarSlot(event: KeyboardEvent): number | undefined {
  if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return undefined;
  const match = /^Digit([1-8])$/.exec(event.code);
  return match ? Number(match[1]) - 1 : undefined;
}
