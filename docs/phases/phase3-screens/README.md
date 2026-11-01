# Phase 3 — the till, as it was proved

Every picture in this folder was taken by `tools/smoke-pos.ps1` while it drove the real
application. Nothing here is a mock-up and nothing was drawn by hand.

**The application draws its own windows.** The script asks the running application, over a capture
pipe, to draw one of its own windows, and the application writes the picture itself. This matters:
a picture taken from outside the process comes back mirrored for a right-to-left window, and the
mirrored version of a till is a lie about the screen. The pictures below are what the owner sees.

The scan in the pictures was made the way a scanner makes it: the barcode went into the field and
Enter finished it. `tools/scan-latency.ps1` measures that wait on the real application — twenty
scans, a median of **4 milliseconds** in the till's own log, and 86 milliseconds for the whole
round trip including the automation watching the screen.

What these pictures were taken to prove, and where to look for it:

- **The till reads right to left** — `phase3-02` has the navigation rail on the **right** and the
  money panel on the left, and `phase3-09` is the same screen in English with the rail on the
  **left**.
- **The money adds up** — `phase3-04` carries the whole chain: `45.00` subtotal, `5.00` discount,
  `40.00` taxable, `0.00` tax, `40.00` total. The taxable amount plus the tax is the total.
- **The customer is told what is coming back** — `phase3-03` shows `100.00` handed over and `55.00`
  coming back.
- **The receipt carries the invoice number** — `phase3-07`: `INV-2026-0001`, the date, the
  cashier, the line, and the same money as the screen.
- **A cart that is put aside is still there** — `phase3-05` shows the waiting cart by the name the
  cashier gave it, with the till empty beside it.

| Picture | What it shows |
|---|---|
| `phase3-01-till-empty-arabic.png` | The till before anything is scanned: the scan field, the empty cart, and the money panel at zero |
| `phase3-02-till-one-line-arabic.png` | The line that the scan produced — `Rice 1kg POS` at `45.00` — with the chip that says the scan was read |
| `phase3-03-till-cash-arabic.png` | The cash handed over: `100.00` received, `55.00` back |
| `phase3-04-till-discounted-arabic.png` | The whole-sale discount of `5.00` taken off: the subtotal, the discount, the taxable amount, the tax and the total, all in one place |
| `phase3-05-till-held-cart-arabic.png` | The cart that was put aside, named `customer waiting`, waiting to be taken back |
| `phase3-06-till-resumed-arabic.png` | The same cart back on the till, with its discount and its `40.00` total |
| `phase3-07-receipt-arabic.png` | The receipt: the invoice number, the date, the cashier, the line, the money, and the buttons for 80 mm, A4 and close |
| `phase3-08-stock-after-the-sale-arabic.png` | The stock on hand after the sale, with the product that was sold |
| `phase3-09-till-english.png` | The same till in English: the rail on the **left**, the money as `EGP` |
| `phase3-10-till-scanned-english.png` | The English till with a line on the cart, scanned the same way |
