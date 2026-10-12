import { useEffect, useRef, useState } from 'react';
import type { AppInfo } from '../api/types';
import {
  abilityOrder,
  abilityOrderIds,
  appearance,
  appearanceIds,
  announceRollsOn,
  applyPreferences,
  contrast,
  diceAnimationOn,
  focusRing,
  motion,
  motionIds,
  setAbilityOrder,
  setAnnounceRolls,
  setAppearance,
  setContrast,
  setDiceAnimation,
  setFocusRing,
  setMotion,
  setTargets,
  setTextSize,
  setTheme,
  setUnderline,
  targets,
  textSize,
  textSizeIds,
  theme,
  themeIds,
  underline,
  type AbilityOrderId,
  type AppearanceId,
  type ContrastId,
  type FocusRingId,
  type MotionId,
  type TargetsId,
  type UnderlineId,
  type TextSizeId,
  type ThemeId,
} from '../settings';

const themeLabels: Record<ThemeId, string> = { cool: 'Cool', forest: 'Forest', violet: 'Violet' };
const appearanceLabels: Record<AppearanceId, string> = { system: 'System', light: 'Light', dark: 'Dark' };
const abilityOrderLabels: Record<AbilityOrderId, string> = { modifier: 'Modifier first', score: 'Score first' };

/** A labelled radio group whose pick is stored and applied at once; the legend is the group's accessible name. */
function Choice<Id extends string>({ legend, ids, labels, value, onPick }: { legend: string; ids: readonly Id[]; labels: Record<Id, string>; value: Id; onPick: (id: Id) => void }) {
  const legendId = `settings-${legend.toLowerCase().replaceAll(/[^a-z]+/g, '-')}`;
  return (
    <fieldset role="radiogroup" aria-labelledby={legendId}>
      <legend id={legendId}>{legend}</legend>
      {ids.map((id) => (
        <label key={id} className="choice">
          <input type="radio" name={legendId} checked={value === id} onChange={() => onPick(id)} /> {labels[id]}
        </label>
      ))}
    </fieldset>
  );
}

/**
 * ADR-015 §4: the app's own preferences, kept in the page's storage on this computer; in no backup or package. Each one is
 * an `html[data-*]` attribute the CSS keys on (investigation 2026-10-06: Appearance, Text size, Ability boxes; the
 * Accessibility section; a static list of the keyboard shortcuts so they can be found).
 */
export function SettingsPanel({ info }: { info?: AppInfo }) {
  const [current, setCurrent] = useState<ThemeId>(theme());
  const [scheme, setScheme] = useState<AppearanceId>(appearance());
  const [size, setSize] = useState<TextSizeId>(textSize());
  const [order, setOrder] = useState<AbilityOrderId>(abilityOrder());
  const [dice, setDice] = useState(diceAnimationOn());
  const [move, setMove] = useState<MotionId>(motion());
  const [more, setMore] = useState<ContrastId>(contrast());
  const [ring, setRing] = useState<FocusRingId>(focusRing());
  const [big, setBig] = useState<TargetsId>(targets());
  const [lines, setLines] = useState<UnderlineId>(underline());
  const [announce, setAnnounce] = useState(announceRollsOn());
  const heading = useRef<HTMLHeadingElement>(null);

  // WCAG 2.4.3: like the other screens, opening Settings moves focus to its heading.
  useEffect(() => heading.current?.focus(), []);

  function pick<T>(store: (value: T) => void, set: (value: T) => void) {
    return (value: T) => {
      store(value);
      set(value);
      applyPreferences();
    };
  }

  function toggleDice(on: boolean) {
    setDiceAnimation(on);
    setDice(on);
  }

  return (
    <section className="panel settings" aria-labelledby="settings-heading">
      <h2 id="settings-heading" tabIndex={-1} ref={heading}>
        Settings
      </h2>

      <h3>Appearance</h3>
      <Choice legend="Colour scheme" ids={appearanceIds} labels={appearanceLabels} value={scheme} onPick={pick(setAppearance, setScheme)} />
      <Choice legend="Theme" ids={themeIds} labels={themeLabels} value={current} onPick={pick(setTheme, setCurrent)} />
      <label className="field">
        Text size
        <select value={size} onChange={(e) => pick(setTextSize, setSize)(e.target.value as TextSizeId)}>
          {textSizeIds.map((id) => (
            <option key={id} value={id}>
              {id}%
            </option>
          ))}
        </select>
      </label>
      <p className="hint">Text size scales the whole app; Ctrl + and Ctrl − zoom the window on top of it.</p>
      <Choice legend="Ability boxes" ids={abilityOrderIds} labels={abilityOrderLabels} value={order} onPick={pick(setAbilityOrder, setOrder)} />
      <label className="choice">
        <input type="checkbox" checked={dice} onChange={(e) => toggleDice(e.target.checked)} /> Animate dice
      </label>

      <h3>Accessibility</h3>
      <Choice legend="Motion" ids={motionIds} labels={{ system: 'Follow Windows', reduce: 'Reduce motion' }} value={move} onPick={pick(setMotion, setMove)} />
      <p className="hint">Dice stop tumbling; nothing else moves today. &quot;Follow Windows&quot; uses your &quot;Animation effects&quot; setting.</p>
      <label className="choice">
        <input type="checkbox" checked={more === 'more'} onChange={(e) => pick(setContrast, setMore)(e.target.checked ? 'more' : 'default')} /> Stronger borders and labels
      </label>
      <label className="choice">
        <input type="checkbox" checked={ring === 'always'} onChange={(e) => pick(setFocusRing, setRing)(e.target.checked ? 'always' : 'auto')} /> Always show the focus outline
      </label>
      <label className="choice">
        <input type="checkbox" checked={big === 'large'} onChange={(e) => pick(setTargets, setBig)(e.target.checked ? 'large' : 'default')} /> Larger buttons
      </label>
      <label className="choice">
        <input type="checkbox" checked={lines === 'on'} onChange={(e) => pick(setUnderline, setLines)(e.target.checked ? 'on' : 'off')} /> Underline links and text buttons
      </label>
      <label className="choice">
        <input type="checkbox" checked={announce} onChange={(e) => { setAnnounceRolls(e.target.checked); setAnnounce(e.target.checked); }} /> Announce each roll to screen readers
      </label>
      <p className="hint">Rolls are always shown as text; turning announcements off takes full effect the next time a sheet is opened.</p>

      <h3>Keyboard shortcuts</h3>
      <dl className="shortcuts">
        <dt>Ctrl+B</dt>
        <dd>Hide or show the sidebar</dd>
        <dt>Ctrl + / Ctrl − / Ctrl 0</dt>
        <dd>Zoom the window in, out, or back to normal</dd>
        <dt>Left, Right, Home, End</dt>
        <dd>Move between the sheet's pages on the tab strip</dd>
        <dt>Tab, Shift+Tab</dt>
        <dd>Move between controls</dd>
      </dl>

      <h3>About</h3>
      <p className="hint">Settings are kept in this app's own storage on this computer and are in no backup or package.</p>
      {info && (
        <p className="hint">
          TomeStack {info.version}, data schema {info.schemaVersion}. Offline: your library stays in this computer's data folder.
        </p>
      )}
    </section>
  );
}
