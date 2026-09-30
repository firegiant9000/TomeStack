import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import type { TreeNode } from '../api/types';

interface Visible {
  node: TreeNode;
  level: number;
  parent?: string;
}

/**
 * M5 slice 5 (B19): a read-only tree, following the WAI-ARIA APG tree view pattern (WCAG 2.2 AA; accessibility checklist
 * item 22). One item is in the tab order (roving tabindex). Up and Down move between visible items, Right opens an item
 * or moves to its first child, Left closes it or moves to its parent, Home and End go to the first and last item, and
 * Enter activates it (for example, showing that rule in the editor). Items state their level, position and whether they
 * are open. No canvas and no drag: every relation is text.
 */
export function TreeView(props: { root: TreeNode; label: string; onActivate?: (node: TreeNode) => void }) {
  const { root } = props;
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set([root.id]));
  const [focusedId, setFocusedId] = useState(root.id);
  // Focus follows the roving item only after a key press inside the tree, never on first render.
  const moveFocus = useRef(false);

  const visible: Visible[] = [];
  const walk = (node: TreeNode, level: number, parent?: string) => {
    visible.push({ node, level, parent });
    if (expanded.has(node.id)) for (const child of node.children) walk(child, level + 1, node.id);
  };
  walk(root, 1);
  const current = visible.find((v) => v.node.id === focusedId) ?? visible[0]!;

  useEffect(() => {
    if (!moveFocus.current) return;
    moveFocus.current = false;
    document.getElementById(domId(current.node.id))?.focus();
  }, [current.node.id]);

  function go(id: string) {
    moveFocus.current = true;
    setFocusedId(id);
  }

  function toggle(id: string, open: boolean) {
    setExpanded((e) => {
      const next = new Set(e);
      if (open) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  function onKeyDown(event: KeyboardEvent<HTMLLIElement>) {
    const index = visible.indexOf(current);
    const node = current.node;
    const open = expanded.has(node.id);
    switch (event.key) {
      case 'ArrowDown':
        if (index < visible.length - 1) go(visible[index + 1]!.node.id);
        break;
      case 'ArrowUp':
        if (index > 0) go(visible[index - 1]!.node.id);
        break;
      case 'ArrowRight':
        if (node.children.length > 0 && !open) toggle(node.id, true);
        else if (open && node.children.length > 0) go(node.children[0]!.id);
        break;
      case 'ArrowLeft':
        if (open && node.children.length > 0) toggle(node.id, false);
        else if (current.parent) go(current.parent);
        break;
      case 'Home':
        go(visible[0]!.node.id);
        break;
      case 'End':
        go(visible[visible.length - 1]!.node.id);
        break;
      case 'Enter':
      case ' ':
        if (event.key === ' ' && node.children.length > 0) toggle(node.id, !open);
        else props.onActivate?.(node);
        break;
      default:
        return;
    }
    event.preventDefault();
    event.stopPropagation();
  }

  function render(node: TreeNode, level: number, position: number, size: number) {
    const open = expanded.has(node.id);
    const hasChildren = node.children.length > 0;
    return (
      <li
        key={node.id}
        id={domId(node.id)}
        role="treeitem"
        // Named by its own label, not by the text of its open children (review fix).
        aria-labelledby={`${domId(node.id)}-label`}
        aria-level={level}
        aria-posinset={position}
        aria-setsize={size}
        aria-expanded={hasChildren ? open : undefined}
        tabIndex={node.id === current.node.id ? 0 : -1}
        onKeyDown={onKeyDown}
        onFocus={(e) => {
          if (e.target === e.currentTarget) setFocusedId(node.id);
        }}
        onClick={(e) => {
          e.stopPropagation();
          setFocusedId(node.id);
          if (hasChildren) toggle(node.id, !open);
        }}
        className={`tree-${node.kind}`}
      >
        <span id={`${domId(node.id)}-label`}>
          {hasChildren && <span aria-hidden="true">{open ? '▾ ' : '▸ '}</span>}
          {node.label}
          {node.note && <span className="hint"> ({node.note})</span>}
        </span>
        {hasChildren && open && (
          <ul role="group">{node.children.map((child, i) => render(child, level + 1, i + 1, node.children.length))}</ul>
        )}
      </li>
    );
  }

  return (
    <ul role="tree" aria-label={props.label} className="tree">
      {render(root, 1, 1, 1)}
    </ul>
  );
}

/** A DOM id from a tree node id (which holds slashes and colons); each other character is escaped, so ids stay unique. */
const domId = (id: string) => `tree-${id.replace(/[^A-Za-z0-9-]/g, (c) => `_${c.charCodeAt(0).toString(16)}_`)}`;
