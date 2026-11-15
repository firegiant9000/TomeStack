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
    <div className="more-menu" onKeyDown={onKeyDown} onBlur={onBlur}>
      <button ref={trigger} type="button" aria-expanded={open} aria-controls={listId} onClick={() => setOpen((o) => !o)}>
        More
      </button>
      {/* A mousedown on the list's gaps or padding would blur the focused button and close the list (onBlur); keep focus where it is.
          Buttons still take focus and click normally because the default is only prevented on this list, not their own handlers. */}
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
