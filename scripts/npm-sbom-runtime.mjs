// Reads a CycloneDX SBOM from `npm sbom --package-lock-only` on stdin and writes the runtime-only SBOM to stdout.
// `npm sbom --omit dev` returns no components at all with npm 10.9 (checked 2026-11-12, even though `npm ls --omit dev`
// lists react), so the release keeps every component npm does not flag as development-only and drops the rest.
// Usage: npm sbom --sbom-format cyclonedx --package-lock-only | node scripts/npm-sbom-runtime.mjs > out.cdx.json
import { readFileSync } from 'node:fs';

const bom = JSON.parse(readFileSync(0, 'utf8'));
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
process.stdout.write(JSON.stringify(bom, null, 2) + '\n');
