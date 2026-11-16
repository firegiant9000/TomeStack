// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Character, GuideLineView, GuideView, KitChoice, KitView } from '../../api/types';
import { expectNoAxeViolations } from '../../test/axe';
import { GearStep, type GearValue } from './GearStep';

// The service omits absent fields, so the fixtures do too.
const line = (over: Partial<GuideLineView>): GuideLineView => ({ kind: 'item', quantity: 1, ...over }) as GuideLineView;

const classGuide: GuideView = {
  guideContentId: 'guide-cleric',
  targetContentId: 'cleric',
  primaryAbilities: [],
  picks: [
    {
      id: 'weapon',
      label: 'Weapon',
      options: [
        { id: 'a', text: 'Mace', lines: [line({ itemContentId: 'mace', name: 'Mace' })] },
        {
          id: 'b',
          text: 'Warhammer',
          requiresProficiency: 'Martial weapons',
          lines: [line({ itemContentId: 'warhammer', name: 'Warhammer' })],
        },
      ],
    },
    {
      id: 'ranged',
      label: 'Ranged',
      options: [
        {
          id: 'a',
          text: 'Light crossbow and 20 crossbow bolts',
         
          lines: [line({ itemContentId: 'lxb', name: 'Light crossbow' })],
        },
        {
          id: 'b',
          text: 'Any simple weapon',
         
          lines: [
            line({
              kind: 'anyOf',
              anyOf: 'simple weapon',
              name: 'Simple weapon',
              choices: [
                { reference: { contentId: 'club', revisionId: 'r1' }, name: 'Club' },
                { reference: { contentId: 'dagger', revisionId: 'r2' }, name: 'Dagger' },
              ] as never,
            }),
          ],
        },
      ],
    },
    {
      id: 'pack',
      label: 'Pack',
      options: [
        {
          id: 'a',
          text: "Priest's pack",
         
          lines: [
            line({
              kind: 'pack',
              itemContentId: 'priest-pack',
              name: "Priest's pack",
              contents: [line({ itemContentId: 'candle', name: 'Candle', quantity: 10 })],
            }),
          ],
        },
        { id: 'b', text: 'Instead: 50 gp', lines: [line({ kind: 'coins', coins: { cp: 0, sp: 0, ep: 0, gp: 50, pp: 0 } })] },
      ],
    },
  ],
};

const backgroundGuide: GuideView = {
  guideContentId: 'guide-acolyte',
  targetContentId: 'acolyte',
  primaryAbilities: [],
  picks: [
    {
      id: 'only',
      label: 'Kit',
      options: [{ id: 'a', text: 'Holy symbol and a prayer book', lines: [] }],
    },
  ],
};

const kitView = (ac: number, problems: KitView['problems'] = []): KitView =>
  ({
    equipment: [],
    coins: { cp: 0, sp: 0, ep: 0, gp: 0, pp: 0 },
    problems,
    preview: {
      sheet: {
        fields: [{ field: 'armorClass', label: 'Armor Class', value: ac, computedValue: ac, trace: [], warnings: [], automation: 'automatic', units: 'score' }],
      },
    },
  }) as unknown as KitView;

const draft = {} as Character;
const empty: GearValue = { skipped: false, choices: {} };

function Harness(props: { onPreview?: (kits: unknown) => Promise<KitView>; classGuide?: GuideView; backgroundGuide?: GuideView; trained?: string[]; initial?: GearValue; spy?: (v: GearValue) => void }) {
  const [value, setValue] = useState<GearValue>(props.initial ?? empty);
  return (
    <GearStep
      draft={draft}
      classGuide={'classGuide' in props ? props.classGuide : classGuide}
      backgroundGuide={'backgroundGuide' in props ? props.backgroundGuide : backgroundGuide}
      className="Cleric"
      backgroundName="Acolyte"
      trained={props.trained ?? []}
      value={value}
      onChange={(v) => {
        props.spy?.(v);
        setValue(v);
      }}
      onPreview={(props.onPreview as never) ?? (() => Promise.resolve(kitView(16)))}
    />
  );
}

const isChecked = (el: HTMLElement) => (el as HTMLInputElement).checked;

afterEach(cleanup);

describe('GearStep', () => {
  it('defaults every pick to option a and shows both sources', async () => {
    render(<Harness />);
    expect(screen.getByText('From your class (Cleric)')).toBeTruthy();
    expect(screen.getByText('From your background (Acolyte)')).toBeTruthy();
    expect(isChecked(screen.getByRole('radio', { name: /^Mace/ }))).toBe(true);
    expect(isChecked(screen.getByRole('radio', { name: /Light crossbow/ }))).toBe(true);
    expect(isChecked(screen.getByRole('radio', { name: /Priest's pack/ }))).toBe(true);
    expect(screen.getByText(/Always/).parentElement?.textContent).toContain('Holy symbol and a prayer book');
    expect(await screen.findByText('AC with this gear: 16')).toBeTruthy();
  });

  it('cannot choose an inert option and shows its reason as text', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);
    const inert = screen.getByRole('radio', { name: /Warhammer/ });
    expect(inert.getAttribute('aria-disabled')).toBe('true');
    expect(screen.getByText('needs Martial weapons proficiency')).toBeTruthy();
    await user.click(inert);
    expect(isChecked(screen.getByRole('radio', { name: /^Mace/ }))).toBe(true);
    expect(spy).not.toHaveBeenCalled();
  });

  it('shows the gold option text when chosen and reports the choice', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);
    await user.click(screen.getByRole('radio', { name: /Instead: 50 gp/ }));
    expect(isChecked(screen.getByRole('radio', { name: /Instead: 50 gp/ }))).toBe(true);
    const last = spy.mock.calls.at(-1)![0] as GearValue;
    const choice = last.choices['guide-cleric']?.find((c: KitChoice) => c.pickId === 'pack');
    expect(choice?.optionId).toBe('b');
  });

  it('requests the preview on change and shows its AC', async () => {
    const user = userEvent.setup();
    const onPreview = vi.fn().mockResolvedValueOnce(kitView(16)).mockResolvedValue(kitView(18));
    render(<Harness onPreview={onPreview} />);
    expect(await screen.findByText('AC with this gear: 16')).toBeTruthy();
    await user.click(screen.getByRole('radio', { name: /Any simple weapon/ }));
    expect(await screen.findByText('AC with this gear: 18')).toBeTruthy();
    expect(onPreview.mock.calls.length).toBeGreaterThanOrEqual(2);
    expect(screen.getByRole('button', { name: 'Why?' })).toBeTruthy();
  });

  it('offers a select for an anyOf line and records the item', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);
    await user.click(screen.getByRole('radio', { name: /Any simple weapon/ }));
    await user.selectOptions(screen.getByRole('combobox', { name: /simple weapon/i }), 'dagger');
    const last = spy.mock.calls.at(-1)![0] as GearValue;
    expect(last.choices['guide-cleric']?.find((c: KitChoice) => c.pickId === 'ranged')?.anyOf).toEqual({ 0: 'dagger' });
  });

  it('discloses a pack\'s contents', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByText("What's in it?"));
    expect(screen.getByText(/10 × Candle/).closest('details')?.open).toBe(true);
  });

  it('lists a problem and still shows the rest', async () => {
    const onPreview = vi.fn().mockResolvedValue(kitView(14, [{ code: 'kit.item', message: 'Candle is not available' }]));
    render(<Harness onPreview={onPreview} />);
    expect(await screen.findByText('Candle is not available')).toBeTruthy();
    expect(screen.getByText('AC with this gear: 14')).toBeTruthy();
    expect(screen.getByRole('radio', { name: /^Mace/ })).toBeTruthy();
  });

  it('says so when there is no guide', () => {
    render(<Harness classGuide={undefined} backgroundGuide={undefined} />);
    expect(screen.getByText('No starting kit for Cleric. Add gear on the Gear tab after saving.')).toBeTruthy();
    expect(screen.getByText('No starting kit for Acolyte. Add gear on the Gear tab after saving.')).toBeTruthy();
  });

  it('skips on request and keeps the choices', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);
    const skip = screen.getByRole('button', { name: "Skip: I'll add gear myself" });
    expect(skip.getAttribute('aria-pressed')).toBe('false');
    await user.click(skip);
    expect(skip.getAttribute('aria-pressed')).toBe('true');
    expect((spy.mock.calls.at(-1)![0] as GearValue).skipped).toBe(true);
  });

  it('has no axe violations', async () => {
    const { container } = render(<Harness />);
    await screen.findByText('AC with this gear: 16');
    await expectNoAxeViolations(container);
  });

  it('reaches every group by keyboard', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await screen.findByText('AC with this gear: 16');
    const stops = [/^Mace/, /Light crossbow/, /Priest's pack/];
    await user.tab(); // the Skip button comes first
    expect(document.activeElement).toBe(screen.getByRole('button', { name: "Skip: I'll add gear myself" }));
    for (const name of stops) {
      await user.tab(); // each group is one tab stop: its checked radio
      expect(document.activeElement).toBe(screen.getByRole('radio', { name }));
    }
  });
});
