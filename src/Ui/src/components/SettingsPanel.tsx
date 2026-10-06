import { useEffect, useRef, useState } from 'react';
import type { AppInfo } from '../api/types';
import { applyTheme, diceAnimationOn, setDiceAnimation, setTheme, theme, themeIds, type ThemeId } from '../settings';

const themeLabels: Record<ThemeId, string> = { cool: 'Cool', forest: 'Forest', violet: 'Violet' };

/** ADR-015: the app's own preferences. Kept in the page's storage on this computer; in no backup or package. */
export function SettingsPanel({ info }: { info?: AppInfo }) {
  const [current, setCurrent] = useState<ThemeId>(theme());
  const [dice, setDice] = useState(diceAnimationOn());
  const heading = useRef<HTMLHeadingElement>(null);

  // WCAG 2.4.3: like the other screens, opening Settings moves focus to its heading.
  useEffect(() => heading.current?.focus(), []);

  function pickTheme(id: ThemeId) {
    setTheme(id);
    applyTheme(id);
    setCurrent(id);
  }

  function toggleDice(on: boolean) {
    setDiceAnimation(on);
    setDice(on);
  }

  return (
    <section className="panel" aria-labelledby="settings-heading">
      <h2 id="settings-heading" tabIndex={-1} ref={heading}>
        Settings
      </h2>
      <fieldset role="radiogroup" aria-labelledby="theme-legend">
        <legend id="theme-legend">Theme</legend>
        {themeIds.map((id) => (
          <label key={id} className="choice">
            <input type="radio" name="theme" checked={current === id} onChange={() => pickTheme(id)} /> {themeLabels[id]}
          </label>
        ))}
      </fieldset>
      <label className="choice">
        <input type="checkbox" checked={dice} onChange={(e) => toggleDice(e.target.checked)} /> Animate dice
      </label>
      <p className="hint">Settings are kept in this app's own storage on this computer and are in no backup or package.</p>
      {info && (
        <p className="hint">
          TomeStack {info.version}, data schema {info.schemaVersion}. Offline: your library stays in this computer's data folder.
        </p>
      )}
    </section>
  );
}
