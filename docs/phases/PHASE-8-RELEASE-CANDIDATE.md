# Phase 8 — Release candidate and final gate

This phase closes the programme record and produces a release candidate. It does not close the shop's manual acceptance. The [PRD](../../StoreFlow_Retail_Management_PRD_EN.md) and [UAT plan](../uat/UAT-PLAN.md) are the authority for the two tables below. The plan says all twenty manual cases are not run; a green automated counterpart and a walkable step do not constitute a signed manual pass. The [Phase 7 report](PHASE-7-HARDENING-AND-RELEASE.md) records the preceding work.

## What was built and decided

Phases 0-7 were built, committed and pushed. This phase closed their record, built the candidate with `installer/build-installer.ps1`, added `-ExePath` to the desktop-driving scripts in `tools/`, and drove both the development build and the installed application. It fixed first run and decided the three debts left by Phase 7. There is no Phase 9.

- **The sale audit is part of the sale transaction.** The sale and its audit entry commit together; an audit failure rolls the sale back. This closes the gap in the sale record. The separate approval-applied audit and other commands still use the independent writer and can fail after their business commit; this decision does not close their gap. `tests/StoreFlow.Infrastructure.Tests/WriteFaultMatrixTests.cs` covers the sale audit write and replay.
- **Accessible names remain an accepted debt.** Twenty controls on six screens now have names bound to visible labels. The [accessibility review](../ACCESSIBILITY-REVIEW.md) lists the rest: 87 controls from the original list remain unnamed. That list actually had 107 entries, not the 96 first reported; the source guard finds 88 candidates, including one outside that list with child text needing runtime verification. The remaining screens need localised names and runtime checks, not guessed labels. A screen-reader user may not be able to use them independently. `tests/StoreFlow.Desktop.Tests/AccessibleNameDebtTests.cs` guards the source-level work.
- **Backup archives are not encrypted. A `.sfb` contains shop data in the clear; keep local and cloud copies on protected storage and restrict access.** PRD §8.5 recommends passphrase encryption. It remains open because scheduled backup, verification, retention, recovery on another PC and a lost passphrase need a complete design and tests together. Adding encryption without proving recovery of old and new copies could cost the shop its backup.
- **The gate tables live in this file.** The final decision needs the evidence and its limits beside each PRD line. The Phase 7 gate points here, so a reader has one closing record to consult. The cost is maintaining this report if the acceptance evidence changes.
- **The tag and the release are published.** `v1.0.0` was pushed and the release carries the installer, its SHA-256 and these notes. The programme's automated evidence is in this report; the shop's own acceptance still has to be walked through on its hardware.

## How it was verified

The orchestrator reports a solution build with **0 warnings and 0 errors**. At the end of the phase it ran the full solution suite **three consecutive times**: each run had **489 passed, 0 failed, 0 skipped**, comprising 25 domain, 137 application, 125 desktop and 202 infrastructure tests. This report did not run a fourth suite. The suite was 483 before the phase; six additions cover first-run markup and loading, the accessible-name guard, and the sale audit transaction, two each.

### The installer

Version **1.0.0** was built on the development laptop and installed from there. The published release carries the installer GitHub's runner built, and publishes that installer's SHA-256 twice: in the release notes and in a file beside the installer itself. The build script writes the MSI under `artifacts/installer/<version>/`. On the development laptop, `msiexec` installed it per user with exit code 0 and without an administrator account, into `%LOCALAPPDATA%\Programs\StoreFlow`. The data is in `%APPDATA%\StoreFlow`, outside the installation. A marker and the database SHA-256 were identical before and after uninstall, and again after reinstall. Phase 7's `installer/verify-installer.ps1` also checks upgrade and data preservation; the candidate's marker and hash comparison is the uninstall/reinstall evidence, not a new upgrade run.

### The application, driven

The following final runs were on the development laptop, not the shop's till. Counts are checks, not separate test cases. Both copies had zero failures and no behavioural difference was found.

| Harness in `tools/` | Development build | Installed copy |
|---|---:|---:|
| `smoke-catalog.ps1` | 38 | 38 |
| `smoke-inventory.ps1` | 24 | 24 |
| `smoke-pos.ps1` | 29 | 29 |
| `smoke-sales.ps1` | 36 | 36 |
| `smoke-reports.ps1` | 68 | 68 |
| `smoke-backup.ps1` | 38 | 38 |
| `scan-latency.ps1` | 5 | 5 |

`smoke-launch.ps1`, `smoke-flow.ps1`, `smoke-login.ps1` and `smoke-shell.ps1` print no counts. Each completed with exit code 0 and no `FAIL` message; the wizard, shell and sign-in window were reached. The latency run used 20 simulated keyboard scans; it does not prove a physical scanner. `smoke-dropdown.ps1` was not run: the locked desktop session cannot reach a combo box popup. That is an environment limitation, not a programme failure.

The first development-build backup run failed four checks because the restore confirmation did not appear. The immediate retry and the installed run each passed 38 of 38. The cause was not established; the record does not identify this as a product defect.

### The shop at full size

The [performance report](../PERFORMANCE-REPORT.md) and Phase 7 report publish measurements from a laptop development machine, **not the shop's till**: 12th Gen Intel Core i7-12700H, 14 cores, 20 threads, 2.3 GHz, 23.6 GB RAM, Windows 11 Pro 64-bit 10.0.26300. The seed had 10,000 products, 100,000 invoices, 199,999 invoice lines and 100,000 stock movements. Each query ran three times; these are the median and slowest on that laptop, not a promise for the till.

| Query | Median | Slowest |
|---|---:|---:|
| Product list, first page | 17 ms | 157 ms |
| Product search | 15 ms | 37 ms |
| Product list, deep page | 6 ms | 7 ms |
| Sales register, all | 604 ms | 628 ms |
| Sales register, quarter | 85 ms | 89 ms |
| Daily sales report | 155 ms | 192 ms |
| Sales by product | 403 ms | 437 ms |
| Stock valuation | 275 ms | 319 ms |

## What running it found

### First run could not open

Driving the installed copy exposed a release blocker. `src/StoreFlow.Desktop/Views/FirstRunWindow.xaml` had eight `AutomationProperties.Name` attributes stranded outside their elements. The markup compiler accepted the text, but WPF rejected it at runtime. A new shop could not open the wizard or complete first run. The same exception is in the Phase 1 through Phase 5 startup logs; it had been live since Phase 7 commit `8c5ce6c`, without a test catching it. The attributes are fixed. `tests/StoreFlow.Desktop.Tests/XamlLoadingTests.cs` scans for stranded attributes and constructs the window on an STA thread in an isolated child process. Both tests were proved to fail against the broken markup before the fix was kept.

### The backup harness held the old shell

The backup smoke run's recovery check could not pass after a successful restore: Phase 7 deliberately restarts the application, but `tools/smoke-backup.ps1` still held the old shell. This was a harness defect, not evidence that the shop could not recover. It now waits for the replacement process, signs in again and uses the new shell. The final backup runs passed 38 checks on each copy; this smoke check guards the recovery flow. The harness now writes pictures to its temporary run folder.

### The sale audit gap

The Phase 7 fault matrix exposed that the independent audit write could fail after a sale committed. The shop could have a complete sale without its audit entry. The sale audit was moved inside the transaction; the write-fault matrix and replay test guard it. Other commands and the separate approval-applied audit remain independent, as stated above.

### What was deliberately left

The archive encryption and remaining accessible names are open as stated above. The first backup confirmation failure has no established cause and was not presented as fixed. Earlier defects and their guards are recorded in the phase reports; no additional defect count has been invented for this phase.

## What is deliberately not in v1.0

The [README scope statement](../../README.md) records the decisions and their costs:

- Manager-approved price override: it needs its own manager prompt; the till refuses the sale instead.
- Selling by weight at the till: it needs a scale on the keyboard.
- A customer on a sale: the PRD does not ask for one and asking for a name slows the till.
- Split tender: the PRD does not ask for it and it changes the sale document.
- A blind count or a second pair of eyes at the shelf: the count is approved, but the book quantity is visible.
- Supplier-statement reconciliation and foreign currency: a purchase has one supplier, currency and document.
- Further backup destinations: Google Drive, OneDrive, Dropbox and WebDAV need separate integrations; local folder and S3-compatible storage are available.
- Incremental backup: every copy carries the whole shop, including its pictures; an incremental copy would be a different file and recovery design.
- Per-entity transactional import/export beyond the existing catalogue and report CSV: bypassing posting and approval rules would endanger the ledger; whole-shop backup and restore carries the data instead.
- Encrypted backup archives and complete accessible names: these remain open, with the consequences described above.

## Known minor items

Picture destinations differ. `smoke-inventory`, `smoke-pos`, `smoke-sales` and `smoke-reports` write into phase screen folders in the repository. `smoke-backup` now uses the temporary run folder. `smoke-catalog`, `smoke-dropdown`, `smoke-flow`, `smoke-launch`, `smoke-login` and `smoke-shell` still point at throwaway October session folders. The checks run and pictures are written; the destinations are inconsistent and worth tidying.

The dropdown harness was not run in the release checks because the locked desktop session cannot reach its popup. It is not evidence of a programme failure, and the popup is not proved by this release run.

## Definition of Done — PRD §12.1

The verdict describes the evidence, not a manual sign-off. The UAT plan requires a human to execute each case, repeat user-facing cases in both languages and sign it. None has been signed off in the plan.

| PRD line | Verdict | Evidence and limit |
|---|---|---|
| Every P0 feature has working implementations and passing critical acceptance tests. | **PARTIAL (implementations and automated counterparts; manual critical acceptance remains)** | All twenty steps are reachable and the suite and smoke runs are green. Critical manual cases have not been signed off; shop hardware and physical conditions remain outstanding. |
| No open critical defects in calculations, stock posting, returns, printing, or recovery. | **PARTIAL (automated checks green; physical printing and manual acceptance outstanding)** | The first-run blocker is fixed. Money, transaction, return and recovery tests pass; the shop's printers and physical failure cases have not been accepted. The backup confirmation flake remains unexplained. |
| Transaction, reconciliation, and backup/restore tests pass. | **PROVED (automated transaction, reconciliation and backup/restore tests)** | `WriteFaultMatrixTests.cs`, `CrashRecoveryTests.cs`, `LedgerReconciliationTests.cs` and `BackupTests.cs` passed in the reported suite. The reconciliation test runs at least 1,000 mixed operations; physical power loss and a second PC are separate checks. |
| Tested on the intended Windows machine and actual shop scanner/printer hardware. | **NEEDS-HARDWARE (shop till, scanner, 80 mm and A4 printers, second PC and physical conditions)** | The development laptop is not the till. The shop must lend its scanner, both printers, a second clean PC and backup medium, permit an unplugged network cable and printer, and permit killing the application during sale posting. A process kill is not a physical power cut. |
| Installer, first-run setup, admin creation, and backup configuration are documented. | **PROVED (documentation and installed candidate)** | The user guide documents these steps; the installed wizard was reached and setup is covered by `SetupTests.cs` and `XamlLoadingTests.cs`. Documentation is not shop sign-off. |
| Short user guide and local diagnostic/recovery procedure exist. | **PROVED (guide and recovery procedure exist)** | [User guide](../USER-GUIDE.md) and [recovery runbook](../RECOVERY-RUNBOOK.md). |

## PRD §11 — twenty UAT rows

`PROVED` in this table means the named automated counterpart is green and the step is walkable; it does **not** mean the manual case has been executed or signed off. `PARTIAL` names a remaining unproved part. `NEEDS-HARDWARE` names the shop device or physical condition still required. Test files below are under `tests/StoreFlow.Infrastructure.Tests/` unless another project is named; harnesses are under `tools/`. All reported automated runs were on the development laptop, not the shop till.

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
| UAT-09 Force app shutdown during posting | **NEEDS-HARDWARE (permission to kill the application during a real sale on the shop PC)** | `CrashRecoveryTests.cs` kills a separate transaction probe before and after commit; `WriteFaultMatrixTests.cs` tests handler write failures. The real posting interruption and physical power loss are not proved by the probe. |
| UAT-10 Return 1 of 3 sold units | **PROVED (automated linked resellable return and walkable step)** | `SalesTests.cs` covers linked returns and restock; `smoke-sales.ps1`. Manual sign-off remains. |
| UAT-11 Attempt return of 4 from invoice of 3 | **PROVED (automated excess-return refusal and walkable step)** | `SalesTests.cs` `Returning_more_than_was_sold_is_refused`; `smoke-sales.ps1`. Manual sign-off remains. |
| UAT-12 Change product price after sale | **PARTIAL (old invoice price protected; complete manual profit check remains)** | `SalesTests.cs` tests that a later price does not change the posted invoice; `smoke-sales.ps1` drives history and `ReportingTests.cs` covers report figures. The exact price-change and profit walkthrough remains unsigned. |
| UAT-13 Cashier attempts unauthorized discount | **PROVED (automated approval gate and walkable step)** | `ApprovalTests.cs` holds an over-limit discount for a manager without an invoice; `smoke-reports.ps1` drives approvals. Manual sign-off remains. |
| UAT-14 Stock count differs by 2, then approve | **PROVED (automated count approval and walkable step)** | `InventoryTests.cs` tests no stock change until approval and posting; `smoke-inventory.ps1`. Manual sign-off remains. |
| UAT-15 Export report containing Arabic names | **PARTIAL (CSV encoding tested; Excel open remains manual)** | `StoreFlow.Application.Tests/CsvWriterTests.cs` tests unchanged text and BOM; `smoke-reports.ps1` drives reports. Opening the exported file in Excel has not been signed off. |
| UAT-16 Back up then restore on clean test PC | **NEEDS-HARDWARE (second clean Windows PC and external backup destination)** | `BackupTests.cs` and `smoke-backup.ps1` restore into a separate data profile and compare figures on this laptop. A different machine and external medium or real S3 destination remain. |
| UAT-17 Disconnect internet and make sale | **NEEDS-HARDWARE (unplugged network cable and local 80 mm printer)** | `SalePathNetworkTests.cs` and `StoreFlow.Desktop.Tests/TillNetworkTests.cs` guard the network-free sale path; `smoke-pos.ps1` drives a sale. An unplugged cable and physical receipt remain. |
| UAT-18 Deactivate historically sold product | **PARTIAL (deactivation and history covered separately; combined manual flow remains)** | `CatalogTests.cs` retains deactivated products; `SalesTests.cs` protects posted invoice lines; `smoke-catalog.ps1` and `smoke-sales.ps1` drive the screens. The exact deactivate, attempt sale and reopen sequence remains unsigned. |
| UAT-19 Receive higher-cost goods under moving average | **PROVED (automated valuation and cost snapshot and walkable step)** | `StoreFlow.Domain.Tests/MovingAverageCostTests.cs`, `InventoryTests.cs` valuation and `SaleTests.cs` line snapshot; `smoke-inventory.ps1` and `smoke-pos.ps1`. Manual sign-off remains. |
| UAT-20 Return damaged item | **PROVED (automated damaged return and walkable step)** | `SalesTests.cs` `A_damaged_return_is_not_put_back_on_the_shelf`; `smoke-sales.ps1`. Manual sign-off remains. |

The PRD also requires at least 1,000 mixed stock operations with zero balance-to-ledger discrepancy. `LedgerReconciliationTests.cs` exercises the real handlers and checks sellable and damaged balances against movements; it is part of the reported green suite.

## Where the programme stands

A shop gets an installable offline retail application, its guide and recovery procedure. The candidate, automated suite, simulated smoke flows and data preservation across uninstall and reinstall are proved on the development laptop. Manual acceptance, the intended till, scanner, both printers, second PC, unplugged conditions and a real posting interruption remain for the shop. Version 1.0.0 is published with its installer; what waits is the shop's own walk-through, not a release.
