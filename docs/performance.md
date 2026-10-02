# Performance baseline (roadmap T4)

ROADMAP "Revision 2026-09-29", T4 · status: **measured 2026-10-01; first paint owed (owner, by hand).** This page measures; it optimises nothing. A surprising number becomes a follow-up item below, with its measurement.

- Code: [`benchmarks/TomeStack.Benchmarks/`](../benchmarks/TomeStack.Benchmarks/) (BenchmarkDotNet 0.15.8). It is in `TomeStack.slnx`, so it builds under the 0-warning rule. It is not a test project: `dotnet test` and CI never run it.
- Raw results (BenchmarkDotNet's GitHub-markdown and CSV exports, two runs): [`performance/raw/2026-10-01/`](performance/raw/2026-10-01/). The exports name no path, user or machine; the logs, which do, are not committed.

## How to run

From anywhere, in Release, with no debugger attached, on AC power, with nothing else heavy running:

```powershell
dotnet run -c Release --project benchmarks/TomeStack.Benchmarks -- --filter "*"
```

Use `--filter "*Calculation*"` and so on for one case, and `--job Dry` to check the set-up only. BenchmarkDotNet writes to `BenchmarkDotNet.Artifacts/` (gitignored). Copy the `-report-github.md` and `-report.csv` files to `docs/performance/raw/<date>/`, after checking that they hold no path, user or machine name.

## Method

- **BenchmarkDotNet defaults:**
  - each case runs out of process, after warm-up, until the confidence interval is tight;
  - the import case uses 2 warm-up and 20 measured iterations of one invocation each, because each needs a fresh data folder.
- **Diagnostics:** `[MemoryDiagnoser]` (GC counts and bytes allocated per operation), plus median, P95, min and max columns.
- **Two full runs,** one after the other on the same commit, with T3's test runs in between. Both are reported, with the spread as (run 2 − run 1) / run 1.

### Environment

| | |
|---|---|
| Machine | A laptop on AC power (battery full), vendor-default power plan |
| CPU | Intel Core Ultra 9 275HX, 24 cores (8 performance and 16 efficient; no SMT), base 2.7 GHz |
| Memory | 32 GB (31.4 GB visible) |
| OS | Windows 11 Home 25H2, build 26200.9457 |
| Runtime | .NET 10.0.9 (SDK 10.0.301), x64 RyuJIT, x86-64-v3; workstation GC |
| Commit | `a96424f` (the benchmark project) on `main` `bfd1bdf` |
| Tool | BenchmarkDotNet 0.15.8 |

## The cases

1. **Level-20 three-class character** (`CalculationBenchmarks`), once per rules family. Wizard 8 / Cleric 6 / Fighter 6 from the bundled SRD packs, with every spell on both casters' lists (309 spells in SRD 5.1, 327 in SRD 5.2.1) and 8 SRD items (6 weapons, heavy armor, a shield). The sheet has 54 fields. It is calculated with `CharacterCalculator.Calculate` over an in-memory catalog: rules core only, no database.
2. **Importing a large pack into a fresh data folder** (`ImportBenchmarks`).
   - The pack is an original source pack: 250 feats with 2 published revisions each, so 500 revisions, 501 entries and about 376 KB.
   - Ids, names and contents are fixed by index. Publish assigns the revision ids, so the bytes differ from run to run but nothing else does.
   - `Preview` is what the import dialog shows first. `Apply` is the import alone; opening the fresh folder (which seeds the SRD) is not measured.
3. **Every formula in the bundled SRD packs** (`FormulaBenchmarks`): 75 formulas (24 distinct) in the 1,320 SRD revisions, parsed (`ParseAll`) and evaluated from pre-parsed ASTs (`EvaluateAll`).
4. **Recalculation after one changed revision across N characters** (`ReviewUpdateBenchmarks`, N = 1, 10, 100).
   - Each character is case 1's character, alternating families, pinning a homebrew feat. A second revision of the feat is then published.
   - `FindUpdates` is `character.updates` for every character: finding the newer revision.
   - `ReviewAll` is `character.reviewUpdate` for every character: both sheets calculated and diffed.
   - This goes through the real SQLite store, as the app does.
5. **Cold start to first paint:** by hand; see "First paint" below.

## Results

Time is the mean; P95 and allocations are per operation. "Spread" compares the two means.

| Case | Run 1 mean | Run 1 P95 | Run 2 mean | Run 2 P95 | Spread | Allocated |
|---|---:|---:|---:|---:|---:|---:|
| 1. Calculate, SRD 5.1 | 839.7 µs | 934.6 µs | 917.5 µs | 980.1 µs | +9 % | 1.22 MB |
| 1. Calculate, SRD 5.2.1 | 743.9 µs | 751.6 µs | 909.3 µs | 928.8 µs | +22 % | 1.24 MB |
| 2. Import preview (500 revisions) | 38.53 ms | 42.89 ms | 33.07 ms | 34.36 ms | −14 % | 67 MB |
| 2. Import apply (500 revisions) | 63.66 ms | 75.16 ms | 65.73 ms | 80.66 ms | +3 % | 72 MB |
| 3. Parse all 75 SRD formulas | 12.43 µs | 12.67 µs | 14.55 µs | 15.19 µs | +17 % | 55.6 KB |
| 3. Evaluate all 75 SRD formulas | 2.650 µs | 2.940 µs | 2.977 µs | 3.065 µs | +12 % | 11.4 KB |
| 4. Find updates, 1 character | 13.01 ms | 14.22 ms | 17.19 ms | 17.33 ms | +32 % | 8.3 MB |
| 4. Find updates, 10 characters | 129.7 ms | 141.5 ms | 176.1 ms | 178.2 ms | +36 % | 82 MB |
| 4. Find updates, 100 characters | 1,330 ms | 1,412 ms | 1,787 ms | 1,822 ms | +34 % | 823 MB |
| 4. Review update, 1 character | 21.64 ms | 23.66 ms | 29.07 ms | 29.44 ms | +34 % | 14.5 MB |
| 4. Review update, 10 characters | 220.8 ms | 236.1 ms | 306.8 ms | 307.7 ms | +39 % | 146 MB |
| 4. Review update, 100 characters | 2,210 ms | 2,355 ms | 3,064 ms | 3,099 ms | +39 % | 1,457 MB |

**The spread is large, and allocations are not.** Allocations matched to within 1 % in every case; times moved by −14 % to +39 % between two runs minutes apart. The cause is not established. The likely suspects are the hybrid CPU (a thread scheduled on an efficient core runs much slower), heat from the test runs in between, and the power plan. Until that is settled (follow-up P-4):
- treat a time change below about 40 % on this machine as noise;
- treat a change in allocations as real.

## First paint (owed: owner, by hand)

Not measured yet. Method, so the result can be compared later:

1. Use the T5 installed build, or else the Release `TomeStack.exe`, on the machine above, with the data folder holding one ordinary character.
2. **Cold:** restart Windows, wait 2 minutes, then start TomeStack. **Warm:** close it and start it again.
3. Record the screen at 60 fps and count frames from the click (or Enter) to the character list showing, or use a stopwatch if no recorder is to hand. Say which.
4. Five cold and five warm starts. Record the median and the range, the build and the date in the table below.

| Build | Date | Cold median (range) | Warm median (range) | Method |
|---|---|---|---|---|
| — | — | owed | owed | — |

The smoke script's `processStartToReadyMs` is not first paint: it is taken when the whole smoke script finishes (`MainWindow.xaml.cs`).

## What the numbers say (the roadmap's questions)

- **Why is `Calculation.cs` 2,249 lines, and where does the time go?**
  - The file is the whole rules pipeline: fields, effect ordering, stacking, multiclass, spellcasting, armor and the trace.
  - The rules core is fast. The heaviest sheet here (20 levels, three classes, 300+ spells) calculates in under 1 ms and allocates 1.2 MB.
  - The time a user waits goes elsewhere. A single update review calculates two sheets (about 2 ms of rules core) but takes 22–29 ms and allocates 14.5 MB. So most of it is in the store path around the calculation, not in `Calculation.cs`.
  - This is inferred by subtraction. Nothing inside the calculation was profiled; that is follow-up P-3.
- **What does allocation tell you that time does not?**
  - Here, stability: allocations repeat to within 1 % while time swings by up to 39 %, so allocation is the regression signal to trust on this machine.
  - It also points at waste. Finding updates for one character only reads, yet allocates 8.3 MB, more than six times what calculating the whole sheet allocates (P-1).
- **What would you change first if a number doubled?**
  - First compare allocations. If they doubled too, the code changed; bisect. If they didn't, look at the machine first (P-4).
  - For a real slowdown, the first place to look is the store path that P-1 and P-2 name, with a before/after table. Nothing is changed in this PR.

## Follow-ups (measured, not fixed here)

| ID | Finding | Measurement | Next step |
|---|---|---|---|
| P-1 | `character.updates` costs about 13–17 ms and 8.3 MB **per character**. It is read-only, and it costs more than half of a full review that calculates two sheets. For each reference it lists every revision of that content from SQLite and deserializes them. This character has about 330 references (classes, 300+ spells, items). | Case 4, `FindUpdates`, linear in N: 1.3–1.8 s for 100 characters | Profile; consider one query per character, or caching deserialized revisions, with a before/after table |
| P-2 | An update review costs 22–29 ms and 14.5 MB per character, against about 2 ms of rules core for its two sheets | Case 4, `ReviewAll` vs case 1 | Profile the store-backed catalog (`SqliteStore.FindRevision` and JSON reads) behind `SheetChanges` |
| P-3 | Where time goes inside `Calculation.cs` is unknown | Case 1: 0.74–0.92 ms, 1.2 MB | A profiler trace (dotnet-trace or BenchmarkDotNet's EventPipe profiler) of case 1, only if a real sheet ever feels slow |
| P-4 | Run-to-run time spread up to 39 % with equal allocations | Both runs above | Repeat with the process pinned to the performance cores and a high-performance plan, and record whether the spread drops |
| P-5 | Previewing a 376 KB pack allocates 67 MB (about 180× its size) | Case 2, `Preview` | Low priority: 33–39 ms is fast enough; note for packs much larger than this |

None of these is visible at today's library sizes: a library of a few characters stays well under a second everywhere. They matter only if a library grows to tens of high-level characters.
