#!/usr/bin/env node
// F2 (10x addendum): the Foundry dnd5e pin-drift check for the weekly job.
// Reads the pinned core and dnd5e versions from src/AppService/Exports/FoundryDnd5e.cs and prints them as JSON.
// With --check, it also fetches the latest dnd5e release, compares the data-model files the adapter was verified
// against (docs/features/export-adapters.md) and the system.json compatibility block between the pinned tag and the
// latest one, and writes an issue body to --body <file> when anything differs.
// Usage: node scripts/foundry-pin.mjs [--check --body <file>]
// Writes `drift=true|false` to $GITHUB_OUTPUT and a summary to $GITHUB_STEP_SUMMARY when those are set.
// Nothing fetched is executed or interpolated into a shell: the latest tag must match TAG before it is used anywhere.
import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = 'foundryvtt/dnd5e';
const TAG = /^release-\d+\.\d+\.\d+$/;

// The files named in export-adapters.md "How the pin was verified" (system.json is compared by its compatibility block).
const FILES = [
  'module/data/actor/character.mjs',
  'module/data/actor/templates/common.mjs',
  'module/data/actor/templates/creature.mjs',
  'module/data/actor/templates/attributes.mjs',
  'module/data/item/class.mjs',
  'module/data/item/subclass.mjs',
  'module/data/item/feat.mjs',
  'module/data/item/spell.mjs',
  'module/data/item/weapon.mjs',
  'module/data/item/templates/item-description.mjs',
  'module/data/item/templates/activities.mjs',
  'module/data/shared/damage-field.mjs',
  'module/data/shared/uses-field.mjs',
];

function parseArgs(argv) {
  const o = { check: false, body: null };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--check') o.check = true;
    else if (argv[i] === '--body') o.body = argv[++i];
    else throw new Error(`Unknown argument: ${argv[i]}`);
  }
  if (o.check && !o.body) throw new Error('--check needs --body <file>');
  return o;
}

function readPin(source) {
  const constant = (name) => {
    const m = source.match(new RegExp(`public const string ${name} = "([^"]+)";`));
    if (!m) throw new Error(`FoundryDnd5e.cs has no ${name} constant`);
    return m[1];
  };
  const system = constant('SystemVersion');
  const core = constant('CoreVersion');
  if (!/^\d+\.\d+\.\d+$/.test(system) || !/^\d+\.\d+$/.test(core)) throw new Error('Unexpected pin format in FoundryDnd5e.cs');
  return { core, system, tag: `release-${system}`, files: FILES };
}

async function get(url, json) {
  const headers = { 'User-Agent': 'tomestack-weekly', Accept: json ? 'application/vnd.github+json' : '*/*' };
  if (json && process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
  let lastErr;
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      const res = await fetch(url, { headers });
      if (res.status === 404) return null;
      if (!res.ok) throw new Error(`HTTP ${res.status} for ${url}`);
      return json ? await res.json() : await res.text();
    } catch (e) {
      lastErr = e;
      await new Promise((r) => setTimeout(r, 1000 * (attempt + 1)));
    }
  }
  throw lastErr;
}

const raw = (tag, file) => get(`https://raw.githubusercontent.com/${REPO}/${tag}/${file}`, false);

function compatibility(systemJson, tag) {
  if (systemJson === null) throw new Error(`system.json not found at ${tag}`);
  const c = JSON.parse(systemJson).compatibility ?? {};
  const field = (v) => (typeof v === 'string' || typeof v === 'number') && /^[\d.]+$/.test(String(v)) ? String(v) : null;
  return { minimum: field(c.minimum), verified: field(c.verified), maximum: field(c.maximum) };
}

async function check(pin) {
  const release = await get(`https://api.github.com/repos/${REPO}/releases/latest`, true);
  const latest = release?.tag_name;
  if (typeof latest !== 'string' || !TAG.test(latest)) throw new Error('The latest dnd5e release tag has an unexpected format');

  const pinnedCompat = compatibility(await raw(pin.tag, 'system.json'), pin.tag);
  const latestCompat = compatibility(await raw(latest, 'system.json'), latest);
  const changed = [];
  if (latest !== pin.tag) {
    for (const file of FILES) {
      const [a, b] = await Promise.all([raw(pin.tag, file), raw(latest, file)]);
      if (a === null) throw new Error(`${file} not found at the pinned tag ${pin.tag}; fix the list in scripts/foundry-pin.mjs`);
      if (a !== b) changed.push(b === null ? `${file} (removed or moved)` : file);
    }
  }
  const compatChanged = ['minimum', 'verified', 'maximum'].filter((k) => pinnedCompat[k] !== latestCompat[k]);
  return { latest, pinnedCompat, latestCompat, changed, compatChanged, drift: changed.length > 0 || compatChanged.length > 0 };
}

function body(pin, r) {
  const compat = (c) => `minimum ${c.minimum ?? 'none'}, verified ${c.verified ?? 'none'}, maximum ${c.maximum ?? 'none'}`;
  return [
    'The weekly job found that the Foundry dnd5e release the export adapter is pinned to differs from the latest release.',
    '',
    `- Pinned: core ${pin.core} with dnd5e ${pin.system} (\`src/AppService/Exports/FoundryDnd5e.cs\`); compatibility ${compat(r.pinnedCompat)}`,
    `- Latest: \`${r.latest}\`; compatibility ${compat(r.latestCompat)}`,
    `- Compare: https://github.com/${REPO}/compare/${pin.tag}...${r.latest}`,
    '',
    r.changed.length ? '**Data-model files that changed:**' : '**No data-model file changed.**',
    ...r.changed.map((f) => `- \`${f}\``),
    '',
    r.compatChanged.length ? `**Compatibility fields that changed:** ${r.compatChanged.join(', ')}` : '**Compatibility unchanged.**',
    '',
    'To move the pin: re-verify the adapter against the new tag, update `FoundryDnd5e.cs`, `docs/features/export-adapters.md` (FoundryPinDocTests checks they agree) and the golden files, then close this issue. This issue is updated in place by `.github/workflows/weekly.yml`.',
    '',
  ].join('\n');
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const pin = readPin(readFileSync(path.join(repoRoot, 'src/AppService/Exports/FoundryDnd5e.cs'), 'utf8'));
  if (!args.check) {
    console.log(JSON.stringify(pin, null, 2));
    return;
  }
  const r = await check(pin);
  if (r.drift) writeFileSync(args.body, body(pin, r));
  const summary = `Foundry pin: dnd5e ${pin.system} (core ${pin.core}); latest ${r.latest}; data-model files changed ${r.changed.length} of ${FILES.length}; compatibility fields changed ${r.compatChanged.length}; drift ${r.drift}`;
  console.log(summary);
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `drift=${r.drift}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`);
}

main().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
