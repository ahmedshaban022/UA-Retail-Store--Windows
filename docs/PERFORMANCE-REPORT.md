# StoreFlow performance report

The measurements below were taken on a **laptop development machine, not the shop's till**: a 12th Gen Intel Core i7-12700H (14 cores, 20 logical processors, 2.3 GHz), 23.6 GB RAM, Windows 11 Pro 64-bit (10.0.26300).

The test filled a shop database with 10,000 products, 100,000 invoices, 199,999 invoice lines and 100,000 stock movements. In everyday terms, this represents a shop that has sold a hundred thousand times.

Each operation was run three times. The **median** is the middle time when those three are ordered: a useful indication of the typical wait in these runs. The **slowest** is the worst of the three, not a guarantee of the worst possible wait.

| What the shop does | Median | Slowest |
|---|---|---|
| the product list, the first page | 17 ms | 157 ms |
| the product list, a search | 15 ms | 37 ms |
| the product list, a deep page | 6 ms | 7 ms |
| the sales register, everything | 604 ms | 628 ms |
| the sales register, a quarter | 85 ms | 89 ms |
| the daily sales report | 155 ms | 192 ms |
| the sales by product report | 403 ms | 437 ms |
| the stock valuation report | 275 ms | 319 ms |

The separate scan measurement in the [point-of-sale report](phases/PHASE-3-POS.md) showed an item being added well under a second. Of the operations above, the slowest was reading the entire sales register, at just over half a second.

These measurements used a filled-in database on a development laptop, not the shop's own PC, scanner and printer. The shop's results will differ; check them on the actual equipment.

To repeat the database measurements, run [the performance harness](../tools/performance-harness.ps1). It fills a separate test shop, prints its location and size, then prints the median and slowest time for each measured operation. The [Phase 7 report](phases/PHASE-7-HARDENING-AND-RELEASE.md) records the original results.