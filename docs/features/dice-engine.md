# Dice engine (skeleton)

SPEC C-04 · ARCHITECTURE "commands vs calculation" · status: rules-core engine and the `roll` command (M1 item 7). The sheet's roll UI is M2.

`src/RulesCore/Dice.cs`, tested in `tests/RulesCore.Tests/DiceTests.cs`.

## Expressions

`[N]dM` terms and integer constants joined by `+` or `-`, with an optional leading sign, for example `1d20+5`, `2d6`, `1d8+1d6+2`, `d20-1`, `-2+1d12`. The input is case-insensitive and whitespace is ignored. Dice expressions are separate from ADR-003 formulas: a roll adds **modifiers** that the caller has already calculated (for example the sheet's initiative value, with its trace origin), instead of evaluating identifiers itself.

**Bounds (SPEC Q-02):** at most 40 characters, 8 terms, 100 dice (doubled on a critical), d2 to d1000, and constants and modifiers within ±1,000. Errors have stable `dice.*` codes and are never exceptions.

## Rolling

- **Advantage / disadvantage:** only for a single `1d20` (a d20 test). Both dice are recorded, with `kept` on one.
- **Critical:** every die term is rolled twice as many times, and the extra dice are marked `fromCritical`. Constants and modifiers are not doubled. Critical doubling belongs to a damage roll and advantage to a d20 test, so a request with both is refused (`dice.critical-with-advantage`).
- **Randomness:** `IRandomSource`. `SystemRandomSource` uses the OS CSPRNG for play. `SeededRandomSource` (SplitMix64 with rejection sampling) gives reproducible tests and examples. Its sequence is pinned by a test against values computed outside .NET.

## Roll record

`{ formula, mode, critical, dice[{term, sides, value, kept, fromCritical}], diceTotal, expressionConstant, modifiers[{label, amount, origin}], total, provenance }`. Provenance cites the roll effect's revision, effect id, source and page, plus `linkedResourceId`.

**Rolling never spends anything.** `DiceRoller` takes no character or resource state, so it cannot change one. `linkedResourceId` only says which resource an *action* would spend. Spending is a separate, confirmed command (M2).

## The `roll` command (M1 item 7)

`roll { characterId, content?, effectId?, field?, mode?, critical? }` (`src/AppService/Rolling.cs`, `tests/AppService.Tests/RollCommandTests.cs`) returns a roll record. It is transport-neutral: the WebView2 bridge, DevHost and tests all dispatch the same JSON.

- **A content roll effect** (`content` + `effectId`): the revision must apply to the character (it is in the sheet's `active` list: pinned, class, granted or chosen). Otherwise the roll is refused (`roll.content-inactive`). The record cites the revision, effect, source and page, and the linked resource if any.
- **A sheet field as a d20 test** (`field`): an ability modifier, saving throw, skill or initiative. The formula is `1d20`, and the field's displayed value (after any override) is the modifier. The modifier's origin is the step that set that value, and the provenance names the field. Other fields are refused (`roll.field-not-rollable`).
- Advantage and disadvantage apply only to a d20 test, and critical doubling only to damage dice. Dice errors come back with their `dice.*` code.
- **It never changes the character.** Nothing is saved, and a roll linked to a resource does not spend it. The test compares the stored character before and after.
- Dice come from the OS CSPRNG. Tests replace the source with `SeededRandomSource`.
