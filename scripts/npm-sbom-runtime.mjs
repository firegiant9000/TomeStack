// Reads a CycloneDX SBOM from `npm sbom --package-lock-only` and writes the runtime-only SBOM.
// `npm sbom --omit dev` returns no components at all with npm 10.9 (checked 2026-11-12, even though `npm ls --omit dev`
// lists react), so the release keeps every component npm does not flag as development-only and drops the rest.
// Usage: node scripts/npm-sbom-runtime.mjs <input.json> --out <output.json>
// The output is UTF-8 without a BOM, written by node itself (no shell pipeline re-encodes it).
import { readFileSync, writeFileSync } from 'node:fs';

const args = process.argv.slice(2);
const outAt = args.indexOf('--out');
const input = args.find((a, i) => !a.startsWith('--') && i !== outAt + 1);
const out = outAt >= 0 ? args[outAt + 1] : undefined;
if (!input || !out) {
  console.error('Usage: node scripts/npm-sbom-runtime.mjs <input.json> --out <output.json>');
  process.exit(2);
}

const bom = JSON.parse(readFileSync(input, 'utf8'));
const isDev = (c) => (c.properties ?? []).some((p) => p.name === 'cdx:npm:package:development' && p.value === 'true');

bom.components = (bom.components ?? []).filter((c) => !isDev(c));
const refs = new Set([bom.metadata.component['bom-ref'], ...bom.components.map((c) => c['bom-ref'])]);
bom.dependencies = (bom.dependencies ?? [])
  .filter((d) => refs.has(d.ref))
  .map((d) => ({ ...d, dependsOn: (d.dependsOn ?? []).filter((r) => refs.has(r)) }));

if (bom.components.length === 0) {
  console.error('No runtime npm components found; refusing to write an empty SBOM.');
  process.exit(1);
}
writeFileSync(out, JSON.stringify(bom, null, 2) + '\n', 'utf8');
