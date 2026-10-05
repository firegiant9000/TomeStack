import type { DdbPreview, RulesFamilyId } from '../../api/types';
import { notBroughtOverLabels } from './ddbLabels';

interface Props {
  preview?: DdbPreview;
  family?: RulesFamilyId;
  suggested?: RulesFamilyId;
  includePlayState: boolean;
  onIncludePlayState: (include: boolean) => void;
  equipMatched: boolean;
  onEquipMatched: (equip: boolean) => void;
}

/** Step 5: what will be created and what will not, before the one write. */
export function DdbSummaryStep({ preview, family, suggested, includePlayState, onIncludePlayState, equipMatched, onEquipMatched }: Props) {
  if (!preview) return <p className="hint">Preparing the summary…</p>;
  const { character, matches, report } = preview;
  const unplaced = report.notFound + report.noPlace + report.unreadable;
  const waiting = matches.filter((m) => m.status === 'choose').length;
  const classes = matches.filter((m) => m.kind === 'class' && m.status === 'matched').map((m) => m.label);
  return (
    <>
      <dl>
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
      <label>
        <input type="checkbox" checked={includePlayState} onChange={(e) => onIncludePlayState(e.target.checked)} /> Bring over current hit points,
        spent hit dice and slots, death saves and inspiration
      </label>
      <label>
        <input type="checkbox" checked={equipMatched} onChange={(e) => onEquipMatched(e.target.checked)} /> Equip matched weapons and armour
      </label>
      <p className="hint">The sheet does not mark what is equipped: this equips every matched weapon, the first armour and the first shield. Check Inventory afterwards.</p>
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
