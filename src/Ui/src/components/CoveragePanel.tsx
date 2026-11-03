import { useEffect, useId, useState } from 'react';
import { client } from '../api/client';
import type { ClassCoverage, CoverageCounts, CoverageReport, FeatureCoverage, SourceCoverage } from '../api/types';

type Loaded = { sourceId: string; includeDrafts: boolean; report: CoverageReport } | { sourceId: string; includeDrafts: boolean; failed: true };

/** "7 automatic · 2 by hand (assisted) · 0 reference · 1 not understood": the four counts, always all four. */
export function coverageText(t: CoverageCounts): string {
  return `${t.automatic} automatic · ${t.assisted} by hand (assisted) · ${t.reference} reference · ${t.unsupported} not understood`;
}

function FeatureRow({ feature }: { feature: FeatureCoverage }) {
  const draft = feature.status === 'draft';
  const unsupported = feature.effects.filter((e) => e.unsupported);
  return (
    <li>
      {feature.name}
      {draft && ' (Draft, not counted)'}
      {`: ${coverageText(feature.totals)}`}
      {unsupported.map((e) => (
        <span key={e.effectId ?? e.type} className="hint">
          {' '}
          {e.type}: TomeStack can't calculate this.
        </span>
      ))}
      {feature.missingMechanic && <span className="hint"> May need a mechanic: by hand today.</span>}
    </li>
  );
}

function ClassBlock({ item, prefix }: { item: ClassCoverage; prefix: string }) {
  const headingId = `${prefix}-${item.contentId}`;
  return (
    <section aria-labelledby={headingId}>
      <h4 id={headingId}>{item.name}</h4>
      <p className="hint">{coverageText(item.totals)}</p>
      {item.levels.map((level) => {
        const levelId = `${headingId}-l${level.level}`;
        return (
          <section key={level.level} aria-labelledby={levelId}>
            <h5 id={levelId}>Level {level.level}</h5>
            <ul>
              {level.features.map((f) => (
                <FeatureRow key={`${f.revisionId}|${f.status}`} feature={f} />
              ))}
            </ul>
          </section>
        );
      })}
    </section>
  );
}

function SourceBlock({ source, prefix }: { source: SourceCoverage; prefix: string }) {
  const otherId = `${prefix}-other`;
  const empty = source.classes.length === 0 && source.other.length === 0;
  return (
    <>
      <p>{coverageText(source.totals)}</p>
      {empty && <p className="hint">Nothing in this source yet.</p>}
      {source.classes.map((c) => (
        <ClassBlock key={c.contentId} item={c} prefix={prefix} />
      ))}
      {source.other.length > 0 && (
        <section aria-labelledby={otherId}>
          <h4 id={otherId}>Other content</h4>
          <ul>
            {source.other.map((f) => (
              <FeatureRow key={`${f.revisionId}|${f.status}`} feature={f} />
            ))}
          </ul>
        </section>
      )}
    </>
  );
}

/**
 * Import Lab (D62 L1): what the engine does with each effect of one source's content, from `content.coverage`. Read-only.
 * Drafts sit beside their published rows when asked for and never enter a total. No live region: the app keeps one status line.
 * `refreshKey` asks again when the host's list of content changed (a save or a publish).
 */
export function CoveragePanel({ sourceId, refreshKey }: { sourceId: string; refreshKey?: unknown }) {
  const prefix = useId();
  const [includeDrafts, setIncludeDrafts] = useState(false);
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    let current = true;
    Promise.resolve()
      .then(() => client.contentCoverage({ sourceId, includeDrafts }))
      .then((report) => { if (current) setLoaded({ sourceId, includeDrafts, report }); })
      .catch(() => { if (current) setLoaded({ sourceId, includeDrafts, failed: true }); });
    return () => { current = false; };
  }, [sourceId, includeDrafts, refreshKey]);

  // Only a reply for this source is shown; the previous one for the same source stays while a refresh is in flight.
  const result = loaded && loaded.sourceId === sourceId ? loaded : null;
  const report = result && 'report' in result ? result.report.sources.find((s) => s.sourceId === sourceId) ?? result.report.sources[0] : undefined;

  return (
    <section aria-labelledby={`${prefix}-coverage`} className="play-panel">
      <h3 id={`${prefix}-coverage`}>Coverage</h3>
      <label className="choice">
        <input type="checkbox" checked={includeDrafts} onChange={(e) => setIncludeDrafts(e.target.checked)} />
        Show drafts
      </label>
      {includeDrafts && <p className="hint">Totals count published content only; drafts are listed beside it and never counted.</p>}
      {result === null ? (
        <p className="hint">Checking what TomeStack does with this source.</p>
      ) : 'failed' in result ? (
        <p className="hint">Couldn't load the coverage.</p>
      ) : report ? (
        <SourceBlock source={report} prefix={prefix} />
      ) : (
        <p className="hint">Nothing in this source yet.</p>
      )}
    </section>
  );
}
