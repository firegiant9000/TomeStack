# M1 acceptance: two rules-family characters calculate and explain their outputs

ROADMAP M1 exit gate · executable in `tests/AppService.Tests/M1AcceptanceTests.cs` · **status: passing (2026-09-26)**

Two level-3 characters, one per rules family, built only from the bundled SRD slice (`src/AppService/Content/`). Both have the same base scores: Str 15, Dex 13, Con 14, Int 8, Wis 12, Cha 10.

| | Korga (SRD 5.1, 2014 rules) | Brenna (SRD 5.2.1, 2024 rules) |
| --- | --- | --- |
| Fixture | `characters/m1-acceptance-srd51-korga.json` | `characters/m1-acceptance-srd521-brenna.json` |
| Species / background | Half-Orc (p. 7) / Acolyte (pp. 60–61) | Dwarf (p. 84) / Soldier (p. 83) |
| Class | Barbarian 3 (pp. 8–9) | Barbarian 3 (pp. 28–30) |
| Choices (all resolved) | Skills: Athletics, Perception. Primal Path: Berserker | Soldier ability scores: Str +2, Con +1. Skills: Perception, Survival. Subclass: Berserker. Primal Knowledge: Animal Handling |
| Override | Hit points 33, "Rolled 8 and 7 at levels 2 and 3" (calculated 32) | none |

## Expected values and why

| Field | Korga | Brenna | Explanation in the trace |
| --- | --- | --- | --- |
| Strength score | 17 | 17 | Korga: +2 from species Half-Orc, p. 7 (2014 species may raise scores, `AbilityIncreaseSource`). Brenna: +2 "chosen from background 'Soldier'", p. 83 (2024 backgrounds may) |
| Constitution score | 15 | 15 | Korga: Half-Orc +1. Brenna: the same Soldier option +1 |
| Strength modifier | +3 | +3 | Rules step: floor((score − 10) / 2) |
| Proficiency bonus | +2 | +2 | Rules step: by total level 3, from class levels |
| Strength save | +5 | +5 | +3, plus proficiency from class 'Barbarian' (Korga p. 8, Brenna p. 28) |
| Constitution save | +4 | +4 | +2, plus proficiency from the class |
| Dexterity save | +1 | +1 | Modifier only |
| Athletics | +5 | +5 | Korga: "chosen from class 'Barbarian'". Brenna: background 'Soldier' |
| Perception | +3 | +3 | Both chosen from the class |
| Intimidation | +2 | +2 | Korga: species Half-Orc (Menacing). Brenna: background 'Soldier' |
| Insight | +3 | +1 | Korga: background 'Acolyte' |
| Religion | +1 | −1 | Korga: background 'Acolyte' |
| Survival | +1 | +3 | Brenna: chosen from the class |
| Animal Handling | +1 | +3 | Brenna: "chosen from feature 'Primal Knowledge'" (granted at Barbarian level 3) |
| Initiative | +1 | +1 | Rules step: starts at the Dexterity modifier |
| Armor class | 13 | 13 | Unarmored Defense (Korga p. 8, Brenna p. 29) replaces 10 + Dex with 10 + DEX.MOD (1) + CON.MOD (2), granted by the class at level 1 |
| Hit points | **33** (override; calculated 32) | 35 | d12 maximum 12, levels 2–3 at 2 × 7, Con +2 × 3 = 32. Korga: the user override is the final step and names the calculated value. Brenna: + Dwarven Toughness, LEVEL = 3 (species Dwarf, p. 84) |

Also checked: Frenzy (the level-3 Berserker feature) is active for both, and Savage Attacker (the 2024 background's origin feat) for Brenna. Neither sheet has a diagnostic. Every active revision comes from an SRD source.

## What "explains itself" means in the test

For every major field (6 scores, 6 modifiers, 6 saves, all 18 skills, proficiency bonus, initiative, armor class, hit points):

- It is `automatic`, and its trace ends at the displayed value.
- Every step has a description and names the character's rules family.
- Every content step names the content revision, the SRD source title and the page.
- An override is the last step and names the calculated value it replaced (SPEC C-06).

Both characters also round-trip through a share package to a clean data folder with an identical sheet.
