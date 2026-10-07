import { useEffect, useState } from 'react';
import { client } from '../../api/client';
import type { Campaign, RulesFamilyId, RulesFamilyPolicy } from '../../api/types';
import { numberPresetIds, numberPresetLabels, type NumberPreset } from './ddbLabels';

interface Props {
  rulesFamilies: RulesFamilyPolicy[];
  suggested?: RulesFamilyId;
  family: RulesFamilyId;
  campaignId?: string;
  onFamily: (family: RulesFamilyId) => void;
  onCampaign: (campaignId: string | undefined) => void;
  numberPreset: NumberPreset;
  onNumberPreset: (preset: NumberPreset) => void;
  onError: (error: unknown) => void;
}

/** Step 2: the layout suggests a family, but the user picks it; picking the other one is warned about (C-01, S-02). */
export function DdbFamilyStep({ rulesFamilies, suggested, family, campaignId, onFamily, onCampaign, numberPreset, onNumberPreset, onError }: Props) {
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);

  useEffect(() => {
    client.listCampaigns().then(setCampaigns).catch(onError);
  }, [onError]);

  const options = campaigns.filter((c) => c.rulesFamily === family);
  const suggestedName = rulesFamilies.find((f) => f.id === suggested)?.displayName;

  return (
    <>
      <fieldset>
        <legend>Rules family</legend>
        {rulesFamilies.map((f) => (
          <label key={f.id}>
            <input type="radio" name="ddb-family" checked={family === f.id} onChange={() => onFamily(f.id)} /> {f.displayName}
            {f.id === suggested && <span className="tag">suggested by the sheet</span>}
          </label>
        ))}
      </fieldset>
      {suggested && suggested !== family && (
        <p role="note" className="warn">
          This looks like a sheet for {suggestedName}; names will match {rulesFamilies.find((f) => f.id === family)?.displayName} content.
        </p>
      )}
      <label>
        Campaign (optional){' '}
        <select value={campaignId ?? ''} onChange={(e) => onCampaign(e.target.value || undefined)}>
          <option value="">No campaign</option>
          {options.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
      </label>
      {/* D16h: the preset decides only the Numbers rows that differ. Matches are about identity and are always reviewed. */}
      <fieldset role="radiogroup" aria-labelledby="ddb-number-preset-legend">
        <legend id="ddb-number-preset-legend">Numbers that differ from TomeStack's calculation</legend>
        {numberPresetIds.map((preset) => (
          <label key={preset}>
            <input type="radio" name="ddb-number-preset" checked={numberPreset === preset} onChange={() => onNumberPreset(preset)} /> {numberPresetLabels[preset]}
          </label>
        ))}
        <p className="hint">Pre-fills the Numbers step; you can still change any row there. The sheet's values become overrides that say "Imported from D&amp;D Beyond".</p>
      </fieldset>
    </>
  );
}
