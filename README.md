# StoreFlow Desktop

An offline-first Windows retail store management, inventory and point-of-sale application.

StoreFlow owns the shop's commercial truth: what exists, what is in stock, what was sold, what it cost, what
was returned, and who did it. It runs entirely on the shop's PC with no network dependency, keeps an
append-only stock ledger that reconciles to the piastre, and is **rebrandable by configuration** — product
name, logo, colours, receipt content, currency, tax and locale are data, so the same binary can be resold to
another shop without a recompile.

**Status: version 1.0.2 is published, and the release cadence is running.** The application builds, the database migrates, the theme switches
live, the AR/EN switch works, login is enforced, and the automated test suite is green. The
catalogue — products, categories, units and suppliers, with the product editor, the search, the
spreadsheet import and export, product pictures and the audit trail — is built and proved by
driving the real application. The **inventory** is built too: purchase receipts drafted and
posted, a reversing document instead of a deletion, opening stock, the stock ledger and the
balances it reconciles with, low-stock flags, a stock count that waits for an approval, manual
adjustments, and the valuation of the shelf. And now the **till**: a barcode scan becomes a line,
the money adds up on the screen, the customer is told what is coming back, the sale becomes an
invoice with a number on it that cannot be posted twice, a cart can be put aside without reserving
stock, and the receipt is drawn from the same layout the printer uses. The **sales register and the
returns** are built: an invoice history and detail, partial and full returns, resellable against
damaged, and a reversal that undoes an invoice instead of deleting it. The **reports** are built:
all eleven the PRD names, the money rules behind them, the dashboard, the approvals, the users and
the roles, and the audit trail read as a report. And the **backup**: the shop takes a copy of itself
on a schedule as a single `.sfb` file that proves its own integrity, the copy goes to a local folder
or to an S3-compatible object store, and the shop can be put back from it — on the same machine or
on another one — with the recovery wizard as the way back in for the day the database will not open
at all. Phase 7 added the write-fault matrix, crash probe, per-user installer, performance harness, accessibility pass, user guide and recovery runbook. Phase 8 drove the **installed** copy as well as the development build, found and fixed a first-run crash that had been hiding since Phase 7, and wrote the two gate tables. **Version 1.0.2 is published** with its installer and its SHA-256. It is the newest release a shop can download today. v1.0.2 is the repair release for the shop that could not be migrated: a shop behind the migrations can be brought up to date again, and the fault under that fix is gone too. The manual acceptance test still needs the shop's own hardware. The programme also updates itself from GitHub Releases: it notices a newer version in the background and asks the shopkeeper before anything is installed — see [`docs/UPDATER.md`](docs/UPDATER.md). Every push is now built and tested by CI, and every release is built by a workflow that refuses to publish unless the whole suite is green.

---

## Requirements

| | |
|---|---|
| **OS** | Windows 11 (Windows 10 also supported) |
| **Runtime for the built app** | None — publish self-contained `win-x64` |
| **SDK for development** | .NET SDK **10.0.402** (pinned by `global.json`) |
| **IDE** | Visual Studio 2022 17.14+ or Rider 2025.x, or the `dotnet` CLI alone |

### .NET SDK on this development machine

This machine had no .NET SDK installed, and `winget install Microsoft.DotNet.SDK.10` stalled in the WiX Burn
engine. The SDK was therefore installed as a **portable extraction** of `dotnet-sdk-10.0.402-win-x64.zip`:

```powershell
# One-time, already applied on this machine
$env:DOTNET_ROOT = 'D:\dotnet'
$env:PATH = 'D:\dotnet;' + $env:PATH
```

To make it permanent for your user account:

```powershell
[Environment]::SetEnvironmentVariable('DOTNET_ROOT', 'D:\dotnet', 'User')
$userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
[Environment]::SetEnvironmentVariable('PATH', 'D:\dotnet;' + $userPath, 'User')
```

**Always invoke the SDK by absolute path in scripts** — a bare `dotnet` will resolve to the stale
`C:\Program Files\dotnet` (9.0 runtime only) on this machine:

```powershell
& 'D:\dotnet\dotnet.exe' --version     # 10.0.402
```

On a machine with a normal .NET 10 installation, plain `dotnet` works and nothing above is needed.

---

## Build and test

```powershell
# The build script names the solution for you
& 'D:\Coding\UA Retail Store\tools\build.ps1' -Test

# Or the commands underneath it
& 'D:\dotnet\dotnet.exe' build 'D:\Coding\UA Retail Store\StoreFlow.sln' --nologo -v q
& 'D:\dotnet\dotnet.exe' test  'D:\Coding\UA Retail Store\StoreFlow.sln' --nologo -v q

# Run the desktop application
& 'D:\dotnet\dotnet.exe' run --project 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\StoreFlow.Desktop.csproj'
```

There is one solution file in the repository root, `StoreFlow.sln`. A stray `StoreFlow.slnx` from the
first `dotnet new sln` used to sit beside it and made a bare `dotnet build` ambiguous; it has been
moved to `build\legacy\`, and `tools\build.ps1` names the solution so that the question never comes
up again.

Current state of the suite — **544 tests, 0 failing**:

| Project | Tests | Covers |
|---|---|---|
| `StoreFlow.Domain.Tests` | 25 | money maths and rounding, moving weighted average, tax inclusive/exclusive, discounts, unit quantities |
| `StoreFlow.Application.Tests` | 158 | password hashing (Argon2id + PBKDF2), permission matrix, scan buffer, CSV reading and writing, WCAG theme contrast validation, the catalogue's validation rules and its spreadsheet mapper, the inventory's validation rules and its approval thresholds, **the till's pricing core**: the tax on a price that carries it and on one that does not, the whole-sale discount spread across the lines, a quantity the selling unit cannot express, and **the reports' money**: the net sales without the tax, the cost of the goods, what a return takes off the profit, and the CSV file that opens in Excel and reads back without a doubled byte-order mark, and **the backup's rules**: the manifest that describes every part, the part that was changed or added after the archive was written, the file name that says which copy it is, the retention that keeps seven dailies and four weeklies and never the newest, and the schedule that is due once its time has passed and catches up once after a missed day |
| `StoreFlow.Infrastructure.Tests` | 233 | real SQLite file: migrations, pragmas, immutability triggers, negative-stock trigger, integrity check, theme store round-trip, **the shop that has already run the programme still being migratable** — the guards taken down before a migration rebuilds a table under them and put back afterwards, a shop whose database file exists but is empty still opening, and a connection that cannot write not being asked to change the file, the catalogue handlers and their barcode triggers, the purchase and inventory handlers, the ledger reconciling to the balance, **the sale handler**: one transaction, one sale for one key, the stock off the shelf exactly once, the change the invoice and the payment row agree on, and a cart that is put aside reserving nothing, and **the reports**: every figure worked out by hand against real documents, the shelf valued at the moving average on a day before a receipt, the dashboard agreeing with the daily sales report, the returns report and the cashier's reconciliation, **the approvals**: a discount over the limit held for a manager with no invoice written, the sale going through only once the request is given, nobody approving their own request, and the request list the manager opens, and **the users**: the last administrator that cannot be deactivated, and **the backup**: the copy taken with SQLite's own online backup, the pictures in the archive, the copy that was changed afterwards refused, the restore refused without the permission and while a sale is being posted, a restore point that cannot be kept not stopping the restore, **the same shop restored into a different one**, the data a restore replaced kept recoverable, the copy written to the log with the checksum that proves it, the local folder target and the S3 target against a local S3 stub, the migration that nobody has reviewed taking a copy first, the scheduler catching up once, and **the shop's settings and details surviving a restart**, and **the audit trail**: a change and its entry written together, so a failure writing the entry leaves neither the change nor the entry behind, and **the updater's quiet failures**: a feed that cannot be reached, one that is rate limited, a release with no installer, a download that breaks in the middle, and a download that is cancelled |
| `StoreFlow.Desktop.Tests` | 128 | AR/EN resource parity, theme engine resource generation, value converters, the shell's contrast and the tables' headings, the localised messages of the import preview, how a code is laid out in an Arabic cell, the inventory screens' error messages and number reading, the editor flows against a real database, the column heading finding its words, the rules that keep a table readable — a cell's margin, the trimming of text that does not fit, a heading that wraps, a box with room for the line it holds, and **every column measured against the value it has to hold** — and **the till**: the money on the screen is the money the cart priced, the change from the cash that was typed, a scan that arrives during a lookup is not lost, every error code having a message of its own, and the receipt carrying the same numbers as the screen, and **the backup screen**: the size written the way a person reads it, the outcome and the destination written in the shop's language, and the two languages genuinely different |

### Useful commands

```powershell
# EF Core migrations (the local tool manifest pins dotnet-ef 10.0.13)
& 'D:\dotnet\dotnet.exe' ef migrations add <Name> --project 'src\StoreFlow.Infrastructure' --startup-project 'src\StoreFlow.Infrastructure'
& 'D:\dotnet\dotnet.exe' ef database update --project 'src\StoreFlow.Infrastructure' --startup-project 'src\StoreFlow.Infrastructure'

# Regenerate the DESIGN.md sidecar after editing the theme tokens
& '.\tools\generate-design-sidecar.ps1'
```

### Driving the real application

Eleven smoke scripts and the scan-latency harness drive the running desktop application through UI Automation. They provide evidence for the phase reports and use throwaway data folders.

```powershell
# The catalogue screens
& '.\tools\smoke-catalog.ps1'

# The purchase and inventory screens
& '.\tools\smoke-inventory.ps1'

# The till
& '.\tools\smoke-pos.ps1'

# The sales register, the return and the reversal
& '.\tools\smoke-sales.ps1'

# The reports, the dashboard, the approvals and the users
& '.\tools\smoke-reports.ps1'

# The backup, the restore and the recovery
& '.\tools\smoke-backup.ps1'

# How long the till takes to answer a scan (twenty scans, with the numbers)
& '.\tools\scan-latency.ps1' 20
```

The phase smoke scripts print their checks, and some of them also save the pictures they took into the
phase screens folders. The latency harness prints two sets of numbers — the
wait the till itself took, from the till's own log, and the whole round trip including the
automation watching the screen — and fails if the till's own 95th percentile is over fifty
milliseconds.

The till is scanned the way a scanner scans: the barcode goes into the field and Enter finishes it.
The harness measures the wait between the Enter and the line appearing on the cart.

**The application takes its own pictures.** A script starts the application with
`--capture-pipe <name>`, and then asks it — over that pipe — to draw one of its own windows. The
application writes the PNG itself (`ScreenCapture`, a `RenderTargetBitmap` of its own visual tree).
This is not a convenience: a picture taken from outside the process comes back mirrored for a
right-to-left window, and a mirrored picture of an Arabic screen is a lie about the screen. The
same switch is how a shopkeeper is asked for a picture of a problem.

```powershell
# Photograph the shell once, with the application started by hand
& '.\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe' --capture 'D:\temp\shell.png'
```

---

## Repository layout

```
D:\Coding\UA Retail Store\
├── StoreFlow.sln                  # the solution — pass this path explicitly
├── Directory.Build.props          # LangVersion 14, nullable, TreatWarningsAsErrors, analyzers
├── Directory.Packages.props       # Central Package Management, pinned versions
├── global.json                    # SDK 10.0.402
├── .editorconfig                  # naming conventions + analyzer severities
├── PRODUCT.md                     # the product record (Impeccable)
├── DESIGN.md                      # the design system (Impeccable), generated from the code
├── .impeccable/design.json        # machine-readable design tokens sidecar
├── docs\
│   ├── decisions\                 # ADR-0001 stack, ADR-0002 money, ADR-0003 valuation, ADR-0004 typography, ADR-0005 archive encryption
│   ├── samples\                   # reference samples (the WPF composite font format)
│   └── uat\UAT-PLAN.md            # the manual acceptance script
├── src\
│   ├── StoreFlow.Domain\          # money, quantity, tax, valuation, entities, permissions
│   ├── StoreFlow.Application\     # use cases, ports, authorisation, validation, theme schema
│   ├── StoreFlow.Infrastructure\  # EF Core + SQLite, theming, security, export, seed data
│   └── StoreFlow.Desktop\         # WPF views, view models, theme engine, localisation, embedded fonts
├── tests\                         # one xUnit project per layer
└── tools\                         # repository tooling (sidecar generator, icon generator, smoke scripts)
```

**Dependency rule** (enforced by project references): `Desktop → Application → Domain`,
`Infrastructure → Application`, and `Domain` references nothing at all.

---

## How it works

### The database

A single SQLite file under `%AppData%\StoreFlow\`. Every connection is opened through an interceptor that
applies:

```
foreign_keys = ON      mandatory
journal_mode = WAL
synchronous  = FULL    power-loss safety; WAL + FULL is the correct pairing for a ledger
busy_timeout = 5000    ms
temp_store  = MEMORY
```

Schema is managed by EF Core 10 migrations. On top of the migrated schema, the initializer installs SQLite
triggers that enforce the invariants the application layer also checks:

- stock on hand can never go negative (`AFTER UPDATE` on `StockBalances` → `RAISE(ABORT)`);
- posted `StockMovements`, `SalesInvoiceLines` and `PurchaseLines` are immutable — corrections happen only
  through reversing documents.

### Money

`decimal` in code, `INTEGER` minor units in the database, `MidpointRounding.AwayFromZero` applied exactly once
per line. All money arithmetic goes through `MoneyMath`; no other class adds or multiplies money directly.
See [ADR-0002](docs/decisions/ADR-0002-money-and-rounding.md).

### Stock valuation

Moving weighted average recomputed in **posting order**, never by business date, so backdating a document
cannot rewrite the cost of a posted sale. A resellable return is modelled as a receipt at the original
invoice-line cost and restores the prior average exactly when nothing else intervened. Damaged returns are
tracked in a separate bucket and never inflate sellable stock. See
[ADR-0003](docs/decisions/ADR-0003-valuation.md).

### Theming and rebranding

The rebrand surface is a versioned JSON file (`sftheme/1`) at `%AppData%\StoreFlow\themes\`. It carries the
product name (per locale), the logo path, every semantic colour role, typography, density, shape, locale,
currency and receipt content.

`ThemeEngine` turns that file into a WPF `ResourceDictionary` at runtime and installs it at the application
root. Every theme-driven style binds with **`DynamicResource`**, so switching a theme repaints every open
window instantly, with no restart and no lost window state. **No view contains a literal colour** — every
colour is a semantic role (`surface.canvas`, `text.primary`, `state.critical`).

Three presets ship: **Ink Light** (default), **Ink Dark**, and **Ink High Contrast**. The high-contrast preset
meets WCAG AAA for body text. The theme editor validates contrast live as you pick colours.

### Localisation

`ar` and `en` resource sets with runtime switching. Arabic is the default. Every control renders with
`TextOptions.TextFormattingMode=Display`, which is required for correct Arabic ligatures — `Ideal` breaks
them. The shell's `FlowDirection` flips at the root for Arabic, so right-to-left is a layout mode rather than a
cosmetic mirror. A unit test asserts that the two resource sets have identical key sets and identical
placeholder signatures, so a missing translation fails the build rather than shipping.

Strings are resolved against **the application's language explicitly**, not against whatever culture happens to
be attached to the calling thread. The ambient thread culture is fragile — it depends on where the call came
from and it can differ between the thread that builds a view model and the thread that renders it — and
relying on it is how a screen ends up in English while the application is in Arabic.

### Fonts

Each language is set in the face designed for it. The **Arabic** interface is drawn in **Tajawal** and the
**Latin** interface in **Segoe UI**; switching the language re-applies the face along with the words. Tajawal
carries a complete Latin of its own, so a product name, a SKU or a figure inside an Arabic line reads as one
voice rather than as two fonts arguing.

Tajawal is **embedded in the application** (`src/StoreFlow.Desktop/Fonts/`, ~120 KB) rather than installed on
the machine, so a shop PC needs no font installation and the interface looks identical everywhere. Its licence
is the SIL Open Font License 1.1 and the licence text ships beside the font in
`src/StoreFlow.Desktop/Fonts/Tajawal-OFL.txt`. Sakkal Majalla, which ships with Windows, is the fallback for
any glyph Tajawal does not carry.

Tajawal was chosen over Cairo deliberately: Tajawal ships static weights, so WPF renders a true bold instead of
synthesising one, and its geometric, low-contrast skeleton sits comfortably beside Segoe UI and holds up at
12 px in a dense table. Cairo is a variable font only, which WPF cannot weight properly, and its softer,
warmer cut suits a marketing page more than an operating screen.

A theme may name other faces, and a rebrand that points at fonts installed on the machine is honoured live.
A rebrand that wants a *different embedded* face ships a different set of faces in the `Fonts` folder. See
[ADR-0004](docs/decisions/ADR-0004-typography.md) for why a single composite family carrying both scripts is
not possible on WPF, and what a malformed file in the font folder costs.

> The `Fonts` folder of the application holds the two faces and **nothing else**. WPF only treats a folder as
> a private font collection if every file in it can be read, so a stray file there would take the whole
> collection down and the Arabic face would silently fall back to the system one. This is pinned by
> `tests/StoreFlow.Desktop.Tests/PrivateFontCollectionTests.cs`.

### Security

- Passwords: **Argon2id** (m = 47 104 KiB, t = 1, p = 1 — OWASP parameters) via
  `Konscious.Security.Cryptography.Argon2`. PBKDF2-HMAC-SHA256 (600 000 iterations) is accepted for imported
  hashes and re-hashed to Argon2id on the next successful login.
- Authorisation is enforced **in the application layer**, never by hiding buttons. A permission enum and a
  permission matrix cover sales, returns, purchases, stock, products, reports, users, settings, backups and
  audit.
- OAuth tokens for cloud backup targets are stored DPAPI-encrypted (`CurrentUser`) outside the database and
  are never logged.
- CSV export neutralises formula injection.

---

## First run

1. Launch the application. On first run it creates the database, applies the migrations, installs the schema
   triggers, and seeds the baseline data (four roles with their permission matrices, the Piece and Kilogram
   units, a standard tax category and the store settings).
2. The **first-run wizard** then asks for the store details, the owner's credentials, the currency and tax
   policy, and the interface language.
3. Subsequent launches go straight to the **login window**. There is no "remember me": the session is
   released when the application closes.

---

## Where the data lives

| What | Where |
|---|---|
| Database | `%AppData%\StoreFlow\data\storeflow.db` |
| Backups (`.sfb` files) | `%AppData%\StoreFlow\backups\`, or the folder the shop names on the Backup screen |
| Restore points (the data a restore replaced) | `%AppData%\StoreFlow\data\restore-points\` |
| Theme files | `%AppData%\StoreFlow\themes\*.sftheme.json` |
| Brand assets (logo) | `%AppData%\StoreFlow\brand\` |
| Cloud tokens (DPAPI) | `%LocalAppData%\StoreFlow\tokens\` |
| Diagnostic log | `%AppData%\StoreFlow\logs\` |

---

## Diagnostics

A shop PC has no console and no debugger, so the application writes what it is doing to
`%AppData%\StoreFlow\logs\`:

| File | What it holds |
|---|---|
| `startup.log` | Every start-up step, and the full exception chain of any failure — including the faces the interface resolved to |
| `storeflow-<date>.log` | The rolling application log, kept for fourteen days |

Two things are worth knowing on a support call:

- **The faces the interface resolved to are written to `startup.log` at start-up.** "The Arabic looks
  wrong" is otherwise unanswerable on a machine you cannot open.
- **The application can photograph itself.** Started with `--capture <path>`, it writes a PNG of the
  shell once the first page has loaded:

  ```powershell
  & 'src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe' --capture 'C:\temp\shell.png'
  ```

  The picture is taken by the application itself rather than by a screen capture, because a
  right-to-left window is not reproduced faithfully by the tools that capture other windows.

---

## What v1.0 is, and what it deliberately is not

Every phase wrote down what it left rough. This is where all of that comes to. Each item is either
**done**, **in v1.0 on purpose**, or **not in v1.0**, with the reason.

### Done in Phase 7

- **The bare `dotnet build` in the repository root is unambiguous.** The stray `StoreFlow.slnx` has
  been moved to `build\legacy\` — the file-safety policy on this machine refuses deletions, so it
  was moved rather than removed — and `tools\build.ps1` names the solution for you. The four
  scratch files the Phase 6 work left in the root have been moved to `build\scratch\` for the same
  reason.
- **The programme restarts itself after a restore.** A restore replaces the database on disk; the
  running programme was still holding the old one, so every sale after a restore would have been
  written to a file that was no longer the shop's. The wizards now restart the programme and say
  so, in the language the shop is using.
- **There is an installer.** `installer\build-installer.ps1` builds a per-user MSI that installs
  into `%LOCALAPPDATA%\Programs\StoreFlow` without an administrator account, and
  `installer\verify-installer.ps1` checks that installing, upgrading over the top and removing it
  all leave the shop's data where it was. The shop's data lives in `%APPDATA%\StoreFlow`, outside
  the installation, so an upgrade keeps the ledger and an uninstall keeps it too.
- **The fields have names, the dialogs answer to Escape, and the contrast validator checks
  thirty-five role pairs instead of sixteen.** See
  [`docs/ACCESSIBILITY-REVIEW.md`](docs/ACCESSIBILITY-REVIEW.md) for what was fixed and what is
  still outstanding.
- **Every write in the application can be failed on purpose, and every one of them is proved.**
  Fifty-five named write points, one scenario each, and a test that fails the build if a write
  point is not covered. Writing the matrix found two write points that named a commit the code
  never made, and one that was doing three jobs at once; all three are fixed.
- **The shop survives the power going off.** A test kills a process in the middle of a sale and
  checks that the ledger is either whole or untouched, and that the till starts again on the
  database it was left with.
- **A shop with a hundred thousand invoices works.** The harness fills one and measures it. It
  found that the sales register, the sales-by-product report and the stock lists were each
  sending one SQL parameter per row, which SQLite refuses once the shop is busy; they no longer
  do, and a test keeps them honest.

### In v1.0 on purpose, with the reason

- **A reversal is a document of its own.** The register shows two rows for one sale. That is
  honest: the original invoice is never edited or deleted, and the books always add up.
- **A return cannot be undone.** A return posted by mistake is corrected by a second return. The
  first return stays on the record.
- **A count and a correction are two documents.** One job for the shopkeeper, two documents on the
  record.
- **A count is not blind.** The book quantity is on the screen while the counter works. The
  separation of duties is an approval, not a second pair of eyes at the shelf.
- **The scheduler is the application's own timer.** It runs while the application is open. A
  Windows Scheduled Task would put the shop's Windows account in the middle of the shop's data
  protection, and that trade was taken deliberately.
- **The pictures show the interface and not the window frame.** A picture taken from outside the
  process comes back mirrored for a right-to-left window, and a mirrored picture of a till is
  worse than no picture at all.
- **The theme's live dictionary is written to the log at start-up.** A missing colour role shows
  up as a control quietly falling back to another colour, and on a shop PC there is no debugger.
- **The day's takings count a reversal as a day with no invoice.** A reversal undoes an invoice;
  it is not an invoice.
- **The returns report groups by product, not by return.** That is what the PRD asks for.

### Not in v1.0, and why

| Left out | Why |
|---|---|
| A price override that asks a manager | A till price that is not the catalogue price needs a second pair of hands at the till, and the flow needs a manager prompt of its own. In v1.0 the till refuses the sale instead, which is safe and unhelpful; it is not wrong. |
| A scale on the keyboard | The till already sells by weight: the Kilogram unit is seeded and the cart takes a fractional quantity. What is missing is the weighing itself — the weight is typed in, not read off a scale. |
| A customer on the sale | The PRD does not ask for one, and a till that asks for a name on every sale is a till that slows down. |
| More than one tender on one sale | The PRD does not ask for it, and a split-tender sale is a bigger change to the sale document than it looks. |
| A second pair of eyes on a stock count | The count is approved, but it is not blind. |
| A supplier statement to reconcile against, and foreign currency | A purchase is one supplier, one currency and one document in v1.0. |
| More backup destinations | The S3-compatible target covers the object store most shops already have. Google Drive, OneDrive and WebDAV are each a separate integration. |
| Incremental backups | Every copy carries the whole shop, so the archive grows with the pictures rather than with the changes. A copy that carried only what had changed would be a different kind of file. |

### Done in Phase 8

- **The first-run wizard opens.** Eight attributes were stranded outside their controls, causing WPF to throw while loading the window and preventing a new shop from completing first run. The same error appears in the Phase 1 through Phase 5 startup logs. It is fixed, with two regression tests.
- **The sale's audit entry is written inside the sale's transaction.** The sale and its audit row commit together, and an audit failure rolls the sale back. The separate approval-applied audit and other commands still use the independent audit writer and can fail after their business commit.
- **The desktop-driving harnesses accept -ExePath.** The smoke scripts and scan-latency harness can target an installed copy; the installed programme was driven this way for the first time.
- **Twenty controls on six screens have accessible names**, bound to the labels the shopkeeper sees.

### Done in Phase 9 — v1.0.1

- **Every business change writes its audit entry in the same write as the change.** Thirty-three commands across the catalogue, the stock, the purchases, the counts, the till, the approvals, the accounts, the setup and the drawer count now add their entry to their own transaction, so the entry and the change are made permanent together or not at all. A new write point, `WriteAuditEntryInTransaction`, and six new tests prove it: a failure writing the entry leaves neither the change nor the entry behind. Two places still write their entry separately — a restore, which replaces the whole database file, and the backup schedule, whose settings store owns its own write — and both are named in the release notes.
- **All 88 controls without an accessible name have one.** The guard's threshold is zero, and the pinned table names the file, the control and the binding for every one of them.
- **The restore-confirmation failure was the harness's fault, not the programme's.** The click landed on a button that was still disabled, and WPF throws a click on a disabled control away without a word. The harness now waits for the button to become usable. Two more checks of the same kind were found and fixed while this phase ran — the checksum the restore wizard shows, and the scheduler's own copy in the backup folder — and the rule is written down: **a wait is for a value the screen must reach, never for a single look.**
- **The updater's first real cycle.** v1.0.1 was published and an installed v1.0.0 was driven through find → download → checksum → upgrade. The shop's data is measured by **what it holds** — the same tables with the same rows before and after — because the bytes of a SQLite file move when the programme closes even when nothing was written to it. `tools/update-cycle.ps1` is that run, and it can be run again for every release.
- **CI on every push and pull request**, and every smoke harness writes its pictures into the repository's phase folders instead of a session's temporary folder.
- **A shop that has run the programme can be migrated again.** The guard triggers are taken down before a migration rebuilds a table under them and put back afterwards; SQLite will not drop a table a trigger still refers to, so without that a shop could not be migrated at all. Taking the guards down opened a second fault, which is also fixed: the framework opens the database **read-only** when it checks whether it exists, and the pragmas were asking that connection to change the journal mode of the file. The rule is written down: **a connection that cannot write is never asked to change the file.** Five new tests, and both fixes are proven by failing against the old code. Both release tags already carry every migration, so a shop that has only ever run a released version never meets the fault; the fix will be in the next release.

### Still open

- **The recovery wizard is a dead end.** When the shop's data cannot be prepared and no backup is restored, the wizard offers only *Close* and the programme then refuses to start. A way to start again with a fresh shop, keeping the old folder aside, is a new screen and has not been built.

- **Backup archives are not encrypted: a `.sfb` holds the shop's data in the clear and must be kept on a protected disk.** Restrict access to local and cloud copies. PRD §8.5 recommends passphrase encryption. The full design — an AES-256-GCM container, an encrypted manifest, a passphrase or a DPAPI-protected unattended backup, and the rules for a lost passphrase — is written down in [`docs/decisions/ADR-0005-archive-encryption.md`](docs/decisions/ADR-0005-archive-encryption.md) and is deliberately **not** in v1.0.1: a format change on the backup and restore path, in the first release after v1.0.0, could leave a shop without a usable copy of its data. Until it arrives, keep the copies on a protected disk and use full-disk encryption on the disk they are on.
- **Two audit entries are still written a moment after the change they describe.** A restore replaces the whole database file, and the backup schedule is written by a settings store that owns its own write; neither can join a transaction with the entry without a change to the shape of those two features. Neither can leave the shop's stock, money or catalogue in a state it should not be in; the only thing at risk in either case is one line of the audit trail.

---

## Delivery plan

Each phase ends in a working, verifiable increment and a written report; implementation **pauses at the
boundary** for an explicit go-ahead.

| Phase | Scope | Exit gate |
|---|---|---|
| **0 — Foundation** ✅ | SDK, solution, analyzers, domain, EF Core + first migration, SQLite pragmas and triggers, theme engine and presets, localisation shell, app shell, first-run wizard, login, roles, logging | App launches, DB migrates, theme switches live, AR/EN switch works, login enforced, all tests green |
| **1 — Catalog** ✅ | Products, categories, units, suppliers, barcode uniqueness, product editor, search, CSV import/export, product images, audit trail | UAT-01/02/18 |
| 2 — Inventory ✅ | Purchase draft → post → receive, opening stock, stock ledger and balances, low-stock, stock count with approval, adjustments, valuation report | UAT-03/14/19; ledger reconciles to zero |
| 3 — POS ✅ | Scan buffer, cart, discounts, tax, payments, idempotent checkout, receipts and preview, hold cart, shortcuts | UAT-04/05/06/07/08; the till answers a scan in a median of 4 ms |
| 4 — Sales & Returns ✅ | Invoice history and detail, partial and full returns, resellable vs damaged, refund recording, reversal policy | UAT-10/11/12/20 |
| 5 — Reports & Admin ✅ | All 11 PRD reports with filters and CSV export, plus the valuation, the cashier's takings and the audit trail as a report, dashboard, users and roles UI, audit browser | UAT-13/15; figures match hand-computed values |
| 6 — Backup & Cloud ✅ | `.sfb` engine, local and S3-compatible targets, scheduler with retention, restore wizard, recovery wizard — **per-entity export/import is deferred**; see the Phase 6 report for the reason | UAT-16; restore into a different profile verified |
| 7 — Hardening & Release ✅ | Performance harness, crash recovery, fault injection on every write point, MSI installer, user guide, recovery runbook, accessibility pass, final design review | Definition of Done met here except the shop-hardware-dependent lines; see the [release-candidate gate tables](docs/phases/PHASE-8-RELEASE-CANDIDATE.md) |
| 8 — Release candidate ✅ | The record closed, the candidate built and installed, the **installed copy** driven as well as the development build, the first-run crash found and fixed, the two gate tables written, **v1.0.0 published** | The Definition of Done and the twenty PRD §11 rows, each labelled with what it does and does not prove; see [Phase 8](docs/phases/PHASE-8-RELEASE-CANDIDATE.md) |
| 9 — v1.0.1 ✅ | CI on every push and pull request, the restore-confirmation flake explained and fixed, **every** control named for a screen reader, the approval audit written inside the sale, the archive-encryption design pinned | The suite green on every push; the updater's first real cycle proved end to end; see [the v1.0.1 report](docs/phases/PHASE-9-V1.0.1.md) |

The manual acceptance script lives in [`docs/uat/UAT-PLAN.md`](docs/uat/UAT-PLAN.md).

---

## Documentation

| Document | What it holds |
|---|---|
| [`PRODUCT.md`](PRODUCT.md) | The product record: users, purpose, operating context, brand commitments, principles, accessibility rules |
| [`DESIGN.md`](DESIGN.md) | The design system: colour roles, typography, layout, density, components, do's and don'ts |
| [`docs/decisions/ADR-0001-stack.md`](docs/decisions/ADR-0001-stack.md) | Why C# / .NET 10 / WPF, and what was rejected |
| [`docs/decisions/ADR-0002-money-and-rounding.md`](docs/decisions/ADR-0002-money-and-rounding.md) | Money representation, the single rounding policy, quantity scaling |
| [`docs/decisions/ADR-0003-valuation.md`](docs/decisions/ADR-0003-valuation.md) | Moving weighted average, posting order, returns, damaged stock |
| [`docs/decisions/ADR-0004-typography.md`](docs/decisions/ADR-0004-typography.md) | One face per locale, the embedded Arabic face, and why a composite family is not possible on WPF |
| [`docs/decisions/ADR-0005-archive-encryption.md`](docs/decisions/ADR-0005-archive-encryption.md) | Encrypted backup archives: the design, and why it is not in v1.0.1 |
| [`docs/uat/UAT-PLAN.md`](docs/uat/UAT-PLAN.md) | The manual acceptance script, one row per PRD case, with sign-off columns |
| [`docs/phases/PHASE-0-FOUNDATION.md`](docs/phases/PHASE-0-FOUNDATION.md) | The Phase 0 report: what was built, how it was verified, and what was found by running it |
| [`docs/phases/phase0-screens/`](docs/phases/phase0-screens/) | The pictures of the first-run wizard, the sign-in and the shell that the Phase 0 report leans on |
| [`docs/phases/PHASE-1-CATALOG.md`](docs/phases/PHASE-1-CATALOG.md) | The Phase 1 report: the catalogue, its screens and its spreadsheet contract, what was verified, and the twelve bugs that running it found |
| [`docs/phases/phase1-screens/`](docs/phases/phase1-screens/) | The twenty-two pictures of the running application that the Phase 1 report leans on |
| [`docs/phases/PHASE-2-INVENTORY.md`](docs/phases/PHASE-2-INVENTORY.md) | The Phase 2 report: purchases, the stock ledger, the count that waits for an approval, the valuation, and what running it found |
| [`docs/phases/phase2-screens/`](docs/phases/phase2-screens/) | The pictures of the running application that the Phase 2 report leans on |
| [`docs/phases/PHASE-3-POS.md`](docs/phases/PHASE-3-POS.md) | The Phase 3 report: the till, the pricing core, the sale that cannot be posted twice, the cart that is put aside without reserving stock, the receipt, and the twelve defects that running it found |
| [`docs/phases/phase3-screens/`](docs/phases/phase3-screens/) | The ten pictures of the running till that the Phase 3 report leans on |
| [`docs/phases/PHASE-4-SALES-AND-RETURNS.md`](docs/phases/PHASE-4-SALES-AND-RETURNS.md) | The Phase 4 report: the sales register, the return that puts the goods back, the reversal that undoes an invoice, and what running it found |
| [`docs/phases/phase4-screens/`](docs/phases/phase4-screens/) | The pictures of the running sales register that the Phase 4 report leans on |
| [`docs/phases/PHASE-5-REPORTS-AND-ADMIN.md`](docs/phases/PHASE-5-REPORTS-AND-ADMIN.md) | The Phase 5 report: the eleven reports, the money rules behind them, the discount that waits for a manager, the users and the roles, and what running it found |
| [`docs/phases/phase5-screens/`](docs/phases/phase5-screens/) | The pictures of the running reports screen that the Phase 5 report leans on |
| [`docs/phases/PHASE-6-BACKUP-AND-CLOUD.md`](docs/phases/PHASE-6-BACKUP-AND-CLOUD.md) | The Phase 6 report: the `.sfb` file and what proves it, the destinations, the schedule and the retention, the restore and the restore point, the recovery wizard, the ten defects that running it found, and the phase gate |
| [`docs/phases/phase6-screens/`](docs/phases/phase6-screens/) | The pictures of the running backup screen, the restore wizard and the recovery wizard that the Phase 6 report leans on |
| [Phase 7 report](docs/phases/PHASE-7-HARDENING-AND-RELEASE.md) | Hardening, release work and verification |
| [User guide](docs/USER-GUIDE.md) | The shop's short guide |
| [Recovery runbook](docs/RECOVERY-RUNBOOK.md) | Local diagnosis and recovery |
| [Accessibility review](docs/ACCESSIBILITY-REVIEW.md) | Accessibility work and outstanding names |
| [Build installer](installer/build-installer.ps1) and [verify installer](installer/verify-installer.ps1) | Per-user MSI creation and verification |
| [Performance harness](tools/performance-harness.ps1) | Fills a shop and measures its queries |
| [Volume tool](tools/StoreFlow.VolumeTool/StoreFlow.VolumeTool.csproj) | Project that fills and measures a shop |
| [Release-cycle harness](tools/update-cycle.ps1) | Installs a published release and drives it through the next upgrade, and proves the shop's data came through it |
| [Shop digest](tools/ShopDigest/ShopDigest.csproj) | Prints what a shop's database holds, and hashes it, so that "nothing changed" can be proved |
| [Update probe](tools/StoreFlow.UpdateProbe/StoreFlow.UpdateProbe.csproj) | Asks the updater's own service to fetch a published release, and times it |
| [Crash probe](tests/StoreFlow.CrashProbe/StoreFlow.CrashProbe.csproj) | Checks interrupted-sale recovery |
| [Performance report](docs/PERFORMANCE-REPORT.md) | Performance results for the shop owner |
| [Release notes](docs/RELEASE-NOTES.md) | v1.0 scope, open items, installation, and where to find the installer's SHA-256 |
| [Hardware sheet](docs/uat/HARDWARE-SHEET.md) | Printable hardware acceptance checklist |
| [Release-candidate report](docs/phases/PHASE-8-RELEASE-CANDIDATE.md) | Release candidate and Definition of Done and UAT gate tables |
| [v1.0.1 report](docs/phases/PHASE-9-V1.0.1.md) | The release cycle proved end to end, the three debts decided, and the updated gate tables |
| [v1.0.1 evidence](docs/phases/phase9-evidence/README.md) | The logs the v1.0.1 report points at: the harness runs, the release cycle, and the proofs |
| [Auto-update](docs/UPDATER.md) | How the programme updates itself from GitHub Releases, and how a release is made |

---

## Licence

Not yet stated. Add the licence the product is shipped under before the first release build.
