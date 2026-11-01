# Phase 4 — Sales and returns

The register. Every document that touched the money of a sale is on one screen: the sale itself,
the returns against it, and the reversal that may have undone it. A sale that has been posted is
never edited and never deleted — it is corrected by a linked document that carries the reason, the
money and the goods.

This phase builds that, and it builds it to be driven: every rule below was proved by the
automated suite, and then proved again by driving the real application through a sale, a return and
a reversal, and taking the pictures the application draws of itself.

---

## What was built

### A return is a document, not an edit

`SalesReturn` is its own document, with its own number (`RET-2026-0001`), its own lines, its own
reason and its own date. It points at the invoice it undoes through `OriginalInvoiceId`, and it
never touches the invoice's own figures. The sale keeps the price, the discount, the tax and the
cost that were snapshotted onto it the day it was made (PRD §5.7, SAL-03).

The only thing that moves on a posted invoice line is the counter of what has come back. That
counter is the one exception the database's immutability trigger now allows, and it is allowed
narrowly: the trigger compares every other column of the line one by one and refuses the write if
any of them has changed. A test proves the trigger still bites — it tries to change the price of a
posted line and the database refuses it.

### The refund is the invoice's own arithmetic

A return carries no price of its own. The operator says which lines come back and how many, and the
money comes from the figures the invoice already carries:

- the refund of a line is the returned quantity's share of what the customer paid for that line,
  so the discount and the tax come back with it;
- returning the whole line refunds the whole line exactly, with no rounding at all;
- returning part of it rounds once, to the nearest minor unit, with the product's rounding policy
  (ADR-0002).

The cumulative rule is a single comparison inside the posting transaction: what has already come
back plus what is coming back now must not exceed what was sold.

### Resellable and damaged are different buckets

A returned line is either **resellable** or **damaged**, and the difference is not a label — it is
where the stock lands (ADR-0003, PRD INV-08).

- A **resellable** return goes back on the shelf through the same ledger that took it off, valued
  at the cost that was snapshotted on the invoice line. The moving average is restored exactly the
  way a receipt restores it, because it is the same formula and there is only one of it.
- A **damaged** return goes into the damaged bucket. It never touches the quantity available to
  sell and it never moves the average. A damaged box on the shelf is not stock the shop can sell.

### A reversal is the other way to correct a sale

Some sales should never have happened: the wrong item was keyed, the wrong price was read out. A
**reversal** undoes the whole invoice in one go, and it is a document of its own (`REV-2026-0001`)
that points back at the invoice through `SalesInvoice.ReversesDocumentId`.

- The original invoice is marked `Reversed`. It is never edited and never deleted.
- The reversal carries the lines of the original, the money that goes back, and the reason.
- The stock comes back at the cost it left at, through the same ledger, in one transaction with the
  refund.
- A posted invoice can be reversed **once**. The unique index on `ReversesDocumentId` is the
  database's half of that rule.

### The refund is money, and it is written down like money

Every refund is a payment row of its own, marked `IsRefund`, pointing either at the return or at
the reversal. The `Payments` table now carries a check constraint: a payment belongs to a sale or to
a return, never to both and never to neither.

The register's **Total** column is signed. A sale is positive; a return and a reversal are
negative, because in both of those money is going back to the customer. A column that nets is worth
more to a shopkeeper than a column that cannot be added up.

### The register itself

One screen, in Arabic and in English, with the same master–detail shape the purchase register
uses:

- **filters** — a search box that matches the document number or the cashier's name, a from-date, a
  to-date, and a cashier picker;
- **the register** — number, kind, date, cashier, lines, total, returned, and the invoice the
  document belongs to;
- **the detail** — the document that is picked: its lines, its money, the returns recorded against
  it, and the invoice a reversal undoes or the reversal that undid it;
- **two commands** — **Return** and **Reverse invoice**, enabled only when the selection allows
  them, and both asking for a reason first.

The return dialog is where the operator types how many come back and whether they can be sold
again, with the refund of each line and the total shown as they type. The reversal dialog is
smaller: a reason, the way the money goes back, and what becomes of the goods.

### The interface speaks both languages

Every string on the three screens comes from the resource sets, and both sets carry the same keys —
a missing translation fails the build rather than shipping. The screens follow the direction of the
page, so in Arabic the amount comes before the currency in the money columns.

---

## The rules this phase decided

| Rule | Where it lives |
|---|---|
| A posted invoice is never edited and never deleted; it is corrected by a linked document | `SalesInvoice.ReversesDocumentId`, `SalesReturn.OriginalInvoiceId`, the immutability triggers |
| The only thing that may change on a posted invoice line is the counter of what has come back | `trg_SalesInvoiceLines_NoUpdate`, compared column by column |
| A return carries no price of its own; the refund comes from the invoice's own figures | `SalesRules.RefundForQuantity` |
| The cumulative quantity returned may never exceed the quantity sold | the single comparison inside `PostReturnCommandHandler` |
| Resellable and damaged are different buckets, and only the resellable one reaches the shelf | `StockMovementType.ReturnRestock`, `StockMovementType.ReturnDamaged` (ADR-0003) |
| A posted invoice may be reversed only once | the unique index on `SalesInvoice.ReversesDocumentId` |
| A refund is money and is written down like money | `Payment.IsRefund`, `Payment.SalesReturnId`, `CK_Payments_SingleOwner` |
| The register's money column nets | `SalesRules.SignedAmount` |
| A return without a reason is not posted | `SalesErrorCodes.ReasonRequired` |
| A quantity the selling unit cannot express is refused, not rounded | `SalesRules.FitsScale` |

---

## How it was verified

### The automated suite

**322 tests, all passing** (`dotnet test StoreFlow.sln`):

| Project | Tests | Before | What the new ones cover |
|---|---|---|---|
| `StoreFlow.Domain.Tests` | 25 | 25 | — |
| `StoreFlow.Application.Tests` | 110 | 102 | The sales rules on their own: the number of a document, the refund of a whole line and of a part of one, the rounding of that refund, the remainder of a line, the sign the register shows, and the quantity a unit cannot express |
| `StoreFlow.Infrastructure.Tests` | 86 | 71 | The returns and the reversals against a real SQLite file: the return document and its link, the refund row, the stock that comes back, the damaged bucket, the average that is restored, the cumulative rule, the refused returns, the reversal and its stock, the double reversal that is refused, the register that signs its column, and the immutability trigger that still refuses an edited price |
| `StoreFlow.Desktop.Tests` | 101 | 101 | The sales screen: every error code becoming a message of its own, the refund the dialog offers being the refund the ledger will post, and the identifiers the smoke script drives the screen by |

The infrastructure tests are the ones that matter. They run against a real database file with the
real migrations, so the unique indexes, the check constraints and the triggers are in play.

### The ledger reconciliation

The PRD asks for a ledger that reconciles. The test drives the real handlers — receipts, sales,
resellable returns, damaged returns and adjustments — against a real database until **a thousand
mixed operations** have gone through, and then compares the cached balance of every product with
the sum of its signed movements, sellable and damaged separately.

**The discrepancy is zero.** The last movement of every product also carries the balance it left
behind, so the ledger can be read on its own without the balances at all.

### Driving the real application

`tools/smoke-sales.ps1` drives the running application through UI Automation, in Arabic and then in
English, against a throwaway data folder. It tells one story:

1. a product is created with a barcode and bought in;
2. three units are scanned at the till and the sale is confirmed;
3. the register is opened and the invoice is found in it;
4. one of the three units is returned, with a reason, and the stock goes back on the shelf;
5. a second sale is made and reversed;
6. the language is switched and the register is read again in English.

Every control is found by its automation identifier, so the script does not depend on the wording
of the interface in either language.

---

## What is not in this phase

- **The reports.** The register is a history, not a report. The eleven reports of PRD §5.8, the
  gross-profit reading behind a price change and the CSV export of the register are Phase 5.
- **The daily takings on the dashboard.** The dashboard's net sales already takes a return and a
  reversal off the day they happened, but it is still a single figure for today and not a report.
- **Reprinting a receipt from the register.** The till can reprint a receipt; the register cannot
  yet print the invoice it is showing. PRD SAL-02 asks for it and Phase 5 is where the printing
  side of the register belongs.
- **A return that crosses two invoices.** One return belongs to one invoice. A customer who
  brings back goods from three different days is three returns.
- **The discount on a return.** The refund carries the discount and the tax that came back with the
  line, but the shopkeeper cannot yet agree a different discount on the way out.

---

## Known papercuts

- **The register lists the reversal as an invoice of its own.** That is deliberate — a reversal is
  a document — but it means a reader sees two rows for one sale, and has to read the "Against"
  column to see that they belong together.
- **A return cannot be undone.** A return that was posted by mistake is corrected by a second
  return of the same goods, which is clumsy but honest: the first return stays on the record. The
  reversal covers the whole invoice only.
- **The cashier filter lists only the people who have posted a sale.** A cashier who has not sold
  anything yet is not in the list, so a shop with several cashiers cannot filter by an empty one.
- **The register has no column for the cost of goods sold.** The invoice carries the cost
  snapshot, and it is in the detail, but the register does not show it. That is a report's column.

---

## The phase gate

PRD §11 numbers twenty acceptance cases. This table answers in **the PRD's own numbering**, the
same way the Phase 3 table does.

| PRD §11 row | What it needs | Where it stands |
|---|---|---|
| UAT-10 | One of three sold units comes back | **Proved**, by the automated suite and by driving the real application |
| UAT-11 | More comes back than was sold | **Proved**, and the stock does not move |
| UAT-12 | The price of a product is changed after it was sold | **Proved for the invoice** — the posted invoice keeps its price and its cost, and the change does not reach it. The gross-profit reading behind the change is Phase 5 |
| UAT-20 | The price is changed after a return, and the return is still right | **Proved for the invoice** — the return keeps the price the invoice was written down at, and the later change does not reach it. The gross-profit reading is Phase 5 |

### The rows this phase did not reach

| PRD §11 row | What it needs | Where it stands |
|---|---|---|
| UAT-02 | A second product on the same barcode is refused | **Proved** in Phase 1, and it still holds |
| UAT-03, UAT-14 | The purchase receipt and the approved count | **Proved** in Phase 2 |
| UAT-04, UAT-05 | Selling three units by scanning, and scanning the same barcode twice | **Proved in substance** by the sale tests and the till smoke run; the manual chain in `docs/uat/UAT-PLAN.md` is still waiting for a person to walk it |
| UAT-06 | A sale for more than the shelf holds | **Proved** in Phase 3 |
| UAT-07 | One sale per key | **Proved** in Phase 3 |
| UAT-08 | The sale stands when the printer is gone | **Proved for the document** in Phase 3 |
| UAT-09 | Killing the application in the middle of a posting | Phase 7 |
| UAT-01 | A product is found by its barcode | **Proved for the till** in Phase 3 |
| UAT-13 | A discount over the policy limit | **Proved for the block** in Phase 3 |
| UAT-15 | A report exported with Arabic names in it | Phase 5 |
| UAT-16 | A backup and a restore on a clean machine | Phase 6 |
| UAT-17 | A sale with the internet disconnected | Phase 6 |
| UAT-18 | A product deactivated after it had already been sold | The catalogue half is proved; the register half is Phase 5 |
| UAT-19 | A receipt at a higher cost, and the cost of goods sold | The shelf half is proved in Phase 2; the sale half is Phase 5 |

**The phase is complete, and the gate is open for the register.** Everything the sales register and
the returns are responsible for is built, proved by tests, and proved again by driving the real
application through a sale, a return and a reversal. The rows that need a report or a sheet of
paper cannot be run here, and saying otherwise would be a lie.
