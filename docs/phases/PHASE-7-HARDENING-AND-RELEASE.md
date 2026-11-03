# Phase 7 — Hardening and release

This phase took a working shop and made it ready to be handed to one: proved it cannot be half
written, measured it against the volume a busy shop reaches, gave it an installer, wrote the books
the shop needs, and went through every screen with a screen reader's eyes on.

It did not add a feature to the shop. Nothing in the interface is new. What is new is that the
shop can be trusted with a till, and that somebody who has never seen the code can install it, use
it and recover it.

---

## What was built

### Every write has a named place to fail

PRD NFR-05 says that no successfully posted sale may lack its stock and payment records, or have
stock and payment records without the sale. Until this phase that could only be argued from
reading the code.

There is now a seam for it. `IWriteFaultInjector` is a port in the application layer whose
production implementation does nothing. `WritePoint` is the catalogue of every place the
application writes — one name per write rather than one name per table:

```csharp
await context.BeforeWriteAsync(WritePoint.PostSaleInvoice, cancellationToken);
...
await context.BeforeCommitAsync(WritePoint.PostSaleCommit, cancellationToken);
```

Each of the fifty-three write points in the product announces itself. A transactional command
announces each of its writes *and* the commit that makes them permanent, so a test can aim a failure
at any one of them:

| A sale | A return | A stock count |
|---|---|---|
| `PostSaleInvoice` | `PostReturnDocuments` | `SaveStockCount` |
| `PostSaleStockMovement` | `PostReturnRestock` | `SubmitStockCount` |
| `PostSaleCommit` | `PostReturnCommit` | `ApproveStockCount` |
| | | `PostStockCountMovements` |
| | | `PostStockCountCommit` |
| | | `CancelStockCount` |

The hook sits in the `StoreFlowDbContext`, which every write reaches through the one context
factory. That means the seam covers the audit writer and the backup runner as well as the
command handlers, without a single extra line in those two. The six saves in
`DatabaseInitializer` are start-up seeding rather than product writes and are left alone.

Failing at the first write of a sale and failing at the commit of the same sale are different
problems. The first proves the sale was never written. The second proves that the invoice, the
lines, the payment and the stock movements, which had all been written inside the transaction,
were thrown away together.

### The matrix: a failure aimed at every single write

A seam nobody tests is a comment. `WriteFaultMatrixTests` is the proof: one scenario for every
write point in the catalogue, fifty-three of them, each one arming the injector at exactly one
point and then checking what the ledger is left holding.

The scenarios do not check a row count. Each one photographs **every mapped table in the database**
before the command runs and again afterwards, as complete sorted rows, and requires them to be
identical. A count would not notice a quantity that changed; a photograph of every value does.

The matrix also proves it reached the point it aimed at. Each scenario asserts that the injector was
asked to fail exactly once, and at the point it named. A scenario that never reached its write
point would otherwise pass by doing nothing.

One guard ties it shut. A test reflects over the `WritePoint` catalogue and compares it with the
points the matrix actually exercises, and requires the two sets to be equal. A new write point that
nobody fails, or a catalogue entry that names a moment the code does not have, fails the build.

Writing the matrix found three catalogue entries that were fiction, and the phase fixed them rather
than deleting them:

- `SaveStoreSettings` and a new `SaveCloudAddress` both existed, but the settings store had one
  private method behind all three kinds of setting and labelled every one of them
  `SaveBackupSchedule`. The method now says which write it is.
- `PostStockAdjustmentCommit` and `ApproveStockAdjustmentCommit` both existed, but the adjustment
  and approval flows never opened a transaction, so there was no commit to fail. Both flows now
  open one, hook the commit, and roll back on failure, like every other posting flow.

The matrix found one thing it could not fix, and says so in the test rather than hiding it. The
audit trail is written by its own writer, through its own context, *after* the business transaction
has committed. A failure aimed at `WriteAuditEntry` therefore leaves a sale that is complete and
committed, with no audit row beside it. The matrix asserts exactly that, and the scenario also
shows the way out: replaying the command writes the audit row and changes nothing else, so no sale
is ever un-audited in practice, only briefly un-audited in the moment between the commit and the
audit row.

### The programme restarts itself after a restore

This was a Phase 6 papercut with teeth.

A restore replaces the database file on disk. The running programme was still holding the old
one. From that moment on, every sale the shop took would have been written to a file that was no
longer the shop's ledger — and the ledger the shop was looking at would have been the restored
one. The two would disagree, and nothing would say so.

The wizards now restart the programme when the restore is done, and say so in the language the
shop is using:

> The shop has been restored. The program will restart now.

### The power goes out in the middle of a sale

PRD NFR-07 says that a forced shutdown must leave the shop with committed-or-rolled-back state. The
database runs in WAL mode with `synchronous = FULL`, which is what makes that true, but a pragma is
a promise and not a proof.

`tests/StoreFlow.CrashProbe` is a small console programme that opens the shop's database with the
application's own pragmas, begins a transaction, writes a sale — invoice, line, payment and stock
movement — and then stops, leaving the transaction open. `CrashRecoveryTests` starts it, waits for
it to say it has finished writing, and then kills it. Not a polite shutdown: `Process.Kill` with the
whole tree.

Two runs, and they are the two halves of the promise:

- Killed **before** the commit, the shop is left with no invoice, no line, no payment and no stock
  movement, and the product still has the stock it started with. The database then opens again and
  the application initialises itself against it — the dirty start.
- Killed **after** the commit, the whole sale is there: invoice, line, payment, stock movement, and
  the stock balance already reduced. A committed sale survives a forced shutdown.

The probe writes the rows directly rather than going through the posting handler, because the
posting handler owns its transaction internally and cannot be paused at the point before the commit.
That is a fair trade: what is being proved is SQLite's recovery of an interrupted transaction, not
the handler's behaviour, and the handler's behaviour is what the write-fault matrix proves.

This is a forced process termination, not a simulated power cut. It exercises the same recovery path
— an interrupted WAL transaction — but a machine that loses power can also lose the page cache, and
that has to be taken on trust until someone cuts the power for real.

### There is an installer

`installer\build-installer.ps1` builds a per-user MSI. `installer\verify-installer.ps1` installs
it, puts a mark in the shop's data folder, installs the next version over the top, and then
removes the programme, checking the mark at every step.

A per-user install was chosen on purpose. A shop does not have a Windows administrator account on
the till, and an installer that needs one is an installer that does not get used.

The important property is that the shop's data is **not** in the installation. It lives in
`%APPDATA%\StoreFlow`, and the installer never writes there and never deletes it. An upgrade
keeps the ledger. An uninstall keeps it. Only the person who owns the shop decides when that
data goes, and the user guide says how.

### The screen a screen reader sees

The application had **two** `AutomationProperties.Name` declarations in the whole of it, both on
the login screen. An automation identifier is how the smoke scripts find a control; it is not
what a screen reader reads out. A control with only an identifier announces itself as an unnamed
edit box.

The fields on the first-run wizard and on the settings screen now carry their names, and the
settings screen — the only view in the application with no automation properties at all — carries
identifiers too. The point-of-sale screen, where the cashier spends the day, has been named in the
same way.

No dialog could be closed with the Escape key. Escape now runs the same command the Cancel
button runs, on all thirteen dialogs. The key is wired to the command rather than to WPF's own
`IsCancel`, because `IsCancel` closes the window as well as raising the click, and these windows
set their `DialogResult` on the way out.

`ThemeContrastValidator` was checking sixteen role pairs and now checks thirty-five: the states
on the raised and sunken surfaces, the selection colours, the subtle state backgrounds, the
overlay, and the accent against the canvas. All three built-in themes still pass.

The row and control heights now follow the font scale. The scale used to multiply the font sizes
only, so raising it grew the text and left every box the same size — which is how Arabic gets cut
off at the bottom of the field it sits in.

### The books the shop needs

`docs/USER-GUIDE.md` — the shop's own book, in English and in Arabic: installing it, the first
run, every screen, the daily routine, the keyboard, and where the data is kept.

`docs/RECOVERY-RUNBOOK.md` — what to do when the till will not start, when the database will not
open, when a sale was interrupted, when the numbers do not add up, and how to move the shop to
another computer. In English and in Arabic.

`docs/ACCESSIBILITY-REVIEW.md` — what was checked, what was found, what was fixed, and the
ninety-six controls that still have no accessible name, listed screen by screen.

### A harness that fills a shop and measures it

`tools\performance-harness.ps1` builds a shop with ten thousand products and a hundred thousand
invoices and measures the queries the screens run against it. The measurements are of the
application's own query handlers, on the application's own database, so what is measured is the
work the shop does rather than a lookalike.

The harness earned its place by finding a fault that a shop with a thousand sales would never
have seen. EF Core turns `someList.Contains(x)` into `WHERE x IN (@p0, @p1, ...)` — one SQL
parameter per element — and SQLite refuses a statement with more than 32,766 parameters. The sales
register asked for the refunds against every invoice it had just read, so at a hundred thousand
invoices it sent a hundred thousand parameters and the screen failed outright with *too many SQL
variables*. The same shape was in the sales-by-product report, and twice in the inventory queries,
where the list was of products rather than invoices and would have broken at around thirty-three
thousand products.

None of those queries now carry a parameter per row. The register finds its refunds through a
subquery over the same filtered invoices; the report restricts its lines the same way; the
inventory queries join the balances to the products they belong to. The shop's data is the same
data, the numbers are the same numbers, and the parameter count no longer depends on how busy the
shop is.

The regression guard is a parameter budget rather than a row count, because the failure only
appears at a size no test can afford to build. A command interceptor records the number of
parameters each statement carries, and the tests require the sales register, the sales-by-product
report and the stock balances to stay in the low tens however many rows are behind them. A list of
ids creeping back into a query fails the build, not the shop.

---

## The rules this phase decided

### A write point is a moment in a command, not a table

Naming the writes by the table they touch would have made the catalogue useless: a sale writes
the invoice, the lines, the payment and the movements, and a failure in any one of them is a
different failure. The catalogue therefore names the *moments* — the invoice, the movements, the
commit — and the matrix has one row for each of them.

### The failure is injected before the write, not instead of it

The injector throws immediately before the `SaveChangesAsync`, so the changes are staged in the
change tracker and never reach the database. That models the failure a shop actually meets: the
write is refused. The commit hook models the other one: the work was done and then thrown away.
Both are needed, because the second is the one that a transaction has to get right.

### The seam lives in the context, not in the handlers

Forty-nine handlers would have meant forty-nine constructor parameters and forty-nine changes to
the tests that build them. The context is the one thing every write already goes through, so the
seam went there. A handler that wants to be covered says which write it is making; a handler that
does not cannot be reached by a write at all.

### The catalogue and the matrix have to agree

A catalogue is only worth what it covers, so the coverage is checked rather than hoped for. A test
reflects over every write point and compares the set with the points the matrix exercises, and the
two sets have to be equal. When the matrix was first written it failed that test three times, and
the three failures were real: two write points named a commit that the code never made, and one
write point was being used for three different writes. The rule is that a write point names a
moment the code really has, and the guard says so out loud.

### A query may not carry a parameter per row

SQLite allows 32,766 parameters in a statement. EF Core turns a list of ids into exactly one
parameter per id, so a list of ids is a query that works until the shop is busy. No query in the
application may depend on the size of the shop to be executable, and the parameter budget in the
tests is what keeps that true.

### The installer never touches the shop's data

The data folder is deliberately absent from the installer's file list. This is the difference
between an upgrade that is safe and one that is not.

### The product identity comes from the version, not from the contents

WiX works a product identity out from the contents of the package by default. That means
rebuilding the same version produces a different product, and a shop that rebuilds ends up with
two copies of the same version installed, one of which Windows Installer will not let it upgrade.
The build script works the identity out from the version instead, so the same version is always
the same product and a new version is always a new product.

### The accessibility work that was not done is written down

Ninety-six controls still have an identifier and no accessible name. They are listed one by one
in the accessibility review, screen by screen, so that the next person does not have to find them
again. Writing them down was not a decision to skip them; it was a decision to say which ones
they are.

---

## How it was verified

### The build

The whole solution builds with **zero warnings**. `TreatWarningsAsErrors` is on for every project
and the analyzer set is the strictest one the SDK offers, so a warning is a build failure.

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### The suite, five times over

Before anything was changed, the whole suite was run five times in a row, and the two projects
that had been flaky before were run five times each on their own.

| Run | Result |
|---|---|
| Whole solution, run 1 | 424 passed, 0 failed |
| Whole solution, run 2 | 424 passed, 0 failed |
| Whole solution, run 3 | 424 passed, 0 failed |
| Whole solution, run 4 | 424 passed, 0 failed |
| Whole solution, run 5 | 424 passed, 0 failed |
| `StoreFlow.Desktop.Tests` alone, five runs | 121 passed, 0 failed, every run |
| `StoreFlow.Infrastructure.Tests` alone, five runs | 141 passed, 0 failed, every run |

No run needed a retry, and no run was voided.

At the end of the phase the suite is larger, because the write-fault matrix added fifty-four tests
to the infrastructure project: 53 for the write points, one for the guard that keeps the catalogue
and the matrix in step.

| Project | Tests | Result |
|---|---|---|
| `StoreFlow.Domain.Tests` | 25 | passed, 0 failed |
| `StoreFlow.Application.Tests` | 137 | passed, 0 failed |
| `StoreFlow.Desktop.Tests` | 121 | passed, 0 failed |
| `StoreFlow.Infrastructure.Tests` | 195 | passed, 0 failed |
| **Total** | **478** | **0 failed** |

### The shop at full size

PRD NFR-04 asks for performance to be validated with at least ten thousand products and a hundred
thousand invoices. The harness builds exactly that — 10,000 products, 100,000 invoices, 199,999
invoice lines and 100,000 stock movements — and then runs the application's own query handlers
against it, three times each, and reports the median and the slowest run.

| What the shop does | Median | Slowest |
|---|---|---|
| the product list, the first page | 17 ms | 157 ms |
| the product list, a search | 15 ms | 37 ms |
| the product list, a deep page | 6 ms | 7 ms |
| the sales register, everything | 604 ms | 628 ms |
| the sales register, a quarter | 85 ms | 89 ms |
| the daily sales report | 155 ms | 192 ms |
| the sales by product report | 403 ms | 437 ms |
| the stock valuation report | 275 ms | 319 ms |

A scan has to add an item in under a second (NFR-03), and the fastest thing here is a page of
products in seventeen milliseconds. The slowest is the sales register reading the whole history,
at just over half a second for a hundred thousand invoices.

This is the number that matters most, though, because it is the number that was **not** there
before. The register used to fail outright at this size.

### The installer, installed and removed

`installer\verify-installer.ps1` installs the programme, marks the shop's data, installs the next
version over the top, and removes the programme. The mark has to survive both.

The check found two real faults, and both are fixed:

- The Arabic satellite assembly was not being installed. The folder that holds it was not nested
  under the installation folder, so Windows Installer had nowhere to put it.
- The product identity was being worked out from the contents of the package, so a rebuild was a
  different product and the upgrade was refused with *another version of this product is already
  installed*.

The installer also had a fault that looked like a slow machine and was not one. The verification
script passed the MSI's path to `msiexec` unquoted, and the path contains a space
(`D:\Coding\UA Retail Store\artifacts\installer\StoreFlow.msi`). `Start-Process -ArgumentList`
joins an array with spaces without quoting it, so `msiexec` received the path in pieces, decided it
had been asked to do nothing in particular, and opened its usage dialog. The dialog waited for an
answer that never came, and the script waited for the dialog.

That is what the "the Windows Installer service is slow" story was: a modal dialog, mistaken for a
sleeping service. The path is quoted now, and a run that does not finish says so and stops instead
of sitting there.

The upgrade failed for a third reason, and it was the subtlest of the three. The build wrote its
package into a shared folder and the script then took the first `.msi` it found there, so the
"upgrade" could easily have been the *first* package again — which is why the upgrade came back
with *another version of this product is already installed*. Each version now builds its own
package at its own path, the build says which file it produced and what its SHA-256 is, and the
verification installs exactly that file and checks the hash before every attempt, so a package
cannot change under the run.

The run that passes now says all of this out loud:

```
=== install 1.0.0 ===
msiexec exit: 0
the programme is installed
the Arabic strings are installed
installed version: 1.0.0

=== build and install 1.0.1 over the top ===
msiexec exit: 0
the shop data survived the upgrade
installed version: 1.0.1
the upgrade replaced the original product registration

=== uninstall ===
msiexec exit: 0
the programme folder was removed
the shop data survived the uninstall

=== the result ===
everything checked out
```

Three more things the check now proves that it did not before: that exactly one product is
registered after the install, that the upgrade *replaced* the original registration rather than
leaving the old one beside it, and that the shop's data file is byte-for-byte unchanged after both
the upgrade and the uninstall. The script also finds the installed product through the registry
rather than through `Win32_Product`, which used to repair every other MSI on the machine as a side
effect of asking what was installed.

### The screen a screen reader sees

Every one of the 389 lookups the eleven smoke scripts make resolves to a control that exists.
That was checked before anything was changed and it was checked again after.

### The application, driven

The application was driven through the sign-in, the first-run wizard and the point of sale.

---

## What is not in this phase

- **The backup archive is not encrypted.** PRD §8.5 asks for a passphrase-encrypted archive. The
  Phase 6 report deferred it here with the reason written down. It is the largest thing this
  phase did not finish, and it is in the README's scope statement as open rather than hidden.
- **Ninety-six controls have no accessible name.** The list is in the accessibility review.
- **The audit row for a sale is written after the sale is committed.** A sale is complete and
  permanent a moment before its audit row exists. Nothing is lost in practice — the matrix proves
  that replaying the command writes the row and changes nothing else — but there is a window, and
  closing it means moving the audit writer inside the caller's transaction.
- **A price override that asks a manager.** The till refuses the sale instead.
- **A blind stock count.** The count is approved, but the book quantity is on the screen.
- **More backup destinations, incremental backups, foreign currency, a customer on the sale.**

---

## The phase gate

The work stops here, at the gate, and nothing starts without an explicit go-ahead.
