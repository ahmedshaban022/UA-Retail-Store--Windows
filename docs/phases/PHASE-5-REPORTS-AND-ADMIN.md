# Phase 5 — Reports and admin

The shopkeeper's questions. How much did we take today, what sold, what is on the shelf and what is
it worth, what came back, who took the money, and who did what. Phase 5 answers them on one screen
and exports the answers to a file that opens in Excel.

It also builds the three things a shop with more than one person needs: a discount that waits for a
manager instead of being refused or slipped through, an administrator who can add a user and give
somebody a role, and an audit trail that can be read by date and by the person who did the thing.

Every rule below was proved by the automated suite, and then proved again by driving the real
application through the reports, the export and the approvals screen, and taking the pictures the
application draws of itself.

---

## What was built

### One reader behind every report and behind the dashboard

`IReportReader` is the only way to ask the shop what happened. Every report is a method on it, and
each one returns **typed rows**: `DailySalesRow`, `SalesByProductRow`, `CashierShiftRow`, and so on.

The eleven reports PRD §5.8 names are all here:

| PRD | Report | The reader's method |
|---|---|---|
| REP-01 | Daily sales summary | `ReadDailySalesAsync` |
| REP-02 | Sales by product | `ReadSalesByProductAsync` |
| REP-03 | Best-selling products | `ReadBestSellersAsync` |
| REP-04 | Stock on hand | `ReadStockOnHandAsync` |
| REP-05 | Low/out-of-stock | `ReadStockOnHandAsync` with the status filter |
| REP-06 | Stock movement history | `ReadStockMovementsAsync` |
| REP-07 | Purchasing | `ReadPurchasesBySupplierAsync` |
| REP-08 | Gross profit | `ReadProfitAndLossAsync` |
| REP-09 | Returns | `ReadReturnsAsync` |
| REP-10 | Export reports to CSV | the export button, over the same rows |
| REP-11 | Cashier shift reconciliation | `ReadCashierShiftsAsync` |

Three more readings were added because the shop asked for them and they cost nothing extra on top
of the same reader: the **stock valuation** (REP-04's stock value with the average cost spelled
out), the **cashier's takings** (REP-01 per person), and the **audit trail read as a report**.

That matters for more than tidiness. The report screen builds its grid columns in code against those
typed rows, and the export writes the same rows to the file. The screen and the file cannot drift
apart, because there is only one shape underneath both.

The dashboard reads its money **through the same reader**. `EfDashboardReader` asks `IReportReader`
for the day's figures rather than working them out a second time, so the number on the dashboard and
the number in the daily sales report are the same number by construction. A test proves it: the
dashboard and the report are compared, field by field, for a day with a sale on it.

### Every figure is stated without the output tax

This is the one rule the whole phase rests on, and it is a house rule rather than a new one
(ADR-0002).

- `Gross sales` is what the customer paid for the goods, before the tax was taken off.
- `Tax` is reported on its own line.
- `Net sales` is `total − tax`, and it is the figure the day's takings are measured by.
- `Gross profit` is net sales **without** the tax, minus the cost of the goods (PRD §5.8).

The rule works the same way whether the shop's prices carry the tax or not, because the invoice
knows which it was. The arithmetic is in one place — `ReportRules` — and it is pure: no database, no
clock, no currency guesswork. `NetSales`, `GrossSales`, `LineNetRevenue`, `LineGoodsValue`,
`LineDiscount`, `LineCostOfGoodsSold`, `LineGrossProfit`, `NetRefund`, `ReturnCostOfGoodsSold`,
`ReturnProfitImpact`.

Splitting money across lines is exact. `ReportRules.Split` divides an amount in proportion to the
parts and gives the remainder to the last part, so the parts always add back up to the whole. A
return's tax is shared across its lines this way, which is why a return's lines add up to the refund
exactly, and to the tax exactly.

### A return takes the money off the day it happened

A return reduces **the day it happened**, not the day of the invoice it undoes. This is a change
from the way the dashboard used to work, and it was made for a reason: if a return reduces the day
of the original sale, then a report of "the last month" and a report of "today" answer the same
question in the same way, and the dashboard can be one of the reports instead of a second opinion
about them.

The arithmetic, worked out by hand in a test:

```
   the 2nd of March   sales 300.00   returns   0.00   net 300.00
   the 3rd of March   sales   0.00   returns 100.00   net -100.00
```

A reversal — the document that undoes an invoice outright — carries a **positive** amount and is
marked by `ReversesDocumentId`. It is subtracted from the day it happened, and the invoice it
undoes becomes `Reversed`, so the report queries include both `Posted` and `Reversed` invoices and
leave the reversals out of the invoice count. A reversal is a document, not a negative sale.

### The shelf is worth what the ledger says it is worth

The stock reports **always replay the ledger** up to the date of the report. They never read the
`StockBalances` cache.

That is the difference between a report that tells the truth about a past date and one that does
not. A test proves it: ten units are bought at fifty and ten more at seventy, and a report dated
**before** the second receipt has to leave that receipt out — it reads 10 units at an average of
50.00, worth 500.00, and not 20 units at 60.00 worth 1,200.00. A report dated after it reads the
moving average the PRD asks for: 20 units at 60.00, worth 1,200.00.

The moving average is the one in ADR-0003 and PRD §6.1: `(10 × 50 + 10 × 70) / 20 = 60.00`.

### The drawer can be reconciled

REP-11 asks for the cash opening and closing values and the variance. Two of those three numbers
are not in the database, because they are things a person counts: what the drawer held when the
day began, and what it was counted to hold at the end of it.

So the shop records them. `RecordDrawerCashCommand` writes the opening and the counted amount
against a cashier and a day, and the report reads them back with the cash that went in and the
cash that was refunded:

```
   opening      50.00
   taken       300.00
   refunded      0.00
   closing     350.00
   counted     350.00
   variance      0.00
```

The counts are kept in the shop's own `Settings` table, under a key like
`cash.drawer.2026-03-16.4`, and **no migration was needed**. They are two numbers per cashier per
day, written once, and the settings table already holds exactly that kind of small piece of shop
state. The variance is empty until somebody counts the drawer — a report that guesses the count
would be worse than one that admits it does not know.

The reason a report needs the reason: a return line may carry its own reason, and when it does not,
the reason of the return it belongs to applies to it. The report would otherwise say nothing about
why goods came back, when the cashier told the customer why.

### A discount over the limit waits for a manager

The shop has a discount limit (`StoreSettings.Defaults.MaximumDiscountPercent`, ten per cent by
default). A discount over it is not refused and not slipped through: **the sale is held**.

1. The cashier confirms a sale with a discount over the limit.
2. The till says the discount needs approving, and **nothing is written** — no invoice, no payment,
   no movement on the shelf. A test proves the ledger is untouched.
3. The request goes to the manager's approvals screen.
4. The manager approves or rejects it, with a reason.
5. The cashier confirms again, and the sale goes through with the approved discount.

The approval belongs to **an exact cart**. When the request is made, the lines and the invoice
discount are written down as a payload and hashed with SHA-256; that fingerprint travels with the
request. If the cart has changed since — a different quantity, a different discount — the approval
does not apply, and the till says so. A test proves it: approve a request, change the cart from
three units to two, and the sale is refused with `approval.mismatch` while no invoice is written.

Two more rules, each with a test:

- **Nobody approves their own request.** The request and the decision are two sessions, so the
  separation is real and not a flag in one object.
- **Asking twice for the same cart asks the manager once.** The fingerprint is what makes the second
  ask the same request.

The request is a row in the `ApprovalRequests` table, which was already in the schema from the
first migration, so this phase added no migration either.

### Users, roles and the audit trail

The **Users** screen lists the accounts, lists the roles with the permissions they carry, adds a
user with a hashed password, changes a role, and deactivates or reactivates an account.

Three rules are enforced, each with a test:

- a user name that is already taken is refused;
- **the last administrator cannot be deactivated** — a shop that locks itself out of its own shop
  is worse than any mistake it was protecting against;
- a signed-in user cannot deactivate themselves, for the same reason.

Deactivating keeps the record. It does not delete it, because the documents that user posted are
still signed with their name, and a report that changes its author when an account is closed is a
report nobody trusts.

The **audit** browser gained two filters: a date range and the actor. Both are optional parameters
on `ListAuditEntriesQuery`, so every existing call site still compiles and still means what it
meant.

### The export opens in Excel

REP-10 asks for UTF-8 with a BOM, so that Arabic names arrive in Excel as Arabic and not as a row
of question marks. The CSV writer already did that; what was missing was a test that pins it, and
there is now one: the file is written, read back, and read back **without a doubled byte-order
mark**. A file with two BOMs opens in Excel with an empty first column, which is a small thing that
ruins a spreadsheet.

The exported heading of the daily sales report is exactly:

```
Date,Invoices,Gross sales,Discounts,Tax,Returns,Net sales
```

---

## The rules this phase decided

| Rule | Where it lives |
|---|---|
| Every report figure is stated without the output tax, and the tax is on its own line | `ReportRules.NetSales`, `ReportRules.GrossSales` |
| A return takes the money off the day it happened, not the day of the invoice | `EfReportReader.ReadReturnLinesAsync` |
| A reversal carries a positive amount and is marked by the invoice it undoes | `SalesInvoice.ReversesDocumentId` |
| The stock reports replay the ledger to the date of the report and never read the balance cache | `InventoryLedgerReplay` |
| The dashboard reads its money through the report reader, so the two cannot disagree | `EfDashboardReader` |
| Splitting money across lines is exact, and the remainder goes to the last part | `ReportRules.Split` |
| A report row has one typed shape, and the screen and the file are built from it | the row records in `ReportModels.cs` |
| A return line with no reason of its own takes the reason of the return it belongs to | `EfReportReader.ReasonOf` |
| The drawer counts are kept in the settings table, and no migration is needed | `DrawerCashStore` |
| The variance is empty until somebody counts the drawer | `EfReportReader.ReadCashierShiftsAsync` |
| A discount over the limit holds the sale and writes nothing | `PosErrorCodes.DiscountApprovalRequired` |
| An approval belongs to an exact cart, and a changed cart invalidates it | `DiscountApproval.Fingerprint` |
| Nobody approves their own request | `ApprovalErrorCodes.SelfApproval` |
| Asking twice for the same cart asks the manager once | `DiscountApproval` |
| The last administrator cannot be deactivated | `UserAdminErrorCodes.LastAdministrator` |
| A user who is signed in cannot deactivate themselves | `UserAdminErrorCodes.SelfDeactivation` |
| A deactivated account keeps its record | `UserAdminCommandHandlers` |
| The exported file is UTF-8 with one byte-order mark | `CsvWriter` |

---

## How it was verified

### The automated suite

**376 tests, all passing** (`dotnet test StoreFlow.sln`):

| Project | Tests | Before | What the new ones cover |
|---|---|---|---|
| `StoreFlow.Domain.Tests` | 25 | 25 | — |
| `StoreFlow.Application.Tests` | 120 | 110 | The reports' money on their own: the net sales without the tax, the cost of the goods, what a return takes off the profit, the exact split of an amount across its parts, and the CSV file that opens in Excel and reads back without a doubled byte-order mark |
| `StoreFlow.Infrastructure.Tests` | 118 | 88 | The reports against a real SQLite file: every figure worked out by hand, the shelf valued at the moving average on a day before a receipt, the returns report, the cashier's reconciliation, a price that changes after the sale not reaching the report, the dashboard agreeing with the daily sales report, **the approvals**: a discount over the limit held for a manager with no invoice written, the sale going through only once the request is given, nobody approving their own request, and the request list that a manager opens — and **the users**: the last administrator that cannot be deactivated |
| `StoreFlow.Desktop.Tests` | 113 | 110 | The reports screen, the approvals screen, the users screen, the audit filters, the Arabic and English resources staying in step, and the till's scan |

The infrastructure tests are the ones that matter. They run against a real database file with the
real migrations, so the indexes, the check constraints and the triggers are in play.

### Driving the real application

`tools/smoke-reports.ps1` drives the running application through UI Automation, against a throwaway
data folder, and tells one story:

1. a product is created with a barcode and bought in;
2. three units are scanned at the till and the sale is confirmed;
3. the dashboard is opened and its figures are read;
4. five reports are run in turn and their figures are read;
5. the daily sales report is exported and the file is opened and read;
6. the approvals screen is opened and is empty.

Every control is found by its automation identifier, so the script does not depend on the wording of
the interface in either language. The pictures are taken by the application itself over its capture
pipe, the way `tools/smoke-sales.ps1` does it, because the desktop is locked and a screenshot taken
from outside would be a picture of a black rectangle.

### What the runs found

The smoke script is worth the time it takes because it drives the real application, and it found
four things the tests could not. Two of them were faults in the application, and both of them were
found only by driving it.

1. **The approvals screen took the application down with it.** Opening the page ordered the
   requests by `CreatedUtc` — a `DateTimeOffset` — and SQLite cannot sort by a date-and-time
   column. The exception was unhandled in the dispatcher, and the process simply ended. It fired
   with an empty approvals table, so it was not about the data. The list is now ordered by the
   identifier, which is the order the requests were written in, and the code says why. The
   codebase already knew this about SQLite — the audit browser says so in a comment — and the
   approvals list did not.

   A test now reads the list against a real database. The test was written **after** the fix, and it
   was then run against the old ordering to prove it fails: it fails with the same
   `NotSupportedException` the application died of.

2. **The till could lose a scan.** A scanner fires again as soon as the next item passes the
   reader, and looking a barcode up takes as long as the database takes. The till's scan command
   refused to run while it was busy, and the screen checked that before running it, so a scan that
   arrived during a lookup was **thrown away silently** — the customer's item was missing from the
   bill and nobody was told. The scan field was also emptied *after* the lookup, which meant a
   keyboard-wedge scanner's next burst of characters landed on top of the previous code.

   The field is now emptied the moment the code is taken, and the codes are queued and looked up
   one at a time in the order they were scanned. A scan that arrives during a lookup waits its turn
   instead of being lost. Two tests cover it: the field is empty the instant the code is taken, and
   a scan that arrives during a lookup still reaches the cart. The second test was run against the
   old behaviour to prove it fails, and it does.

3. **The harness was waiting for the wrong thing.** The script waited for the cart to have a row
   before it sent the next scan — but three scans of the same barcode are *one* line of three, so
   the row count never changed and the second and third scans were sent while the till was still
   answering the first. The script now waits for the cart to be worth one more unit than it was.
   This one was the script's own fault, and it is the reason the run before it reported the day's
   takings as 100.00 instead of 300.00.

4. **The report's date fields cut the date short.** The picture of the reports screen showed
   `2026-11-0` in both date boxes: the last digit of the day was clipped by the field, on the one
   screen where the day is the first thing the reader has to be sure about. The fields are now the
   width the dashboard's and the inventory's date fields have always been.

The screens themselves were also read for what they say rather than what they were asked to say:
the report grid's columns are built from the typed row, so the heading of the grid and the heading
of the exported file are the same words.

**The last run: 68 checks, no failures.** The dashboard read 300.00 of net sales and 150.00 of
gross profit on one invoice; the daily sales report read 300.00 gross and 300.00 net; the sales by
product read 3 units, 300.00 of revenue, 150.00 of cost of goods sold and 150.00 of gross profit;
the shelf read 7 units worth 350.00 at an average cost of 50.00; and the exported file held the
heading and one row of 300.00, with the byte-order mark at the front of it. Every one of those
figures is the figure worked out by hand before the run.

---

## What is not in this phase

- **Printing a report.** The reports export to a file; they do not print. PRD §5.8 does not ask
  for it, and the receipt printing of Phase 3 is a different thing.
- **A category filter on every report.** The reader carries a category filter and the reports that
  the PRD gives one to use it; the screen offers the date range and the cashier, which are the
  filters the PRD gives to most of the reports.
- **A shift, as a unit.** REP-11 asks for a shift. The report works by cashier and by day, because
  the application has no shift entity and inventing one is a Phase 6 decision.
- **Scheduled reports.** Nothing is sent anywhere on a schedule; the reports are run by hand.
- **The reports' Arabic column headings are the same words as the English ones.** The headings are
  the column names of the typed rows, so they are localised, but the *order* of the columns is the
  order of the row.
- **The gross profit of a return.** The return's effect on the profit is in the profit and loss
  report, but there is no report of "the profit the returns took away" on its own.

---

## Known papercuts

- **The cash drawer is counted by hand, on the reports screen.** The screen has two boxes for the
  opening and the counted amount, and a button. It is not a separate "count the drawer" workflow,
  and there is no second pair of eyes on the count — a manager who wants to check a cashier's count
  has nothing to check it against.
- **The variance is empty until the drawer is counted.** A report that says nothing is better than
  a report that guesses, but a shopkeeper reading the reconciliation for the first time has to be
  told that the empty variance means "not counted yet" and not "nothing is missing".
- **The returns report groups by product, not by return.** A return of three different products
  appears as three rows, and the reason of the return appears on each of them. That is what the
  PRD asks for, but a shopkeeper asking "what came back on Tuesday" has to read three rows.
- **The approvals screen does not filter.** It shows every request, and the manager reads them. A
  shop with a hundred requests a week has to scroll.
- **The audit browser filters, but does not export.** The reports export to CSV; the audit trail
  does not, even though the audit trail is the one thing a shopkeeper is most likely to want to
  hand to somebody else.
- **The report of the day's takings counts a reversal as a day with no invoice.** That is correct —
  a reversal undoes an invoice, it is not an invoice — but a day that had a sale and a reversal
  reads as one invoice and a negative net, which takes a moment to understand.

---

## The phase gate

PRD §11 numbers twenty acceptance cases. This table answers in **the PRD's own numbering**, the
same way the Phase 4 table does.

| PRD §11 row | What it needs | Where it stands |
|---|---|---|
| UAT-13 | A discount over the policy limit | **Proved**, by the automated suite and by driving the real application: the sale is held, nothing is written, a manager decides, and the sale then goes through with the approved discount |
| UAT-15 | A report exported with Arabic names in it | **Proved**, by the automated suite and by the smoke run: the exported file begins with the byte-order mark and the figures in it are the figures on the screen |

### The rows this phase did not reach

| PRD §11 row | What it needs | Where it stands |
|---|---|---|
| UAT-02 | A second product on the same barcode is refused | **Proved** in Phase 1, and it still holds |
| UAT-03, UAT-14 | The purchase receipt and the approved count | **Proved** in Phase 2 |
| UAT-04, UAT-05 | Selling three units by scanning, and scanning the same barcode twice | **Proved in substance** by the till tests and the smoke run — the scan that arrives during a lookup is the one this phase had to fix. The manual chain in `docs/uat/UAT-PLAN.md` is still waiting for a person to walk it |
| UAT-06 | A sale for more than the shelf holds | **Proved** in Phase 3 |
| UAT-07 | One sale per key | **Proved** in Phase 3 |
| UAT-08 | The sale stands when the printer is gone | **Proved for the document** in Phase 3 |
| UAT-09 | Killing the application in the middle of a posting | Phase 7 |
| UAT-01 | A product is found by its barcode | **Proved for the till** in Phase 3 |
| UAT-10 | One of three sold units comes back | **Proved** in Phase 4 |
| UAT-11 | More comes back than was sold | **Proved** in Phase 4 |
| UAT-12, UAT-20 | The price of a product is changed after it was sold | **Proved**, in both halves: the posted invoice keeps its price and its cost (Phase 4), and the report still reads the revenue, the cost of the goods and the profit the sale was made at — a test changes the catalogue price to five hundred and the report still reads three hundred, a hundred and fifty and a hundred and fifty |
| UAT-16 | A backup and a restore on a clean machine | Phase 6 |
| UAT-17 | A sale with the internet disconnected | Phase 6 |
| UAT-18 | A product deactivated after it had already been sold | **Proved**: the catalogue half in Phase 1, and the reports keep the historical figures in Phase 5 — the report reads the invoice, not the catalogue |
| UAT-19 | A receipt at a higher cost, and the cost of goods sold | **Proved**: the shelf half in Phase 2, and the cost of goods sold in this phase's reports, read from the cost the invoice was written down at |

**The phase is complete, and the gate is open for the shop's books.** Every report PRD §5.8 names
is built, the money in them follows one stated rule, the discount that is over the limit waits for
a manager, the users and the roles are manageable, and the audit trail can be read by date and by
person. Two rows of the gate were reached by this phase, and the two faults the smoke run found are
fixed and proved by tests that fail without the fix.
