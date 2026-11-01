# Phase 4 — the sales register, as it was proved

Every picture in this folder was taken by `tools/smoke-sales.ps1` while it drove the real
application. Nothing here is a mock-up and nothing was drawn by hand.

**The application draws its own windows.** The script asks the running application, over a capture
pipe, to draw one of its own windows, and the application writes the picture itself. This matters:
a picture taken from outside the process comes back mirrored for a right-to-left window, and the
mirrored version of a register is a lie about the screen. The pictures below are what the owner
sees.

The run told one story. A product was created with a barcode and bought in; three units were
scanned at the till and the sale was confirmed; one of the three units came back with a reason;
the shelf took it back; and then a second sale was reversed. The register is read in Arabic and
then in English.

What these pictures were taken to prove, and where to look for it:

- **The register holds the sale** — `phase4-01` shows `INV-2026-0001` for `135.00`, the kind of the
  document, the date, the cashier and the line count, with the detail of the same invoice beside
  it: the subtotal, the discount, the tax, the total, the change and the lines.
- **The refund is the invoice's own arithmetic** — `phase4-03` is the return dialog with the line
  of the invoice, the three units that were sold, the three that may still come back, and the
  `45.00` that one of them is worth.
- **A return is a row of its own** — `phase4-04` shows `RET-2026-0001` under the invoice it undoes,
  with a **negative** amount, because the money went back to the customer.
- **The goods went back on the shelf** — `phase4-05` shows the stock after the return: twenty
  bought, three sold, one back.
- **A reversal is a document of its own** — `phase4-07` shows `REV-2026-0001` under the invoice it
  undoes, also with a negative amount.

| Picture | What it shows |
|---|---|
| `phase4-01-sales-register-arabic.png` | The sales register in Arabic with the sale `INV-2026-0001` in it, and the detail of that invoice beside it |
| `phase4-02-sales-detail-arabic.png` | The detail panel on its own: the money of the invoice, its lines, and the returns recorded against it |
| `phase4-03-return-dialog-arabic.png` | The return dialog: the line of the invoice, what was sold, what came back already, what is left to return, the quantity to type, the condition of the goods and the refund of the line |
| `phase4-04-sales-after-the-return-arabic.png` | The register after the return: the sale and the return, the return with a negative amount and the invoice it belongs to |
| `phase4-05-stock-after-the-return-arabic.png` | The stock on hand after the return, with the unit that came back on the shelf |
| `phase4-06-reversal-dialog-arabic.png` | The reversal dialog: the reason, the way the money goes back, and what becomes of the goods |
| `phase4-07-sales-after-the-reversal-arabic.png` | The register after the reversal: the sale, the reversal and the return, all three on one screen |
| `phase4-08-sales-register-english.png` | The same register in English, with the rail on the **left** and the money as `EGP` |
