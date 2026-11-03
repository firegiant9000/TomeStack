import type { UserEvent } from '@testing-library/user-event';

/**
 * Puts `text` into an empty text field in one input event: click to focus, then paste. Use it instead of
 * `user.type` when a test cares about the value that results and not about keystrokes, because typing re-renders
 * (and re-queries) once per character. Keep `user.type` for key syntax ({Enter}, {Backspace}), appending to
 * existing text, and anything that reacts to individual keys.
 */
export async function fill(user: UserEvent, element: Element, text: string): Promise<void> {
  await user.click(element);
  await user.paste(text);
}
