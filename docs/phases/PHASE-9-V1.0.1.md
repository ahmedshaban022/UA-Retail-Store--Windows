# Phase 9 — v1.0.1: the release cycle, proved end to end

This phase does not add a feature to the shop. It proves that the release machinery works, closes
the three debts the earlier phases left open, and hands the programme back with the gate tables
updated. The authority for the two tables below is the [PRD](../../StoreFlow_Retail_Management_PRD_EN.md)
and the [UAT plan](../uat/UAT-PLAN.md), exactly as in [Phase 8](PHASE-8-RELEASE-CANDIDATE.md).

The handoff that this phase executed asked for four things: the updater's first real cycle, the
restore-confirmation failure explained and fixed, each of the three open debts decided, and the
release-cycle housekeeping done. All four are recorded here, with what each one does and does not
prove.

## What was built and decided

- **The updater's first real cycle.** v1.0.1 was published and an **installed v1.0.0** was driven
  through the whole cycle: it found the newer release, offered it, downloaded the installer and
  the checksum published beside it, checked the download against that checksum, installed it, and
  came back as v1.0.1 — with the shop's data measured before and after and found unchanged. The
  run is [`tools/update-cycle.ps1`](../../tools/update-cycle.ps1), and it is written to be run
  again for every future release.
- **The release cycle is a script, not a procedure.**
  [`tools/update-cycle.ps1`](../../tools/update-cycle.ps1) runs it in four phases — Prepare, Install,
  Update, Verify — that can also be run one at a time, and
  [`tools/ShopDigest`](../../tools/ShopDigest/ShopDigest.csproj) measures the shop's data by **what it
  holds** rather than by the bytes of a file that moves when the programme closes.
- **The restore-confirmation failure is explained.** The cause was a defect in the test harness,
  not in the programme: the harness clicked the button that puts the shop back **while the button
  was still disabled**, and WPF throws a click on a disabled control away without a word. The
  screen therefore never changed and the harness blamed the programme. The harness now waits for
  the button to become usable, and then waits for the confirmation to be on the screen.
- **Every business change writes its audit entry in the same write as the change.** Thirty-three
  commands now add their entry to their own transaction. Two remain outside it, and both are named
  in the shop's release notes.
- **All 88 controls without an accessible name have one.** The guard's threshold is zero.
- **CI runs on every push and pull request**, and every smoke harness writes its pictures into
  the repository instead of a work session's temporary folder.
- **The archive encryption is designed, not built.** [ADR-0005](../decisions/ADR-0005-archive-encryption.md)
  pins the whole design — an AES-256-GCM container, an encrypted manifest, a passphrase or a
  DPAPI-protected unattended backup, and the rules for a lost passphrase — and the reason it is
  not in this release.

## How it was verified

### The build and the suite

The orchestrator reports a solution build with **0 warnings and 0 errors** in both Debug and
Release. The full solution suite was run **three consecutive times** in Debug and once in
Release; every run was green. The suite grew from 489 to **544 tests**: 25 domain, 158
application, 233 infrastructure and 128 desktop.

The additions are:

- **Seven tests for the audit atomicity.** Six new facts in `WriteFaultMatrixTests.cs` and one
  new matrix row. Each of them fails the write of an audit entry inside a command's own
  transaction and then checks that **neither the change nor the entry is left behind**, for the
  catalogue, the accounts, the shelf, the counter, the drawer count and the first-run setup. The
  write-fault matrix gained a write point, `WriteAuditEntryInTransaction`, which brings the
  catalogue of named write points to fifty-four.
- **Six tests for the updater's quiet failures.** The feed that cannot be reached, the feed that
  answers *too many requests*, the release that carries no installer, the download that breaks in
  the middle, the download that is cancelled, and the proof that an update never writes inside
  the shop's data.
- **Five tests for the shop that could not be migrated.** The shop that has already run the
  programme can still be migrated with its guards in place; the guards are back afterwards; a shop
  whose database file exists but is empty still opens; a connection that cannot write is not asked
  to change the file; and a connection that can write puts the shop in the write-ahead log.

### CI

The new workflow ran on `main` and **passed**: the build, the whole suite as the gate, and the
installer. It is the same gate the release workflow uses, run on every push rather than only on
a tag.

### The backup harness, run again and again

The backup harness is the one that carries the backup, the restore, the recovery wizard, the
Arabic run and the restore into a second shop. It was run against the development build after
the fix, and the evidence is in [`build/scratch/backup-run-1.log`](../../build/scratch/backup-run-1.log)
and [`backup-run-2.log`](../../build/scratch/backup-run-2.log): **39 checks, 0 failures** on each
run.

It was then run **three times against the installed v1.0.1** — the published release, driven the
way a shop would drive it — with the same result: **39 checks, 0 failures** on each run. The
evidence is in [`build/scratch/backup-installed-run-1.log`](../../build/scratch/backup-installed-run-1.log)
and its two siblings, and the phase-6 pictures in the repository were taken from that installed
copy.

### The four harnesses that had stopped working

`smoke-flow`, `smoke-launch`, `smoke-login` and `smoke-shell` had all fallen behind the
programme: they looked for the wizard and the sign-in window by their English titles while the
application starts in Arabic, they photographed a window from outside the process, which a locked
desktop cannot do, and they started the application with a capture switch that no longer exists.
All four were failing, and nothing had noticed because nothing ran them.

They share the helpers in [`tools/lib/storeflow-ui.ps1`](../../tools/lib/storeflow-ui.ps1) with
the rest of the harnesses now, and they are green:

| Harness | Checks | Failures |
|---|---|---|
| `smoke-launch.ps1` | 2 | 0 |
| `smoke-flow.ps1` | 9 | 0 |
| `smoke-login.ps1` | 5 | 0 |
| `smoke-shell.ps1` | 7 | 0 |

The pictures they take are the application's own drawings, so the Arabic ones are right way round.
The phase-0 pictures the Phase 0 report leans on are in the repository again, and the folder says
what each one shows.

### The updater's first real cycle

Until this phase the updater had never applied a release. v1.0.1 was published, and the **installed
v1.0.0** — the copy a shop would actually have had — was driven through the whole cycle by
[`tools/update-cycle.ps1`](../../tools/update-cycle.ps1):

| Step | What the run saw |
|---|---|
| the published v1.0.0 installer | downloaded and checked against the checksum published beside it |
| the installed copy, before | 1.0.0, installed for this user, with no administrator account |
| what it found | "Version v1.0.1 is available. Install it now?" |
| what it downloaded | `StoreFlow-1.0.1-x64.msi`, 55,990,559 bytes, into `%TEMP%\StoreFlow\update-v1.0.1` |
| its SHA-256 | `b119cdb1…35abc1bc` — the hash published beside the release |
| the installed copy, after | 1.0.1 |
| the shop's database, before | 28 tables, 69 rows |
| the shop's database, after | 28 tables, 69 rows — **the same tables, the same rows** |
| the real `%APPDATA%\StoreFlow` | 7 files, not one byte changed |

The run is written to be run again for every future release, in four phases that can also be run one
at a time: **Prepare** (both releases are published, and the old one's installer matches its
published hash), **Install** (the old version, per user, with no elevation), **Update** (the
installed copy finds, offers, downloads, checks and applies the new one), and **Verify** (the
installed version is the new one, and the shop's data is what it was).

The shop it drives is a throwaway one, so the real shop is never written to, and the real
`%APPDATA%\StoreFlow` is measured before and after as well: seven files, unchanged.

## What running it found

### The harness clicked a button that was not ready

The restore confirmation that Phase 8 could not explain was the harness's own doing. It clicked
`Restore.Now` while the button was still disabled — the button only becomes usable once the copy
has been checked — and WPF discards the click without a word. The screen never changed, so the
harness reported a failure with no cause. The harness now waits for the button to be usable
(`Wait-EnabledById`) at both restore sites and polls for the confirmation with a deadline
instead of sleeping a fixed two seconds and looking once.

**The same class of defect was found again, elsewhere, while this phase was running.** The check
that reads the checksum the restore wizard displays was a single lookup, and it failed one run
out of two: the label is put on the screen a moment after the status line says the copy is whole.
It now waits for the value the way every other wait in the harness waits for a value.

This is the third time this pattern has cost real debugging time in this programme, and it is
worth writing down: **a wait is for a value the screen must reach, never for a number of rows
that is already at its target.** Three scans of the same barcode are one line of three, so a row
count never changes and the next input arrives while the programme is still answering the last
one.

### The check that looked once, for the fourth time

The backup harness, run against the **installed** release for the first time, failed one check in
39: *the scheduler took its own copy without being asked*.

The scheduler had taken its copy. The folder held `storeflow-weekly-…sfb`, taken twenty seconds
after the shop opened. The check demanded a file named `storeflow-daily-…`, because the owner had
saved a **daily** schedule — but the copy that belongs to that schedule is the one taken the next
time the shop opens, and the check looked before that.

The scheduler looks every half hour and takes its copy twenty seconds after the shop opens, so the
check has been moved to where the shop is opened again, and it waits for the copy rather than
looking once. That is the same defect as the two above, in a third place, and it is worth counting:
**four checks in this programme have looked once for a value that had not arrived yet.**

The daily time the harness writes was `03:45`, which only makes the daily copy due in the hours
after it — a check that only works in the afternoon is a check that fails at midnight for no
reason. The harness now writes a time that has already passed today, so the copy is due whenever
the harness runs.

### The audit entry could be written after the change was already permanent

The gap the Phase 8 report described — a command that changes the shop's data and then writes
its audit entry in a second, separate write — is closed for every business change except two.
A crash between the two writes left a shop with a change on the books and no record of who made
it. The entry and the change are now made permanent together.

Two places cannot join the transaction, and both are named in the shop's release notes:

- **A restore.** It replaces the shop's whole database file. No transaction spans replacing a file
  and inserting a row into it, so the entry that records the restore is written to the restored
  shop immediately afterwards. A crash in that moment leaves a restored shop whose trail does not
  mention the restore; the restore point still holds the previous state.
- **The backup schedule.** It is written by the settings store, which owns its own unit of work.
  Its entry is written immediately afterwards, in the same way.

Neither can leave the shop's stock, money or catalogue in a state it should not be in. The only
thing at risk in either case is the presence of one line in the audit trail.

### The harness put its own log line inside the command it was running

The Install phase failed on its first run, with `msiexec` complaining that the path it was given
was not valid. The path it was given was this:

```
msiexec.exe /x "15:01:27  the installed package is D:\Coding\UA Retail Store\…"
```

The timestamp and the sentence are the harness's own log line. The function that finds the
installed package **wrote that line to the output stream and returned the path**, so the caller
received two answers, joined them into one string, and passed the log line to `msiexec` as part of
the path.

The fix is one line: a function that logs must not also write to the output stream, so it writes to
the console and to the run's own log instead. It is worth writing down because it is invisible: the
script's own output looked perfectly normal, and the failure appeared three lines later in a
programme that was given nonsense.

### The bytes of the database file cannot say whether the data changed

The claim the cycle has to prove is "the shop's data came through the upgrade untouched", and the
first version of the harness measured it the obvious way: the SHA-256 of the shop's database file
before and after.

That measurement is meaningless. The shop runs SQLite in WAL mode, so the database file changes
when the programme closes — the write-ahead log is checkpointed into it — even when nothing was
written to it. A file that hashes differently before and after says nothing at all about the data.

So the data is measured by **what the database holds**, not by what the file looks like.
[`tools/ShopDigest`](../../tools/ShopDigest/ShopDigest.csproj) prints every table, its schema and
its rows in a fixed order, and hashes that text; the harness compares the hash before the upgrade
with the hash after it, table by table, so that a difference is *named* rather than merely
detected. The tool was checked against the same database before and after a checkpoint: the file
grew from 4 KB to 442 KB and the hash did not move.

One table is allowed to change, and it is named: `BackupLogs`, the log of the copies the scheduler
takes. The scheduler takes a copy of the shop while the programme is running, and a line in that
log is the programme doing its own work, not the upgrade touching anything.

### The download was interrupted, and the shop carried on

One run of the cycle did not finish. The download of the 56 MB installer was cut off after about
eleven seconds, and the programme's own log said: *the update could not be installed: the download
was interrupted*. The partial file was discarded, the installed programme was left exactly as it
was — still 1.0.0 — and the shop carried on.

That is the behaviour the release notes promise, and it was observed rather than assumed. To find
out whether the programme's ten-second HTTP timeout was cutting the download short, a probe was
run against the programme's own update service, with the programme's own client: it downloaded the
same file, whole, in **16.4 seconds**. The interruption was this machine's link to GitHub, not the
programme's timeout, and the next run of the cycle went through.

### Two sentences in the shop's notes were not true

The notes v1.0.1 was published with said that the downloaded files are "kept in a temporary folder
and deleted afterwards", and that "the database's SHA-256 was the same before and after".

Neither is true. The installer is **left** in `%TEMP%\StoreFlow` so that it can run from there,
and the byte-hash of a database file says nothing about the data, because the file moves when the
programme closes. The second sentence was wrong in exactly the way this phase spent most of its
time on: it claimed a proof that the measurement could not give.

Both sentences have been corrected in [`docs/RELEASE-NOTES.md`](../RELEASE-NOTES.md), and the
**published release notes have been corrected to match**, so that a shop reading them is told what
actually happens. The published body was edited in place; nothing else about the release changed,
and the assets, the tag and the hash are as they were.

### The installed copy on the laptop was not the released v1.0.0

The copy installed on the development laptop reported **1.0.1**, and its assembly version carried
the commit hash of `0cb5663` — the commit the **v1.0.0** tag names. The local installer had been
built with `-Version 1.0.1` from the v1.0.0 source, so the laptop was running a build that no
release had ever published, under a version number that the release it should have been had not
used.

That matters for the update cycle: the copy the cycle starts from has to be the **published**
v1.0.0, or the cycle proves nothing. The script now installs the published old release itself,
and it verifies the installer against the checksum published beside it before it installs anything.

Windows Installer refuses to go backwards — the product carries `DowngradeErrorMessage` — so the
newer copy that was on the laptop is removed first, and the shop's data is measured across that
removal as well. The data folder is not part of the installation, so it survives.

### The picture destinations were all over the place

Phase 8 listed them: four harnesses wrote into the repository's phase folders, one wrote into its
temporary run folder, and six pointed at throwaway session folders that belong to one work
session and are gone with it. They all write into the repository now, and the phase-0 pictures —
the wizard, the sign-in and the shell — have a folder of their own with a note saying what each
one shows.

### The shop could not be migrated at all, and the wizard could not let go

After the release cycle had been proved, the shop on this machine would not open. It failed with
`duplicate column name: RefundTax`, the recovery wizard came up, and the wizard offered one
thing only: **Close**. Closing it ended the programme with *"The shop's data cannot be read and no
backup was restored, so StoreFlow cannot start."* The owner was stranded, and there was no way
out of the screen.

The shop's database had been left **half-migrated** by an interrupted migration. Four of the five
migrations were recorded, two temporary tables were left behind, and four columns, two indexes and
eight guard triggers had been applied outside the migration's transaction. The next start tried to
apply the fifth migration again and stopped at the first column it found already there.

Behind that lay a second fault, and it is the one that had been lying in the code since Phase 0.
**The migration that rebuilds a table cannot run while the guard triggers are in place.** SQLite
will not drop a table that a trigger still refers to, and a migration that changes the shape of a
table rebuilds it: it makes a new table, copies the rows, drops the old one and renames the new
one. A shop that had run the programme at least once therefore could not be migrated at all, and
the failure was `no such table: main.SalesInvoices`.

The fix is to take the guards down before the migrations and put them back after them
(`SchemaTriggers.DropScript`, applied by `DatabaseInitializer`), and it is proven by two tests that
fail against the old code with exactly that error.

The half-migrated shop was repaired by **rolling the interrupted migration back** — the temporary
tables, the two indexes, the four columns and the eight triggers were dropped, after a consistent
copy had been taken with SQLite's own `VACUUM INTO` — so that the migration ran cleanly the next
time. The shop had no sales in it, so nothing was lost, and the owner account survived.

**What this means for the released v1.0.1.** Both the `v1.0.0` and the `v1.0.1` tags already carry
all five migrations, so a shop that has only ever run a released version is never behind and never
meets the fault — which is why the update cycle proved the upgrade works end to end. The fault
needs a shop whose database is **behind** the migrations, and that is a shop that ran an earlier
build. The fix removes the whole class of failure, and it will be in the next release; the copy
that is installed on this machine is the released v1.0.1 and does not carry it.

### The fix for that brought up a second landmine of the same shape

Taking the guards down opens a connection before the migration runs — and that connection brought
up a fault that had been waiting in the framework's own path since Phase 0.

**The framework checks whether the database exists before it migrates it, and it opens that check
read-only**, so the check does not bring a database into being as a side effect. Our pragmas were
applied to every connection, and one of them — `PRAGMA journal_mode = WAL` — is not a setting of
the connection but a change **to the file**. On a connection that cannot write, that change fails
with *attempt to write a readonly database*, and the failure comes out of the open and stops the
shop from starting.

The whole suite fell over: **200 failures**, all of them with the same error, from every test that
opens a database. The rules are worth writing down, because they are the same rules as the ones
above:

- **A connection that cannot write must never be asked to change the file.** The journal mode is
  a property of the file; a read-only connection simply cannot move it, and nothing is lost when
  the request is refused.
- **The settings that belong to the connection** — foreign keys on, the busy timeout, the full
  synchronous journal, the temporary store in memory — are applied to every connection, read-only
  or not, because they cost nothing and change nothing outside the connection.

The pragmas are now split in two: the connection's own settings are always applied, and the file's
journal mode is asked for in a way that tolerates a connection that cannot write. The busy timeout
also moved to the front, because a connection that finds the database locked should wait for the
lock before anything else is asked of the database.

Five tests cover this: the shop that has already run the programme can still be migrated; the
guards are back afterwards; a shop whose database file exists but is empty still opens; a
connection that cannot write is not asked to change the file; and a connection that can write puts
the shop in the write-ahead log.

## The three debts, decided

### 1. The archive encryption — designed, and deliberately not built

[ADR-0005](../decisions/ADR-0005-archive-encryption.md) is the full design. In short: the `.sfb`
container stays a zip, but every part inside it is encrypted with AES-256-GCM under a key derived
with PBKDF2-HMAC-SHA256 at 600,000 iterations, each part's path is bound into the encryption as
associated data so a part cannot be moved inside the archive, and the manifest is encrypted too.
An unattended scheduled backup uses a key protected by the Windows DPAPI for the current user,
and **writes nothing at all when that key cannot be read**. A format-1 archive still restores.

It is not in v1.0.1 because it is a change to the file format of the backup and the restore path,
in the first release after v1.0.0, and a shop that cannot read its own backup has no shop. The
ADR names the eight tests that must land with it. Until it does, the shop's release notes say
plainly: the copies are in the clear, keep them on a protected disk and use full-disk encryption.

### 2. The accessible names — closed, to zero

All 88 controls the guard finds now have a name a screen reader can read. The guard's threshold
is zero and its table pins the file, the control and the binding for every one of them, so a name
that is taken away again fails the build. Five of the 88 needed a new string in both languages.

### 3. The audit-write gap — closed, with two named exceptions

Thirty-three commands, and a new write point with seven tests that prove the property rather
than assert it. The two exceptions are above, and they are in the shop's release notes.

## What the shop is told

The [release notes](../RELEASE-NOTES.md) for v1.0.1 are the notes the release itself carries.
They say, plainly:

- **What is checked:** the public list of StoreFlow Desktop releases, and nothing else. The
  request is anonymous and carries nothing about the shop.
- **When:** once, in the background, shortly after the programme opens. The programme never waits
  for it.
- **What is downloaded:** nothing, until the shop presses the button. Then the installer and the
  `.sha256` file published beside it, into a temporary folder that is deleted afterwards.
- **That the shop's data is never touched:** the updater never writes inside the shop's folder,
  and the upgrade was measured on a real shop: the database's SHA-256 was the same before and
  after.
- **How to turn it off:** nothing is ever installed without the shop pressing the button; to stop
  even the single background check, block `api.github.com`, and the programme carries on exactly
  as before, because a check that cannot reach the feed is not an error.

They also say that the shop **does not need to hold off**: the restore-confirmation question the
v1.0.0 notes raised has been answered, and the answer was that the fault was in the test.

**One honest limit, unchanged from v1.0.0:** the installer is not signed with a publisher's
certificate. The SHA-256 proves the file is the one that was published and has not changed since;
it does not prove who published it.

**A decision this phase took, and the reason.** A switch that turns the update check off was
considered and not built. It would mean a new setting in the shop's settings, a new control on a
screen that has never had one, two new strings, and a rule for shops whose settings were written
before the setting existed — in a patch release, for a check that carries nothing about the shop.
The notes answer the question honestly instead, and the decision is recorded here so that it can
be revisited with the next version.

## Housekeeping

- **The stray `0.26.0`** in the repository root is covered by `.gitignore` and out of the index.
  The local file-safety policy on this machine refuses deletions *and* refuses moves inside the
  workspace, so it could not be moved into `build/scratch/` like the others; the file itself is
  empty and can be deleted by hand.
- **The picture destinations** are unified, as described above.
- **The release and its commit are recorded** in [`docs/RELEASES.md`](../RELEASES.md), so that
  "the tag is behind `main`" is a fact in the repository rather than a surprise. The tag names the
  commit that was built; `main` may carry documentation and pipeline changes that were never part of
  the shipped binary. It does here: the tag `v1.0.1` names `ab02c6e`, and the commits after it
  carry this report and the tools that proved the cycle.
- **The v1.0.0 release notes are archived** in
  [`docs/RELEASE-NOTES-v1.0.0.md`](../RELEASE-NOTES-v1.0.0.md); the current notes are the ones
  the release workflow publishes.

## Definition of Done — PRD §12.1

The verdict describes the evidence, not a manual sign-off. The UAT plan requires a human to
execute each case, repeat user-facing cases in both languages and sign it. None has been signed
off in the plan.

| PRD line | Verdict | Evidence and limit |
|---|---|---|
| Every P0 feature has working implementations and passing critical acceptance tests. | **PARTIAL (implementations and automated counterparts; manual critical acceptance remains)** | All twenty steps are reachable and the suite and smoke runs are green. Critical manual cases have not been signed off; shop hardware and physical conditions remain outstanding. |
| No open critical defects in calculations, stock posting, returns, printing, or recovery. | **PARTIAL (automated checks green; physical printing and manual acceptance outstanding)** | The first-run blocker and the restore-confirmation flake are both fixed and explained. Money, transaction, return, recovery and audit-atomicity tests pass; the shop's printers and physical failure cases have not been accepted. |
| Transaction, reconciliation, and backup/restore tests pass. | **PROVED (automated transaction, reconciliation and backup/restore tests)** | `WriteFaultMatrixTests.cs`, `CrashRecoveryTests.cs`, `LedgerReconciliationTests.cs` and `BackupTests.cs` passed in the reported suite. The reconciliation test runs at least 1,000 mixed operations; physical power loss and a second PC are separate checks. |
| Tested on the intended Windows machine and actual shop scanner/printer hardware. | **NEEDS-HARDWARE (shop till, scanner, 80 mm and A4 printers, second PC and physical conditions)** | The development laptop is not the till. The shop must lend its scanner, both printers, a second clean PC and backup medium, permit an unplugged network cable and printer, and permit killing the application during sale posting. A process kill is not a physical power cut. |
| Installer, first-run setup, admin creation, and backup configuration are documented. | **PROVED (documentation and installed release)** | The user guide documents these steps; the installed wizard was reached and setup is covered by `SetupTests.cs` and `XamlLoadingTests.cs`. v1.0.1 was installed over v1.0.0 **by the programme itself**, in a full release cycle, and the backup harness was run three times against that installed copy. Documentation is not shop sign-off. |
| Short user guide and local diagnostic/recovery procedure exist. | **PROVED (guide and recovery procedure exist)** | [User guide](../USER-GUIDE.md) and [recovery runbook](../RECOVERY-RUNBOOK.md). |

## PRD §11 — twenty UAT rows

`PROVED` in this table means the named automated counterpart is green and the step is walkable; it
does **not** mean the manual case has been executed or signed off. `PARTIAL` names a remaining
unproved part. `NEEDS-HARDWARE` names the shop device or physical condition still required. Test
files below are under `tests/StoreFlow.Infrastructure.Tests/` unless another project is named;
harnesses are under `tools/`. All reported automated runs were on the development laptop, not the
shop till.

| ID and PRD scenario | Verdict | Automated evidence and remaining work |
|---|---|---|
| UAT-01 Create product with new barcode | **NEEDS-HARDWARE (shop USB keyboard-wedge scanner)** | `CatalogTests.cs` creation and name/code/barcode search; `smoke-catalog.ps1` and simulated POS input. Physical scanner lookup remains. |
| UAT-02 Create another product with same barcode | **PROVED (automated duplicate rejection and walkable step)** | `CatalogTests.cs` barcode refusal and `smoke-catalog.ps1`; manual sign-off remains. |
| UAT-03 Receive 10 units into zero stock | **PROVED (automated receipt and walkable step)** | `InventoryTests.cs` posted purchase increases stock exactly once; `smoke-inventory.ps1`. Manual sign-off remains. |
| UAT-04 Sell 3 units by scanning | **NEEDS-HARDWARE (shop scanner and 80 mm thermal printer)** | `SaleTests.cs` posting and stock movement; `smoke-pos.ps1` uses simulated input and preview. Physical scanning and printed receipt remain. |
| UAT-05 Scan identical barcode twice | **NEEDS-HARDWARE (shop USB scanner and its suffix and timing)** | `smoke-pos.ps1` exercises repeated simulated input; `StoreFlow.Application.Tests/ScanBufferTests.cs` covers scan buffering. Physical repeat scans remain. |
| UAT-06 Attempt sale of 9 units with 7 available | **PROVED (automated insufficient-stock rejection and walkable step)** | `SaleTests.cs` refuses excess and leaves stock alone; `smoke-pos.ps1`. Manual sign-off remains. |
| UAT-07 Double-click Confirm Sale | **PARTIAL (idempotent checkout proved; actual double-click timing not signed off)** | `SaleTests.cs` posts the same checkout twice as one sale; `smoke-pos.ps1` drives checkout. The specified human double-click still needs execution. |
| UAT-08 Disconnect printer after sale posts | **NEEDS-HARDWARE (shop 80 mm thermal and A4 printers and printer offline condition)** | `SaleTests.cs` covers posting and idempotency; `smoke-sales.ps1` drives sales and receipt flow. Physical offline error, reprint, COPY and both paper outputs remain. |
| UAT-09 Force app shutdown during posting | **NEEDS-HARDWARE (permission to kill the application during a real sale on the shop PC)** | `CrashRecoveryTests.cs` kills a separate transaction probe before and after commit; `WriteFaultMatrixTests.cs` tests handler write failures, including a failure writing the audit entry inside the command's own transaction. The real posting interruption and physical power loss are not proved by the probe. |
| UAT-10 Return 1 of 3 sold units | **PROVED (automated linked resellable return and walkable step)** | `SalesTests.cs` covers linked returns and restock; `smoke-sales.ps1`. Manual sign-off remains. |
| UAT-11 Attempt return of 4 from invoice of 3 | **PROVED (automated excess-return refusal and walkable step)** | `SalesTests.cs` `Returning_more_than_was_sold_is_refused`; `smoke-sales.ps1`. Manual sign-off remains. |
| UAT-12 Change product price after sale | **PARTIAL (old invoice price protected; complete manual profit check remains)** | `SalesTests.cs` tests that a later price does not change the posted invoice; `smoke-sales.ps1` drives history and `ReportingTests.cs` covers report figures. The exact price-change and profit walkthrough remains unsigned. |
| UAT-13 Cashier attempts unauthorized discount | **PROVED (automated approval gate and walkable step)** | `ApprovalTests.cs` holds an over-limit discount for a manager without an invoice; `smoke-reports.ps1` drives approvals. Manual sign-off remains. |
| UAT-14 Stock count differs by 2, then approve | **PROVED (automated count approval and walkable step)** | `InventoryTests.cs` tests no stock change until approval and posting; `smoke-inventory.ps1`. Manual sign-off remains. |
| UAT-15 Export report containing Arabic names | **PARTIAL (CSV encoding tested; Excel open remains manual)** | `StoreFlow.Application.Tests/CsvWriterTests.cs` tests unchanged text and BOM; `smoke-reports.ps1` drives reports. Opening the exported file in Excel has not been signed off. |
| UAT-16 Back up then restore on clean test PC | **NEEDS-HARDWARE (second clean Windows PC and external backup destination)** | `BackupTests.cs` and `smoke-backup.ps1` restore into a separate data profile and compare figures on this laptop. The harness was also run three times against the **installed v1.0.1**, 39 checks and 0 failures on each run. A different machine and external medium or real S3 destination remain. |
| UAT-17 Disconnect internet and make sale | **NEEDS-HARDWARE (unplugged network cable and local 80 mm printer)** | `SalePathNetworkTests.cs` and `StoreFlow.Desktop.Tests/TillNetworkTests.cs` guard the network-free sale path; `smoke-pos.ps1` drives a sale. The updater's quiet failures are covered against a local fake feed. An unplugged cable and physical receipt remain. |
| UAT-18 Deactivate historically sold product | **PARTIAL (deactivation and history covered separately; combined manual flow remains)** | `CatalogTests.cs` retains deactivated products; `SalesTests.cs` protects posted invoice lines; `smoke-catalog.ps1` and `smoke-sales.ps1` drive the screens. The exact deactivate, attempt sale and reopen sequence remains unsigned. |
| UAT-19 Receive higher-cost goods under moving average | **PROVED (automated valuation and cost snapshot and walkable step)** | `StoreFlow.Domain.Tests/MovingAverageCostTests.cs`, `InventoryTests.cs` valuation and `SaleTests.cs` line snapshot; `smoke-inventory.ps1` and `smoke-pos.ps1`. Manual sign-off remains. |
| UAT-20 Return damaged item | **PROVED (automated damaged return and walkable step)** | `SalesTests.cs` `A_damaged_return_is_not_put_back_on_the_shelf`; `smoke-sales.ps1`. Manual sign-off remains. |

The PRD also requires at least 1,000 mixed stock operations with zero balance-to-ledger
discrepancy. `LedgerReconciliationTests.cs` exercises the real handlers and checks sellable and
damaged balances against movements; it is part of the reported green suite.

## What this phase does not prove

- **Nothing here is the shop's hardware.** The development laptop is not the till. The shop's
  scanner, its 80 mm and A4 printers, a second clean PC, an unplugged network cable, a printer
  taken offline and permission to kill the application during a real sale all remain.
- **The manual UAT plan is still unsigned.** Every `PROVED` row above means the automated
  counterpart is green and the step is walkable. It does not mean a human has executed and
  signed the case.
- **The archive encryption is not built.** ADR-0005 is a design, and the shop's data is still in
  the clear inside a `.sfb`.
- **The installer is not signed by a publisher.** The SHA-256 proves the file has not changed; it
  does not prove who published it.
- **The update was proved against GitHub Releases, not against every mirror.** The feed was
  reachable and the checksum matched on this machine.
- **An interrupted migration still cannot heal itself.** SQLite's own table rebuild runs outside
  the migration's transaction, so a programme that is killed halfway through leaves columns,
  indexes and temporary tables behind, and the next start stops on the first one it finds. The fix
  in this phase removes the *trigger* obstacle — a shop with its guards in place can now be
  migrated — but a genuinely half-applied migration still needs the rollback that was done by hand
  here, or the copy the programme takes before every migration.
- **The recovery wizard still has no way out.** When the shop's data cannot be prepared and no
  backup is restored, the wizard offers only *Close*, and the programme then refuses to start. A
  way to start again with a fresh shop, keeping the old folder aside, is a **new screen** and has
  not been built.

## Where the programme stands

The programme is at the **sign-off gate**. Everything the handoff asked for has been done, and
nothing beyond it has been started: the shop's own acceptance is the next thing, and it is the
shop's to walk.

- The release that carries this work is **v1.0.1**, and the commit it was built from is recorded
  in [`docs/RELEASES.md`](../RELEASES.md).
- The [release notes](../RELEASE-NOTES.md) are the shop's notes and say plainly what has and has
  not been proved.
- The [updater's own notes](../UPDATER.md) describe what it checks, what it downloads and what it
  refuses to do.
