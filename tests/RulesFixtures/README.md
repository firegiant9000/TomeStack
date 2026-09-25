# Rules fixtures

All content here is **original** and was written for TomeStack tests. It contains no SRD or third-party rules text (SPEC Q-03). Names start with "Fixture".

| File | Schema | Seeded into user data | Purpose |
| --- | --- | --- | --- |
| `fixture-pack.json` | content v1 (upcast on read) | yes (embedded in AppService; the seeding decision is open) | M0: ability-increase policy, draft isolation, an unknown effect type |
| `fixture-pack-m1.json` | content v2 | no, tests only | M1: the second policy difference, a cross-edition conflict, grants |
| `characters/srd51-quickfoot.json`, `srd521-courier.json` | character v1 | no | M0 initiative traces |
| `characters/srd51-ash-m1.json`, `srd521-ash-m1.json` | character v2 | no | Side-by-side: the same inputs under each family |
| `characters/srd521-rook-exception.json` | character v2 | no | A recorded cross-family exception (BACKLOG B06) |

## Acceptance example (level 5, PB +3; base Dex 14, Wis 13)

Executable in `tests/RulesCore.Tests/RulesFamilySideBySideTests.cs`.

| | Ash, SRD 5.1 | Ash, SRD 5.2.1 | Why |
| --- | --- | --- | --- |
| Wisdom score | 13 | 14 | "Fixture Wayfarer" (background) gives +1 Wis. Only 2024 backgrounds grant ability increases (`AbilityIncreaseSource`) |
| Initiative | +2 | +5 | Wayfarer grants the feat "Fixture Watchful" (+PB to initiative). Only 2024 backgrounds grant feats (`BackgroundGrantsFeat`) |
| Stealth | +6 | +8 | Two different revisions named "Fixture Keen Senses", one per family, never merged by name: 2014 gives +1 Dex and +1 Stealth; 2024 gives Stealth expertise |
| Dex score | 15 | 14 | 2014 Keen Senses is species content, and species grant ability increases under 2014 |

Rook (SRD 5.2.1) pins the 2014 Keen Senses with a recorded exception. It applies under 2024 policy (+1 Stealth; the species Dex increase is refused) and raises a `content.cross-family-exception` warning.
