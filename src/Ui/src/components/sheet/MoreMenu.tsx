import { useId, useRef, useState } from 'react';
import type { FocusEvent, KeyboardEvent, RefObject } from 'react';

export interface MoreMenuItem {
  id: string;
  label: string;
  onSelect: () => void;
  disabled?: boolean;
}

/**
 * An overflow disclosure: a "More" button that shows a plain list of buttons. Deliberately not an ARIA menu (no menu roles,
 * no arrow-key model), so Tab walks the items. Esc closes it and returns focus to "More". The handler is its own and calls
 * `preventDefault`, so the global Esc handler (escape.ts) skips the same key press instead of toggling the list a second time.
 */
export function MoreMenu({ items, triggerRef }: { items: MoreMenuItem[]; triggerRef?: RefObject<HTMLButtonElement | null> }) {
  const listId = useId();
  const [open, setOpen] = useState(false);
  const ownTrigger = useRef<HTMLButtonElement>(null);
  const trigger = triggerRef ?? ownTrigger;

  function onKeyDown(event: KeyboardEvent) {
    if (event.key !== 'Escape' || !open || event.nativeEvent.isComposing) return;
    event.preventDefault();
    setOpen(false);
    trigger.current?.focus();
  }

  // Focus leaving the whole disclosure (Tab past the last item, a click elsewhere) closes the list without moving focus.
  function onBlur(event: FocusEvent<HTMLDivElement>) {
    if (open && !event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false);
  }

  return (
    // eslint-disable-next-line jsx-a11y/no-static-element-interactions -- a disclosure wrapper: it only observes Escape and focus-leave bubbling from the real buttons inside.
    <div className="more-menu" onKeyDown={onKeyDown} onBlur={onBlur}>
      <button ref={trigger} type="button" aria-expanded={open} aria-controls={listId} onClick={() => setOpen((o) => !o)}>
        More
      </button>
      {/* A mousedown anywhere in the list (its gaps and padding, and the item buttons, since it bubbles) keeps focus where it is, so
          it never blurs the disclosure and closes it (onBlur). The click still runs: an item's onClick focuses More, then onSelect. */}
      {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- pointer-only focus keeper; the buttons inside carry the keyboard and screen-reader interaction. */}
      <ul id={listId} className="more-menu-list" hidden={!open} onMouseDown={(e) => e.preventDefault()}>
        {open &&
          items.map((item) => (
            <li key={item.id}>
              <button
                type="button"
                disabled={item.disabled}
                onClick={() => {
                  // The chosen button unmounts when the list closes; focus "More" first so focus is never lost to the page,
                  // and a caller that moves focus (to the panel it opens) still wins because it runs after this.
                  setOpen(false);
                  trigger.current?.focus();
                  item.onSelect();
                }}
              >
                {item.label}
              </button>
            </li>
          ))}
      </ul>
    </div>
  );
}
