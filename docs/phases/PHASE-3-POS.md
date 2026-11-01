# Phase 3 — Point of sale

The till. A scanner puts a barcode into a field, the line appears on the cart, the money adds up
on the screen, the customer is told what is coming back, and the sale becomes an invoice with a
number on it that a shop can find again tomorrow.

This phase builds that, and it builds it to be driven: every rule below was proved by the
automated suite, and then proved again by driving the real application with a real scanner-shaped
keyboard and taking the pictures the application draws of itself.

---

## What was built

### The money is a pure function of the cart

`src/StoreFlow.Application/Pos/PosCartBuilder.cs` is the pricing core, and it is a pure function:
it reads the lines, the catalogue and the discount, and it returns the priced cart. It reads no
database, touches no file and knows nothing about WPF. The rules it applies are the rules the
till, the receipt and the invoice all share, so they cannot drift apart.

The order of the arithmetic is the order a shopkeeper would use:

1. **The gross.** The price of the catalogue times the quantity.
2. **The line discount.** A percentage of the line or an amount off it.
3. **The net.** What the line is worth after its own discount.
4. **The invoice discount**, spread across the lines in proportion to what each line is worth. The
   remainder goes to the dearest line, so the shares add up to the discount exactly and no
   fraction is lost or invented.
5. **The tax.** The rate is snapshotted from the product's tax category, and a price that already
   carries the tax is not taxed twice.
6. **The total.** What the customer pays.

A quantity the selling unit cannot express is refused rather than rounded. Two and a half pieces
of something sold by the piece is not a quantity of anything, and the cashier is told so instead
of being handed three.

### The invoice is one transaction, and it can be posted twice without being sold twice

`src/StoreFlow.Infrastructure/Pos/PostSaleCommandHandler.cs` posts the sale. It authorises first,
validates second, and commits exactly one transaction: the invoice, its lines, the payments, and
the stock movements that come off the shelf are written together or not at all.

The idempotency is the same ledger the rest of the application uses. The till sends one key for
one attempt at one checkout; the key is claimed as `Pending` inside the sale's own transaction and
flipped to `Committed` before that transaction commits. A second attempt with the same key is
recognised and the first sale is replayed rather than repeated. **A double click on *Confirm sale*
cannot make two sales**, and the test suite proves it against a real database.

The invoice number is `INV-<year>-<NNNN>`, unique and in order, and it is allocated inside the same
transaction that writes the invoice.

### A held cart is a note, not a movement

`HeldCart` is one table with a JSON payload: a snapshot of the till, not a posted document. Putting
a cart aside reserves no stock and writes no movement — the goods are still on the shelf, because
they have not been sold. Taking the cart back rebuilds the till from the snapshot, including the
whole-sale discount. The number of the cart is `HLD-<NNNN>` and it is shown on the screen, so a
cashier can say "your basket is under the name *Ahmed*" and mean it.

### The receipt is the same drawing as the paper

`src/StoreFlow.Application/Printing/PrintLayout.cs` lays a document out in points. The on-screen
preview in the receipt window and the GDI+ printer in `WindowsPrintService` both draw that same
layout, so **the preview is the paper and not a picture of it** (PRD §5.1 GEN-05).

The printer prints through the Windows printing subsystem, on 80 mm or 58 mm thermal paper or on an
A4 sheet, with the Arabic face that ships inside the application added as a private font — so a
shop PC with no font installed still prints a receipt in Arabic.

### The screens

- **`PosView`** — the till. The scan field is the only thing the cashier has to touch: Enter
  finishes a scan, and the rest of the screen follows from that. The cart, the money panel, the
  payment buttons, the cash received, the discount on the whole sale, and the three actions that
  are not the sale — *put aside*, *empty the cart*, *take back*.
- **`ReceiptWindow`** — the receipt, drawn on a canvas at the size of the paper, with the buttons to
  print it on 80 mm or on A4 and to close it.
- The **POS** rail entry is live from this phase, in both languages.

### The interface speaks both languages

Fifty-nine new strings were added for the till and the receipt, in `Strings.resx` and
`Strings.ar.resx`, in Arabic and English. The till reads the numbers the cashier types the same
way in both languages, so `12.5` means the same on an Arabic screen as on an English one.

---

## The rules this phase decided

1. **The tax follows the price.** A discount is a discount on the price, and the tax is computed
   on what the customer actually pays. For a price that does not carry the tax, a 10% discount
   also takes 10% off the tax, so the customer saves slightly more than the discount that was
   granted. This is deliberate: it is how the price was quoted, and it is what the tax law asks
   for.

2. **The receipt shows the taxable amount, so the arithmetic reconciles on paper.** The receipt
   prints *subtotal, discount, taxable amount, tax, total, paid, cash received, change*. The
   taxable amount is what the tax was charged on, and **the taxable amount plus the tax is always
   the total** — whether the price carried the tax or not. A reader of an Egyptian VAT receipt can
   check the arithmetic without guessing which convention the shop used.

3. **The change is the cash that was handed over, less the cash that paid for the invoice.** The
   invoice, the payment row and the receipt all carry the same figure. When they disagreed — and
   they did, at first — the printed receipt said there was no change while the customer was owed
   forty pounds.

4. **The invoice foots to its own lines.** Each line carries the share of the whole-sale discount
   that was spread onto it, so the line discounts and the invoice-discount shares add up to the
   discount on the header, and the line totals add up to the total. A tax document that does not
   add up is a tax document nobody can check.

5. **A number is read the same way in every language.** The invariant culture, with the Arabic
   separators understood and a decimal comma accepted where it is unambiguous.

6. **An empty cart is not an error worth shouting about**, and a refused line is. The till says
   what is wrong with the line it refused, and says nothing when there is simply nothing on the
   cart.

---

## How it was verified

### The automated suite

**299 tests, all passing** (`dotnet test StoreFlow.sln`):

| Project | Tests | Before | What the new ones cover |
|---|---|---|---|
| `StoreFlow.Domain.Tests` | 25 | 25 | — |
| `StoreFlow.Application.Tests` | 102 | 83 | The pricing core: the tax on an inclusive and an exclusive price, the invoice discount spread across the lines, the remainder, the quantity the unit cannot express, the duplicate reported after the missing product |
| `StoreFlow.Infrastructure.Tests` | 71 | 57 | The sale handler against a real SQLite file: one transaction, one sale for one key, the stock off the shelf exactly once, the change the invoice and the payment row agree on, the invoice footing to its lines, the held cart reserving nothing |
| `StoreFlow.Desktop.Tests` | 101 | 88 | The till: the money on the screen is the money the cart priced, the change from the cash that was typed, the discount read as a percentage or an amount, every error code having a message of its own, the receipt carrying the same numbers as the screen, the reprint marked as a copy |

The infrastructure tests are the ones that matter. They run against a real database file with the
real migrations, so the unique indexes and the transaction are in play. Among them:

- **A sale is one sale.** Posting the same key twice returns the first sale and does not sell
  twice.
- **The stock comes off the shelf exactly once**, and the invoice carries the cost that was
  snapshotted at the time of the sale.
- **The invoice and the payment row agree about the change.**
- **The invoice foots to its lines.**
- **A cart that is put aside reserves no stock** and writes no movement, and taking it back
  restores the cart exactly as it was, discount and all.
- **A sale for more than the shelf holds is refused**, and the stock is untouched.
- **A price the cashier changed needs the permission to change prices**, and a discount over the
  shop's limit needs the permission to exceed it.

### Driving the real application

`tools/smoke-pos.ps1` drives the running application through UI Automation, in Arabic and then in
English, the way `tools/smoke-inventory.ps1` does. Every control is found by its automation
identifier rather than by its label, so the script does not depend on the wording of the interface
in either language.

The script creates a product with a barcode, buys twenty of it in, opens the till, **scans the
barcode the way a scanner does** — the code goes into the field and Enter finishes it — reads the
total and the change, applies a discount on the whole sale, puts the cart aside, takes it back,
sells it, and reads the same till in English. The pictures it takes are in
[`docs/phases/phase3-screens/`](phase3-screens/).

**The run that backs this report: 29 checks, 0 failures, 10 pictures.** The pictures are drawn by
the application itself — the script asks the running application, over the capture pipe, to draw
one of its own windows — so a right-to-left screen comes out the way the owner sees it rather than
mirrored.

### How fast the till answers

`tools/scan-latency.ps1` measures the wait between the Enter key going down and the line appearing
on the cart, over and over, on the real application. The cart is emptied between the scans, so
every scan is the first of its kind.

Two figures come out of the run, and they are different questions:

| What is measured | Fastest | Median | 95th percentile | Slowest |
|---|---|---|---|---|
| **The till's own answer**, from the till's own log | 2 ms | **4 ms** | **5 ms** | 70 ms |
| **The whole round trip**, from the key going down to the line appearing on the screen | 74 ms | 86 ms | 122 ms | 220 ms |

The first is the till's work: read the barcode, find the product, price it, put it on the cart.
The second also carries the cost of the automation watching the screen, which a cashier does not
pay. **The plan's gate — a scan answered inside fifty milliseconds — is met by the first, with
room to spare.** The slowest scan of the twenty was the first one, at 70 ms, which is the price of
the cold cache; every scan after it was inside ten milliseconds.

A till that answers a scan inside a tenth of a second does not make a queue at the counter.

### What running it found

Driving the application found twelve defects that a tester would have had to find by hand. All of
them are fixed, and the ones with a rule behind them are pinned by tests.

| What to check | What was wrong | What it is now |
|---|---|---|
| The change on the receipt | The invoice said there was no change to give back while the payment row said forty pounds — the invoice subtracted the total from the amount paid, and the row subtracted it from the cash that was handed over | One figure, the same on the invoice, the payment row, the till and the receipt |
| The receipt | It never said how much cash was handed over, only what was paid and what came back | The cash received is on the receipt, between the two |
| A quantity the unit cannot express | Two and a half pieces of something sold by the piece was quietly rounded to three | The line is refused and the reason is on the screen |
| A line for a product that is not sold | The cashier was told the same product was on the cart twice | The missing product is named first |
| Opening the till | The application died: the payment buttons passed the word "Cash" to a command that wanted the payment method | Three named buttons, each with its own command |
| Opening the till | The application died again: the list of waiting carts was ordered by a moment in time, and the database cannot sort a moment in time | The list is read and put in order by the application |
| The printer | The fallback face was added with a call that takes a file name, not a face, so it threw when the embedded faces could not be read | The faces are read from the application's own resources, and a face Windows is certain to have is used if that fails |
| Closing the receipt | The close button shut the whole application down | It closes the receipt |
| A refused line | The till threw the cart's complaints away — the line was dropped from the total and nothing was said | The reason is on the screen, and is taken away when the reason is answered |
| The numbers the cashier types | They were read with the language of the screen, and the Arabic decimal separator is not a dot — so `12.5` did not parse on an Arabic till and the line was refused | The same rule as the rest of the application, with the Arabic marks understood |
| The invoice | The discount on the header was more than the sum of the discounts on the lines, so the invoice did not foot | Each line carries the share of the whole-sale discount that was spread onto it |
| The invoice number | Emptying the cart wiped the message that said which invoice had been posted, so the cashier could not tell the customer the number | The number is on the screen when the receipt closes |

---

## What is not in this phase

1. **There is no sales screen.** The **Sales** rail entry is still disabled. The sale exists, in
   the database and on the invoice, but there is no register to read it back from, and no way to
   look at yesterday's takings. That is Phase 4.

2. **There are no returns.** A sale cannot be returned, refunded or exchanged.

3. **A discount over the shop's limit is refused, not approved.** The gate is real — the
   permission has to be there — but the approval request that the inventory's stock adjustments
   use is not wired to the till. A cashier over the limit is told so and nothing happens; a
   manager cannot yet approve it from the till.

4. **Nothing was printed on paper.** There is no printer on this machine, so the print path was
   proved by the document it produces and by the layout the preview and the printer share, and
   not by a sheet of paper. The receipt in the pictures is the preview.

5. **The scanner settings are not used.** `ScanSuffix` and `ScanInterKeyDelayMs` are read with
   the rest of the shop's settings, but the till finishes a scan on Enter and ignores both.

6. **The cashier's own name is not on the sale line.** The receipt carries the name; the invoice
   line does not.

---

## Known papercuts

Nine things are rough, or missing, and are written down here so that they are found here rather
than by a shopkeeper.

1. **The scan chip covers the quantity for a moment.** The chip that says "this was read" sits in
   the quantity cell for nine hundred milliseconds, so the number underneath it cannot be read
   until the chip goes. It is the number the cashier is least likely to be looking at, but it is
   still a number that is hidden.

2. **The English screens are reached by starting the application again.** The language picker is
   a combo box, and its list is a popup window of its own, which the automation cannot reach when
   the desktop is locked. The script therefore writes the language into the theme and starts the
   application again. The English screens are the real ones, but they are not the same session as
   the Arabic ones.

3. **The cash has to be typed again after a cart is taken back.** The money belongs to the
   customer who is paying, not to the one who left the cart aside, so the field is empty when the
   cart comes back. A cashier in a hurry has to remember it.

4. **The discount field takes a percentage or an amount, and says so nowhere on the screen.** A
   `5` is five pounds and a `5%` is five percent; there is no label to say which is which.

5. **There is no way to sell a fraction of a kilogram from the till.** The quantity is a number
   in the product's own unit, which is right, but a weight sold by weight wants a scale on the
   keyboard and there is none.

6. **The till has no customer.** A sale carries no customer, no loyalty card and no note beyond
   what the invoice itself says.

7. **The receipt window is a window and not the paper.** The picture in the evidence is the
   preview drawn at the size of the roll. It is the same drawing, but it is not a photograph of a
   printed receipt, because there was no printer to photograph.

8. **A sale is one till and one currency.** There is no multi-tender sale: the cash and the card
   cannot be added together in one sale.

9. **The catalogue price is the price.** The till cannot offer a different price without the
   permission to change prices, and there is no "price override" flow that asks a manager — the
   sale is refused instead.

---

## The phase gate

PRD §11 numbers twenty acceptance cases. This table answers in **the PRD's own numbering**, because
the labels a phase attaches to its own work are worth nothing if the plan and the report cannot be
read against the same list. The first table is the PRD §11 rows the till answers for; the second is
the till's own requirements in PRD §5.5, which §11 has no row for.

| PRD §11 row | What it needs | Where it stands |
|---|---|---|
| UAT-01 | A product is found by its barcode at the till | **Proved for the till half**, by the application and by the database; the save and the search were proved in Phase 1 |
| UAT-06 | The checkout is rejected whole when the shelf is short | **Proved**, by the application and by the database |
| UAT-07 | One sale per key, however many times the button is pressed | **Proved**, against a real database |
| UAT-08 | The sale stands when the printer is gone, and can be printed again | **Proved for the document only**; the printer itself was not exercised, because there is no printer here |
| UAT-09 | Nothing is left half-written when the application is killed mid-posting | **Not reached** — killing the process is Phase 7's row |
| UAT-13 | A discount over the policy limit is blocked or routed to an approver | **Proved for the block only**; the approval request is not wired to the till |

### The till's own requirements

PRD §11 has no row for the tax, the payment method, the cash and the change, the held cart, the
keyboard or the invoice numbering, so they are proved against PRD §5.5 instead. They are listed
here so that the two documents cannot be mistaken for one list:

| PRD §5.5 row | What it needs | Where it stands |
|---|---|---|
| POS-01 | A complete scan resolves one barcode to one product | **Proved** |
| POS-02 | The cart is edited without touching the stock | **Proved** |
| POS-04 | The tax is right, for a price that carries the tax and for one that does not | **Proved** |
| POS-05 | The payment method is recorded, and nothing claims a processor settled it | **Proved** |
| POS-06 | The cash handed over and the change given back are right | **Proved** |
| POS-07 | The invoice numbers are unique and in order | **Proved** |
| POS-08 | 80 mm and A4, and a reprint that does not sell again | **Proved for the document**; the printer itself was not exercised, because there is no printer here |
| POS-09 | A cart can be put aside without reserving stock | **Proved** |
| POS-10 | The till can be worked from the keyboard | **Proved**, and measured |

### The rows this phase did not reach

The rest of PRD §11 belongs to another phase, or to a person standing at the till with the
hardware. Saying which is which is the point of the table:

| PRD §11 row | What it needs | Where it stands |
|---|---|---|
| UAT-02 | A second product on the same barcode is refused | **Proved** in Phase 1, and it still holds |
| UAT-03, UAT-14 | The purchase receipt and the approved count | **Proved** in Phase 2 |
| UAT-04, UAT-05 | Selling three units by scanning, and scanning the same barcode twice | **Proved in substance** by the sale tests and the till smoke run; the manual chain in `docs/uat/UAT-PLAN.md` is still waiting for a person to walk it |
| UAT-09 | Killing the application in the middle of a posting | Phase 7 |
| UAT-10, UAT-11, UAT-12, UAT-20 | The sales register, the returns, and the gross-profit reading behind a price change | Phase 4 |
| UAT-15 | A report exported with Arabic names in it | Phase 5 |
| UAT-16 | A backup and a restore on a clean machine | Phase 6 |
| UAT-17 | A sale with the internet disconnected | Phase 6 |
| UAT-18 | A product deactivated after it had already been sold | The catalogue half is proved; the register half is Phase 4 |
| UAT-19 | A receipt at a higher cost, and the cost of goods sold | The shelf half is proved in Phase 2; the sale half is Phase 4 |

**The phase is complete, and the gate is open for the till.** Everything the point of sale is
responsible for is built, proved by tests, and proved again by driving the real application with a
scanner and a keyboard. The rows that need a register, a return or a sheet of paper cannot be run
here, and saying otherwise would be a lie.

The next phase is **Phase 4 — sales and returns**: the register that reads the day's takings back,
the returns that put the goods on the shelf again, and the cost of goods that the sale carries.
