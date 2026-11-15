import { useEffect, useRef } from 'react';
import gettingStarted from '../../../../docs/guides/getting-started.md?raw';
import knownGaps from '../../../../docs/guides/known-gaps.md?raw';
import { AllGapNotesPanel } from './AllGapNotesPanel';
import { GuideText } from './help/GuideText';

interface Props {
  onError: (error: unknown) => void;
  /** Opens the character's sheet on Features, where its gap notes are. */
  onOpenCharacter: (characterId: string) => void;
}

const glossary: [term: string, meaning: string][] = [
  ['Draft', 'A character or homebrew entry you saved part-way to finish later. A draft changes nothing until you finish it.'],
  ['version', 'The release of TomeStack (for example 0.8.1) that made or can open a file, or one saved state of a homebrew entry.'],
  ['rules (2014/2024)', 'The two rule sets TomeStack knows. Each character uses one, and the two are never mixed.'],
  ['campaign', 'A list of the rules and books a group allows. It marks what is outside it and never changes a number.'],
  ['TomeStack file', 'A file you save to share a character, homebrew or a campaign, or to open on another computer.'],
  ['Why?', 'A button under a number that shows, step by step, where the number came from.'],
  ['You apply this', 'TomeStack shows the rule and the numbers; you decide when it applies and spend or track it yourself.'],
  ['Text only', 'TomeStack shows the rule as written but does no maths for it.'],
];

/**
 * Wave 3 spec 3.10: one place for help. The Help screen has one h1 and an h2 per section; the guides are plain text files
 * under docs/guides, shown by GuideText (their `##` becomes an h3).
 */
export function HelpPanel({ onError, onOpenCharacter }: Props) {
  const heading = useRef<HTMLHeadingElement>(null);

  // WCAG 2.4.3: like the other screens, opening Help moves focus to its heading.
  useEffect(() => heading.current?.focus(), []);

  return (
    <section className="panel help" aria-labelledby="help-heading">
      <h1 id="help-heading" tabIndex={-1} ref={heading}>
        Help
      </h1>

      <section aria-labelledby="help-getting-started">
        <h2 id="help-getting-started">Getting started</h2>
        <GuideText markdown={gettingStarted} />
      </section>

      <section aria-labelledby="help-tabs">
        <h2 id="help-tabs">The sheet&apos;s six tabs</h2>
        <ul>
          <li>
            <strong>Play</strong>: hit points, rolls, resources, conditions and rests.
          </li>
          <li>
            <strong>Spells</strong>: spells and slots. Only characters that cast spells have it.
          </li>
          <li>
            <strong>Gear</strong>: what you carry and wear.
          </li>
          <li>
            <strong>Features</strong>: what your class, species and background give you, and this character&apos;s gap notes.
          </li>
          <li>
            <strong>Skills and saves</strong>: every skill, save and worked-out number.
          </li>
          <li>
            <strong>Journal</strong>: notes, details and the story so far.
          </li>
        </ul>
        <p>
          The <strong>More</strong> list beside the tabs has Choices (when the character has some to change), Print…, Export…, Snapshots, Updates and
          Archive.
        </p>
      </section>

      <section aria-labelledby="help-why">
        <h2 id="help-why">&quot;Why?&quot; and how numbers are worked out</h2>
        <p>
          TomeStack works a number out from your choices: your class, species, background, gear and anything you set by hand. Press <strong>Why?</strong>{' '}
          under a number to see each step and where it came from. If a number is wrong for your table, set it by hand; the sheet marks it as set by hand.
        </p>
      </section>

      <section aria-labelledby="help-backups">
        <h2 id="help-backups">Backups and moving to another computer</h2>
        <p>
          Your characters live in one data folder on this computer and are never sent anywhere. Make a backup now and then from <strong>Backups</strong>, and
          keep a copy somewhere else, such as a cloud drive or a memory stick.
        </p>
        <p>
          To use a character on another computer, export it as a TomeStack file from the sheet&apos;s More list and open that file there with{' '}
          <strong>Add a character</strong>. To move everything, restore a backup on the other computer.
        </p>
      </section>

      <section aria-labelledby="help-gaps">
        <h2 id="help-gaps">What the sheet can&apos;t do yet</h2>
        <GuideText markdown={knownGaps} />
      </section>

      <section aria-labelledby="help-shortcuts">
        <h2 id="help-shortcuts">Keyboard shortcuts</h2>
        <dl className="shortcuts">
          <dt>Ctrl+B</dt>
          <dd>Hide or show the sidebar</dd>
          <dt>Ctrl + / Ctrl − / Ctrl 0</dt>
          <dd>Zoom the window in, out, or back to normal</dd>
          <dt>Left, Right, Home, End</dt>
          <dd>Move between the sheet&apos;s pages on the tab strip</dd>
          <dt>Tab, Shift+Tab</dt>
          <dd>Move between controls</dd>
        </dl>
      </section>

      <section aria-labelledby="help-gap-notes">
        <h2 id="help-gap-notes">Gap notes</h2>
        <AllGapNotesPanel onError={onError} onOpenCharacter={onOpenCharacter} />
      </section>

      <section aria-labelledby="help-glossary">
        <h2 id="help-glossary">Glossary</h2>
        <dl className="glossary">
          {glossary.map(([term, meaning]) => (
            <div key={term}>
              <dt>{term}</dt>
              <dd>{meaning}</dd>
            </div>
          ))}
        </dl>
      </section>
    </section>
  );
}
