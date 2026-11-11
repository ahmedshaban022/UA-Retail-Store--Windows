# Published releases

Every published release of StoreFlow Desktop, with the commit it was built from. The tag names
the commit, so this table is a record of what a shop actually received — not a claim about what
`main` happened to hold on the day.

| Release | Tag | Commit | Published | Installer | SHA-256 |
|---|---|---|---|---|---|
| v1.0.2 | `v1.0.2` | `079a870` | 2026-11-11T06:32:54Z | `StoreFlow-1.0.2-x64.msi` (56,019,231 bytes) | `29d4f95e52d1b07103eabf95a2e1d86b8ff69ae27b1a56812178c76ee0e05c87` |
| v1.0.1 | `v1.0.1` | `ab02c6e` | 2026-11-04T12:59:23Z | `StoreFlow-1.0.1-x64.msi` (55,990,559 bytes) | `b119cdb140957ed77db2c01bf039db74a5db3f1c1b12d3782ec4b72435abc1bc` |
| v1.0.0 | `v1.0.0` | `0cb5663` | 2026-11-04T06:59:47Z | `StoreFlow-1.0.0-x64.msi` (55,961,887 bytes) | `69aa3e5ec9403ee8308f21acd243a1c27e3641aab72b96df2c36a9bd59363c9b` |

## How a release is made

1. The version in `src/StoreFlow.Desktop/StoreFlow.Desktop.csproj` is bumped. The release
   workflow refuses a tag that does not match it, so the version and the tag can never disagree.
2. The tag is pushed. The workflow builds the programme, runs the whole suite as its gate, builds
   the installer, computes the installer's SHA-256 and publishes the release with that hash beside
   it.
3. The row above is filled in from the published release, and the commit the tag points at is
   recorded here.
4. The status line in `docs/RELEASE-NOTES.md` is changed from `unpublished` to `published`, in the
   same commit as the row.

The installer's size and SHA-256 in the table are read from the published release's own asset — the
file the shop downloads — and are not written down from memory.

## How a release that has not been made yet says so

`docs/RELEASE-NOTES.md` carries one line that says whether the release has been published:

```
**Release status: `unpublished`.**
```

It is there because a release that has been written, tested and committed but never published
looks exactly like a release that has been published — the same heading, the same notes, the same
version in the project file. Nothing in the repository would have said otherwise, and a shop
waiting for the fix would wait for ever. The line is taken out of the body the release workflow
publishes, because it is bookkeeping about the repository and not something a shop needs to read.

The guard test in `tests/StoreFlow.Desktop.Tests/ReleaseStatusTests.cs` holds the three files to
each other: the version in the project file is the version the notes describe, the status line is
there and holds one of the two words, and the status agrees with the table above — a release that
says `published` has a row, and a release that says `unpublished` has not. That test is what
would have caught v1.0.2 sitting in the tree, ready and never published.

**The tag is the truth.** A release is built from the commit the tag points at, which is why a
tag may sit one commit behind `main` — `main` can carry documentation and pipeline changes that
were never part of the shipped binary. What a shop runs is what the tag names, so that is what is
written down here.

## Why the repository has to stay public

The updater reads the public GitHub Releases API anonymously, with no token and no account. A
private repository answers every such request with a 404, so the in-application update could never
fire and the installer could never be downloaded. This is written down in full in
[`UPDATER.md`](UPDATER.md).
