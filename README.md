# StoreFlow Desktop

An offline-first Windows retail store management, inventory and point-of-sale application.

StoreFlow owns the shop's commercial truth: what exists, what is in stock, what was sold, what it cost, what
was returned, and who did it. It runs entirely on the shop's PC with no network dependency, keeps an
append-only stock ledger that reconciles to the piastre, and is **rebrandable by configuration** — product
name, logo, colours, receipt content, currency, tax and locale are data, so the same binary can be resold to
another shop without a recompile.

**Status: Phase 2 (Inventory) complete.** The application builds, the database migrates, the theme switches
live, the AR/EN switch works, login is enforced, and the automated test suite is green. The
catalogue — products, categories, units and suppliers, with the product editor, the search, the
spreadsheet import and export, product pictures and the audit trail — is built and proved by
driving the real application. The **inventory** is now built too: purchase receipts drafted and
posted, a reversing document instead of a deletion, opening stock, the stock ledger and the
balances it reconciles with, low-stock flags, a stock count that waits for an approval, manual
adjustments, and the valuation of the shelf. Feature work (POS, sales, reports, backup) follows the
phase plan below and stops at each phase boundary for review.

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
# Build (must name the .sln explicitly — see "Known papercuts" below)
& 'D:\dotnet\dotnet.exe' build 'D:\Coding\UA Retail Store\StoreFlow.sln' --nologo -v q

# Test
& 'D:\dotnet\dotnet.exe' test  'D:\Coding\UA Retail Store\StoreFlow.sln' --nologo -v q

# Run the desktop application
& 'D:\dotnet\dotnet.exe' run --project 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\StoreFlow.Desktop.csproj'
```

Current state of the suite — **253 tests, all passing**:

| Project | Tests | Covers |
|---|---|---|
| `StoreFlow.Domain.Tests` | 25 | money maths and rounding, moving weighted average, tax inclusive/exclusive, discounts, unit quantities |
| `StoreFlow.Application.Tests` | 83 | password hashing (Argon2id + PBKDF2), permission matrix, scan buffer, CSV reading and writing, WCAG theme contrast validation, the catalogue's validation rules and its spreadsheet mapper, the inventory's validation rules and its approval thresholds |
| `StoreFlow.Infrastructure.Tests` | 57 | real SQLite file: migrations, pragmas, immutability triggers, negative-stock trigger, integrity check, theme store round-trip, the catalogue handlers and their barcode triggers, the purchase and inventory handlers, and the ledger reconciling to the balance |
| `StoreFlow.Desktop.Tests` | 88 | AR/EN resource parity, theme engine resource generation, value converters, the shell's contrast and the tables' headings, the localised messages of the import preview, how a code is laid out in an Arabic cell, the inventory screens' error messages and number reading, the editor flows against a real database, the column heading finding its words, and the rules that keep a table readable — a cell's margin, the trimming of text that does not fit, a heading that wraps, a box with room for the line it holds, and **every column measured against the value it has to hold** |

### Useful commands

```powershell
# EF Core migrations (the local tool manifest pins dotnet-ef 10.0.13)
& 'D:\dotnet\dotnet.exe' ef migrations add <Name> --project 'src\StoreFlow.Infrastructure' --startup-project 'src\StoreFlow.Infrastructure'
& 'D:\dotnet\dotnet.exe' ef database update --project 'src\StoreFlow.Infrastructure' --startup-project 'src\StoreFlow.Infrastructure'

# Regenerate the DESIGN.md sidecar after editing the theme tokens
& '.\tools\generate-design-sidecar.ps1'
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
│   ├── decisions\                 # ADR-0001 stack, ADR-0002 money, ADR-0003 valuation, ADR-0004 typography
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

## Known papercuts

- **Two solution files exist in this working copy.** `StoreFlow.sln` (classic format, authoritative) and
  `StoreFlow.slnx` (created by the initial `dotnet new sln` before the format was corrected). A bare
  `dotnet build` in the repository root is ambiguous — **always pass `StoreFlow.sln` explicitly**. The
  `.slnx` file is deliberately **not tracked** and should be deleted from the working copy by hand; the
  local file-safety policy on this machine refuses to perform deletions.
- **`src/StoreFlow.Domain/Quantity/Quantity.cs`** held the `QuantityScale` type and was renamed to
  `QuantityScale.cs` to match its contents. The `UnitQuantity` type lives in `UnitQuantity.cs` beside it.

---

## Delivery plan

Each phase ends in a working, verifiable increment and a written report; implementation **pauses at the
boundary** for an explicit go-ahead.

| Phase | Scope | Exit gate |
|---|---|---|
| **0 — Foundation** ✅ | SDK, solution, analyzers, domain, EF Core + first migration, SQLite pragmas and triggers, theme engine and presets, localisation shell, app shell, first-run wizard, login, roles, logging | App launches, DB migrates, theme switches live, AR/EN switch works, login enforced, all tests green |
| **1 — Catalog** ✅ | Products, categories, units, suppliers, barcode uniqueness, product editor, search, CSV import/export, product images, audit trail | UAT-01/02/18 |
| 2 — Inventory ✅ | Purchase draft → post → receive, opening stock, stock ledger and balances, low-stock, stock count with approval, adjustments, valuation report | UAT-03/14/19; ledger reconciles to zero |
| 3 — POS | Scan buffer, cart, discounts, tax, payments, idempotent checkout, receipts and preview, hold cart, shortcuts | UAT-04/05/06/07/08; scan < 50 ms measured |
| 4 — Sales & Returns | Invoice history and detail, partial and full returns, resellable vs damaged, refund recording, reversal policy | UAT-10/11/12/20 |
| 5 — Reports & Admin | All 11 reports with filters and CSV export, dashboard, users and roles UI, audit browser | UAT-13/15; figures match hand-computed values |
| 6 — Backup & Cloud | `.sfb` engine, local and cloud targets, scheduler with retention, restore wizard, per-entity export/import, recovery wizard | UAT-16; restore into a different profile verified |
| 7 — Hardening & Release | Performance harness, crash recovery, fault injection on every write point, MSI installer, user guide, recovery runbook, accessibility pass, final design review | Definition of Done fully met |

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
| [`docs/uat/UAT-PLAN.md`](docs/uat/UAT-PLAN.md) | The manual acceptance script, one row per PRD case, with sign-off columns |
| [`docs/phases/PHASE-0-FOUNDATION.md`](docs/phases/PHASE-0-FOUNDATION.md) | The Phase 0 report: what was built, how it was verified, and what was found by running it |
| [`docs/phases/PHASE-1-CATALOG.md`](docs/phases/PHASE-1-CATALOG.md) | The Phase 1 report: the catalogue, its screens and its spreadsheet contract, what was verified, and the twelve bugs that running it found |
| [`docs/phases/phase1-screens/`](docs/phases/phase1-screens/) | The twenty-two pictures of the running application that the Phase 1 report leans on |
| [`docs/phases/PHASE-2-INVENTORY.md`](docs/phases/PHASE-2-INVENTORY.md) | The Phase 2 report: purchases, the stock ledger, the count that waits for an approval, the valuation, and what running it found |
| [`docs/phases/phase2-screens/`](docs/phases/phase2-screens/) | The pictures of the running application that the Phase 2 report leans on |

---

## Licence

Not yet stated. Add the licence the product is shipped under before the first release build.
