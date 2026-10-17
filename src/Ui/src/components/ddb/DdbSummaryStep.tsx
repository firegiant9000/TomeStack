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
}

/** Step 5: what will be created and what will not, before the one write. */
export function DdbSummaryStep({ preview, family, suggested, includePlayState, onIncludePlayState, publishClass, onPublishClass }: Props) {
  if (!preview) return <p className="hint">Preparing the summary…</p>;
  const { character, matches, report } = preview;
  const unplaced = report.notFound + report.noPlace + report.unreadable;
  const waiting = matches.filter((m) => m.status === 'choose').length;
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
          <p className="hint">The class has only a hit die and saving throws. Add features, spells and proficiencies in the studio.</p>
          <label>
            <input type="checkbox" checked={publishClass} onChange={(e) => onPublishClass(e.target.checked)} /> Publish it
          </label>
          <p role="note">
            {publishClass
              ? 'The class is published with the character, so its hit die and saving throws count now.'
              : 'The class is a draft: the character gets nothing from it until you publish it in the studio.'}
          </p>
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
