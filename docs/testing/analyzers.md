# .NET analyzers

The .NET code-quality analyzers run in every build, and the gate's warning budget of 0 applies to them (`TreatWarningsAsErrors`). The settings are in `Directory.Build.props`:

| Property | Value | Why |
|---|---|---|
| `AnalysisLevel` | `latest-recommended` | The SDK's recommended set of CA rules at warning. |
| `AnalysisLevelSecurity` | `latest-all` | Every Security rule at warning. It has to be `AnalysisLevel<Category>`: with a compound `AnalysisLevel`, the `-recommended` suffix wins over `AnalysisModeSecurity`. |
| `AnalysisLevelStyle` | `latest-default` | Raises no code-style (IDE) rule to a warning. Without it, Style inherits `recommended`, which turns about 90 IDE rules into build warnings, the formatter's layout rule IDE0055 among them. The repo's compact layout is deliberate and is never "fixed" by reformatting. |
| `EnforceCodeStyleInBuild` | `true` | IDE rules that a project or `.editorconfig` sets to warning run in the build, not only in the editor. |

## How suppressions are kept

- **Permanent exceptions** (test and benchmark projects): one `NoWarn` block for every `*.Tests` project and `TomeStack.Benchmarks`. These rules don't fit test code; they are not debt.
- **Burn-down**: one `NoWarn` block per project, listing the rules that were not fixed when the analyzers were turned on, with each rule's count at that time. Fix the occurrences, then remove the rule from the list. A rule in a burn-down list is off for the whole project, so new code isn't checked for it either until the rule is removed.
- **Targeted**: a false positive in shipped code under a Security rule gets a `[SuppressMessage]` with a justification on the one member, so the rule keeps checking new code. Generated code is silenced only by path (`src/ImportWorker.Host/.editorconfig`).

## Counts when turned on

The build had 2,868 findings across 11 projects. 2,655 were CA1707 (underscores in test names).

### Fixed

None of these fixes changes behaviour.

| Project | Rule | Count | Fix |
|---|---|---|---|
| AppService | CA1861 | 1 | Constant array moved to a `static readonly` field (`DdbParser`). |
| AppService | CA1822 | 1 | Private `SourcePacks.Merged` made `static`. |
| AppService | CA2263 | 1 | `Duplicates.Of` uses the generic `SerializeToNode<IReadOnlyList<Effect>>`, which is the same type and options as before, so the hashes don't change. |
| ImportWorker | CA1861 | 2 | `ClassProposals` armor and weapon kinds moved to `static readonly` fields. |
| DesktopShell | CA1861 | 1 | The smoke's rules-family array moved to a `static readonly` field (same JSON). |
| AppService.Tests | CA1829 | 2 | `.Count` property in place of `Count()`. |

### Security (every finding read by hand)

None is a real issue.

| Project | Rule | Count | Ruling |
|---|---|---|---|
| AppService | CA2100 | 4 | False positive: `SqliteStore.Command` and `RestoreDrill`'s `Digest`/`Execute`/`Rows` take SQL from callers that pass constant text; the interpolated parts are fixed clauses, placeholder names (`$i0`, `$t0`) or an `int` (`PRAGMA user_version`), and every value is a parameter. Targeted `[SuppressMessage]`. |
| ImportWorker.Host | CA5392 22, CA5394 2, CA5350 1 | 25 | All in the C#/WinRT projection generated under `obj/` (P/Invokes to Windows' own COM/WinRT DLLs, `Random` in event-token tables, SHA-1 to derive WinRT interface ids). No hand-written P/Invoke, `Random`, SHA-1 or MD5 exists in `src/`. Silenced for that path only, in `src/ImportWorker.Host/.editorconfig`. |
| Test projects | CA2100 13, CA5394 30 | 43 | Permanent: constant raw SQL against test stores; seeded `Random` for reproducible inputs. |

### Permanent (test and benchmark projects)

| Rule | Count | Why |
|---|---|---|
| CA1707 | 2,655 | Test and benchmark names use underscores (`Opening_a_newer_folder_is_refused`), the repo's convention. |
| CA1825 | 14 | Fires on the compiler's empty `params` array when a collection expression builds xUnit `TheoryData`; there is nothing to change in source. |
| CA1861 | 22 | Constant array arguments in test bodies that run once; inline literals keep the expectation next to the assertion. |
| CA2100 | 13 | See Security. |
| CA5394 | 30 | See Security. |

### Burn-down

| Project | Rule | Count | Why not fixed here |
|---|---|---|---|
| RulesCore | CA1859 | 3 | Changes field and parameter types (`Specs`/`SpecIndex` are interface-typed on purpose; the calculator partials are being edited by other lanes). |
| RulesCore | CA1711, CA1720, CA1716 | 2, 2, 1 | Renames public types and members (`CampaignException`, `Int`, `IRandomSource.Next`): public API shape. |
| RulesCore | CA1854 | 1 | `TryGetValue` in `Calculation.Graph.cs`: safe, but the calculator partials are being edited by other lanes. |
| AppService | CA1859 | 14 | Concrete types in signatures and fields; changes method shapes. |
| AppService | CA1826 | 6 | Indexer in place of `First()`/`Last()`: changes the exception thrown on an empty list. |
| AppService | CA1000 | 3 | Static members on generic `Read<T>` (`DdbSheet`): public API shape. |
| AppService | CA1305 | 3 | `RestoreDrill.Format`, the dev-only drill's console table. Only the `:` time separator can follow the machine's culture; no user-facing text is affected. Culture change, so not here. |
| AppService | CA1822 | 1 | `TomeStackApp.PreviewFormula` is public: making it static changes API shape. |
| ImportWorker | CA1859 | 3 | Concrete return types; changes method shapes. |
| DesktopShell | CA1001 | 1 | `MainWindow` owns a `SemaphoreSlim`; making the window `IDisposable` changes its lifetime contract. |
| DesktopShell | CA1826 | 1 | Exception type on an empty list. |
| DevHost | CA1848, CA1873 | 1, 1 | One startup log line in the dev-only host. |
| Benchmarks | CA1001 | 1 | `LabBenchmarks` disposes its store in `[GlobalCleanup]`, which is BenchmarkDotNet's lifetime hook; `IDisposable` would duplicate it. |
| AppService.Tests | CA1826 20, CA1305 18, CA1716 2, CA1862 2, CA1869 2, CA1822 1, CA1859 1, CA2201 1 | 47 | Exception types, culture or string comparison, renames, and a deliberate `COMException` double. |
| RulesCore.Tests | CA1305 | 2 | Culture. |
| ImportWorker.Tests | CA1068 1, CA1859 1, CA2201 1 | 3 | Parameter order, signature, a deliberate `COMException` double. |
| DesktopShell.Tests | CA1859 | 1 | Signature. |
