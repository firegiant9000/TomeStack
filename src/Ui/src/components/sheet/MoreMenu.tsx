import { useId, useRef, useState } from 'react';
import type { KeyboardEvent, RefObject } from 'react';

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

  return (
    <div className="more-menu" onKeyDown={onKeyDown}>
      <button ref={trigger} type="button" aria-expanded={open} aria-controls={listId} onClick={() => setOpen((o) => !o)}>
        More
      </button>
      <ul id={listId} className="more-menu-list" hidden={!open}>
        {open &&
          items.map((item) => (
            <li key={item.id}>
              <button
                type="button"
                disabled={item.disabled}
                onClick={() => {
                  setOpen(false);
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
