import { useEffect, useMemo, useState } from 'react';
import { client } from '../api/client';
import type { CharacterSheet, MechanicEntry, MechanicsReport, PageRef } from '../api/types';
import { pageText } from './PlayPanels';

/** One line of a group: the feature, what it does by hand, and the page it comes from. */
interface ByHandRow {
  key: string;
  name: string;
  detail?: string;
  page?: PageRef;
}

interface ByHandGroups {
  spend: ByHandRow[];
  reference: ByHandRow[];
  assisted: ByHandRow[];
  unsupported: ByHandRow[];
}

type ReportResult =
  | { sheet: CharacterSheet; characterId: string; report: MechanicsReport }
  | { sheet: CharacterSheet; characterId: string; failed: true };

const rowKey = (m: MechanicEntry) => `${m.content.revisionId}|${m.effectId ?? ''}|${m.effectType}`;

/** The sources the sheet's features come from, sorted so the same sheet always asks the same question. */
function sheetSourceIds(sheet: CharacterSheet): string[] {
  return [...new Set((sheet.features ?? []).flatMap((f) => (f.origin.sourceId ? [f.origin.sourceId] : [])))].sort();
}

/**
 * The one place the mechanics report is read (D69). It asks for the sheet's own sources only while `active`, again whenever
 * the `sheet` object changes, and a result is used only for the sheet and character it was asked for. Lab Task 3 swaps
 * this for the effective automation; nothing else in the panel changes.
 */
function useMechanicsReport(characterId: string, sheet: CharacterSheet, sourceIds: string[], active: boolean) {
  const [result, setResult] = useState<ReportResult | null>(null);
  const sourceKey = sourceIds.join(',');
  useEffect(() => {
    if (!active || sourceKey === '') return;
    let current = true;
    client
      .characterMechanics(characterId, sourceKey.split(','))
      .then((report) => { if (current) setResult({ sheet, characterId, report }); })
      .catch(() => { if (current) setResult({ sheet, characterId, failed: true }); });
    return () => { current = false; };
  }, [active, characterId, sheet, sourceKey]);
  return result && result.sheet === sheet && result.characterId === characterId ? result : null;
}

/** "Spend by hand" comes from the sheet: a roll that names a resource (the test `MechanicsInventory.Of` uses for limited-use actions). */
function spendRows(sheet: CharacterSheet): ByHandRow[] {
  return (sheet.features ?? [])
    .filter((f) => f.origin.sourceId)
    .flatMap((f) =>
      f.effects
        .filter((e) => e.type === 'roll' && e.resourceId)
        .map((e) => ({ key: `${f.content.revisionId}|${e.id}`, name: f.name, detail: e.label, page: f.origin.page })),
    );
}

function groupReport(sheet: CharacterSheet, report: MechanicsReport | null): ByHandGroups {
  const toRow = (m: MechanicEntry): ByHandRow => ({ key: rowKey(m), name: m.featureName, page: m.page });
  const unique = (rows: ByHandRow[]) => rows.filter((r, i) => rows.findIndex((o) => o.key === r.key) === i);
  const unsupported = report?.unsupported ?? [];
  const unsupportedKeys = new Set(unsupported.map(rowKey));
  // Unsupported is a subset of Mechanics: a row in "Not understood" appears nowhere else. Pure text is never listed.
  const rest = (report?.mechanics ?? []).filter((m) => !unsupportedKeys.has(rowKey(m)) && m.effectType !== 'text');
  return {
    spend: unique(spendRows(sheet)),
    reference: unique(rest.filter((m) => m.automation === 'reference').map(toRow)),
    assisted: unique(rest.filter((m) => m.automation === 'assisted').map(toRow)),
    unsupported: unique(unsupported.map(toRow)),
  };
}

function Group({ id, title, rows }: { id: string; title: string; rows: ByHandRow[] }) {
  if (rows.length === 0) return null;
  return (
    <section aria-labelledby={id}>
      <h4 id={id}>{title}</h4>
      <ul>
        {rows.map((row) => (
          <li key={row.key}>
            {row.name}
            {row.detail ? `: ${row.detail}` : ''}
            {row.page ? `, ${pageText(row.page)}` : ''}
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * P18 (D69): at the end of the Features tab, what TomeStack will not do for the player: limited-use rolls to spend,
 * reference-only text, partly automatic effects and effects it does not understand. UI only; it reads `character.mechanics`.
 */
export function ByHandPanel({ characterId, sheet, active }: { characterId: string; sheet: CharacterSheet; active: boolean }) {
  const sourceIds = useMemo(() => sheetSourceIds(sheet), [sheet]);
  const result = useMechanicsReport(characterId, sheet, sourceIds, active);
  const failed = result !== null && 'failed' in result;
  const report = result !== null && 'report' in result ? result.report : null;
  const loading = sourceIds.length > 0 && result === null;
  const groups = groupReport(sheet, report);
  const empty = groups.spend.length + groups.reference.length + groups.assisted.length + groups.unsupported.length === 0;

  return (
    <section aria-labelledby="by-hand-heading" className="play-panel">
      <h3 id="by-hand-heading">TomeStack won't do these for you</h3>
      {failed ? (
        <p className="hint">Couldn't list what needs you by hand.</p>
      ) : loading ? (
        <p className="hint">Checking what needs you by hand.</p>
      ) : (
        <>
          <p className="hint">TomeStack never acts on its own; these need you.</p>
          {empty && <p className="hint">Everything on this sheet is calculated.</p>}
          <Group id="by-hand-spend" title="Spend by hand" rows={groups.spend} />
          <Group id="by-hand-reference" title="Reference only" rows={groups.reference} />
          <Group id="by-hand-assisted" title="Partly automatic" rows={groups.assisted} />
          <Group id="by-hand-unsupported" title="Not understood" rows={groups.unsupported} />
        </>
      )}
    </section>
  );
}
