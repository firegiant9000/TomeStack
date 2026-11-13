// Adds the bundled .NET runtime packs to the NuGet CycloneDX SBOM (D101). The installer is published self-contained, so
// it ships Microsoft.NETCore.App and Microsoft.WindowsDesktop.App, which CycloneDX (restored packages only) never lists.
// Their versions come from the published app's deps.json, `libraries` entries of type `runtimepack`
// (key `runtimepack.<name>/<version>`). Fails when the deps.json is missing or lacks either pack; never duplicates a component.
// Usage: node scripts/sbom-add-runtime.mjs <sbom.json> <deps.json> --out <output.json>
// The output is UTF-8 without a BOM, written by node itself.
import { readFileSync, writeFileSync } from 'node:fs';

const NAMES = ['Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64'];

const args = process.argv.slice(2);
const outAt = args.indexOf('--out');
const positional = args.filter((a, i) => !a.startsWith('--') && i !== outAt + 1);
const out = outAt >= 0 ? args[outAt + 1] : undefined;
if (positional.length !== 2 || !out) {
  console.error('Usage: node scripts/sbom-add-runtime.mjs <sbom.json> <deps.json> --out <output.json>');
  process.exit(2);
}
const [sbomPath, depsPath] = positional;

let deps;
try {
  deps = JSON.parse(readFileSync(depsPath, 'utf8'));
} catch (e) {
  console.error(`Cannot read deps.json ${depsPath}: ${e.message}`);
  process.exit(1);
}
const bom = JSON.parse(readFileSync(sbomPath, 'utf8'));
bom.components ??= [];

const libraries = Object.entries(deps.libraries ?? {});
for (const name of NAMES) {
  const prefix = `runtimepack.${name}/`;
  const found = libraries.find(([key, lib]) => lib?.type === 'runtimepack' && key.startsWith(prefix));
  if (!found) {
    console.error(`No runtimepack entry for ${name} in ${depsPath}; refusing to write an SBOM without it.`);
    process.exit(1);
  }
  const version = found[0].slice(prefix.length);
  const purl = `pkg:nuget/${name}@${version}`;
  if (bom.components.some((c) => c['bom-ref'] === purl || c.purl === purl)) continue;
  bom.components.push({ type: 'library', 'bom-ref': purl, name, version, purl });
}

writeFileSync(out, JSON.stringify(bom, null, 2) + '\n', 'utf8');
