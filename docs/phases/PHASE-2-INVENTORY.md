# Phase 2 — Inventory

**Status:** complete · **Date:** 2026-11-01

The third of eight phases. This phase ends with a shop's stock in place: a purchase receipt drafted
and posted, the goods received, a ledger that says exactly how the shelf got to where it is, a
balance that agrees with it, a low-stock flag, a stock count that waits for a second pair of eyes
before it changes anything, a manual adjustment that leaves a reason behind it, and a valuation of
what is on the shelf.

---

## What was built

### The inventory in the application layer

The inventory is a set of commands and queries that mean the same thing wherever they are called
from, and they live in `src/StoreFlow.Application/Inventory`:

| File | What it holds |
|---|---|
| `InventoryCommands.cs` | The writes: save a purchase, post it, reverse it, export the register, record opening stock, post and approve an adjustment, save, submit, approve, post and cancel a stock count |
| `InventoryQueries.cs` | The reads: the purchase register, one purchase with its lines, the stock balances, the ledger, the low-stock list, the valuation, the stock counts, one count with its lines, and the pickers the screens need |
| `InventoryValidation.cs` | Every rule about what a valid line, adjustment, count or opening balance is, as a pure function that returns a list of issues with a stable code |
| `InventoryRules.cs` | The numbers the policies are made of: when an adjustment is large enough to need an approver, and how a document number is shaped |
| `InventoryErrorCodes.cs` | The stable codes the layer answers with, so the interface can localise the message instead of guessing at the wording |
| `InventoryDtos.cs` | The shapes that cross the boundary, already formatted for display |

The rules are **pure and separate from the database**, exactly as the catalogue's are.
`InventoryValidation` answers "is this quantity usable in this unit" and "does this adjustment
carry a reason" with a list of problems, each tagged with a code such as
`inventory.negative_stock`. The infrastructure turns those codes into failures, and the desktop
turns them into sentences in the shopkeeper's language.

### The ledger has exactly one writer

`src/StoreFlow.Infrastructure/Inventory/InventoryLedger.cs` is the **only** place a stock movement
is written. Every entry to the ledger and every change to the cached balance goes through it, so
the two cannot drift apart inside a transaction (PRD §5.4 invariant 1).

One call does four things at once:

1. it refuses a movement that would take the sellable quantity below zero;
2. it recomputes the moving average **in posting order**, never by business date (ADR-0003);
3. it writes the ledger row, with the running balance it produced;
4. it updates the cached `StockBalances` row and bumps its version.

The moving average is computed by `MovingAverageCost`, the same function the domain tests already
pin. A stock-out never touches the average; a stock-in recomputes it; a damaged movement touches
neither the sellable quantity nor the average, because damaged stock is tracked in its own bucket
and never inflates what can be sold.

`InventoryLedgerReplay` replays the ledger to a point in time using **exactly the same rules**, and
the valuation report uses it. A report date that falls before a backdated movement therefore leaves
that movement out, which the cached balance alone could not do.

### A purchase is a document, and a correction is another document

A purchase is drafted, then posted. Drafting changes nothing in stock. Posting increases the stock
**exactly once** and writes one `PurchaseReceipt` movement per line, each carrying the document
number it came from.

Posting a purchase that has already been posted is refused (`inventory.not_draft`), so the goods
cannot be received twice.

A posted purchase is not edited and not deleted. It is **reversed**: the application writes a
second document that points back at the first (`ReversesDocumentId`), writes a `Reversal` movement
for every line, and marks the original `Reversed`. The history is therefore complete — the receipt
that happened *and* the correction that undid it are both on the record, which is what
PRD PUR-06 asks for.

The reversal is refused when the stock has already gone: a reversal that would take the shelf
negative fails with `inventory.negative_stock` and the transaction rolls back whole.

### Opening stock is a movement, never an edit

Opening balances are recorded as `Opening` movements, so they land in the ledger with a date, a
reason and an author, and the balance follows from them rather than being set behind the ledger's
back (PRD INV-06).

Opening stock is only allowed on a product that has **no movements at all**. A product that
already has history has its opening balance inside that history, and offering to set it again would
be a second source of truth. The application refuses it with `inventory.already_stocked` and
points the operator at an adjustment instead.

### A count never changes stock by itself

Counting is a three-step dance, and the middle step is the point of the whole feature:

| Step | What it does | What it does *not* do |
|---|---|---|
| **Save** | Records the counted quantities and the book quantities as they were when the count started | Touch no stock |
| **Submit → Approve** | Asks a second authorised person to sign the difference off | Touch no stock |
| **Post** | Writes one `CountCorrection` movement per line that differs from the book | Change anything that was counted correctly |

The book quantity is captured at the moment the count is saved, so a later receipt does not
retroactively change what the counter was comparing against.

The approval is a real separation of duties: the person who counted cannot approve their own
count (`inventory.self_approval`). The one exception is an **administrator** — the owner of a
one-account shop is the only person who could ever sign off the count, and a shop that can never
close a count is a shop with a broken count. That exception is a deliberate decision, it is
written down here, and it is pinned by a test.

The count's reason is required at approval, not before, because until somebody approves it there
is nothing to explain.

### An adjustment that is too large waits for an approver

A manual adjustment carries a product, a signed quantity, a cost and a reason. When it is large —
**100 base units or more, or a value of 1 000.00 or more, whichever comes first** — it is not
posted. Instead an `ApprovalRequest` of kind `StockAdjustment` is written with the whole change in
its payload, and the adjustment waits for somebody who holds the stock-approval permission.

The thresholds live in `InventoryRules` as named constants, in one place, so the rule is readable
rather than buried in a handler. They are a Phase 2 default: a shop that wants a different policy
changes one file, and the tests follow.

As with the count, the person who raised the adjustment cannot approve it — unless they are an
administrator.

### The screens

| Screen | What it is for |
|---|---|
| **Purchases** | The purchase register: search by document number or supplier, a date range, a supplier filter, and the commands to open, post, reverse and export a receipt |
| **Inventory** | Three ledgers on one screen — **stock on hand**, the **movements** that produced it, and the **counts** that were taken — under a valuation panel that says what the shelf is worth right now |
| **Purchase editor** | A dialog for one receipt: supplier, date, discount, notes, and a table of lines with product, quantity, unit cost and tax |
| **Stock count** | A dialog for one count: the products and the quantities the counter found |
| **Stock adjustment** | A dialog for one adjustment: product, quantity change, unit cost, sellable or damaged, and the reason |
| **Opening stock** | A dialog for the opening balances of a shop that is starting |
| **Reason prompt** | The one place that asks for a reason — a reversal and an approval both use it |

The valuation panel carries the report date. A date in the past replays the ledger; today or later
reads the movements as they stand.

The register and the stock screens are **two entries in the navigation rail**, not tabs inside one
screen, because receiving goods and counting stock are two different kinds of work.

### The interface speaks both languages

151 new strings were added to `StringKeys.cs`, `Strings.resx` and `Strings.ar.resx`: the screen
titles, every column heading, every button, every field label, every status, every kind of stock
movement and every error message the inventory can produce.

The Arabic resource set is not a translation of the English one with the words swapped. The
quantity fields keep their numbers **left to right** inside an Arabic line, because a quantity
typed as `12.50` has to read the same way in both languages. The pickers show the stock-keeping
code next to the name — `RICE-001 - أرز 1كجم` — because the code is the one thing about a product
that does not change with the language, and because two products may share a name.

The error messages are localised **from the stable code**, not by parsing an English sentence:
`InventoryErrorMessages` maps `inventory.negative_stock` to the Arabic string and the English
string from the same table, so an Arabic operator is never shown an English error.

---

## The rules this phase decided

Six decisions were not written down anywhere before this phase, and each of them changes what the
application does:

1. **A purchase is reversed, never edited.** The alternative — editing a posted receipt — would
   silently change history that a cost was already calculated from.
2. **The count's book quantity is captured when the count is saved**, not when it is approved. A
   receipt that arrives during the count must not change what the counter was comparing against.
3. **An administrator may approve their own count and their own adjustment.** Segregation of
   duties is the default; a one-account shop is the exception, because the alternative is a feature
   that cannot be used at all.
4. **The valuation report always replays the ledger**, even for today. The cached balance is a
   cache; the ledger is the record. A report date that falls before a backdated movement has to
   leave that movement out, and the cache cannot answer that.
5. **A value is shortened, never cut.** A table column is a fixed width and the value beside it is
   not, so the rule that decides what happens when they disagree belongs to the theme rather than
   to each screen: the value is trimmed with an ellipsis, it keeps a margin on both sides, and the
   whole of it is one hover away. The same rule applies to the words in a box, which are never cut
   off at the bottom.
6. **An amount that is read is written by the theme; an amount that is edited is written plain.**
   One shelf is worth one number: the grids, the valuation panel and the totals of the purchase
   editor all write `6,460.00 EGP`. A field the owner types over writes `1200.00`, because a
   thousands separator in the middle of it is read back as a different number by anybody who writes
   a decimal comma.

---

## How it was verified

### The automated suite

**253 tests, all passing** (`dotnet test StoreFlow.sln`):

| Project | Tests | What the new ones cover |
|---|---|---|
| `StoreFlow.Application.Tests` | 83 | The inventory's validation rules: a quantity the unit cannot express, a negative cost, a repeated product, a missing reason, an adjustment that needs an approver, the document number's shape |
| `StoreFlow.Infrastructure.Tests` | 57 | The purchase and inventory handlers against a real SQLite file with the real migrations and the real triggers |
| `StoreFlow.Desktop.Tests` | 88 | The inventory screens' error codes becoming localised messages, the numbers the operator types being read the same way in both languages, the editor flows against a real database, the column heading finding its words, and the rules that keep a table readable — including **every column measured against the value it has to hold** |

The infrastructure tests are the ones that matter. They run against a real database file, so the
unique indexes, the immutability triggers and the negative-stock trigger are all in play. Among
them:

- **The ledger reconciles to the balance.** After a receipt, an adjustment and an opening entry,
  the sum of the sellable movements of every product equals its cached quantity, exactly.
- A draft purchase changes no stock at all.
- A posted purchase increases the stock exactly once, and posting it twice is refused.
- A reversal returns the stock to where it was, and is refused when the stock has already gone.
- A count changes nothing until it is approved and posted, and then it is off by exactly the
  difference the counter found.
- A count cannot be approved by the person who counted it — and an administrator can.
- A large adjustment waits for an approver and reaches the ledger only once that approver signs.
- A small adjustment reaches the ledger straight away.
- Damaged stock never inflates the sellable quantity.
- Opening stock is recorded as an opening movement, and refused on a product that already has one.
- The valuation uses the moving average: ten at 50.00 and ten at 70.00 is a shelf worth 1 200.00 at
  an average of 60.00, and the same report dated before the second receipt says 500.00.
- Low stock lists the products at or below their reorder level, and only those.
- The purchase register exports to a CSV the application can read back.

### Driving the real application

`tools/smoke-inventory.ps1` drives the running application through UI Automation, in Arabic and
then in English, the way Phase 1's `tools/smoke-catalog.ps1` does. Every control is found by its
automation identifier rather than by its label, so the script does not depend on the wording of the
interface in either language.

The script creates two products, receives twenty of one of them on a purchase, reads the balance
and the ledger, takes a stock count of eighteen, approves it, posts it, posts a manual adjustment,
and then reads the same screens in English. The pictures it takes are in
[`docs/phases/phase2-screens/`](phase2-screens/).

### What running it found

The script is the part of the verification that earns its keep. Every one of the eight defects below
was invisible to the unit tests, because the unit tests never put a picker inside a table cell,
never opened a tab that the screen had not opened yet, never asked what a column heading could
see, and never looked at a picture of the finished screen.

**1. A picker in a table cell never wrote its choice back.** WPF defers a `Selector`'s source
update until the control loses the focus — and a control inside a table cell never loses the focus,
because the cell takes it back at once. Every line of a purchase, a count and the opening balances
therefore saved with no product on it, and the screen said only "this value is required". The
Phase 1 product editor had the same fault in its three pickers. All eleven picker bindings now
write back as soon as the choice is made, and `ComboBoxBindingTests` and `InventoryEditorFlowTests`
pin both the behaviour and the flow.

**2. The column headings of the movements and the counts came out blank.** A column of a data grid
is not part of the visual tree, so its heading cannot reach the page the way the rest of the screen
does: WPF hands the heading a context of its own, and a heading that reads from that context finds
nothing. The headings of the *first* tab worked, because that table is realised while the page is
being laid out; the headings of the other two did not, because their tables are realised long
afterwards, when the user opens the tab.

Naming the table with `x:Reference` — the obvious answer — is worse than the disease: the table
holds the heading that names it, and the markup loader cannot resolve that without a cycle. It
throws `Cannot call MarkupExtension.ProvideValue because of a cyclical dependency`, and takes the
application down with it. The fix is `GridHeadingContext`, which hands every heading the table's
own words when the table is realised. `GridHeadingTests` and `GridHeadingSourceTests` pin both the
fix and the three ways that do not work.

**3. The ledger did not say which product had moved.** The query read the movement's product name
and its stock-keeping code without loading the product, so the ledger showed the quantity and the
balance and nothing else. A missing `Include`, found by dumping the database of a failed run.

**4. Values were cut by the column next door.** A column is a fixed width and the value beside it
is not, so a value that did not fit was sliced through by the boundary of the next column — "Count
correctio" instead of "Count correction", "One bag was dam" instead of "One bag was damaged". In
the other direction, the digits in a text box were cut off at the bottom, because the box left
eighteen pixels for a line of text that needs twenty. The Arabic purchases screen was worse still:
its toolbar was wider than the window, and the last button was pushed off the edge.

The fix has four parts, and each is pinned by a test:

- **Every cell keeps a margin on both sides**, and the heading keeps the same margin, so a heading
  stands exactly over the values it names.
- **Text that does not fit is shortened with an ellipsis** rather than sliced, and a value that is
  free text — a name, a reason — carries a tool tip, so the whole of it can still be read.
- **Every column was measured**, not guessed: the width of each one is the width of the words it
  has to hold, in English and in Arabic, plus the margin the cell keeps.
- **A box has room for the line it holds**, and a row that holds a box is taller than a row that
  only holds text.

The measurement is not left to the eye. `TableColumnWidthTests` reads the widths out of the views
and measures them against the real faces of the application — Segoe UI and the embedded Tajawal —
so a column that is narrowed later fails a test instead of a screenshot.

**5. The badge that marks a low product was an empty box.** The badge on the stock-on-hand list
read a property the stock position does not have, so the border was drawn and the word never was.
It now reads the flag the position does carry, and the badge says "Low" in English and "منخفض" in
Arabic.

**6. The search box never said what it was for.** The placeholder was bound to the flag that says
the box is empty, and the binding turned that flag around — so the placeholder appeared exactly
when it should have disappeared, and was never on the screen when the box was empty. The inversion
is gone.

**7. The moment a movement was posted was cut off.** "2026-11-01 13:45" did not fit the column it
stood in and was shortened to "2026-11-01 13:…". The column is now measured against the timestamp
it has to hold, in both languages.

**8. The same money was written two ways.** The grids wrote an amount the way a ledger reads it —
`6,460.00 EGP` — while the valuation panel and the totals of the purchase editor wrote it as
`7600.00`, with no separator and no currency. An amount that is only **read** is now written by the
theme's own money format, in every place it appears, so the shelf is worth one number and not two.
An amount that is **edited** keeps plain digits, because a thousands separator in a field the
owner types over turns "1200" into "1,200" — a different number to somebody who writes a decimal
comma.

---

## What is not in this phase

- **Sales and the point of sale.** The ledger already has a `Sale` movement type and the counter
  and the invoice both write to it, but no screen creates one yet. That is Phase 3 and Phase 4.
- **Returns.** A resellable return and a damaged return are modelled in the domain and in the
  valuation rules, and the damaged bucket exists in the ledger, but no screen creates one. That is
  Phase 4, with UAT-20.
- **The eleven reports.** The valuation report is here because the inventory needs it. The rest of
  the report suite is Phase 5.
- **Cost of goods sold on an invoice.** UAT-19 checks that the average cost and the value are
  right; the COGS snapshot on the invoice line belongs to Phase 4, when there is an invoice.
- **A reconciliation job.** The ledger and the balance are reconciled by a test, not by a
  scheduled job that reports a drift to the operator. That is worth having and belongs with the
  reports.
