# Phase 5 — the reports, as they were proved

Every picture in this folder was taken by `tools/smoke-reports.ps1` while it drove the real
application. Nothing here is a mock-up and nothing was drawn by hand.

**The application draws its own windows.** The script asks the running application, over a capture
pipe, to draw one of its own windows, and the application writes the picture itself. This matters:
a picture taken from outside the process comes back mirrored for a right-to-left window, and the
mirrored version of a report is a lie about the screen. The pictures below are what the owner
sees.

The run told one story. A product was created with a barcode and bought in; three units were
scanned at the till and the sale was confirmed; the dashboard was read; five reports were run in
turn and their figures were read; the daily sales report was exported and the file was opened and
read; and the approvals screen was opened and was empty.

Every figure in these pictures is the figure worked out **by hand** before the run: three units at
`100.00` sold for `300.00`, bought at `50.00`, so the cost of the goods is `150.00` and the gross
profit is `150.00`, and ten bought less three sold leaves seven units worth `350.00` on the shelf.
The run read the same numbers off the screen, and off the exported file.

What these pictures were taken to prove, and where to look for it:

- **The dashboard and the report say the same thing** — `dashboard.png` shows the day's takings,
  and `reports-daily-sales.png` shows the daily sales report. Both read `300.00` of net sales and
  `150.00` of gross profit for the same day, because the dashboard reads its money through the
  report reader.
- **Every figure is stated without the tax** — the daily sales report has a column for the tax and
  a column for the net sales, and the tax is `0.00` here because the shop's tax rate is zero.
- **The shelf is worth what the moving average says** — `reports-stock-on-hand.png` reads seven
  units worth `350.00`, and `reports-stock-valuation.png` reads the same seven units with the
  average cost of `50.00` spelled out.
- **The profit is the net sales without the tax, less the cost of the goods** —
  `reports-profit-and-loss.png` reads `300.00` of net sales, `150.00` of cost of goods sold and
  `150.00` of gross profit.
- **The export is the report** — `reports-export.png` shows the export dialog over the report it
  is exporting, and the file it wrote held the same heading and the same row of figures, with the
  byte-order mark at the front of it so that Arabic opens in Excel as Arabic.
- **The approvals screen stands open** — `approvals-empty.png` shows the screen with nothing waiting
  for a decision and the two buttons greyed. It is in this folder because this screen used to take
  the application down with it.

| Picture | What it shows |
|---|---|
| `dashboard.png` | The dashboard for the day: net sales `300.00`, gross profit `150.00`, one invoice, the returns, the stock value and the low-stock count |
| `reports-daily-sales.png` | The daily sales report: the day, the invoices, the gross sales, the discounts, the tax, the returns and the net sales |
| `reports-sales-by-product.png` | The sales by product report: the quantity sold, the net revenue, the cost of the goods sold and the gross profit |
| `reports-stock-on-hand.png` | The stock on hand report: the quantity on the shelf, the reorder level, the unit cost and the value of the stock |
| `reports-stock-valuation.png` | The valuation report: the same shelf, with the moving average cost of a unit spelled out |
| `reports-profit-and-loss.png` | The profit and loss statement for the period: the net sales, the cost of the goods sold and the gross profit |
| `reports-export.png` | The daily sales report being exported, with the file dialog driven and the file written |
| `approvals-empty.png` | The approvals screen with nothing waiting for a decision |
