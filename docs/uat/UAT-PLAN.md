# StoreFlow — User Acceptance Test Plan

## 1. Purpose and scope

This is the manual user acceptance test script for StoreFlow. It has one row per acceptance case in
`StoreFlow_Retail_Management_PRD_EN.md` §11, in the PRD's own order and with the PRD's own IDs
(`UAT-01` … `UAT-20`), plus the PRD requirement IDs that each case exercises. The case wording follows the
PRD; where this plan is more specific than the PRD (menu paths, button labels, exact expected amounts), the
extra detail is a test fixture, not a new requirement.

Automated coverage exists separately and is not duplicated here. The xUnit suites live under `tests/` and are
split into four projects:

| Suite | Path | Covers |
|---|---|---|
| Domain | `tests/StoreFlow.Domain.Tests/` | Money, tax, discount, quantity and moving-average costing rules |
| Application | `tests/StoreFlow.Application.Tests/` | Authorization, scan buffer, CSV writer, password hashing, theme contrast |
| Infrastructure | `tests/StoreFlow.Infrastructure.Tests/` | SQLite schema, triggers, seeding, integrity, theme store |
| Desktop | `tests/StoreFlow.Desktop.Tests/` | Localization parity, converters, theme engine |

This document covers the end-to-end manual pass only: the parts that prove the rules are wired to a real
screen, a real printer and a real shop PC. Nothing in this plan has been executed yet.

**Column meanings**

- `Requirement(s)` — the PRD requirement IDs the case exercises. `—` means the PRD does not map the case to a
  requirement ID; no ID is invented.
- `Manual / auto` — `Auto` when an automated test in the suites above covers the rule (the manual step then
  only confirms the wiring to the UI); `Manual` when a human must do it; `Manual (hardware)` when it needs
  physical hardware and cannot be automated in CI.
- `Status` — `Not run` for every row until a tester records an outcome.
- `Signed off / date` — left empty until signed.

## 2. How to run this plan

### 2.1 Environment

1. Work from the repository root `D:\Coding\UA Retail Store`.
2. The .NET 10 SDK is portable at `D:\dotnet`. Set `DOTNET_ROOT=D:\dotnet` and prepend `D:\dotnet` to `PATH`.
   Never invoke a bare `dotnet` — always call the SDK through `D:\dotnet` on `PATH` or `D:\dotnet\dotnet.exe`.
3. Build: `dotnet build "D:\Coding\UA Retail Store\StoreFlow.sln"`.
   Always pass `StoreFlow.sln` explicitly. There is a stray `StoreFlow.slnx` in the repository root that must
   not be used.
4. Run the automated suites: `dotnet test "D:\Coding\UA Retail Store\StoreFlow.sln"`. All four suites must be
   green before a case with an automated counterpart can be signed off.
5. Launch the app: `dotnet run --project "D:\Coding\UA Retail Store\src\StoreFlow.Desktop\StoreFlow.Desktop.csproj"`.
6. First run opens the first-run setup (`src/StoreFlow.Desktop/Views/FirstRunWindow.xaml`): store name, address,
   telephone, your display name, a password, a theme and a language. This creates the owner account. Subsequent
   runs open the sign-in window (`src/StoreFlow.Desktop/Views/LoginWindow.xaml`).
7. The application data lives under the Windows application-data folder, not next to the executable: the database
   is `%APPDATA%\StoreFlow\data\storeflow.db` (`src/StoreFlow.Infrastructure/AppPaths.cs`). To start from a
   clean store, delete that folder. Use a copy of it for the restore case (UAT-16).
8. The navigation rail and the command bar are defined in `src/StoreFlow.Desktop/Views/MainWindow.xaml` and
   `src/StoreFlow.Desktop/ViewModels/ShellViewModel.cs`; the rail entries are Dashboard, Products, Purchases,
   Inventory, POS, Sales, Reports, Users, Settings, Backup and Audit. Entries that this build does not provide
   yet are disabled in the rail. Labels come from `src/StoreFlow.Desktop/Localization/StringKeys.cs` and
   `src/StoreFlow.Desktop/Resources/Strings.resx`.
9. The **Purchases** and **Inventory** rail entries are live from Phase 2. **Purchases** is the purchase register;
   **Inventory** carries three tabs — *stock on hand*, *movements* and *counts* — under a valuation panel that
   shows what the shelf is worth. The dialogs are **New purchase**, **New stock count**, **Stock adjustment**,
   **Opening stock** and the reason prompt that a reversal and an approval both use.

### 2.2 Test data precondition

Every case runs against one seeded store so the numbers can be checked by hand. Create the catalogue below
first (Products → New Product), with tax disabled for the baseline pass (PRD §6.2 allows taxes to be disabled)
so every amount is hand-checkable. All products start with **zero stock**; stock is created by UAT-03.

| SKU | Name | Arabic name | Barcode | Unit | Sale price | Unit cost | Reorder level |
|---|---|---|---|---|---|---|---|
| RICE-001 | Rice 1kg | أرز | 6221000000017 | Piece | 450.00 EGP | 50.00 EGP | 2 |
| WTR-002 | Bottled Water 500ml | مياه | 6221000000024 | Piece | 15.00 EGP | 10.00 EGP | 5 |
| DET-003 | Detergent 1L | منظف | 6221000000031 | Piece | 150.00 EGP | 100.00 EGP | 3 |
| VAL-004 | Valuation Test Item | بند تقييم | 6221000000048 | Piece | 90.00 EGP | 50.00 EGP | 2 |

Barcode values are test fixtures created for this run. Store barcodes as text so leading zeros survive
(PRD §5.2).

One supplier is also needed for the purchase cases: name "Test Supplier", any phone number.

The moving-average case uses the PRD's own worked example (PRD §6.1): 10 pieces at EGP 50, then a receipt of
10 pieces at EGP 70, giving an average of EGP 60 and a sale of 3 pieces recording EGP 180 COGS.

### 2.3 Rules of execution

1. Each case is executed on a real Windows PC against a real local SQLite database — not a mock, not a shared
   file on network storage (PRD §8.4).
2. Execute the cases in PRD order. UAT-03 → UAT-04 → UAT-05 → UAT-06 form a chain on RICE-001 and are run on
   the same product state: receive 10, sell 3 (leaving 7 available), then attempt 9. Reset the cart between
   them, and reset the product's stock if a case is re-run out of order.
3. For every case that shows user-facing text, repeat the case in Arabic. Switch the locale with the language
   combo in the command bar of `src/StoreFlow.Desktop/Views/MainWindow.xaml` and confirm the same outcome, the
   same numbers and the same error in the Arabic interface. This document is written in English; Arabic
   correctness is additionally covered by the localisation parity test
   (`tests/StoreFlow.Desktop.Tests/LocalizationTests.cs`) and the separate RTL audit.
4. Record the outcome against the `Status` column and initial the `Signed off / date` column. A case counts as
   passed only when its automated counterpart (if any) is green **and** the manual step has been executed.
5. `Status` values used: `Not run`, `Pass`, `Fail`, `Blocked`. A `Fail` needs the invoice number, document
   number and the exact message shown on screen.

## 3. UAT cases (PRD §11)

| ID | Requirement(s) | Precondition | Steps | Expected result | Manual / auto | Status | Signed off / date |
|---|---|---|---|---|---|---|---|
| UAT-01 | PRD-01, PRD-02, PRD-03, BAR-01 | Owner signed in; no product uses barcode 6221000000017 | Products → New Product; enter the RICE-001 row; Save; then scan the barcode | Product saves, is searchable by name, SKU and barcode, and the scanner lookup returns RICE-001 | Manual (hardware) | Not run |  |
| UAT-02 | PRD-03, PRD-08 | RICE-001 exists with barcode 6221000000017 | Products → New Product; enter barcode 6221000000017 for a second product; Save | Actionable duplicate-barcode error; no second product saved; RICE-001 unchanged | Manual | Not run |  |
| UAT-03 | PUR-03, INV-01, INV-02 | RICE-001 exists with zero stock | Purchases → New purchase; pick Test Supplier; line RICE-001, qty 10, unit cost 50.00 EGP; Save draft; Post | On-hand becomes 10; exactly one +10 PurchaseReceipt movement referencing the document number | Auto | Not run |  |
| UAT-04 | POS-01, INV-01, INV-02, POS-08, BAR-01 | UAT-03 done: RICE-001 on hand 10 | POS → scan 6221000000017 three times → Confirm Sale | Exactly one invoice; on-hand 7; one -3 Sale movement; receipt total 1,350.00 EGP | Manual (hardware) | Not run |  |
| UAT-05 | POS-01, POS-02 | RICE-001 on hand 10, empty cart | POS → scan 6221000000017 → scan the same barcode again | One cart line with quantity 2, unit price 450.00 EGP, line total 900.00 EGP | Manual (hardware) | Not run |  |
| UAT-06 | INV-04 | RICE-001 on hand 7 (after UAT-04) | POS → set the RICE-001 line to 9 → Confirm Sale | Whole checkout rejected; no invoice, no payment, no stock change; on-hand stays 7 | Manual | Not run |  |
| UAT-07 | POS-07 | Cart with one RICE-001 line, qty 1 | Double-click Confirm Sale | Exactly one invoice created; stock deducted once; the second click is ignored | Manual | Not run |  |
| UAT-08 | POS-08, SAL-02, GEN-05 | 80 mm thermal printer selected; RICE-001 on hand at least 3 | Sell 1 unit and let it post; disconnect the printer; reopen the invoice and Print; also print the A4 invoice | Sale persisted once; printable error shown; no second sale; later reprint succeeds and is marked COPY | Manual (hardware) | Not run |  |
| UAT-09 | NFR-05, NFR-07 | Cart with one RICE-001 line; no invoices yet for this run | Click Confirm Sale and kill the process during posting; relaunch StoreFlow | Either the invoice, payment and stock movement all commit, or none do; no partial invoice; ledger and balance agree | Manual (hardware) | Not run |  |
| UAT-10 | SAL-03, SAL-04, SAL-05, INV-01 | A posted invoice for 3 units of RICE-001 exists | Sales → that invoice → Return; qty 1; reason; disposition Resellable; refund Cash; Post | Linked return document; refund 450.00 EGP; sellable on-hand 7 → 8; +1 ReturnRestock movement at 50.00 EGP | Manual | Not run |  |
| UAT-11 | SAL-03 | A posted invoice for 3 units with no prior return | Sales → that invoice → Return; enter qty 4; Post | Return rejected with an actionable message; no return document; stock unchanged | Manual | Not run |  |
| UAT-12 | PRD-05, PRD-01 | A posted invoice for RICE-001 at unit price 450.00 EGP exists | Products → RICE-001 → sale price 500.00 EGP → Save; reopen the invoice and the gross-profit report | Old invoice still shows 450.00 EGP and 1,350.00 EGP; profit unchanged; new sales use 500.00 EGP | Manual | Not run |  |
| UAT-13 | POS-03, GEN-02, GEN-03 | Cashier signed in; discount policy limit set; a manager account available | POS → RICE-001 qty 1 → apply a 50.00 EGP discount → Confirm Sale | Discount blocked or routed to manager approval; no invoice without approval; total never negative | Manual | Not run |  |
| UAT-14 | INV-05, INV-02 | RICE-001 book quantity known | Inventory → the **Counts** tab → New stock count; select RICE-001; counted 5 against book 7; Submit for approval; Approve with a reason; Post count | Book quantity unchanged until approval; after approval on-hand 5 with a -2 CountCorrection movement and reason; audit entry recorded | Auto | Not run |  |
| UAT-15 | REP-10 | A product with an Arabic name exists (WTR-002 "مياه") | Reports → Daily sales summary → Export CSV; open the file in Excel | CSV opens in Excel with Arabic text intact and no mojibake; headers and rows in order | Auto | Not run |  |
| UAT-16 | NFR-08, NFR-12 | Backup target configured; invoice count, sales total and on-hand quantity recorded | Backup → Back up now; copy the artifact to a clean test PC; install StoreFlow there; restore; compare the figures | Invoice count, sales total and inventory figures identical after restore; backup integrity check passes | Manual (hardware) | Not run |  |
| UAT-17 | NFR-01 | Network cable unplugged; RICE-001 in stock | POS → scan 6221000000017 → Confirm Sale → print the receipt | Checkout completes offline; local receipt prints; amount due 450.00 EGP; no network error | Manual | Not run |  |
| UAT-18 | PRD-01, SAL-01 | RICE-001 has a posted invoice | Products → RICE-001 → Deactivate; try to add it to a new POS sale; reopen the old invoice | RICE-001 not selectable in a new sale; old invoice opens with its original values; product not deleted | Manual | Not run |  |
| UAT-19 | PUR-04, INV-07 | VAL-004 on hand 10 at average cost 50.00 EGP | Purchases → receipt 10 units of VAL-004 at unit cost 70.00 EGP → Post; then sell 3 units | Inventory value 1,200.00 EGP and average cost 60.00 EGP; COGS on the 3-unit sale 180.00 EGP, snapshotted on the invoice line | Auto | Not run |  |
| UAT-20 | INV-08, SAL-04 | A posted invoice with at least 1 unit of RICE-001 exists | Sales → that invoice → Return; qty 1; reason; disposition Damaged; refund Cash; Post | Refund 450.00 EGP recorded under its own return number; sellable on-hand unchanged; damaged quantity +1; item not offered for sale | Manual | Not run |  |

### UAT-01 — Create product with new barcode

1. Sign in as the owner. Confirm no product uses barcode `6221000000017`.
2. Navigation rail → **Products** → **New Product**.
3. Enter SKU `RICE-001`, name `Rice 1kg`, Arabic name `أرز`, category (or explicit *Uncategorized*), unit
   **Piece**, sale price `450.00`, cost `50.00`, reorder level `2`, barcode `6221000000017`.
4. Press **Save**.
5. Search the product by name, then by SKU, then by barcode — each returns RICE-001.
6. Open the POS screen and scan the barcode with the USB scanner.

Expected: the save succeeds with no validation error; all three searches return the single row RICE-001; the
scan adds RICE-001 to the cart at 450.00 EGP. The barcode is stored as text.

### UAT-02 — Create another product with the same barcode

1. Products → **New Product**.
2. Enter a different SKU and name, and set the barcode to `6221000000017` — the barcode already used by
   RICE-001.
3. Press **Save**.

Expected: the save is refused with an actionable message naming the barcode that is already in use. No second
product row is created, the product count is unchanged, and RICE-001 still holds that barcode. The save must
not be possible by editing the RICE-001 row either. The unique index on `Products.Barcode`
(`src/StoreFlow.Infrastructure/Persistence/Configurations/CatalogConfigurations.cs`) is the backstop, but the
user-facing message is what this case verifies.

### UAT-03 — Receive 10 units into zero stock

1. Purchases → **New purchase**; supplier **Test Supplier**.
2. Add the line: RICE-001, quantity `10`, unit cost `50.00`.
3. **Save draft** the document.
4. Reopen the draft and confirm the on-hand quantity of RICE-001 is still `0` (a draft changes nothing).
5. Press **Post** (receive the goods). **Save and post** does both in one step.

Expected: on-hand quantity of RICE-001 becomes `10`; the stock movement ledger shows exactly one `+10`
PurchaseReceipt movement whose source document is the purchase number, with the posting user and timestamp; the
average unit cost becomes `50.00 EGP`. The document is no longer editable, and pressing **Post** a second
time is refused with a message that says the document has already been posted.

### UAT-04 — Sell 3 units by scanning

1. POS → the barcode input is focused (PRD §7.3).
2. Scan `6221000000017` three times.
3. Confirm the totals panel reads: Subtotal `1,350.00`, Discount `0.00`, Tax `0.00`, Amount due `1,350.00` EGP.
4. Choose **Cash**, enter the received amount, then press **Confirm Sale**.
5. Print the 80 mm receipt.

Expected: exactly one invoice is created with one line of quantity 3 at unit price 450.00 EGP and a grand
total of `1,350.00 EGP`; on-hand of RICE-001 becomes `7`; the ledger shows one `-3` Sale movement; the printed
receipt shows the store details, the line, and the total `1,350.00 EGP`. No second invoice appears.

### UAT-05 — Scan identical barcode twice

1. Start from an empty cart with RICE-001 in stock.
2. Scan `6221000000017` once.
3. Scan the same barcode a second time.

Expected: the cart holds **one** line, not two, with quantity `2`, unit price `450.00 EGP` and line total
`900.00 EGP`. The totals panel shows Subtotal `900.00` and Amount due `900.00` EGP. No stock is deducted until
checkout (PRD POS-02).

### UAT-06 — Attempt sale of 9 units with 7 available

1. POS → scan RICE-001 once, then set the line quantity to `9`.
2. Press **Confirm Sale**.

Expected: the entire checkout is rejected. No invoice is created, no payment record is written, no stock
movement is posted, and on-hand remains `7`. The message names the available quantity and the requested
quantity. There is no partial invoice and no partial stock deduction (PRD INV-04).

### UAT-07 — Double-click Confirm Sale

1. POS → scan RICE-001 once (quantity 1, 450.00 EGP).
2. Double-click **Confirm Sale** so that two clicks land inside the posting window.
3. Check the invoice list and the on-hand quantity of RICE-001.

Expected: exactly one invoice exists for the operation; the invoice count increases by one, not two; on-hand
drops by exactly one unit; a second click produces no second invoice, no second payment and no second stock
movement. If the second click is rejected with a message, the sale still stands exactly once.

### UAT-08 — Disconnect printer after sale posts

1. Confirm the 80 mm thermal printer is installed and selected in Settings.
2. POS → sell 1 unit of RICE-001; let the sale post; the print is attempted.
3. Disconnect the printer (or set it offline in Windows) after the sale has posted.
4. Go to Sales → the invoice → **Print** again. Also print the A4 invoice from the same invoice.
5. Reconnect the printer and print once more if needed.

Expected: the sale is persisted exactly once and is never re-posted by printing. While the printer is offline
the failure is reported on screen in plain language. Once the printer is back, the reprint succeeds and the
printed document is marked **COPY** (PRD SAL-02). The A4 invoice prints on A4 paper (PRD POS-08, NFR-09).
Stock and revenue are unchanged by any of the print attempts.

### UAT-09 — Force app shutdown during posting

1. POS → scan RICE-001 once and leave the cart ready.
2. Press **Confirm Sale** and, while the posting transaction is in flight, end the process (Task Manager →
   End task) or cut the power to the PC.
3. Relaunch StoreFlow and sign in.
4. Inspect the invoice list, the payment records, the stock movement ledger and the RICE-001 on-hand quantity.

Expected: the outcome is all-or-nothing. Either the invoice, the payment record, the `-1` Sale movement and
the on-hand decrease are all present, or none of them are. There is no invoice without its stock and payment
records and no stock movement without its invoice (PRD NFR-05, NFR-07). The ledger and the cached balance
still agree. Repeat the kill a few times at different points in the posting window.

### UAT-10 — Return 1 of 3 sold units

1. Sales → open the invoice created in UAT-04 (3 units of RICE-001, 1,350.00 EGP).
2. Choose **Return**; select the RICE-001 line; quantity `1`; enter a reason; disposition **Resellable**;
   refund method **Cash**; enter the refund amount `450.00`.
3. **Post** the return.
4. Check the return document, the refund, and the stock movement ledger.

Expected: a separate return document is created, linked to the original invoice, with its own number. The
refund of `450.00 EGP` is recorded with the chosen method. Sellable on-hand of RICE-001 goes from `7` to `8`,
and the ledger shows one `+1` ReturnRestock movement at the original invoice cost of `50.00 EGP`. The original
invoice is unchanged.

### UAT-11 — Attempt return of 4 from an invoice of 3

1. Use an invoice for 3 units of RICE-001 that has no prior return (a fresh invoice if UAT-10 already ran).
2. Sales → that invoice → **Return**; enter quantity `4`.
3. Press **Post**.

Expected: the return is rejected with an actionable message. No return document is created, no refund is
recorded, and the sellable on-hand quantity is unchanged. Cumulative returns can never exceed the originally
sold quantity (PRD SAL-03).

### UAT-12 — Change product price after sale

1. Note the invoice from UAT-04: unit price `450.00`, quantity 3, total `1,350.00`, and the gross profit
   reported for that date.
2. Products → open RICE-001 → set the sale price to `500.00` → **Save**.
3. Reopen the UAT-04 invoice. Then open Reports → Gross profit for that date.

Expected: the old invoice still shows unit price `450.00` and total `1,350.00` EGP, and its COGS snapshot is
unchanged, so the reported gross profit for that date does not change. A new sale after the price change
uses `500.00 EGP`. The current product price is not written back over the posted document (PRD PRD-05).

### UAT-13 — Cashier attempts unauthorized discount

1. Sign in as a **Cashier** whose role does not carry `SalesDiscountOverLimit`
   (`src/StoreFlow.Domain/Authorization/Permission.cs`). Have a manager account available.
2. POS → scan RICE-001 once → apply a discount of `50.00` EGP → **Confirm Sale**.

Expected: the discount is either refused outright, or the operation is held for manager approval and the
cashier cannot complete the checkout without it. No invoice is created without the approval. The error is
actionable and does not expose a raw exception (PRD POS-03, GEN-02). The grand total can never become
negative: with a `50.00` EGP discount on a `450.00` EGP line the amount due is `400.00` EGP. Approve the
request as the manager and confirm the sale then completes with the approved discount only.

### UAT-14 — Stock count differs by 2, then approve

1. Inventory → the **Counts** tab → **New stock count**; select RICE-001; note the book quantity (7 after
   UAT-04).
2. Enter the counted quantity `5` and **Submit for approval**.
3. Confirm the book quantity is still `7` before approval (a count does not change posted stock).
4. Press **Approve**, give the reason, and then press **Post count**.

Expected: the difference is shown as `-2` with an estimated value difference. After approval and posting,
the on-hand quantity of RICE-001 is `5`, and the ledger contains one `-2` CountCorrection movement that
references the count document and carries the adjustment reason. The count, the approver and the timestamp
are recorded in the audit trail (PRD INV-05, INV-02). The selling price is not altered by the count.

The approval needs a **second authorised person**: a clerk who counted the shelf cannot approve their own
count, and the application refuses it with a message saying so. An administrator — the owner of a shop
with one account — may approve their own count, because otherwise a one-account shop could never close a
count.

### UAT-15 — Export report containing Arabic names

1. Reports → **Daily sales summary** (REP-01), set a date range that covers a sale of WTR-002, whose name is
   `مياه`.
2. Export to CSV.
3. Open the exported file in Microsoft Excel.

Expected: the file opens in Excel with no repair prompt and no mojibake; the Arabic cell shows `مياه` as
readable Arabic, not `???` or a mis-decoded sequence. The header row and the data rows are intact and in
order. The file is UTF-8 with a byte-order mark, which is what makes Excel read it correctly
(`src/StoreFlow.Application/Common/CsvWriter.cs`, PRD REP-10). The automated counterpart is
`tests/StoreFlow.Application.Tests/CsvWriterTests.cs` (`Arabic_text_is_written_unchanged`,
`The_byte_order_mark_is_written_for_excel`); the manual step is the Excel open itself.

### UAT-16 — Back up then restore on a clean test PC

1. On the shop PC, record the exact invoice count, the total sales for today, and the on-hand quantity and
   inventory value of every product.
2. Backup → **Back up now** to the configured target (USB media or a cloud provider account such as the
   Google Drive, OneDrive, Dropbox or S3-compatible target defined by
   `src/StoreFlow.Application/Abstractions/IBackupTarget.cs`).
3. Confirm the backup log records the artifact path, its checksum and a successful result.
4. On a clean test PC with no StoreFlow data, install StoreFlow and sign in as the owner.
5. Restore the backup artifact and let the integrity check run.
6. Compare the figures from step 1 with the restored store.

Expected: the restored store has the same invoice count, the same sales total and the same inventory
quantities and values as the source PC; the restored database passes its integrity check; the restore is
recorded in the backup log and the audit trail. The restore is restricted to an administrator
(`Permission.BackupRestore`), and a restore point is preserved where possible (PRD §8.5, NFR-08).

### UAT-17 — Disconnect internet and make sale

1. Disconnect the network: unplug the cable and confirm the PC reports no connection.
2. POS → scan RICE-001 once → **Confirm Sale** → print the receipt.

Expected: the checkout completes with no network access and no network error. The receipt prints on the local
80 mm thermal printer. The amount due is `450.00 EGP` and the invoice is posted normally, with the stock
movement and the payment record written. No core workflow may depend on a network (PRD NFR-01).

### UAT-18 — Deactivate historically sold product

1. Products → open RICE-001 → **Deactivate**.
2. POS → try to add RICE-001 to a new cart, by scan and by search.
3. Sales → open the invoice from UAT-04 and its detail view.
4. Check the product in the Products list.

Expected: RICE-001 cannot be added to a new sale; the historical invoice still opens and still shows its
original values — product name, unit price `450.00`, quantity 3, total `1,350.00` EGP — and remains valid.
The product is deactivated, not deleted, and its history is intact (PRD PRD-01, SAL-01).

### UAT-19 — Receive higher-cost goods under moving average

1. Confirm VAL-004 is on hand `10` at an average cost of `50.00` EGP (create it with a receipt at
   `50.00` if it is not yet there).
2. Purchases → new document; supplier **Test Supplier**; line VAL-004, quantity `10`, unit cost `70.00` EGP;
   **Save**; **Post**.
3. Check the inventory valuation for VAL-004: quantity, average unit cost and total value.
4. Sell 3 units of VAL-004 at 90.00 EGP and open the invoice lines.

Expected, using the PRD's own worked example (PRD §6.1):

| Check | Expected value |
|---|---|
| Quantity on hand | 20 |
| Total inventory value | 1,200.00 EGP (500.00 + 700.00) |
| Average unit cost | 60.00 EGP (1,200.00 ÷ 20) |
| COGS on the 3-unit sale | 180.00 EGP (3 × 60.00), snapshotted on the invoice line |

The receipt at `70.00` must raise the average rather than overwrite it, and the COGS written on the invoice
line must be the snapshot taken at sale time. The automated counterpart is
`tests/StoreFlow.Domain.Tests/MovingAverageCostTests.cs`
(`Receipt_recomputes_the_average_as_the_prd_example_shows`); the manual step confirms the valuation and the
invoice snapshot in the UI.

### UAT-20 — Return damaged item

1. Sales → open an invoice that contains RICE-001.
2. Choose **Return**; quantity `1`; enter a reason; disposition **Damaged** (not resellable); refund method
   **Cash**; refund amount `450.00`.
3. **Post** the return.
4. Check the on-hand quantity, the damaged quantity, and try to sell RICE-001 again.

Expected: the refund of `450.00 EGP` is recorded under its own return number, with the reason, the damaged
disposition, the user and the timestamp. The sellable on-hand quantity of RICE-001 is **unchanged** — a
damaged return never inflates the quantity available to sell — and the damaged quantity increases by 1
(`StockDisposition.Damaged` in `src/StoreFlow.Domain/Enumerations.cs`). The returned unit is not offered for
resale. The original invoice is unchanged (PRD INV-08, SAL-04).

### Ledger reconciliation check (PRD §11, after the case table)

Run at least 1,000 automated mixed stock operations — purchase receipts, sales, returns, counts and
adjustments — then compare each product's cached balance against the aggregated signed stock movements for
that product. The expected discrepancy is **zero**. This is an automated check, not one of the 20 cases; it
belongs with the suites in `tests/`.

## 4. Device-dependent cases

These six cases need physical hardware or a physical device condition. They cannot be automated in CI and
they remain an **unverified residual risk** until a human signs them off on a real shop PC with the real
peripherals.

**UAT-01 — Create product with new barcode**
Hardware: a USB HID / keyboard-wedge barcode scanner.
Simulation if unavailable: type the barcode into the POS field manually. This exercises the manual-entry
path (BAR-04) only; the keyboard-wedge path stays unverified.

**UAT-04 — Sell 3 units by scanning**
Hardware: the USB barcode scanner and an 80 mm thermal receipt printer.
Simulation if unavailable: enter the barcode by hand and check the receipt in the on-screen print preview.
The physical scan and the physical receipt stay unverified.

**UAT-05 — Scan identical barcode twice**
Hardware: the USB barcode scanner.
Simulation if unavailable: type the barcode twice into the POS field. The increment-on-repeat behaviour is
checkable, but the scanner's suffix handling and timing (BAR-02) stay unverified.

**UAT-08 — Disconnect printer after sale posts**
Hardware: an 80 mm thermal receipt printer and an A4 printer, with the ability to take the printer offline
(disconnect the cable or the network print port).
Simulation if unavailable: select a printer that does not exist, or pause the print spooler. The failure
path is then exercised, but the actual paper output, the paper width and the A4 layout stay unverified.

**UAT-09 — Force app shutdown during posting**
Hardware: a PC where power can be cut, or a controlled kill of the process during the posting transaction.
Simulation if unavailable: close the application window during posting, or terminate the process from Task
Manager. That tests an orderly close and an ungraceful process kill, but it does not test the power-loss
path, so the WAL and journal recovery behaviour stays partly unverified.

**UAT-16 — Back up then restore on a clean test PC**
Hardware: a second, clean Windows PC and a backup target — USB media, or a real cloud provider account
(Google Drive, OneDrive, Dropbox, S3-compatible or WebDAV) for the remote target implementations of
`src/StoreFlow.Application/Abstractions/IBackupTarget.cs`.
Simulation if unavailable: restore on the same PC into a different data folder. That does not prove the
backup survives a different machine or a disk failure, so the recovery objective stays unverified.

Each of the six above must be executed on the shop's own scanner and printer, on the intended Windows
machine, before the release candidate is accepted (PRD §12.1 definition of done, NFR-09).

### 4.1 Which cases this build can reach

The catalogue, the inventory, the point of sale and the sales register are built, so these cases can
be executed end to end on this build:

| Case | Reaches |
|---|---|
| UAT-01, UAT-02, UAT-18 | The catalogue screens and the till. A product is found by its barcode at the till, so the till half of UAT-01 and UAT-18 can now be run. |
| UAT-03, UAT-14 | The purchase and inventory screens. Both are fully implementable now. |
| UAT-19 | The purchase and the valuation are implementable now; **step 4 (the sale and its COGS snapshot) needs Phase 5**, so the case cannot be signed off before then. |
| UAT-04, UAT-05, UAT-06, UAT-07, UAT-08 | The point of sale. Every one of these is implemented and proved in substance by the Phase 3 gate table; the manual steps are still waiting for a person to walk them. |
| UAT-10, UAT-11, UAT-12, UAT-20 | The sales register and the returns. The register, the return and the reversal are implemented and proved in substance by the Phase 4 gate table; **the gross-profit reading behind UAT-12 and UAT-20 needs Phase 5**, so those two cannot be signed off before then. |

Each phase says in its own report which of these rows it proved and which it could not reach, and
this plan and that report must not disagree. The gate table in
[`docs/phases/PHASE-2-INVENTORY.md`](../phases/PHASE-2-INVENTORY.md) is the Phase 2 answer: it
proves UAT-03 and UAT-14, proves the shelf half of UAT-19, and says plainly that the till half of
UAT-01 and UAT-18 waits for Phase 3. The gate table in
[`docs/phases/PHASE-3-POS.md`](../phases/PHASE-3-POS.md) is the Phase 3 answer: it proves UAT-01
for the till, UAT-06, UAT-07 and UAT-08 for the document, and UAT-13 for the block, and it says
plainly that UAT-09 — killing the application in the middle of a posting — belongs to Phase 7,
and that the register behind UAT-18 and UAT-19 is Phase 4 or later. The gate table in
[`docs/phases/PHASE-4-SALES-AND-RETURNS.md`](../phases/PHASE-4-SALES-AND-RETURNS.md) is the
Phase 4 answer: it proves UAT-10 and UAT-11 outright, proves the invoice half of UAT-12 and
UAT-20, and says plainly that the gross-profit reading behind them is Phase 5.

The `Status` column above stays `Not run` until a person executes the manual step, because rule 4
of §2.3 requires both the automated counterpart to be green **and** the manual step to have been
executed. A phase report is evidence that the automated half is green; it is not a sign-off.

The reports, the backup and the users screens are not built yet, so the cases that depend on them
cannot be executed. The rail entries for them are disabled rather than hidden, so the plan and the
application agree on what exists. The **Point of sale** rail entry is live from Phase 3, in both
languages, and the **Sales** rail entry is live from Phase 4, in both languages.

### 4.2 What to look at while running these cases

Running the application through UI Automation for Phase 2 turned up eight defects that a tester
would otherwise have to find by hand. They are fixed and pinned by tests, but they are worth a
glance, because each of them was invisible on the screen until the right action was taken:

| What to check | What was wrong | What to look for now |
|---|---|---|
| Picking a product in a purchase, a count or the opening balances | The picker did not write the choice back until the field lost the focus, which never happens inside a table cell — so the line saved with no product at all | Pick a product in a line; the quantity and cost fill in and the line saves with that product on it |
| The **movements** and **counts** tabs of the Inventory screen | The column headings came out blank, because a heading is not part of the visual tree and could not find the page it belongs to | Open both tabs; every column has its heading, in English and in Arabic |
| The ledger in the Inventory screen | The ledger showed the quantity and the balance but not which product had moved | The ledger names the product and shows its stock-keeping code beside the name |
| Every table in the application | A value that did not fit its column was cut through by the column next door, and the digits in a field were cut off at the bottom | No value is cut; a value that is too long is shortened with an ellipsis and the whole of it is shown when the pointer rests on it |
| The **stock on hand** tab of the Inventory screen | The badge that marks a low product read a property the stock position does not have, so the badge came out as an empty box | A product at or below its reorder level carries a badge that reads "Low" in English and "منخفض" in Arabic |
| The search box on every list screen | The placeholder was bound to the flag that says the box is empty, and the binding turned that flag around — so the placeholder appeared exactly when it should have disappeared | An empty search box says what it searches for; the placeholder goes as soon as you type |
| The **movements** tab of the Inventory screen | The moment a movement was posted did not fit its column: "2026-11-01 13:45" was shortened to "2026-11-01 13:…" | The whole timestamp is readable |
| Every amount — the valuation panel, the purchase totals, a stock value | The grids wrote the money one way (6,460.00 EGP) and the totals another (7600.00), so the same money looked like two different numbers | Every amount that is read is written the same way, with the currency; a field that is edited keeps plain digits |

Running the application through UI Automation for Phase 3 turned up twelve more defects, and each of
them was invisible until the right action was taken. They are fixed and pinned by tests, and they
are worth a glance because a tester would otherwise meet them and not know they were not supposed
to be there:

| What to check | What was wrong | What to look for now |
|---|---|---|
| The change on the receipt | The invoice subtracted the total from the amount paid, and the payment row subtracted it from the cash that was handed over — so the invoice said there was no change while the customer was owed forty pounds | The invoice, the payment row, the till and the receipt all carry the same change |
| The receipt | It never said how much cash was handed over, only what was paid and what came back | The receipt carries the cash received between the paid and the change |
| A quantity the selling unit cannot express | Two and a half pieces of something sold by the piece was quietly rounded to three, and the total was for three | The line is refused and the reason is on the screen; the total is for the line as it stands |
| A line for a product the shop does not sell | The cashier was told the same product was on the cart twice | The missing product is named, and the duplicate is not raised |
| Opening the till | The application died: the payment buttons passed the word "Cash" to a command that wanted the payment method | The till opens; the three payment buttons choose cash, card and other |
| Opening the till | The application died again: the list of waiting carts was ordered by a moment in time, which the database cannot sort | The till opens and the waiting carts are listed oldest first |
| The printer | The fallback face was added with a call that takes a file name, so it threw when the embedded faces could not be read | The receipt prints in Arabic on a machine with no font installed |
| Closing the receipt | The close button shut the whole application down | It closes the receipt and leaves the till open |
| A line the cart refuses | The till threw the cart's complaints away — the line was dropped from the total and nothing was said | The reason is on the screen, and it goes when the reason is answered |
| The numbers the cashier types | They were read with the language of the screen, and the Arabic decimal separator is not a dot — so `12.5` did not parse on an Arabic till | The same rule in both languages; `12.5` means twelve and a half on either screen |
| The invoice | The discount on the header was more than the sum of the discounts on the lines, so the invoice did not foot | The line discounts and the invoice-discount shares add up to the discount on the header |
| The invoice number | Emptying the cart wiped the message that said which invoice had been posted | The number is on the screen when the receipt closes |

Two more things are worth a glance, because they are new and not defects:

- **The taxable amount is on the receipt and on the screen.** It is what the tax was charged on,
  and the taxable amount plus the tax is always the total, whether the price carried the tax or
  not. A discount is a discount on the price and the tax follows the price, so on a price that
  does not carry the tax the customer saves a little more than the discount that was granted.
- **A cart that is put aside reserves no stock.** The goods are still on the shelf, because they
  have not been sold. The cart is a note, not a movement.

## 5. Sign-off
| Field | Value |
|---|---|
| Tester name | |
| Date | |
| Build version | |
| Result | |

Notes on the sign-off:

- A case counts as **passed** only when its automated counterpart (if any) is green in
  `dotnet test "D:\Coding\UA Retail Store\StoreFlow.sln"` **and** the manual step in §3 has been executed.
  A green automated suite alone does not sign off a manual case.
- The six cases in §4 cannot be signed off from CI results alone. They require a named tester, the date, and
  the identity of the shop hardware used.
- Any case marked `Fail` or `Blocked` must carry the invoice or document number, the exact message shown on
  screen, and the store state that produced it.
- This plan covers the manual pass for the PRD §11 cases. The localisation parity test
  (`tests/StoreFlow.Desktop.Tests/LocalizationTests.cs`) and the RTL audit cover the Arabic interface
  separately and are not a substitute for the Arabic repetition of each case described in §2.3.
