# Restore drill procedure (roadmap T6)

ROADMAP T6 · status: **tooling and procedure ready (2026-10-02); the drill itself is owed (owner), on the published 0.4.0.**

The drill proves that a real library survives a move to another machine. It also closes the M2.1 owner check ("restore a full backup on a second machine") and the cross-machine part of the M6 exit gate.

**The record is public**, so it holds only counts, byte totals, SHA-256 digests, times and error codes. It holds no titles, names, text, file names or paths. The tool below prints nothing else.

## The tool: `--drill-report` and `--drill-compare`

A mode of the dev-only DevHost (`src/DevHost`). The shipped app has no such mode and still opens no socket (ADR-006); the counting code is `AppService.Diagnostics.RestoreDrill` (`RestoreDrillTests`).

```powershell
# Once, on the development machine: a self-contained copy that runs on a machine without .NET.
# Copy the folder to the clean machine (USB stick, VM shared folder).
dotnet publish src/DevHost -c Release -r win-x64 --self-contained -o <drill tool folder>

# Count a data folder (close TomeStack first), print a table, write JSON:
TomeStack.DevHost.exe --drill-report "$env:LOCALAPPDATA\TomeStack" --out before.json

# Compare two reports; exit code 0 when every difference is an expected one:
TomeStack.DevHost.exe --drill-compare before.json after.json
```

**What it reads.**
- It refuses a folder TomeStack has open: it only tries to read `tomestack.lock`, which a running TomeStack holds exclusively.
- It copies `tomestack.db` (and a `-wal`/`-shm`, if a crash left them) to a temporary folder, and opens only the copy, with `Mode=ReadOnly`.
- In the data folder itself it only lists files.
- `RestoreDrillTests.Counting_a_closed_data_folder_changes_nothing_in_it` checks that every file's size, write time and SHA-256 are unchanged.

**What it reports.**

| Group | Counts |
|---|---|
| Content | revisions published and draft; bundled SRD and your own |
| Sources | total; `local`, `received`, unknown origin (stored before database v8); import-derived; bundled SRD |
| Library | characters (and how many archived), campaigns, gap notes |
| PDFs | attachment records (managed and linked); managed files present, their total bytes, and any missing |
| Other | installed extensions (and how many enabled), extension files, snapshots, import jobs |

It also gives a **digest per table**: SHA-256 over the sorted lines `id:hash`.
- Revisions use their stored SHA-256. Sources, characters, campaigns, gap notes and snapshots use the SHA-256 of their stored JSON.
- Attachments use hash, mode and size; a linked path is machine-local and not included.
- Extensions use the file's SHA-256; grants and on/off never travel.
- **`sheets`** is the digest of every character's calculated sheet, computed from the copy with this build's rules. Equal digests mean every character calculates the same on both machines. It is computed only for a database at the tool's own version, so build the tool from the release's commit.

**What `--drill-compare` treats as expected** (the drill's rules, `RestoreDrill.Compare`):
- **Snapshots** are not in a full backup (LIVING_SPECS D14).
- **Extensions** come back turned off with no grants (ADR-011): `ExtensionsEnabled` differs.
- **Import jobs** and extracted text are machine-local (ADR-009).
- **Bundled SRD revisions and sources** are seeded by each build: they differ only if the two machines run different builds.
- **The database version** is the restoring build's.

Anything else is listed as `DIFFERS` and counts as a failure until explained. Not handled by `--drill-compare`, because these are judged by hand:
- **`origin`** follows package-format rule 11. A full restore keeps the origin this machine recorded, so a library backup should show no origin change. A package import records every new source as `received`.
- **Pack sources** arrive as `origin: received`, never `local`.

## Source machine (the real library)

1. Install or update to the published **0.4.0** and open your library once. That migrates it to database v9 and leaves `tomestack.db.v<old>.bak`.
2. In TomeStack, **Backups → Back up everything…**. Save the backup outside the data folder. Note its size, and how long the backup took.
3. Export the three packages, each to a file:
   - a **character package** of one character, using **Personal backup**, so its homebrew and gap notes come too;
   - a **source pack** of one homebrew source you marked as your own work;
   - a **campaign pack** of one campaign.

   Note what each export preview says it includes (counts only).
4. **Close TomeStack**, then count:
   `TomeStack.DevHost.exe --drill-report "$env:LOCALAPPDATA\TomeStack" --out source.json`
5. Copy the backup, the three packages, `source.json` and the drill tool folder to the clean machine. **Not** the data folder.

## Clean machine or VM

The machine has never had TomeStack, uses a standard user, and has the network off after copying.

1. Install the published 0.4.0 (`TomeStack.App-win-Setup.exe`; the SmartScreen prompt is expected, the build is unsigned). Start TomeStack once and close it.
2. **Restore, timed:**
   1. Start TomeStack and choose **Restore full backup** with the backup file. Start the stopwatch when you confirm the restore.
   2. Stop it when the character list shows the restored characters. Record the time, and every warning or error **code** the preview and the restore show.
3. Open **every** character, and look at each sheet: it must open with no new "missing content" diagnostics. Close TomeStack.
4. **Count and compare:**
   `TomeStack.DevHost.exe --drill-report "$env:LOCALAPPDATA\TomeStack" --out restored.json`
   `TomeStack.DevHost.exe --drill-compare source.json restored.json`
   Expected:
   - `MATCH`, with only the expected differences above;
   - the `sheets` digest equal (every sheet calculates the same);
   - `ManagedFiles` and `ManagedFileBytes` equal (every managed PDF came back);
   - `ManagedFilesMissing` 0.
5. **The packages, in a second clean folder:**
   1. Start TomeStack with an empty folder: `TomeStack.exe --data-dir <empty folder>`.
   2. Import the character package, then the source pack, then the campaign pack, previewing each. Record every warning or error code, then close TomeStack.
   3. Count: `--drill-report <empty folder> --out packages.json`.

   Expected:
   - the characters, sources, own revisions and campaign match what the three export previews said they include;
   - the pack sources are `received` (`SourcesReceived`);
   - no `ManagedFiles`, because packages never carry PDFs;
   - extensions 0.

   This one is not compared with `--drill-compare`, because the folder holds a subset by design.

## Fix, then repeat once

- For every `DIFFERS` line, every unexpected warning or error code, and every sheet that changed: record it, generically, in the drill record.
- A **bug** gets its own PR with a regression test, before the repeat.
- Then repeat the clean-machine part once, from a fresh VM snapshot or a new Windows user. Use a new backup if the fix changes what a backup writes.
- Record the repeat as a second run.

## The record

Write `docs/features/restore-drill-YYYY-MM-DD.md` from the template below. Paste the two reports' count tables and the compare output: they hold nothing private. Then close the loop:
- ROADMAP T6 acceptance;
- the M2.1 owner check in ROADMAP;
- the M6 exit gate's cross-machine part, with a decision on whether the M6 gate is now met;
- the ROADMAP risk row "Corruptions or lost attachments", pointing at the record.

```markdown
# Restore drill, YYYY-MM-DD (roadmap T6)

| | Source machine | Clean machine (run 1) | Clean machine (run 2) |
|---|---|---|---|
| Machine | generic: laptop/desktop/VM, Windows version, cores, RAM | | |
| TomeStack | 0.4.0 (installer from the GitHub release) | | |
| Database version | | | |
| Backup size | bytes | | |
| Restore time | | seconds | seconds |
| Compare result | | MATCH / N unexpected | |

## Counts
(paste the --drill-report tables: source, restored, packages)

## Differences
(paste --drill-compare; for each unexpected one: what it was, the issue or PR, what was done)

## Warnings and errors
(codes only, with where they appeared)

## Packages
(character package, source pack, campaign pack: what the previews said, what the counts show)

## Conclusion
(M2.1 owner check closed or not; M6 cross-machine part recorded; M6 gate met or not, and why)
```
