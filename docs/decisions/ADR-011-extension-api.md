# ADR-011: Extension API (M6, SPEC P-05)

Status: **proposed** (2026-09-28). Nothing is built. **The owner chooses the execution model** (options A to D below). Nothing that runs third-party code is built without that decision.
Date: 2026-09-28

## Context

- **SPEC P-05:** "A future extension API exposes versioned, permissioned data/import/export hooks; it does not grant raw code execution to content packs."
- **ROADMAP M6** lists a "plugin SDK sandbox". Its exit gate needs "an external sample extension".
- **Constraints that hold whatever is chosen:**
  - **SPEC Q-02:** imported archives and formulas are untrusted, with bounded parsing, isolated failures and never JavaScript from homebrew.
  - **ADR-001:** no network in normal use.
  - **ADR-006:** no listening socket.
  - **ADR-004:** nothing becomes active without review.
  - **ADR-007:** non-redistributable content never leaves the machine in a share.
  - **Architecture:** `RulesCore` stays free of persistence, UI and Windows.
- **What exists (checked against the code 2026-09-28):**
  - There is no extension, plugin, hook or adapter code. The command protocol (`CommandDispatcher`: `{id, command, payload}`) has no version field. The documented JSON schemas (`docs/schemas`) are the only versioned data shapes.
  - The one isolation pattern is the PDF worker (ADR-009): a child process over stdin and stdout with heap, time and size caps and a watchdog. It has no Windows job object, no AppContainer and no network block, because it runs TomeStack's own code.

## Decision (the parts that do not depend on the execution model)

### An extension is a separate artifact, never content

- An extension is a file `*.tomestack-ext.zip`. It is read under the package limits and path allowlist (package-format.md "Import rules" 1–3: sizes checked while reading, no `..` or absolute paths, no extraction to disk, every entry listed and hashed).
- A content pack (M6 slice 1), a campaign pack or a character package can never contain an extension, and importing one never installs one (`package.extension-not-allowed`). **Content packs stay data only.**

### Manifest `extension.json` (schema `docs/schemas/extension-manifest.v1.schema.json`)

| Field | Meaning |
| --- | --- |
| `format` / `formatVersion` | `tomestack.extension` / `1` |
| `id` | A UUID; the display name is never identity (ADR-002) |
| `name`, `version` (semver), `author`, `license`, `homepage?` | Shown at install. The install is refused unless `license` is present |
| `extensionApi` | The API version it was written for (integer). `app.info.extensionApi` lists the versions this build supports. An unknown or newer version is refused (`extension.api-unsupported`), and it is never partly loaded |
| `runtime` | How its hooks run: `declarative` in API v1 (option A); other values only if the owner picks B or C |
| `permissions[]` | What it may read or write (below). Unknown permissions refuse the install (`extension.permission-unknown`) |
| `hooks[]` | `{ kind: "import" \| "export", id, label, accepts?, produces? }` |

### Versioned data shapes

Extensions and export adapters (ADR-012) read the same documented shapes, never internal records:

- **Sheet export model v1** (new, `docs/schemas/sheet-export.v1.schema.json`): the computed sheet. It has fields with values and automation status (no traces), abilities, skills, saves, hit points, hit dice, class levels, attacks, spellcasting, slots, resources, toggles, conditions and features by name and text.
  - It never carries local paths, attachment ids, gap notes or override reasons. Its schema is an **allowlist**: there is no field a path could go in. That is the primary privacy control. The output scan below is a second line.
  - It has `notices[]` for every source whose content it includes: title, publisher, license, attribution and modification notice (ADR-007 item 1, CC-BY §3). Every consumer must carry them.
  - **It is filtered by the run's `purpose`, whatever the extension's permissions (review fix).** `share` is the default. In a share, a `redistributable: false` source contributes only aggregate totals: ability scores, Armor Class, hit point maximum, save and skill totals, slot counts. Its features, resources and uses, attacks, spells and `scales` (ADR-010) are dropped and listed in the preview. `personal` is allowed only for sources the user created locally and that are not import-derived (see M6 slice 1's durable provenance flag). Other `redistributable: false` sources are always filtered, because a VTT or extension output is meant to leave the machine. This rule needs an ADR-007 amendment, made in the slice that builds the model: shares have always omitted whole revisions, and the receiver recalculates without them.
- **Content revisions** in their documented schema (`content-revision.v<N>`). An extension for API v1 sees revisions up to v9. A newer build maps a newer revision down to an older API only when a "fits schema ≤ N" check passes. That check covers formula identifiers too, not only effect types and fields (the gap found in ADR-010's review). Otherwise the build leaves the revision out and names it in the preview.
- A new API version is a new ADR revision and a new entry in the compatibility table (`docs/authoring/extensions.md`, M6 slice 5).

### Permissions: declared, granted by the user, revocable

| Permission | Grants | Never grants |
| --- | --- | --- |
| `read.sheet` | The sheet export model of a character **the user picks at run time** | Other characters, stored character JSON, gap notes |
| `read.content` | Published revisions of sources **the user picks at run time**, filtered by the run's purpose as the sheet model is. It only widens which sources can be picked | Drafts, extracted PDF text, PDFs, attachments |
| `import.file` | The bytes of one file **the user picks in TomeStack's native Open dialog**, at most 5 MB, parsed by TomeStack (JSON or CSV, under the transform bounds). The extension never sees a path | Any other file access |
| `write.drafts` | New **draft** revisions and new homebrew sources, from an import hook. They are validated like `content.saveDraft` and shown in a preview first. Publishing is the user's step in the studio (ADR-004). A source created by an extension is always `redistributable: false`, with license "Personal homebrew", and carries the durable import-derived flag. If the same run read any source (`read.content`), its drafts inherit the most restrictive redistribution among the sources it read. So an extension cannot copy a bought book into a shareable source (review fix) | Published revisions, edits of existing revisions, characters, marking anything shareable |
| `export.file` | One output file, written through the native Save dialog (as `package.saveAs`). The extension never sees a path | Any other file access |

- **Never, under any runtime:** network, file system, clipboard, settings, other extensions, backups, gap notes, PDFs, extracted text, or running without the user.
- **Grants are bound to the file's SHA-256, not to the self-declared id (review fix).** They are shown at install. **Every** update, even one with the same permissions, asks again and shows the manifest and permission differences. An update whose `author` differs from the installed one is refused (`extension.author-changed`). Before option B or C is ever built, a signing key replaces `author` as that check. "Remove extension" revokes everything. Its drafts stay, because they are the user's data now.
- **Every run is user-initiated.** There is no auto-run, no background hook and no hook on start-up. A run shows a **preview** before anything is written. For an import, that means the drafts, validation results and warnings. For an export, it means the file name, what is included and what is left out.
- **Outputs are untrusted input:**
  - Import output goes through `ContentValidator` and the package checks.
  - Export output is size-capped (5 MB) and must be valid UTF-8 or JSON, as the hook declares. It is scanned before writing (`extension.output-refused`). The scan unescapes JSON, accepts both slash directions and ignores case. It looks for the data-folder path, the user-profile path, the Windows user name, and every linked-PDF path (they can lie outside the data folder).
  - Tests run the exporter with a data folder and a linked PDF under a **sentinel** user name and assert the sentinel never appears. A golden fixture alone can't prove this, because it never contains a real name.
  - Error messages name the extension and a code, never quote its output (SPEC "errors and logs never quote user or third-party text").

### Storage and backup

- A new database table `extensions` (id, version, sha256, manifest, grants, enabled), added by a forward-only migration with the usual pre-upgrade backup. The files are kept at `<data dir>/extensions/<sha256>.zip`, read-only.
- A full library backup includes installed extensions **without their grants**. That takes a new library-backup format version (ROADMAP "Package format numbers") and an allowlisted `extensions/<sha256>.zip` entry, whose size is included in the backup's limits.
- **A restore installs nothing by itself (review fix).**
  - Each extension zip goes through the full install checks: package limits for the inner zip as well, the manifest, the API version, the permissions and the hash.
  - It is restored **disabled and ungranted**, and the restore preview lists it.
  - So a backup handed over by someone else cannot install their extensions.
- Character, content and campaign packages never include extensions.

## Options: how an extension's logic runs (owner decision)

| | A. Declarative transforms only | B. Native code in an AppContainer child process | C. WebAssembly, capability-free | D. In-process .NET plugins |
| --- | --- | --- | --- | --- |
| What runs | **No third-party code.** An extension carries mapping documents that a TomeStack engine interprets. They select from the input (JSON Pointer), map values through tables, fill string templates, and iterate arrays under bounds. Inputs are JSON or CSV, parsed by TomeStack. Outputs are JSON or text | A third-party executable, started as a child in an AppContainer (or LPAC) with **no capabilities**: no `internetClient`, so no network, and no user files. It runs in a Job Object (kill-on-close, memory and CPU-time caps, one process, UI restrictions: no clipboard, desktop or global atoms) and talks JSON lines over stdin and stdout, with the ADR-009 message limits | A third-party `.wasm` module in a WebAssembly runtime such as Wasmtime, with no WASI capabilities except input and output buffers. It has fuel (instruction) and memory limits and can run in a child process for crash isolation, as in ADR-009 | A .NET assembly loaded into the app (`AssemblyLoadContext`) |
| Security cost | **Lowest.** The same class of risk as the formula grammar: a bounded interpreter that TomeStack owns, with fuzz tests. No syscalls, no I/O | **Highest.** Third-party native code executes on the user's machine. Containment relies on Windows AppContainer and job-object correctness, and a kernel or driver exploit escapes it. Also: supply chain (a malicious update), antivirus reputation, and a code-signing and trust question | **Moderate.** Third-party code runs, but inside a capability-less VM with no syscalls, so escaping needs a runtime bug. It adds a native dependency (Wasmtime is Apache-2.0 WITH LLVM-exception, which is compatible, and goes into `ATTRIBUTION.md`; its size is to be measured) | **Unacceptable.** Full trust: file system, network and the user's data. It breaks ADR-001 and Q-02 |
| Build cost | Medium: design and bound the mapping language, write fuzz tests, document it | High: Windows APIs (`CreateAppContainerProfile`, `SECURITY_CAPABILITIES`, `STARTUPINFOEX`, job objects) in a Windows-only project (the shell or a new host project; `AppService` stays `net10.0` without Windows references), a trust UX, and tests that the sandbox really denies network and files | Medium to high: host the runtime, marshal the data shapes, set fuel and memory limits, write an author guide for a WASM toolchain | Low, but rejected |
| Expressiveness | Limited. Good for "import this JSON or CSV homebrew format" and "export this sheet as text or JSON". Complex VTT mappings are first-party adapters instead (ADR-012) | Anything | Anything computable, with no I/O | Anything |
| Fits "content packs never execute code" | Yes; nothing executes anywhere | Yes for packs; extensions do execute | Yes for packs; extensions do execute | No meaningful sandbox |

**Rejected in every case:** JavaScript through Jint or the WebView (Q-02 "never evaluate JavaScript from homebrew", and script in the WebView would share the UI origin and the bridge); Lua or Python embedding (the same class of risk as C without its isolation); any loopback or named-pipe server that extensions connect to (ADR-006).

**Recommendation: A for M6.** It meets the gate (an external sample extension) with no third-party code: for example "Import a CSV spell list as draft spells" and "Export a sheet as Markdown". The manifest's `runtime` field keeps B or C possible later, behind the same permissions and preview, through a revision of this ADR. If a hook later needs real logic that A cannot express, C is the next step, before B.

## Consequences

- Extensions can never do more than the five permissions allow, whatever the runtime.
- With A, TomeStack owns and maintains a small transform language. It must stay bounded the way ADR-003 bounds formulas (review fix: per-construct limits alone are not enough, because nested loops multiply). Its bounds:
  - a **total step budget** (fuel) and a wall-clock timeout for each run;
  - a cap on the product of nested iterations;
  - input limits: at most 5 MB, JSON depth ≤ 32, CSV rows ≤ 50,000 and fields ≤ 10,000 characters, with strict quoting;
  - mapping tables of at most 10,000 entries;
  - template expansion counted in the step budget, so a template that references itself runs out of steps;
  - **no regular expressions**; matching is exact or by prefix;
  - output ≤ 5 MB.
  
  Each bound has a refusal test, and the whole language is fuzzed.
- Adapters with a real mapping (Foundry, ADR-012) are first-party code, written against the same sheet export model, so extensions and adapters share one versioned data contract.
- A new database table, and one more thing in the library backup.

## Evidence (planned; none yet)

- `ExtensionManifestTests`: an unknown or newer API is refused; unknown permissions are refused; package limits and paths are enforced; content packs cannot carry an extension.
- `DeclarativeTransformTests` (if A): hostile cases and fuzzing; each bound is refused with a diagnostic.
- `ExtensionImportTests`: output becomes drafts only, is validated, and nothing is written before the preview.
- `ExtensionExportTests`: the share rules apply; no paths, gap notes or attachment ids appear; a path in the output is refused.
- e2e: install the sample from `examples/extensions/`, grant it, run both hooks, and remove it.
- The smoke's existing checks stay: `blockedRequests: []` and no socket.

Supersedes: none. Extends ADR-004 (import output is drafts), ADR-006 (no socket) and ADR-007 (share rules apply to hook output).
