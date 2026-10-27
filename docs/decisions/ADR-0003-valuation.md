# ADR-0003 — Stock valuation: moving weighted average in posting order

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-25 |
| **Deciders** | Implementation agent |
| **Phase** | 0 — Foundation |
| **Supersedes** | — |
| **Nature** | A policy choice, not a fact — pinned here so any future change is a deliberate, tested edit. |

## Context

A shop's stock has a value. The owner needs to know it, the sale invoice needs a cost of goods sold to report
a gross margin, and the ledger needs to reconcile. The question is **which cost** attaches to stock that was
bought at two different prices.

The PRD (§6.1) mandates **moving weighted average**, and gives the worked example: 10 units at 50 and 10 units
at 70 ⇒ average 60 per unit. The alternatives in general retail accounting are FIFO and LIFO. All three are
defensible; the PRD has already chosen one, so the work here is to specify the moving average precisely enough
that it cannot be implemented two different ways.

Four properties of the product constrain the specification:

1. **The ledger is append-only.** `StockMovements` rows are immutable and are protected by SQLite triggers.
   Corrections happen only through reversing documents. A valuation rule that requires rewriting history is
   therefore not implementable here at all.
2. **Documents can be backdated.** A purchase receipt entered today may carry a business date of last week.
   If the average were recomputed by business date, backdating would silently rewrite the cost of every sale
   posted in between. That is unacceptable — a posted invoice must keep the cost it had when it was posted.
3. **Returns must restore stock faithfully.** When a customer brings back a jar of jam, the shop gets jam
   back, and the shop's stock value should reflect that. But the jam had a specific cost, and returning it at
   today's average would be a fiction.
4. **Damaged goods are not sellable stock.** A returned item that the shop cannot resell is not inventory.
   Counting it as sellable would inflate the stock number and the valuation.

## Decision

### 1. The average is recomputed in posting order, never by business date

The authoritative order is `StockMovements.Id` — the sequence in which movements were actually posted. A
backdated document enters the sequence at the moment it is posted, so it affects the average from that point
forward and cannot retroactively alter an earlier posting's cost.

### 2. The formulas

Let `qty` be the current sellable quantity, `avg` the current average cost per base unit, and `costIn` the
incoming unit cost.

**Receipt** (purchase posting, opening balance, or a resellable return):

```
newAvg = round( (qty × avg + qtyIn × costIn) / (qty + qtyIn) )
```

If nothing was on hand (`qty + qtyIn == 0`), the average simply becomes `costIn`.

**Sale:**

```
cogsUnit = avg                       // snapshotted onto the invoice line
qty      -= qtySold
```

The cost of goods sold is the average **at the moment of the sale**, and it is copied onto the invoice line
as `UnitCostSnapshotMinor`. Once snapshotted, it is frozen: later receipts change the average, never a
posted invoice.

**Return (resellable):** the item goes back on the shelf **at the original invoice line's cost snapshot**, and
is modelled as a negative receipt — the same formula as a receipt:

```
newAvg = round( (qty × avg + qtyRestored × origCost) / (qty + qtyRestored) )
```

Because it is the same formula, a return **restores the pre-sale average exactly** when no other movement
intervened, and produces a correctly *different* average when one did. This is the property that makes the
policy honest rather than merely arithmetic.

Worked example — receipt then sale then return, no other movement:

| Step | Action | qty | avg | note |
|---|---|---|---|---|
| 1 | Receive 10 @ 50 | 10 | 50 | avg = costIn |
| 2 | Receive 10 @ 70 | 20 | 60 | (10×50 + 10×70) / 20 = 60 |
| 3 | Sell 5 | 15 | 60 | COGS unit cost = 60, snapshotted on the line |
| 4 | Return 5 @ original cost 60 | 20 | 60 | (15×60 + 5×60) / 20 = 60 — average restored |

Worked example — the same sale and return, but a receipt lands in between:

| Step | Action | qty | avg | note |
|---|---|---|---|---|
| 1 | Receive 10 @ 50 | 10 | 50 | |
| 2 | Receive 10 @ 70 | 20 | 60 | |
| 3 | Sell 5 | 15 | 60 | COGS unit cost = 60 |
| 4 | Receive 10 @ 90 | 25 | 72 | (15×60 + 10×90) / 25 = 72 |
| 5 | Return 5 @ original cost 60 | 30 | 68 | (25×72 + 5×60) / 30 = 68 — not 60, and correctly so |

Step 5 is the case that distinguishes a policy from a slogan: the returned item really was bought at 60, and
the average really is 68, because the shelf now holds a mix of three cost layers.

### 3. Damaged returns are a separate bucket

A return whose `IsResellable` flag is false is posted with `Disposition = Damaged` and increments
`DamagedQuantityScaled` on the balance. It **does not** touch `QuantityOnHandScaled` and **does not** affect
the moving average. Sellable stock and damaged stock are counted separately everywhere: in the stock
overview, in the valuation report, and in low-stock alerts (which watch the sellable quantity only).

### 4. Stock count adjustments are valued at the current average

An approved stock count writes an adjustment movement at the **current** average cost at the moment of
approval. Count differences are reported as a separate adjustment line in the valuation report and are never
netted into revenue.

### 5. Rounding

The average is stored as an integer number of minor units, so the recomputation rounds **once per movement**,
half away from zero (`MoneyMath.RoundToWholeMinorUnits`). This is the same rounding policy as ADR-0002 and is
applied at the same granularity — per movement, not per document.

### Implementation

`src/StoreFlow.Domain/Valuation/MovingAverageCost.cs` exposes:

- `AfterStockIn(quantityOnHandScaled, currentAverageCostMinor, quantityInScaled, unitCostMinor, quantityScale)`
  — the receipt formula.
- `AfterResellableReturn(...)` — identical to `AfterStockIn` **by construction**, not by duplicated arithmetic.
  There is one implementation, so the two paths cannot drift apart.
- `CostOfGoodsSoldPerUnit(currentAverageCostMinor, quantitySoldScaled)` — the snapshot source for a sale line.

Quantities are scaled integers (`10^quantityScale`), and the formula divides the scale back out before
dividing values, so a 3-decimal unit such as kilograms is handled without floating point.

## Consequences

**Positive**

- The valuation is O(1) per movement. There is no cost layer table and no FIFO queue, so a million movements
  cost a million constant-time updates rather than a layer explosion.
- Backdating is safe: it cannot rewrite the cost of a posted sale.
- A return restores the prior average exactly, which is the property a shop owner will check by hand.
- Damaged stock never inflates sellable quantity or valuation.
- The rule is small enough to state in one sentence on a printed policy page, and to test exhaustively.

**Negative**

- The average is **not** the price the shop actually paid for the most recent units. For a product bought at
  wildly different prices, the inventory valuation is an average, not a market fact. This is the PRD's
  choice, and it is the standard for small retail.
- The return-restores-the-average property holds only when no other movement intervened between the sale and
  the return. The worked example above shows the alternative case honestly rather than hiding it.
- Because a sale's COGS is snapshotted, the **gross margin reported on an invoice cannot be recomputed later**
  from the current average — it must be read from the snapshot. This is correct for an audit trail but means
  historical margin reports read the ledger, not the live balance.

**Contradictions to watch for**

- A report that recomputes COGS from the *current* average rather than from the snapshot will disagree with
  the invoices. The valuation report must read `UnitCostSnapshotMinor`.
- A report that sums the movement ledger and compares it to the cached balance will disagree if damaged
  quantities are included in one and not the other. The ledger reconciliation test must apply the same
  disposition filter to both sides.

## References

- `src/StoreFlow.Domain/Valuation/MovingAverageCost.cs`
- `src/StoreFlow.Domain/Money/MoneyMath.cs` (`RoundToWholeMinorUnits`)
- `tests/StoreFlow.Domain.Tests/MovingAverageCostTests.cs`
- `StoreFlow_Retail_Management_PRD_EN.md` §6.1 (valuation), §2.3, §11
- `docs/decisions/ADR-0002-money-and-rounding.md` — the rounding policy referenced above
