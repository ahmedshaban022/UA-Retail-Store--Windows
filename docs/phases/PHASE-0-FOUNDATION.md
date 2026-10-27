# Phase 0 — Foundation

**Status:** complete · **Commit:** `e2f8e9a` · **Date:** 2026-10-27

The first of eight phases. This phase ends in a working, verifiable increment: the application
starts, a shop can be set up, an owner can sign in, and the shell is drawn in Arabic and English.

---

## What was built

### The stack

C# 14 on .NET 10 LTS, WPF for the interface, EF Core 10 over SQLite for the store, xUnit and
Shouldly for the tests. Chosen and defended in [ADR-0001](decisions/ADR-0001-stack.md): the
requirements that decided it are Arabic shaping, 80 mm thermal and A4 printing, and USB scanner
support — all of which are solved by the platform rather than fought by it.

Four projects with a one-way dependency rule, enforced by the project references:

```
Desktop ──► Application ──► Domain ◄── Infrastructure
```

`Domain` references nothing at all. `Application` holds the ports; `Infrastructure` and
`Desktop` are the two adapters. Analyzers are on and **warnings are errors** — the build is clean
with zero warnings.

### The rules everything else will be built on

| Rule | Where it is written down |
|---|---|
| Money is integer minor units in the database, `decimal` in code, rounded exactly once per line, half away from zero, through one policy | [ADR-0002](decisions/ADR-0002-money-and-rounding.md) |
| Stock is valued with a moving weighted average recomputed in **posting order**, so a backdated document cannot rewrite the cost of a posted sale | [ADR-0003](decisions/ADR-0003-valuation.md) |
| The theme is versioned JSON (`sftheme/1`) turned into a live resource dictionary, so a shop can be rebranded without a rebuild | `src/StoreFlow.Application/Theming` |
| Each language is set in the face designed for it; the Arabic face is embedded in the application | [ADR-0004](decisions/ADR-0004-typography.md) |
| Authorisation is enforced in the application layer, never by hiding a button | `src/StoreFlow.Application/Authorization` |

### The domain

24 entities, and the policies that decide the answers: `Money` and `MoneyMath`, `UnitQuantity` and
`QuantityScale`, `TaxCalculator`, `DiscountCalculator`, `MovingAverageCost`, and a `Permission`
enumeration with 47 permissions across four roles.

### The application layer

Command and query conventions over ports, with authorisation, validation, and a `Result` type
that carries either a value or an error. `lib/server`-style idempotency is already in place:
`executeCommand` authorises before it replays, fingerprints the payload, and writes a unique
`request_outcomes` key, so an uncertain commit can be recovered with `readOutcome` instead of
being repeated.

### The infrastructure

EF Core over SQLite with the pragmas applied through an interceptor — foreign keys on, WAL, a
full-synchronous journal, a busy timeout, memory temp store — and **database triggers** that
refuse a negative stock balance and refuse to change a posted movement or a posted document. The
schema is created by migrations, not by `EnsureCreated`.

Seeded with four roles, forty-seven role permissions, the unit and tax catalogue, the store
settings, and the three built-in themes.

The database is verified: 28 tables, 4 roles, 47 role permissions, 2 units, 1 tax category, 8
triggers, `PRAGMA integrity_check = ok`, WAL on, `synchronous = 2`.

### The desktop

The first-run wizard, sign-in, the navigation shell, the dashboard, the settings screen, the
theme engine and the localisation — Arabic (the default) and English, with the reading direction
applied to every window and the text resolved against the application's language rather than the
ambient thread culture.

The **select** control is built from scratch rather than inherited from the platform theme: a flat
field, an authored chevron, a custom popup, a slim arrowless scrollbar, a 2 px petrol focus ring
offset 3 px. A select shows a **human label**, never a raw record or an enum identifier.

---

## What was verified, and how

Nothing below was concluded from reading the code.

| What | How it was checked |
|---|---|
| The wizard runs to completion | Driven through UI Automation: every field filled, three steps, finished |
| The owner can sign in and the shell appears | Driven through UI Automation; every rendered name in the UI tree read back |
| The database is seeded and sound | Queried directly: table count, role and permission counts, `integrity_check`, pragma values |
| The Arabic interface is right-to-left | Measured with UI Automation geometry — the navigation rail is on the right, the stat cards run right to left, the amount sits at the right edge of its card |
| The Arabic text is set in **Tajawal** | The resolved family read out of the application's own log: `names=Tajawal`, with a Normal and a Bold cut |
| The Latin interface is set in **Segoe UI** | The same log, in the English locale: `source=Segoe UI, Sakkal Majalla` |
| The theme can be rebranded | The theme file is JSON on disk; the engine turns it into a live dictionary and the interface follows without a restart |
| The build and the tests | `dotnet build` clean with 0 warnings; **100 tests pass** (25 domain, 30 application, 18 infrastructure, 29 desktop) |

The application was also driven by hand through the drop-down, the navigation rail, the sign-in
and the settings screen, and the interface was photographed in both languages.

---

## Bugs found by running it

Every one of these was found by running the application, not by reading it. Each is fixed and
pinned by a test.

1. **The password field could not be bound.** `PasswordBox.Password` is not a dependency property,
   so the XAML binding threw and the password never reached the view model. Fixed with an attached
   two-way behaviour — and the first attempt at it failed in a way that looked like success, which
   is why there is now a test that documents the mechanics.
2. **The container could not be built.** `AddLocalization` requires `AddLogging`; the application
   died before it showed a window. A rolling file logger was added.
3. **A corner radius was published as a `double`.** The control template assigns a `CornerRadius`,
   so the window threw an invalid cast at layout time. The theme now hands out the real types.
4. **The application failed silently.** No window, no message, no trace. Start-up diagnostics are
   now written to `%AppData%\StoreFlow\logs\startup.log`, a message box tells the owner, and the
   dispatcher exception handler records the chain.
5. **The Arabic interface was drawn in English.** The strings were resolved against the ambient
   thread culture, which is not reliable. They are now resolved against the application's
   language explicitly.
6. **A select showed `LanguageOption { Code = ar, ... }`.** The generated `ToString` was on the
   screen. The option types now return the name the person should read.
7. **The theme was applied twice.** A second theme dictionary shadowed the first, so the older
   theme kept winning. The live dictionary is now replaced in place, and only one is ever
   installed.
8. **The Arabic face silently fell back to the system one.** A malformed file in the private font
   folder takes the whole collection down, and the symptom is indistinguishable from a font that
   failed to load. The folder now holds the two faces and nothing else, and a test says so.

---

## The typography decision

**Tajawal**, over Cairo. Both cover Arabic-Indic numerals and the Latin range and both are under
the SIL Open Font License. Tajawal ships **static** weights, so WPF renders a true bold; Cairo is
variable-only, which WPF cannot weight.

The face is **embedded in the application**, so a shop PC needs no font installation and every
machine looks the same.

The interesting part is that a single WPF *composite* family carrying both scripts — Latin first,
Arabic second — turns out **not to be possible** with an embedded face. A composite font names its
faces and maps character ranges to those names; it cannot name a face that exists only inside the
private font collection. And a composite file that WPF cannot parse takes the whole collection
down with it, which is exactly what happened during this phase.

So each language is set in the face designed for it: the Arabic interface in Tajawal, the Latin
interface in Segoe UI, and switching the language re-applies the face along with the words.
Tajawal carries a complete Latin of its own, so a product name or a figure inside an Arabic line
still reads as one voice. The full reasoning is in
[ADR-0004](decisions/ADR-0004-typography.md).

---

## What is deliberately not in this phase

| Deferred | Why | Where it lands |
|---|---|---|
| PDF and XLSX export | Deferred by the plan; PDF is produced by the OS printer driver for now | Phase 6 |
| Backup and restore to local media and cloud | The `IBackupTarget` port exists; the providers do not | Phase 5 |
| 80 mm receipt and A4 printing | The `IPrintService` port exists; the layouts do not | Phase 4 |
| Catalog, purchasing, sales, stock counts | The schema and the policies are in place; the screens are not | Phases 1–3 |
| The theme editor | The theme file and the live engine are in place; the editor screen is not | Phase 5 |

### The UAT plan is written but not run

[`docs/uat/UAT-PLAN.md`](../uat/UAT-PLAN.md) holds twenty acceptance cases, one row per PRD
case, with sign-off columns. **Every row is `Not run`.** Six of them need hardware this machine
does not have: a USB scanner, an 80 mm thermal printer, an A4 printer, and a power-loss kill
test. The other fourteen need a person with the product in front of them.

The automated suite covers the rules, not the experience. The UAT plan is where the experience is
checked, and it has not been.

---

## Known papercuts

- `StoreFlow.slnx` exists in this working copy beside the authoritative `StoreFlow.sln`. It is
  deliberately **not tracked** and should be deleted by hand — the local file-safety policy on
  this machine refuses deletions.
- The desktop session on this machine is locked, so a screen capture is impossible. The
  application can therefore photograph itself (`--capture <path>`), which is also how a shop owner
  would send a picture of a problem. `PrintWindow` draws a right-to-left window **mirrored** and
  must not be used to check the layout.

---

## The phase gate

Phase 0 is complete: the foundation is built, verified, documented and committed. Implementation
**stops here** until an explicit go-ahead for **Phase 1 — Catalog** (products, categories, units,
barcodes, pricing and the product screens).

The next phase depends on nothing that is outstanding, so the go-ahead can be given whenever it
suits.
