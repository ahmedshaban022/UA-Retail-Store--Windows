# Phase 2 — the screens, as they were proved

Every picture in this folder was taken by `tools/smoke-inventory.ps1` while it drove the real
application: the application asked each window to draw itself, and the script saved what it drew.
Nothing here is a mock-up and nothing was drawn by hand.

The Arabic pictures are flipped back, because a right-to-left window comes out mirrored when it is
captured from outside its own process. The English pictures are left as they are, because flipping a
left-to-right window would be a lie about the screen. Each `.raw.png` is the capture exactly as it
came out of the window, before the flip.

What these pictures were taken to prove, and where to look for it:

- **Nothing is cut** — `phase2-17` shows the whole moment a movement was posted, `2026-11-01 14:52`,
  and `phase2-04` shows the whole of every figure in a line.
- **Every heading is there** — `phase2-07` and `phase2-08` are the two tabs whose headings used to
  come out blank.
- **The ledger says which product moved** — `phase2-07`, where the stock-keeping code stands beside
  the name.
- **One amount is written one way** — `phase2-16` and `phase2-04` both write `6,460.00 EGP` /
  `7,600.00 ج.م`, while a field that is edited keeps plain digits.
- **The badge and the placeholder say what they mean** — `phase2-16` carries a "Low" badge that
  reads, and a search box that says what it searches for.

| Picture | What it shows |
|---|---|
| `phase2-01-shell-arabic.png` | The shell, with the navigation rail on the **right** and **المشتريات** and **المخزون** open for the first time |
| `phase2-02-purchases-empty-arabic.png` | The purchase register, empty: the search, the date range, the supplier filter and the commands |
| `phase2-03-purchase-editor-arabic.png` | The purchase editor: supplier, date, discount, notes, and an empty table of lines |
| `phase2-04-purchase-editor-filled-arabic.png` | The purchase editor with a line: `RICE-001`, twenty units at 380.00, and the totals following the line |
| `phase2-05-purchases-posted-arabic.png` | The register after the receipt was posted: the document number, the date, the quantity and the total |
| `phase2-06-inventory-balances-arabic.png` | The inventory screen: the valuation panel at the top, and the stock on hand below it |
| `phase2-07-inventory-movements-arabic.png` | The ledger: the receipt that produced the stock, with its signed quantity and its running balance |
| `phase2-08-inventory-counts-arabic.png` | The stock counts, with the approve, post and cancel commands |
| `phase2-09-stock-count-arabic.png` | The stock count dialog: the date, the product and the quantity the counter found |
| `phase2-10-stock-count-filled-arabic.png` | The count, filled in — eighteen of the twenty that were received |
| `phase2-11-approve-count-arabic.png` | The reason prompt: the count cannot be approved without a reason |
| `phase2-12-inventory-counts-posted-arabic.png` | The counts after the count was posted |
| `phase2-13-stock-adjustment-arabic.png` | The stock adjustment dialog: product, quantity change, unit cost, the sellable or damaged bucket, and the reason |
| `phase2-14-inventory-balances-after-arabic.png` | The stock on hand after the count and the adjustment |
| `phase2-15-purchases-english.png` | The purchase register in English: the rail on the **left**, the money as `EGP` |
| `phase2-16-inventory-balances-english.png` | The stock on hand in English |
| `phase2-17-inventory-movements-english.png` | The ledger in English |
