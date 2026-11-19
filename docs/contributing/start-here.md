# Start here

TomeStack is a Windows character sheet for 5E-compatible play where every number explains itself: tap a value and "Why?" shows the rule, the source page and the choice behind it. It is free, offline and open source.

There are three ways to help, and only the last one needs a build:

- [Play and report](#play-and-report): use it at the table and tell us what was wrong or missing.
- [Write homebrew](#write-homebrew): author your own content in the app and tell us where the guides fall short.
- [Fix the UI](#fix-the-ui): a small change to the React interface.

## Play and report

1. Install the latest release from the [Releases page](https://github.com/firegiant9000/TomeStack/releases). The installer is not signed yet, so Windows asks you to confirm the first run. The [getting started guide](../guides/getting-started.md) walks through your first character.
2. Play with it. What it does not do yet is in [known gaps](../guides/known-gaps.md), so you can skip reporting those.
3. When something is off, [open a bug](https://github.com/firegiant9000/TomeStack/issues/new/choose):
   - **A number is wrong:** open its "Why?" and say which step of the trace is wrong and what it should be. The trace is the most useful thing you can send.
   - **A rule or feature is missing:** add a gap note on that field or feature in the app ([gap notes](../features/gap-notes.md)), then use the note's "Copy as issue text" and paste it into the bug.
   - **It crashed or misbehaved:** Settings > Copy diagnostics, and paste it into the bug. Check it for anything personal first.

The bug form asks for your TomeStack version (Settings > About), your Windows version, the steps, and what you expected. Never paste text from a book you do not own the rights to share.

## Write homebrew

TomeStack has a homebrew studio: you write classes, subclasses, species, backgrounds, items and spells in the app, try them in a sandbox on an unsaved copy of a character, and publish them when they are ready.

- The [authoring guides](../authoring/README.md) cover a [class](../authoring/class.md), a [source pack](../authoring/source-pack.md) you can share, and an [extension](../authoring/extension.md).
- **Original content only.** Write your own words and numbers. Do not copy rules text from published books; the repository only carries the attributed SRD text that is already bundled, and adding more needs a licence review.
- To share what you made, or to tell us where a guide was unclear, [open an issue](https://github.com/firegiant9000/TomeStack/issues/new/choose) and attach the source pack file.

## Fix the UI

The interface is React and TypeScript in [`src/Ui`](../../src/Ui). A first change there needs Node and the .NET SDK (see [Prerequisites](../../README.md#prerequisites-windows-1011)), then, in `src/Ui`:

```
npm ci
npm run lint
npm test
```

The UI flow tests (`npm run test:e2e`) also need the .NET build; [Build and test](../../README.md#build-and-test) in the README has the order.

A few rules the checks enforce:

- Components talk to the app only through [`src/Ui/src/api/client.ts`](../../src/Ui/src/api/client.ts); lint rejects `fetch` anywhere else.
- Each change adds a line to the [CHANGELOG](../CHANGELOG.md) under Unreleased.
- Keep the change small and add or update a test for it.

You do not need to run the Windows desktop checks yourself: CI runs the desktop smoke, the two-instance check and the accessibility check in Edge on every pull request. Issues labelled [good first issue](https://github.com/firegiant9000/TomeStack/labels/good%20first%20issue) are a good place to start.

The full gate and the project's rules are in [CONTRIBUTING.md](../../CONTRIBUTING.md).
