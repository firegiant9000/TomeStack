import { useEffect, useRef, type KeyboardEvent, type ReactNode } from 'react';

export interface TabSpec<Id extends string> {
  id: Id;
  label: string;
}

export const tabId = (prefix: string, id: string) => `${prefix}-tab-${id}`;
export const panelId = (prefix: string, id: string) => `${prefix}-panel-${id}`;

interface ListProps<Id extends string> {
  /** The accessible name of the tablist. */
  label: string;
  /** Prefix for the tab and panel element ids; one tablist per prefix on a page. */
  idPrefix: string;
  tabs: readonly TabSpec<Id>[];
  active: Id;
  onActivate: (id: Id) => void;
}

/**
 * WAI-ARIA APG tabs (ADR-013; accessibility checklist item 29). One tab is in the tab order (roving tabindex). Left and
 * Right move to the previous and next tab and wrap, Home and End go to the first and last. Activation is automatic:
 * moving selects, because every panel is already mounted and switching costs nothing. Focus follows the selected tab
 * only after a key press, never on first render or after a click elsewhere.
 */
export function TabList<Id extends string>({ label, idPrefix, tabs, active, onActivate }: ListProps<Id>) {
  const moveFocus = useRef(false);

  useEffect(() => {
    if (!moveFocus.current) return;
    moveFocus.current = false;
    document.getElementById(tabId(idPrefix, active))?.focus();
  }, [active, idPrefix]);

  function go(index: number) {
    const next = tabs[(index + tabs.length) % tabs.length];
    if (!next) return;
    moveFocus.current = true;
    onActivate(next.id);
  }

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    const index = tabs.findIndex((t) => t.id === active);
    switch (event.key) {
      case 'ArrowRight':
        go(index + 1);
        break;
      case 'ArrowLeft':
        go(index - 1);
        break;
      case 'Home':
        go(0);
        break;
      case 'End':
        go(tabs.length - 1);
        break;
      default:
        return;
    }
    event.preventDefault();
  }

  return (
    <div role="tablist" aria-label={label} className="tabs">
      {tabs.map((tab) => {
        const selected = tab.id === active;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={tabId(idPrefix, tab.id)}
            aria-selected={selected}
            aria-controls={panelId(idPrefix, tab.id)}
            tabIndex={selected ? 0 : -1}
            onKeyDown={onKeyDown}
            onClick={() => onActivate(tab.id)}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

/** One panel. Inactive panels stay mounted (nothing typed is lost) and are hidden, so they leave the accessibility tree. */
export function TabPanel({ idPrefix, id, active, children }: { idPrefix: string; id: string; active: boolean; children: ReactNode }) {
  return (
    <div role="tabpanel" id={panelId(idPrefix, id)} aria-labelledby={tabId(idPrefix, id)} hidden={!active} tabIndex={-1} className="tabpanel">
      {children}
    </div>
  );
}
