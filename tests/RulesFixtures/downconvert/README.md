# Down-conversion goldens ("Save for an older TomeStack", D85)

Everything here is original test data written for TomeStack: no SRD or third-party rules text, no book or setting names.

Each folder pins what a character export made for an older release writes, byte for byte
(`DownConversionGoldenTests.Golden_copy_matches_byte_for_byte`).

| Folder | Holds |
|---|---|
| `for-0.6.0/library/` | "Compat Hero" at character schema v10 with an active toggle that has rounds left, pinning the v9 feature "Compat Steady Hands" (passes through), v11 features that cover every warn row of spec section 2 "Content v11 → v10" that can warn without a counting-up pool (Tide Stance, Calm Waters, Rising Tide, Deep Currents), the class "Compat Path of Tides" whose subclass "Compat Tide Walker" grants the v11 feature "Compat Tide Mark" (two cascade copies), and the bundled revision `51e00000-0000-4000-8000-000000000001` (in the 0.6.0 set; written as it is), all under the source "Compat Homebrew A" (`pack.json`). A 0.6.0 copy drops `roundsLeft` and the listed effects and fields; nothing blocks. "Compat Draw Down" has a roll with `drawsDown: true` on a counting-up pool declared in another content ("Compat Reservoir", not pinned, so its own block does not apply). `resource.counts` and `resourceFull` exist only on a counting-up pool, which blocks, so they are not here. |
| `for-0.5.0/library/` | "Compat Hero" at character schema v9 with details, a custom item and a fire resistance, pinning v10 revisions with resistance and immunity grants, item charges, a speed bonus and a passive bonus, a v11 revision that goes straight to v9, and the same bundled revision (in the 0.5.0 set). A 0.5.0 copy drops all of these; nothing blocks. |
| `<folder>/expected/entries/` | Every entry of the package the export writes, under its path in the zip, except the bundled SRD revision and its source: their text lives only in `src/AppService/Content/`, so the test compares those two entries with the store and the manifest's matching `sha256`, `size` and notice text appear as `{bundled}`. |
| `<folder>/expected/changes.json` | The `changes` list the preview returns (what the copy loses). |

The test loads the library into a fresh store, previews the share export for the target, exports with the preview's
`changesHash`, compares each entry with `expected/entries/`, validates the manifest against
`docs/schemas/package-manifest.v<format>.schema.json` and compares the change list with `changes.json`.

The manifest's `appVersion` is the true maker and changes every release, so both sides write it as `{appVersion}`
before comparing, and line endings are compared as LF (the repository stores LF; the writer uses the platform's). The
manifest's `sha256` and `size` values are taken from the bytes the writer produced on Windows (CRLF), so the golden is a
Windows test, like the app. Everything else is exact.

## Regenerating `expected/`

Only when the lowering, the package format or a fixture changes on purpose. From the repository root:

```
$env:TOMESTACK_REGENERATE_GOLDENS = "1"
dotnet test tests/AppService.Tests -c Release --filter FullyQualifiedName~DownConversionGoldenTests
Remove-Item Env:TOMESTACK_REGENERATE_GOLDENS
```

That rewrites both `expected/` folders from the implementation. The regenerate run fails on purpose, so it never reads as green; that failure is expected, not a bug. Then review the diff by eye against SPEC section 2 (what a
copy for each release keeps, drops and blocks) before committing: the golden proves the output is stable, not that it is right.
