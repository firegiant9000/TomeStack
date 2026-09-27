# Rules fixtures

All content here is **original** and was written for TomeStack tests. It contains no SRD or third-party rules text (SPEC Q-03). Names start with "Fixture". The bundled SRD content is separate, in `src/AppService/Content/` ([license review](../../docs/licensing/srd-pack-review.md)).

| File | Schema | Seeded into user data | Purpose |
| --- | --- | --- | --- |
| `fixture-pack.json` | content v1 (upcast on read) | development only (DevHost, tests, `TOMESTACK_DEV_FIXTURES=1`); the shipped app seeds the SRD packs instead (owner decision 2026-09-26) | M0: ability-increase policy, draft isolation, an unknown effect type |
| `fixture-pack-m1.json` | content v2 and v3 | no, tests only | M1: the second policy difference, a cross-edition conflict, grants; classes "Fixture Warden" (d10) and "Fixture Scholar" (d6) with level-gated features, a hit point feat and an armor class item (item 5); choices: Warden skills (2 of 3) and a level-3 path, and the background "Fixture Crossroads" with a chosen ability increase (item 4) |
| `characters/srd51-quickfoot.json`, `srd521-courier.json` | character v1 | no | M0 initiative traces |
| `characters/srd51-ash-m1.json`, `srd521-ash-m1.json` | character v2 | no | Side-by-side: the same inputs under each family |
| `characters/srd521-rook-exception.json` | character v2 | no | A recorded cross-family exception (BACKLOG B06) |

## Private fixtures (not in the repo)

Material that may not be published, starting with the owner's **Stardust Guardian** homebrew (MVP definition of done 3, M3), goes in `tests/RulesFixtures/local/`. That folder is gitignored, because the repository is public. Nothing in the gate reads from it, so the gate passes without it. A test that needs it must skip when the folder is absent and must never copy its contents into a committed file, a log or a snapshot. Before committing, `git status` must not show anything under `local/`.

## Acceptance example (level 5, PB +3; base Dex 14, Wis 13)

Executable in `tests/RulesCore.Tests/RulesFamilySideBySideTests.cs`.

| | Ash, SRD 5.1 | Ash, SRD 5.2.1 | Why |
| --- | --- | --- | --- |
| Wisdom score | 13 | 14 | "Fixture Wayfarer" (background) gives +1 Wis. Only 2024 backgrounds grant ability increases (`AbilityIncreaseSource`) |
| Initiative | +2 | +5 | Wayfarer grants the feat "Fixture Watchful" (+PB to initiative). Only 2024 backgrounds grant feats (`BackgroundGrantsFeat`) |
| Stealth | +6 | +8 | Two different revisions named "Fixture Keen Senses", one per family, never merged by name: 2014 gives +1 Dex and +1 Stealth; 2024 gives Stealth expertise |
| Dex score | 15 | 14 | 2014 Keen Senses is species content, and species grant ability increases under 2014 |

Rook (SRD 5.2.1) pins the 2014 Keen Senses with a recorded exception. It applies under 2024 policy (+1 Stealth; the species Dex increase is refused) and raises a `content.cross-family-exception` warning.
