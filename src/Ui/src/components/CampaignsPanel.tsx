import { useEffect, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { Campaign, RulesFamilyId, RulesFamilyPolicy, SourceRecord } from '../api/types';

interface Props {
  rulesFamilies: RulesFamilyPolicy[];
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

const blank = (family: RulesFamilyId): Campaign => ({ id: '00000000-0000-0000-0000-000000000000', name: '', rulesFamily: family, allowedSources: [] });

/**
 * M2 item 7, SPEC P-01, BACKLOG B12: local campaign profiles. Each names a rules family and the sources its characters
 * may use. The builder hides nothing silently: content outside the campaign is listed as not allowed, and using it
 * needs a reason (a recorded exception). Profiles never change calculation.
 */
export function CampaignsPanel({ rulesFamilies, onError, onStatus }: Props) {
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [sources, setSources] = useState<SourceRecord[]>([]);
  const [editing, setEditing] = useState<Campaign>();

  useEffect(() => {
    Promise.all([client.listCampaigns(), client.listSources()])
      .then(([c, s]) => {
        setCampaigns(c);
        setSources(s);
      })
      .catch(onError);
  }, [onError]);

  async function save(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editing) return;
    try {
      const saved = await client.saveCampaign(editing);
      setCampaigns(await client.listCampaigns());
      setEditing(undefined);
      onStatus(`Saved the campaign ${saved.name}.`);
    } catch (error) {
      onError(error);
    }
  }

  const familySources = editing ? sources.filter((s) => s.rulesFamilies.includes(editing.rulesFamily)) : [];

  return (
    <section className="panel" aria-labelledby="campaigns-heading">
      <h2 id="campaigns-heading">Campaigns</h2>
      <p className="hint">
        A campaign says which rules and sources its characters use. Content from other sources is marked as not allowed; the
        player can still use it with a reason. Campaigns never change how numbers are calculated.
      </p>
      {campaigns.length === 0 ? (
        <p className="hint">No campaigns yet.</p>
      ) : (
        <ul className="resources">
          {campaigns.map((c) => (
            <li key={c.id} className="resource">
              <span className="option-name">{c.name}</span> <span className="tag">{c.rulesFamily}</span>{' '}
              <span className="hint">
                {c.allowedSources.length} allowed source{c.allowedSources.length === 1 ? '' : 's'}
              </span>{' '}
              <button type="button" onClick={() => setEditing(c)}>
                Edit {c.name}
              </button>
            </li>
          ))}
        </ul>
      )}
      {!editing && (
        <button type="button" onClick={() => setEditing(blank(rulesFamilies[0]?.id ?? 'srd-5.1'))}>
          New campaign
        </button>
      )}
      {editing && (
        <form onSubmit={save} aria-label="Campaign" className="play-panel">
          <label className="field">
            Campaign name
            <input required value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
          </label>
          <fieldset>
            <legend>Campaign rules</legend>
            {rulesFamilies.map((f) => (
              <label key={f.id} className="choice">
                <input
                  type="radio"
                  name="campaign-family"
                  checked={editing.rulesFamily === f.id}
                  onChange={() => setEditing({ ...editing, rulesFamily: f.id })}
                />
                {f.displayName}
              </label>
            ))}
          </fieldset>
          <fieldset>
            <legend>Allowed sources</legend>
            {familySources.map((s) => (
              <label key={s.id} className="choice">
                <input
                  type="checkbox"
                  checked={editing.allowedSources.includes(s.id)}
                  onChange={() =>
                    setEditing({
                      ...editing,
                      allowedSources: editing.allowedSources.includes(s.id)
                        ? editing.allowedSources.filter((x) => x !== s.id)
                        : [...editing.allowedSources, s.id],
                    })
                  }
                />
                {s.title} <span className="option-source">{s.publisher}</span>
              </label>
            ))}
          </fieldset>
          <label className="field">
            House rules and notes
            <textarea rows={3} value={editing.houseRules ?? ''} onChange={(e) => setEditing({ ...editing, houseRules: e.target.value })} />
          </label>
          <div className="actions">
            <button type="submit">Save campaign</button>
            <button type="button" onClick={() => setEditing(undefined)}>
              Cancel
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
