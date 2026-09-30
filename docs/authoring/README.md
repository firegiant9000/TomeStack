# Author documentation

For people who write homebrew for TomeStack, or extensions for it (ROADMAP M6 slice 5).

- [class.md](class.md): write a class in the Homebrew studio, from its features to its level table, and check it.
- [source-pack.md](source-pack.md): share your homebrew sources (and campaigns) with other people.
- [extension.md](extension.md): write a declarative extension that imports or exports data, with the compatibility table.

**These guides are tested.** Every JSON block tagged `tomestack-example:<name>` is read by `tests/AppService.Tests/AuthoringGuideTests.cs`: the class guide's feature and class are published and built at levels 1, 2, 5 and 20 in both rules families; the source-pack steps are followed with that class into a clean data folder; and the extension guide's two blocks are zipped, installed, granted and run, and the e2e flow does the same through the Extensions screen. If a guide and TomeStack disagree, the tests fail.

All examples are original to TomeStack. The guides quote no SRD or third-party rules text (SPEC Q-03), and your homebrew should hold only your own work if you mean to share it.
