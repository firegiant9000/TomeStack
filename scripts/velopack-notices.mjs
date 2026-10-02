#!/usr/bin/env node
// Generates third-party notices for the Rust crates statically linked into Velopack's
// Setup.exe / Update.exe, from velopack/velopack's Cargo.lock at a given tag.
// Usage: node scripts/velopack-notices.mjs [--tag 1.2.161] [--out docs/licensing/velopack-third-party-notices.md]
import { gunzipSync } from 'node:zlib';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

function parseArgs(argv) {
  const o = { tag: '1.2.161', out: 'docs/licensing/velopack-third-party-notices.md' };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--tag') o.tag = argv[++i];
    else if (argv[i] === '--out') o.out = argv[++i];
    else throw new Error(`Unknown argument: ${argv[i]}`);
  }
  return o;
}

async function download(url, binary = false) {
  let lastErr;
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      const res = await fetch(url);
      if (!res.ok) throw new Error(`HTTP ${res.status} for ${url}`);
      return binary ? Buffer.from(await res.arrayBuffer()) : await res.text();
    } catch (e) {
      lastErr = e;
      await new Promise((r) => setTimeout(r, 500 * (attempt + 1)));
    }
  }
  throw lastErr;
}

async function pool(items, limit, fn) {
  const results = new Array(items.length);
  let next = 0;
  const workers = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (true) {
      const i = next++;
      if (i >= items.length) return;
      results[i] = await fn(items[i], i);
    }
  });
  await Promise.all(workers);
  return results;
}

// ---- Cargo.lock ----
function parseLock(text) {
  const pkgs = [];
  let cur = null;
  let inDeps = false;
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (line === '[[package]]') {
      cur = { name: '', version: '', source: null, dependencies: [] };
      pkgs.push(cur);
      inDeps = false;
      continue;
    }
    if (!cur) continue;
    if (inDeps) {
      if (line.startsWith(']')) inDeps = false;
      else {
        const m = line.match(/^"([^"]*)"/);
        if (m) cur.dependencies.push(m[1]);
      }
      continue;
    }
    let m;
    if ((m = line.match(/^name = "(.*)"$/))) cur.name = m[1];
    else if ((m = line.match(/^version = "(.*)"$/))) cur.version = m[1];
    else if ((m = line.match(/^source = "(.*)"$/))) cur.source = m[1];
    else if (line.startsWith('dependencies = [')) {
      if (line.endsWith(']')) {
        for (const d of line.matchAll(/"([^"]*)"/g)) cur.dependencies.push(d[1]);
      } else inDeps = true;
    }
  }
  return pkgs;
}

function resolveDep(dep, byName) {
  const m = dep.match(/^(\S+)(?: (\S+))?(?: \((.*)\))?$/);
  const [, name, version, source] = m;
  let cands = byName.get(name) ?? [];
  if (version) cands = cands.filter((p) => p.version === version);
  if (source) cands = cands.filter((p) => p.source === source);
  if (cands.length !== 1) throw new Error(`Cannot resolve dependency "${dep}" (${cands.length} candidates)`);
  return cands[0];
}

// ---- tar ----
function cstr(buf) {
  const z = buf.indexOf(0);
  return buf.toString('utf8', 0, z === -1 ? buf.length : z);
}

function parseTar(buf) {
  const entries = [];
  let off = 0;
  let longName = null;
  let paxPath = null;
  while (off + 512 <= buf.length) {
    const h = buf.subarray(off, off + 512);
    if (h.every((b) => b === 0)) break;
    let name = cstr(h.subarray(0, 100));
    const size = parseInt(cstr(h.subarray(124, 136)).trim() || '0', 8);
    const type = String.fromCharCode(h[156] || 0x30);
    if (cstr(h.subarray(257, 263)).startsWith('ustar')) {
      const prefix = cstr(h.subarray(345, 500));
      if (prefix) name = `${prefix}/${name}`;
    }
    const data = buf.subarray(off + 512, off + 512 + size);
    off += 512 + Math.ceil(size / 512) * 512;
    if (type === 'L') {
      longName = cstr(data);
    } else if (type === 'x') {
      const s = data.toString('utf8');
      let p = 0;
      const b = Buffer.from(s, 'utf8');
      while (p < b.length) {
        const sp = b.indexOf(0x20, p);
        if (sp === -1) break;
        const len = parseInt(b.toString('utf8', p, sp), 10);
        if (!len) break;
        const rec = b.toString('utf8', sp + 1, p + len - 1);
        const eq = rec.indexOf('=');
        if (eq !== -1 && rec.slice(0, eq) === 'path') paxPath = rec.slice(eq + 1);
        p += len;
      }
    } else if (type === 'g') {
      // global pax header: ignore
    } else {
      if (longName !== null) name = longName;
      if (paxPath !== null) name = paxPath;
      longName = null;
      paxPath = null;
      if (type === '0' || type === '\0') entries.push({ name, data });
    }
  }
  return entries;
}

function parseManifest(text) {
  let section = '';
  let license = null;
  let licenseFile = null;
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    const sec = line.match(/^\[(.+)\]$/);
    if (sec) {
      section = sec[1];
      continue;
    }
    if (section !== 'package') continue;
    let m;
    if ((m = line.match(/^license\s*=\s*"(.*)"/))) license = m[1];
    else if ((m = line.match(/^license-file\s*=\s*"(.*)"/))) licenseFile = m[1];
  }
  return { license, licenseFile };
}

const LICENSE_RE = /^(LICEN[CS]E|COPYING|NOTICE|COPYRIGHT|UNLICENSE)/i;

async function processCrate(pkg) {
  const url = `https://static.crates.io/crates/${pkg.name}/${pkg.name}-${pkg.version}.crate`;
  const tgz = await download(url, true);
  const entries = parseTar(gunzipSync(tgz));
  const root = `${pkg.name}-${pkg.version}/`;
  const manifest = entries.find((e) => e.name === `${root}Cargo.toml`);
  const info = manifest ? parseManifest(manifest.data.toString('utf8')) : { license: null, licenseFile: null };
  const files = [];
  for (const e of entries) {
    if (!e.name.startsWith(root)) continue;
    const rel = e.name.slice(root.length);
    if (rel.includes('/') || !LICENSE_RE.test(rel)) continue;
    const text = e.data
      .toString('utf8')
      .replace(/^﻿/, '')
      .replace(/\r\n?/g, '\n')
      .split('\n')
      .map((l) => l.replace(/[ \t]+$/, ''))
      .join('\n')
      .trim();
    if (text) files.push({ file: rel, text });
  }
  files.sort((a, b) => (a.file < b.file ? -1 : a.file > b.file ? 1 : 0));
  return { ...pkg, license: info.license, licenseFile: info.licenseFile, files };
}

const cmp = (a, b) => (a < b ? -1 : a > b ? 1 : 0);

function fence(text) {
  let f = '```';
  while (text.includes(f)) f += '`';
  return f;
}

async function main() {
  const { tag, out } = parseArgs(process.argv.slice(2));
  const outPath = path.resolve(repoRoot, out);
  const base = `https://raw.githubusercontent.com/velopack/velopack/${tag}`;
  const [lockText, binsToml] = await Promise.all([download(`${base}/Cargo.lock`), download(`${base}/src/bins/Cargo.toml`)]);

  const nameM = binsToml.match(/\[package\][^[]*?^\s*name\s*=\s*"([^"]+)"/ms);
  if (!nameM) throw new Error('Cannot find [package] name in src/bins/Cargo.toml');
  const binsName = nameM[1];

  const pkgs = parseLock(lockText);
  const byName = new Map();
  for (const p of pkgs) {
    if (!byName.has(p.name)) byName.set(p.name, []);
    byName.get(p.name).push(p);
  }
  const roots = (byName.get(binsName) ?? []).filter((p) => !p.source);
  if (roots.length === 0) throw new Error(`Bins package ${binsName} not found in Cargo.lock`);

  const seen = new Set(roots);
  const stack = [...roots];
  while (stack.length) {
    const p = stack.pop();
    for (const d of p.dependencies) {
      const r = resolveDep(d, byName);
      if (!seen.has(r)) {
        seen.add(r);
        stack.push(r);
      }
    }
  }
  const crates = [...seen]
    .filter((p) => p.source && p.source.startsWith('registry+'))
    .sort((a, b) => cmp(a.name, b.name) || cmp(a.version, b.version));
  const nonRegistry = [...seen].filter((p) => p.source && !p.source.startsWith('registry+'));
  if (nonRegistry.length) {
    console.warn(`WARNING: non-registry sources skipped: ${nonRegistry.map((p) => `${p.name} ${p.version} (${p.source})`).join(', ')}`);
  }
  console.log(`Bins crate ${binsName}; ${crates.length} registry crates in closure`);

  const failures = [];
  const results = await pool(crates, 8, async (pkg) => {
    try {
      return await processCrate(pkg);
    } catch (e) {
      failures.push(`${pkg.name} ${pkg.version}: ${e.message}`);
      return null;
    }
  });
  if (failures.length) {
    console.error(`FAILURES:\n${failures.join('\n')}`);
    process.exit(1);
  }

  // Velopack's own license
  const velLicense = await download(`https://raw.githubusercontent.com/velopack/velopack/${tag}/LICENSE`);
  const copyrightLine = velLicense.split(/\r?\n/).find((l) => /copyright/i.test(l))?.trim() ?? '(copyright line not found)';
  const hasWebview2 = results.some((r) => r.name === 'webview2-com-sys');

  // Summary
  const summary = new Map();
  for (const r of results) {
    const k = r.license ?? (r.licenseFile ? `(license-file: ${r.licenseFile})` : '(none declared)');
    summary.set(k, (summary.get(k) ?? 0) + 1);
  }

  // Dedupe license texts
  const groups = new Map();
  const noFile = [];
  for (const r of results) {
    if (r.files.length === 0) {
      noFile.push(r);
      continue;
    }
    for (const f of r.files) {
      const key = f.text.replace(/\s+/g, ' ').trim();
      if (!groups.has(key)) groups.set(key, { text: f.text, crates: new Set() });
      groups.get(key).crates.add(`${r.name} ${r.version}`);
    }
  }
  const sortedGroups = [...groups.values()]
    .map((g) => ({ text: g.text, crates: [...g.crates].sort(cmp) }))
    .sort((a, b) => b.crates.length - a.crates.length || cmp(a.crates[0], b.crates[0]));

  const date = new Date().toISOString().slice(0, 10);
  const L = [];
  L.push('# Third-party notices: Velopack Setup.exe and Update.exe', '');
  L.push(
    `Generated by \`scripts/velopack-notices.mjs\` from velopack/velopack tag \`${tag}\` \`Cargo.lock\` (bins crate \`${binsName}\`) on ${date} (UTC). ` +
      `It lists ${results.length} crates.io crates in the transitive dependency closure of the bins crate. ` +
      'Cargo.lock carries no target information, so all targets are included. This over-includes: the Windows binaries contain a subset of these crates. ' +
      'License texts are taken from each published `.crate` archive.',
    '',
  );
  L.push(`Velopack itself is MIT licensed ([LICENSE](https://github.com/velopack/velopack/blob/${tag}/LICENSE)): ${copyrightLine}`, '');
  if (hasWebview2) {
    L.push(
      "The Windows binaries also statically link Microsoft's WebView2 loader (`webview2-com-sys` / `WebView2LoaderStatic.lib`) under the Microsoft WebView2 SDK license (BSD-3-Clause per the Microsoft.Web.WebView2 package).",
      '',
    );
  }
  L.push('## License summary', '', '| License expression | Crates |', '| --- | ---: |');
  for (const [k, v] of [...summary.entries()].sort((a, b) => b[1] - a[1] || cmp(a[0], b[0]))) L.push(`| ${k.replace(/\|/g, '\\|')} | ${v} |`);
  L.push('', '## Crates', '', '| Crate | Version | License | crates.io |', '| --- | --- | --- | --- |');
  for (const r of results) {
    const lic = (r.license ?? (r.licenseFile ? `see ${r.licenseFile}` : 'none declared')).replace(/\|/g, '\\|');
    L.push(`| ${r.name} | ${r.version} | ${lic} | https://crates.io/crates/${r.name}/${r.version} |`);
  }
  L.push('', '## License texts', '');
  L.push(`Identical texts (compared with whitespace normalized) are printed once, ${sortedGroups.length} distinct texts in total.`, '');
  sortedGroups.forEach((g, i) => {
    L.push(`### Text ${i + 1} (${g.crates.length} crate${g.crates.length === 1 ? '' : 's'})`, '');
    L.push(g.crates.join(', '), '');
    const f = fence(g.text);
    L.push(`${f}text`, g.text, f, '');
  });
  L.push('### Crates without a license file', '');
  if (noFile.length === 0) L.push('None.', '');
  else {
    L.push('These crates ship no license file in their published archive. The standard text of the declared SPDX license applies.', '');
    for (const r of noFile) L.push(`- ${r.name} ${r.version}: ${r.license ?? 'none declared'}`);
    L.push('');
  }

  let md = L.join('\n').split('\n').map((l) => l.replace(/[ \t]+$/, '')).join('\n');
  md = md.replace(/\n+$/, '\n');
  mkdirSync(path.dirname(outPath), { recursive: true });
  writeFileSync(outPath, md, 'utf8');
  console.log(`Wrote ${path.relative(repoRoot, outPath).replace(/\\/g, '/')} (${Buffer.byteLength(md)} bytes)`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
