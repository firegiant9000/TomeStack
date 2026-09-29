# Character snapshots (M5 slice 8, BACKLOG B08)

A snapshot is a fixed copy of a saved character to come back to: its choices, pins, class levels, equipment, spells, overrides and play state. The owner decided (LIVING_SPECS D14) that snapshots are **taken by hand only**, apart from the undo snapshot every restore takes, and are **not in the full library backup**.

## Where they are

- **The sheet's "Snapshots" section:** an optional name, **Take snapshot**, and the list, newest first. Each has **Restore …**.
- **Commands:**
  - `character.snapshot { characterId, label? }` (label at most 200 characters);
  - `character.snapshots { characterId }`;
  - `character.restorePreview { characterId, snapshotId }`;
  - `character.restoreSnapshot { token, confirm }`.

## Restoring

1. **The preview**, shown like an update review. It lists the calculated values that change, the problems the restored character would have (content it pins that is not installed shows as `content.missing`), how many content references come back or go, and whether the play state changes. It writes nothing. It returns a **one-use token** bound to the character as it is at that moment (a hash of its stored state).
2. **Restore**, with `confirm`, **in one SQLite transaction**: it first takes an **undo snapshot** of the character as it is (reason `beforeRestore`, named "Before restoring …"), then saves the snapshot's state through the normal save checks. If anything fails, neither happens. The undo snapshot can itself be restored.
3. **The token is used once:** a repeated request finds none and changes nothing (`snapshot.token-unknown`). A character changed after its preview refuses (`snapshot.character-changed`), because the user never saw what the restore would do.

Other refusals: `snapshot.confirm-required`, `snapshot.not-found` (also for another character's snapshot), `snapshot.label-too-long`, and `character.not-found`.

## Rules

- **The archive mark (SPEC C-08):** a snapshot never carries it. A restore keeps the character's current mark, so it never archives or unarchives.
- **Campaign membership:** a restore keeps the character's current campaign and that campaign's recorded exceptions. So it never moves a character between campaigns, or back into a campaign deleted since (the `campaign.in-use` rule). The preview lists any campaign warning the restored content would bring. It also says when the name or the cross-family exceptions go back to the snapshot's.
- **One pending restore per character:** a newer preview replaces an older, unused token. A failed restore also uses its token, so the studio closes the preview and asks for a new one. Focus then moves to the "Snapshots" heading, as it does after a restore.
- **The list** shows the newest 100 snapshots (`SqliteStore.MaxListedSnapshots`). Older ones stay stored, because snapshots are never removed.
- **Insert-only:** database migration **v7** adds `character_snapshots`. Triggers refuse any `UPDATE` or `DELETE`, so a snapshot never changes or disappears. The migration is forward-only, and the usual `tomestack.db.v6.bak` is taken first. Older builds refuse a v7 data folder (`NewerDatabaseException`), as for every migration.
- **Local only:** no character package (backup or share) and no full library backup includes snapshots. There is no package format change. The copy of the database taken before a library restore (package-format rule 10) is a full database copy, so it holds them.
- **No new character or content schema:** a snapshot stores the character JSON as it is (character schema v7).

## Acceptance

- `AppService.Tests/SnapshotTests`:
  - the preview, the restore, the undo snapshot and the one-use token;
  - confirmation, and a character changed after the preview;
  - the archive mark both ways, and campaign membership kept (including a campaign deleted since);
  - `content.missing`;
  - a failing restore that leaves no undo snapshot (the one transaction), and a newer preview replacing an older token;
  - insert-only triggers, and no export, share or library backup holding them;
  - the v6 → v7 upgrade, keeping a character written by v6, with the v6 copy holding it too.
- The e2e flow "takes a snapshot of a character, previews the restore, restores it and keeps an undo snapshot".
