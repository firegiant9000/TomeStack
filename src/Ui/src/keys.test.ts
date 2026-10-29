// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { isTypingTarget, quickbarSlot } from './keys';

const press = (init: KeyboardEventInit) => new KeyboardEvent('keydown', init);

describe('quickbarSlot', () => {
  it('maps Alt+1 to Alt+8 on the digit row to 0..7', () => {
    expect(quickbarSlot(press({ code: 'Digit1', altKey: true }))).toBe(0);
    expect(quickbarSlot(press({ code: 'Digit8', altKey: true }))).toBe(7);
  });
  it('ignores everything else', () => {
    expect(quickbarSlot(press({ code: 'Digit9', altKey: true }))).toBeUndefined();
    expect(quickbarSlot(press({ code: 'Digit0', altKey: true }))).toBeUndefined();
    expect(quickbarSlot(press({ code: 'Numpad1', altKey: true }))).toBeUndefined();
    expect(quickbarSlot(press({ code: 'Digit1', altKey: true, ctrlKey: true }))).toBeUndefined();
    expect(quickbarSlot(press({ code: 'Digit1', altKey: true, shiftKey: true }))).toBeUndefined();
    expect(quickbarSlot(press({ code: 'Digit1', altKey: true, metaKey: true }))).toBeUndefined();
    expect(quickbarSlot(press({ code: 'Digit1' }))).toBeUndefined();
  });
});

describe('isTypingTarget', () => {
  const input = (type: string) => Object.assign(document.createElement('input'), { type });
  it('is true for text entry', () => {
    expect(isTypingTarget(input('text'))).toBe(true);
    expect(isTypingTarget(input('number'))).toBe(true);
    expect(isTypingTarget(document.createElement('textarea'))).toBe(true);
    expect(isTypingTarget(document.createElement('select'))).toBe(true);
    const editable = document.createElement('div');
    Object.defineProperty(editable, 'isContentEditable', { value: true }); // jsdom does not implement it
    expect(isTypingTarget(editable)).toBe(true);
  });
  it('is false for checkboxes, buttons and nothing', () => {
    expect(isTypingTarget(input('checkbox'))).toBe(false);
    expect(isTypingTarget(input('radio'))).toBe(false);
    expect(isTypingTarget(document.createElement('button'))).toBe(false);
    expect(isTypingTarget(null)).toBe(false);
  });
});
