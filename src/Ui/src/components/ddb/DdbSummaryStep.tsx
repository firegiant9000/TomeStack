import type { DdbPreview, RulesFamilyId } from '../../api/types';
import { abilityNames, notBroughtOverLabels } from './ddbLabels';

interface Props {
  preview?: DdbPreview;
  family?: RulesFamilyId;
  suggested?: RulesFamilyId;
  includePlayState: boolean;
  onIncludePlayState: (include: boolean) => void;
  /** D35: publish the created class with the character (off by default). */
  publishClass: boolean;
  onPublishClass: (publish: boolean) => void;
  /** Item 16: publish the reference drafts with the character (off by default). */
  publishReferences?: boolean;
  onPublishReferences?: (publish: boolean) => void;
  /** Item 19a: keep the lines a text import could not read as session notes (on by default; part of the request). */
  keepNotRead?: boolean;
  onKeepNotRead?: (keep: boolean) => void;
}

/** How many not-read lines the summary lists before "and N more" (all of them are still kept when the tick is on). */
const shownNotRead = 200;

/** How many reference drafts the summary lists before "and N more". */
const shownDrafts = 10;

/** Step 5: what will be created and what will not, before the one write. */
export function DdbSummaryStep({ preview, family, suggested, includePlayState, onIncludePlayState, publishClass, onPublishClass, publishReferences = false, onPublishReferences, keepNotRead = true, onKeepNotRead }: Props) {
  if (!preview) return <p className="hint">Preparing the summary…</p>;
  const { character, matches, report } = preview;
  const unplaced = report.notFound + report.noPlace + report.unreadable;
  const waiting = matches.filter((m) => m.status === 'choose').length;
  const drafts = preview.referenceDrafts ?? [];
  const notRead = preview.notRead ?? [];
  const classes = matches.filter((m) => m.kind === 'class' && m.status === 'matched').map((m) => m.label);
  return (
    <>
      <dl>
        {report.layoutName && (
          <>
            <dt>Layout read</dt>
            <dd>{report.layoutName}</dd>
          </>
        )}
        <dt>Name</dt>
        <dd>{character.name}</dd>
        <dt>Classes</dt>
        <dd>{classes.length > 0 ? classes.join(' / ') : 'None (the classes could not be read or matched)'}</dd>
        <dt>Will be created</dt>
        <dd>
          {character.pins.length} pinned, {character.choices.length} choices answered, {character.spells?.length ?? 0} spells,{' '}
          {character.equipment?.length ?? 0} items, {character.overrides.length} overrides
        </dd>
        <dt>Gap notes</dt>
        <dd>{unplaced} items not brought over will be noted as gaps on the character.</dd>
      </dl>
      {preview.stub && (
        <section aria-labelledby="ddb-stub-class">
          <h4 id="ddb-stub-class">Homebrew class to create</h4>
          <dl>
            <dt>Class</dt>
            <dd>{preview.stub.name}</dd>
            <dt>Rules</dt>
            <dd>{preview.stub.rulesFamily}</dd>
            <dt>Hit die</dt>
            <dd>d{preview.stub.hitDie}</dd>
            <dt>Saving throws</dt>
            <dd>{preview.stub.saves.length > 0 ? preview.stub.saves.map((s) => abilityNames[s] ?? s).join(', ') : 'None'}</dd>
            <dt>Saved in</dt>
            <dd>{preview.stub.sourceTitle}, a source that is never shared</dd>
          </dl>
          <p className="hint">
            The class has only a hit die and saving throws. Add features, spells and proficiencies in the studio. It lives in a source that is never shared,
            so your later work on it cannot be shared or moved; to share your own version, make a new class.
          </p>
          <label>
            <input type="checkbox" checked={publishClass} onChange={(e) => onPublishClass(e.target.checked)} /> Publish it
          </label>
          <p role="note">
            {publishClass
              ? 'The class is published with the character, so its hit die and saving throws count now.'
              : 'The class is a draft: the character gets nothing from it until you publish it in the studio and accept the update on the character.'}
          </p>
        </section>
      )}
      {drafts.length > 0 && (
        <section aria-labelledby="ddb-reference-drafts">
          <h4 id="ddb-reference-drafts">Reference drafts</h4>
          <ul>
            {drafts.slice(0, shownDrafts).map((d) => (
              <li key={d.rowId}>
                <strong>{d.name}</strong>
                {d.firstLine && <> — {d.firstLine}</>}
              </li>
            ))}
            {drafts.length > shownDrafts && <li>and {drafts.length - shownDrafts} more</li>}
          </ul>
          <label>
            <input type="checkbox" checked={publishReferences} onChange={(e) => onPublishReferences?.(e.target.checked)} />{' '}
            {drafts.length === 1 ? 'Publish this reference draft' : `Publish these ${drafts.length} reference drafts`}
          </label>
          <p role="note">
            They hold a name and a description only, no mechanics. Features are kept as reference content in the studio; the gap notes stay on the
            character. Published feats (if any) are pinned on the character.
          </p>
          {preview.referenceNote === 'campaign.source-not-allowed' && (
            <p role="note" className="warn">
              This character&apos;s campaign does not allow the source these drafts are saved in. A published feat is still pinned.
            </p>
          )}
        </section>
      )}
      <label>
        <input type="checkbox" checked={includePlayState} onChange={(e) => onIncludePlayState(e.target.checked)} /> Bring over current hit points,
        spent hit dice and slots, death saves and inspiration
      </label>
      <section aria-labelledby="ddb-not-brought-over">
        <h4 id="ddb-not-brought-over">Not brought over</h4>
        <ul>
          {report.notBroughtOver.map((code) => (
            <li key={code}>{notBroughtOverLabels[code] ?? code}</li>
          ))}
          {unplaced > 0 && <li>{unplaced} items with no match or no place (kept as gap notes)</li>}
          {report.leftOut > 0 && <li>{report.leftOut} items you left out</li>}
        </ul>
      </section>
      {notRead.length > 0 && (
        <section aria-labelledby="ddb-not-read">
          <h4 id="ddb-not-read">Lines not read</h4>
          <ul>
            {notRead.slice(0, shownNotRead).map((n) => (
              <li key={n.line}>
                Line {n.line}: {n.text}
              </li>
            ))}
            {notRead.length > shownNotRead && <li>and {notRead.length - shownNotRead} more</li>}
          </ul>
          <label>
            <input type="checkbox" checked={keepNotRead} onChange={(e) => onKeepNotRead?.(e.target.checked)} /> Keep the lines that were not read as session notes
          </label>
        </section>
      )}
      {report.lines && report.lines.length > 0 && (
        <section aria-labelledby="ddb-report-lines">
          <h4 id="ddb-report-lines">From reading the sheet</h4>
          <ul>
            {report.lines.map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        </section>
      )}
      {report.familyMismatch && suggested && family && (
        <p role="note" className="warn">
          The sheet looks like {suggested} rules; this character uses {family}.
        </p>
      )}
      {report.sameNameExists && (
        <p role="note">A character named {character.name} already exists. A second one will be created; nothing is replaced.</p>
      )}
      {!preview.canApply && (
        <p role="note" className="warn">
          {waiting > 0
            ? `${waiting} item(s) still need a choice or “Leave out” (step 3).`
            : 'The character cannot be created yet: it needs at least one class that TomeStack has.'}
        </p>
      )}
    </>
  );
}
