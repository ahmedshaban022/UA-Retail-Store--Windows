# ADR-0002 — Money representation, rounding, and the single calculation policy

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-25 |
| **Deciders** | Implementation agent |
| **Phase** | 0 — Foundation |
| **Supersedes** | — |

## Context

StoreFlow is a **ledger of record**. The PRD requires exact money, a ledger that reconciles to the penny, and
a stock valuation that agrees with the invoice totals. Egyptian retail pricing uses piastres (1 EGP = 100
piastres), so the minor unit is 2 decimal places.

Three ways to hold money are common, and two of them are wrong here:

| Option | Problem |
|---|---|
| `float` / `double` | Binary floating point cannot represent 0.10 exactly. Summing a few hundred invoice lines produces a total that is off by a fraction of a piastre, and the discrepancy grows with the number of lines. Unacceptable for a ledger. |
| `decimal` everywhere, including storage | `decimal` is exact, but SQLite has no decimal type — it stores it as `REAL`, which reintroduces the binary problem at the storage boundary. |
| `decimal` in code, **integer minor units** in the database | Exact at both ends. The code does arithmetic in a type humans reason about; the database stores an exact integer that `SUM()` gets right by construction. |

There is also a rounding question that decides whether the books balance. If each line is rounded to the
minor unit and then summed, the invoice total equals the sum of the line totals exactly — the property an
auditor checks by adding up the printed receipt. If each line is left at full precision and only the total is
rounded, the printed lines do not add up to the printed total. That failure is visible to the customer and
unrecoverable after the fact, so the first behaviour is the one the product must have.

Finally, rounding *mode* has to be fixed. `MidpointRounding.ToEven` (banker's rounding) and
`MidpointRounding.AwayFromZero` disagree on exact .5 values, and the choice changes which way a customer is
short-changed on a .5 piastre. "Round half away from zero" is the convention a shop owner already has in
their head from cash handling.

## Decision

1. **Money is `decimal` in code, `long` minor units in the database.**
   - Code-facing type: `StoreFlow.Domain.Money.Money` — an immutable value object wrapping a `decimal`
     amount, with the currency's minor-unit scale.
   - Storage: `INTEGER` columns named `*Minor` (e.g. `UnitPriceMinor`, `TaxMinor`, `TotalMinor`), converted
     at the EF boundary by a `ValueConverter` in `src/StoreFlow.Infrastructure/Persistence/Converters.cs`.
   - The database is the source of truth for the scale. The scale is `2` for EGP and lives in the theme/
     settings file, not in a constant scattered through the code.

2. **All money arithmetic happens in one place: `StoreFlow.Domain.Money.MoneyMath`.**
   No other class is permitted to add, subtract, multiply or divide money directly. This is what makes the
   rounding policy auditable — there is exactly one implementation to read and one to test.

3. **Rounding is `MidpointRounding.AwayFromZero`, applied exactly once per line.**
   - A *line* is the rounding boundary: a unit price is a rate, not an amount, and is never rounded.
   - Discount amount, tax amount and line total are each rounded once, at the line level, from the rate and
     the quantity.
   - Document totals are then the **exact sum of the already-rounded line values** — no second rounding pass,
     so the printed receipt adds up.
   - `MoneyMath.RoundToWholeMinorUnits` exists alongside `RoundToMinorUnits` because the two are different
     operations: `RoundToMinorUnits` converts a rate to an amount at the currency's scale, while
     `RoundToWholeMinorUnits` rounds a value to a whole number of minor units. **Using the wrong one is a
     bug, not a style issue** — `RoundToMinorUnits` on a per-unit cost multiplies it by 100. This mistake was
     made and caught during Phase 0 implementation, in `MovingAverageCost`, and is why both methods now exist
     with explicit names.

4. **Quantities follow the same discipline, with a different scale.**
   - Code-facing type: `StoreFlow.Domain.Quantity.UnitQuantity` (a `decimal` plus a scale), named to avoid
     colliding with the `Quantity` property carried by business entities.
   - Storage: `QuantityScaled` (`long`) + `QuantityScale` (`int`), where the scale comes from the unit's
     configured precision — `0` for whole pieces, `3` for kilograms to the gram. No floating point anywhere.
   - This is the same principle as money: exact integer storage, human-scale code arithmetic.

5. **Tax is computed per line, and the rate is snapshotted onto the line.**
   A tax category stores its rate in basis points and its inclusivity. When a line is posted, the rate and the
   inclusivity are copied onto the invoice line. A later change to the store's tax settings therefore cannot
   alter a posted invoice — the invoice remains readable as it was at the moment of sale, which is what makes
   the ledger auditable.

## Consequences

**Positive**

- Money is exact end to end. `SUM(TotalMinor)` over any set of lines is the true total, with no drift.
- The printed receipt adds up. The invoice total is the sum of the printed line totals, by construction.
- There is exactly one rounding policy, in one file, covered by tests. A future change to the rounding mode
  is a single, deliberate, tested edit.
- Storage is integer, so SQLite's own `SUM` and `AVG` are exact and can be used in report aggregation
  without pulling values back into `decimal` arithmetic.
- The `*Minor` column naming makes the storage convention visible in every query and every schema review.

**Negative**

- Every amount crosses a conversion boundary (code `decimal` ↔ database `long`). This is a real cost, paid at
  the EF boundary only, and it is the reason the converters live in exactly one file.
- Per-unit values must be handled as rates, not amounts. A developer who forgets will hit a factor-of-100
  error — which is exactly why `RoundToMinorUnits` and `RoundToWholeMinorUnits` have distinct names and are
  separately tested.
- Currency scale is not free: changing the store's currency from a 2-decimal to a 0-decimal currency is a
  migration concern, not just a setting change. The MVP ships EGP at scale 2 and the schema carries the
  scale so a future change is a deliberate migration.

**Neutral**

- Rounding away from zero on a negative amount (a refund line) rounds the refund *up* in magnitude, which is
  the conservative choice for the shop and is symmetric with the purchase side.

## References

- `src/StoreFlow.Domain/Money/Money.cs`, `src/StoreFlow.Domain/Money/MoneyMath.cs`
- `src/StoreFlow.Domain/Quantity/Quantity.cs` (`UnitQuantity`, `QuantityScale`)
- `src/StoreFlow.Infrastructure/Persistence/Converters.cs`
- `src/StoreFlow.Domain/Tax/TaxCalculator.cs`
- `tests/StoreFlow.Domain.Tests/MoneyMathTests.cs`, `tests/StoreFlow.Domain.Tests/TaxAndDiscountTests.cs`
- `PRODUCT.md` — "Money is exact: integer minor units, never floating point."
