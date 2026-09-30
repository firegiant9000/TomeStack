import { useState } from 'react';
import { client } from '../api/client';
import type { ImportResult, PackageItem, PackagePreview, SourceChoice } from '../api/types';

/**
 * A source whose metadata differs from the local record, or (M6 slice 2) a campaign pack's campaign that differs from
 * yours: show the diff and require an explicit choice.
 */
export function SourceDecision({ item, choice, onChoose }: { item: PackageItem; choice?: SourceChoice; onChoose: (choice: SourceChoice) => void }) {
  const name = `${item.kind}-choice-${item.id}`;
  return (
    <fieldset className="source-decision">
      <legend>
        {item.kind === 'campaign' ? `Campaign “${item.name}” differs from yours` : `“${item.name}” differs from your local record`}
      </legend>
      <table>
        <caption className="visually-hidden">Differences in {item.name}</caption>
        <thead>
          <tr>
            <th scope="col">Field</th>
            <th scope="col">Your local version</th>
            <th scope="col">In the package</th>
          </tr>
        </thead>
        <tbody>
          {item.changes?.map((change) => (
            <tr key={change.field}>
              <th scope="row">{change.field}</th>
              <td>{change.local ?? '(not set)'}</td>
              <td>{change.imported ?? '(not set)'}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <label className="choice">
        <input type="radio" name={name} checked={choice === 'keepLocal'} onChange={() => onChoose('keepLocal')} />
        Keep my local version
      </label>
      <label className="choice">
        <input type="radio" name={name} checked={choice === 'useImported'} onChange={() => onChoose('useImported')} />
        Use the imported version
      </label>
    </fieldset>
  );
}

interface Props {
  fileName: string;
  base64: string;
  preview: PackagePreview;
  onApplied: (result: ImportResult) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
}

/** SPEC P-02: the importer shows dependencies and conflicts; nothing is written until the user applies. */
export function ImportPreview({ fileName, base64, preview, onApplied, onCancel, onError }: Props) {
  const [applying, setApplying] = useState(false);
  const [choices, setChoices] = useState<Record<string, SourceChoice>>({});
  const [campaignChoices, setCampaignChoices] = useState<Record<string, SourceChoice>>({});
  const decisions = preview.items.filter((item) => item.kind === 'source' && item.changes && item.changes.length > 0);
  // M6 slice 2: a campaign pack's campaign that differs from yours needs the same explicit choice.
  const campaignDecisions = preview.items.filter((item) => item.kind === 'campaign' && item.changes && item.changes.length > 0);
  const undecided =
    decisions.filter((item) => !choices[item.id]).length + campaignDecisions.filter((item) => !campaignChoices[item.id]).length;
  const impact = preview.campaignImpact ?? [];

  async function apply() {
    setApplying(true);
    try {
      onApplied(await client.applyImport(base64, choices, campaignChoices));
    } catch (error) {
      onError(error);
    } finally {
      setApplying(false);
    }
  }

  return (
    <section className="panel" aria-labelledby="import-heading">
      <h2 id="import-heading">Import {fileName}</h2>

      {preview.errors.length > 0 && (
        <div role="alert">
          <h3>This package cannot be imported</h3>
          <ul className="errors">
            {preview.errors.map((e, i) => (
              <li key={i}>{e.message}</li>
            ))}
          </ul>
        </div>
      )}

      {preview.items.length > 0 && (
        <table>
          <caption>Package contents</caption>
          <thead>
            <tr>
              <th scope="col">Type</th>
              <th scope="col">Name</th>
              <th scope="col">Action</th>
              <th scope="col">Details</th>
            </tr>
          </thead>
          <tbody>
            {preview.items.map((item) => (
              <tr key={`${item.kind}-${item.id}`} className={`action-${item.action}`}>
                <td>{item.kind}</td>
                <td>{item.name}</td>
                <td>{item.action}</td>
                <td>{item.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {preview.warnings.length > 0 && (
        <ul className="warnings">
          {preview.warnings.map((w, i) => (
            <li key={i}>{w.message}</li>
          ))}
        </ul>
      )}

      {preview.manifest && (
        <>
          <h3>Licenses</h3>
          <ul>
            {preview.manifest.notices.map((n) => (
              <li key={n.sourceId}>
                {n.title} ({n.publisher}): {n.license}
                {n.redistributable ? '' : ' · not for redistribution'}
              </li>
            ))}
          </ul>
          <p className="hint">{preview.manifest.attachmentPolicy}</p>
          {preview.manifest.scope === 'source' && (
            <p>
              This is a source pack. Its sender stated that each source is their own work; TomeStack cannot check that. The
              sources are recorded as received from someone else, so you cannot mark them as your own. A copy of your
              library is saved in the backups folder before anything changes.
            </p>
          )}
          {preview.manifest.scope === 'campaign' && (
            <p>
              This is a campaign pack: a campaign profile and the homebrew its sender marked as their own work. It carries no
              characters. Sources that were not the sender&apos;s to share are only named: the campaign waits for them until
              you install them yourself. If the import changes anything, a copy of your library is saved in the backups folder
              first.
            </p>
          )}
        </>
      )}

      {decisions.map((item) => (
        <SourceDecision
          key={item.id}
          item={item}
          choice={choices[item.id]}
          onChoose={(choice) => setChoices((current) => ({ ...current, [item.id]: choice }))}
        />
      ))}

      {campaignDecisions.map((item) => (
        <SourceDecision
          key={item.id}
          item={item}
          choice={campaignChoices[item.id]}
          onChoose={(choice) => setCampaignChoices((current) => ({ ...current, [item.id]: choice }))}
        />
      ))}

      {impact.length > 0 && (
        <section aria-labelledby="campaign-impact-heading">
          <h3 id="campaign-impact-heading">Characters affected if you use the imported campaign</h3>
          <ul>
            {impact.map((i) => (
              <li key={`${i.campaignId}-${i.characterId}`}>
                {i.characterName}:{' '}
                {i.notAllowed.length > 0 && <>no longer allowed: {i.notAllowed.join(', ')}.</>}
                {i.rulesFamily && <> The campaign would play {i.rulesFamily}.</>}
              </li>
            ))}
          </ul>
          <p className="hint">Nothing changes on these characters; their sheets show the content as not allowed until you decide.</p>
        </section>
      )}

      {undecided > 0 && (
        <p className="hint" id="import-undecided">
          Choose a version for {undecided === 1 ? 'the item above' : `each of the ${undecided} items above`} before importing.
        </p>
      )}

      <div className="actions">
        <button
          type="button"
          onClick={apply}
          disabled={!preview.canApply || applying || undecided > 0}
          aria-describedby={undecided > 0 ? 'import-undecided' : undefined}
        >
          {applying ? 'Importing…' : 'Apply import'}
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </section>
  );
}
