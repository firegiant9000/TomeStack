# 0.4.0 install verification (roadmap T5)

ROADMAP T5 · status: **owed (owner), on the published 0.4.0 `Setup.exe`.**

Everything in 0.4.0 since 0.3.0 is fixture-verified: the automated tests and the CI desktop smoke pass. This list moves it to **Windows-install verified**: the same features used once through the installed app. Each line is one owner check. Record the date, the Windows version (10 or 11) and pass or fail, and describe failures generically, with no names, paths or homebrew text (the repository is public). Anything not done stays an owner check; it is not rounded up.

Install with the published `TomeStack.App-win-Setup.exe` on a machine or VM without a real library, or with `--data-dir` pointing at an empty folder.

## Install

| # | Check | Expected |
|---|---|---|
| I1 | Run `Setup.exe` as a standard user | SmartScreen's "Windows protected your PC" (unsigned; More info → Run anyway); no admin prompt; TomeStack starts |
| I2 | The install folder | `%LOCALAPPDATA%\TomeStack.App\current` holds `LICENSE`, `NOTICE`, `ATTRIBUTION.md`, `THIRD-PARTY-NOTICES-Velopack.md` |
| I3 | About / version | 0.4.0, database schema 9 |
| I4 | Offline | With the network off, TomeStack starts and every check below works |

## M2.2: the Fighter

| # | Check | Expected |
|---|---|---|
| F1 | Build an SRD 5.2.1 Fighter to level 5 (Champion at 3), with chain mail and a longsword | Two attacks per Attack action; Improved Critical gives 19–20; AC 16; the armor's Strength and Stealth warnings show as in `equipment.md` |
| F2 | The same under SRD 5.1 | It builds the same way, with no Weapon Mastery choices (2024 only) |
| F3 | Second Wind and Action Surge | Resources tracked; Second Wind rolls 1d10 + Fighter level; a short rest restores them |

## M5 slice 1: author a class in the studio (M5's "delivered" condition)

| # | Check | Expected |
|---|---|---|
| S1 | Homebrew studio → **New class**: set a hit die, saving throws, a skill choice, one class column (for example "Ink"), a level-1 feature and a level-3 subclass choice. Publish | Published without errors; "Find problems" finds none that matter |
| S2 | Build a character with it at level 1, then level 3, then multiclass it with an SRD class | The class column shows under "Class columns"; hit points and features follow the class; the multiclass prerequisites apply |
| S3 | "Try it" on an unpublished change | The sandbox shows the change; nothing is saved |

## M6 screens

| # | Check | Expected |
|---|---|---|
| M1 | Sources → **Mark as shareable…** on your homebrew source → **Share sources as a pack**; import it with `--data-dir` into an empty folder | The import preview shows the sender's statement; content arrives; the source is "received" |
| M2 | Campaigns → **Share …**; import it into the empty folder | The campaign's rules and allowed sources arrive; SRD named, not copied; no characters |
| M3 | Extensions → install `examples/extensions/spell-list-and-sheet-summary` (zipped), grant its permissions, run the Markdown summary on a character | Permissions listed before install; the preview before writing; the file has the notices and no paths |
| M4 | Sheet → **Export for a virtual tabletop**: Foundry VTT (dnd5e) and sheet JSON | Both files save; the preview lists what is left out; (optional) the Foundry file imports into a Foundry 14 world with dnd5e 6.0.5 |
| M5 | **Back up everything**, then restore into an empty folder | Everything comes back; the extension comes back turned off with no permissions |

## Results

| Date | Windows | Checks | Result | Notes |
|---|---|---|---|---|
| — | — | I1–I4, F1–F3, S1–S3, M1–M5 | owed (owner) | |

When every line passes:
- ROADMAP T5 can record "install verification done";
- M5 is **delivered** (merged, and authored once on an installed build);
- M2.2 and the M6 screens become Windows-install verified.

The M6 exit gate still needs its cross-machine part (T6).
