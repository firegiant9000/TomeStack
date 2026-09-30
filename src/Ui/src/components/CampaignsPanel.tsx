import { useEffect, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { Campaign, CampaignPackPreview, RulesFamilyId, RulesFamilyPolicy, SourceRecord } from '../api/types';
import { downloadBase64 } from '../files';

interface Props {
  rulesFamilies: RulesFamilyPolicy[];
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

const blank = (family: RulesFamilyId): Campaign => ({ id: '00000000-0000-0000-0000-000000000000', name: '', rulesFamily: family, allowedSources: [] });

/**
 * M6 slice 2: share a campaign as a campaign pack. The preview says what goes (the profile, and the homebrew you marked
 * as your own work), what is referenced (the SRD) and what is left out and why; nothing is written until "Save".
 */
function CampaignPackForm({ campaign, onError, onStatus }: { campaign: Campaign } & Omit<Props, 'rulesFamilies'>) {
  const [preview, setPreview] = useState<CampaignPackPreview>();

  useEffect(() => {
    client.campaignPackPreview(campaign.id).then(setPreview).catch(onError);
  }, [campaign.id, onError]);

  async function save() {
    try {
      const outcome = await client.saveCampaignPackAs(campaign.id);
      if (outcome.saved) onStatus(`Saved the campaign pack ${outcome.fileName}.`);
    } catch (error) {
      if (error instanceof TomeStackError && error.code === 'unsupported') {
        // Browser development (DevHost) has no native Save dialog: download the pack instead.
        try {
          const exported = await client.exportCampaignPack(campaign.id);
          downloadBase64(exported.fileName, exported.base64);
          onStatus(`Downloaded the campaign pack ${exported.fileName}.`);
        } catch (inner) {
          onError(inner);
        }
        return;
      }
      onError(error);
    }
  }

  return (
    <section className="play-panel" aria-label={`Share ${campaign.name}`}>
      <h3>Share {campaign.name} as a campaign pack</h3>
      <p className="hint">
        A campaign pack carries the profile (rules, allowed sources and house rules) and the published content of homebrew
        you marked as your own work. It never carries characters, gap notes, drafts or PDFs. The SRD is named, not copied.
      </p>
      {!preview ? (
        <p>Checking what the pack would hold…</p>
      ) : (
        <>
          <p>
            {preview.fileName}: {preview.included.length} source{preview.included.length === 1 ? '' : 's'} carried with{' '}
            {preview.revisions} published revision{preview.revisions === 1 ? '' : 's'}
            {preview.referenced.length > 0 ? `; ${preview.referenced.map((s) => s.title).join(', ')} named, not copied` : ''}.
          </p>
          <div role="region" aria-label="Left out of the campaign pack">
            {preview.leftOut.length === 0 ? (
              <p>Nothing is left out.</p>
            ) : (
              <>
                <p>These allowed sources are only named; whoever imports the pack needs their own copy:</p>
                <ul>
                  {preview.leftOut.map((l) => (
                    <li key={l.sourceId}>
                      {l.title} ({l.publisher}): {l.reason.message}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>
          {preview.warnings.length > 0 && (
            <ul className="warnings">
              {preview.warnings.map((w, i) => (
                <li key={i}>{w.message}</li>
              ))}
            </ul>
          )}
          <div className="actions">
            <button type="button" onClick={save}>
              Save campaign pack…
            </button>
          </div>
        </>
      )}
    </section>
  );
}

/**
 * M2 item 7, SPEC P-01, BACKLOG B12: local campaign profiles. Each names a rules family and the sources its characters
 * may use. The builder hides nothing silently: content outside the campaign is listed as not allowed, and using it
 * needs a reason (a recorded exception). Profiles never change calculation.
 */
export function CampaignsPanel({ rulesFamilies, onError, onStatus }: Props) {
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [sources, setSources] = useState<SourceRecord[]>([]);
  const [editing, setEditing] = useState<Campaign>();
  const [sharing, setSharing] = useState<Campaign>();

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
  // M6 slice 2: allowed sources a campaign pack named that are not installed yet. They stay allowed until unticked.
  const pendingOf = (c: Campaign) => (c.pendingSources ?? []).filter((p) => !sources.some((s) => s.id === p.sourceId));

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
              </button>{' '}
              <button type="button" onClick={() => setSharing(sharing?.id === c.id ? undefined : c)} aria-expanded={sharing?.id === c.id}>
                Share {c.name}…
              </button>
              {pendingOf(c).length > 0 && (
                <p className="hint">
                  Waiting for sources that are not installed:{' '}
                  {pendingOf(c)
                    .map((p) => `${p.title} (${p.publisher})`)
                    .join(', ')}
                  . Install them to use their content here.
                </p>
              )}
            </li>
          ))}
        </ul>
      )}
      {sharing && <CampaignPackForm key={sharing.id} campaign={sharing} onError={onError} onStatus={onStatus} />}
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
                  onChange={() =>
                    // Sources of the other family are hidden below, so they are dropped rather than saved unseen.
                    setEditing({
                      ...editing,
                      rulesFamily: f.id,
                      // A pending source is not installed, so its families are unknown: it stays (it is listed below).
                      allowedSources: editing.allowedSources.filter(
                        (id) => sources.some((s) => s.id === id && s.rulesFamilies.includes(f.id)) || pendingOf(editing).some((p) => p.sourceId === id),
                      ),
                    })
                  }
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
            {pendingOf(editing)
              .filter((p) => editing.allowedSources.includes(p.sourceId))
              .map((p) => (
                <label key={p.sourceId} className="choice">
                  <input
                    type="checkbox"
                    checked
                    onChange={() => setEditing({ ...editing, allowedSources: editing.allowedSources.filter((x) => x !== p.sourceId) })}
                  />
                  {p.title} <span className="option-source">{p.publisher}</span> <span className="tag">not installed</span>
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
