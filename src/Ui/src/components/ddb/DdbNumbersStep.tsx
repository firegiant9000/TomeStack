import type { DdbPreview, NumberAction } from '../../api/types';
import { numberActionLabels, numberPresetLabels, type NumberPreset } from './ddbLabels';

interface Props {
  preview?: DdbPreview;
  numbers: Record<string, NumberAction>;
  onChange: (field: string, action: NumberAction) => void;
  equipMatched: boolean;
  onEquipMatched: (equip: boolean) => void;
  preset: NumberPreset;
}

const actions: NumberAction[] = ['useTomeStack', 'keepSheet', 'note'];

/**
 * Step 4: the sheet's numbers beside TomeStack's, differences first. Per difference: TomeStack's number (the default), the
 * sheet's as an override (the calculated value stays beneath it), or a gap note. There is no "keep all".
 */
export function DdbNumbersStep({ preview, numbers, onChange, equipMatched, onEquipMatched, preset }: Props) {
  if (!preview) return <p className="hint">Comparing the numbers…</p>;
  const differences = preview.comparison.filter((n) => n.differs).length;
  return (
    <>
      <label>
        <input type="checkbox" checked={equipMatched} onChange={(e) => onEquipMatched(e.target.checked)} /> Equip matched weapons and armour
      </label>
      <p className="hint">
        The sheet does not mark what is equipped: this equips every matched weapon, the first armour and the first shield. It changes Armor Class, so it
        sits here, before you compare numbers. Check Inventory afterwards.
      </p>
      <p>
        {differences === 0 ? 'Every number the sheet shows matches TomeStack.' : `${differences} of ${preview.comparison.length} numbers differ.`} Speed and
        passive Perception are not calculated by TomeStack, so they are not compared.
      </p>
      {preset !== 'manual' && <p className="hint">Pre-filled from your choice on step 2 ("{numberPresetLabels[preset]}"). Change any row below.</p>}
      {preview.abilityPlan.notes.length > 0 && (
        <ul aria-label="Ability score notes">
          {preview.abilityPlan.notes.map((n) => (
            <li key={`${n.ability}-${n.code}`}>{n.message}</li>
          ))}
        </ul>
      )}
      {preview.comparison.length > 0 && (
        <table>
          <caption>Sheet and TomeStack numbers</caption>
          <thead>
            <tr>
              <th scope="col">Number</th>
              <th scope="col">Sheet</th>
              <th scope="col">TomeStack</th>
              <th scope="col">Your choice</th>
            </tr>
          </thead>
          <tbody>
            {preview.comparison.map((row) => {
              const headerId = `ddb-number-${row.field.replaceAll('.', '-')}`;
              const chosen = numbers[row.field] ?? 'useTomeStack';
              return (
                <tr key={row.field}>
                  <th scope="row" id={headerId}>
                    {row.label}
                  </th>
                  <td>{row.sheet ?? '—'}</td>
                  <td>{row.calculated}</td>
                  <td>
                    {row.differs ? (
                      <div role="radiogroup" aria-labelledby={headerId}>
                        {actions.map((action) => (
                          <label key={action}>
                            <input type="radio" name={headerId} checked={chosen === action} onChange={() => onChange(row.field, action)} />{' '}
                            {numberActionLabels[action]}
                          </label>
                        ))}
                      </div>
                    ) : (
                      'Same'
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </>
  );
}
