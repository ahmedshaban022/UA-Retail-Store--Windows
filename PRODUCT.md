# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows-desktop

## Stack

delegated: C# 14 / .NET 10 LTS / WPF + MVVM (CommunityToolkit.Mvvm source generators), EF Core 10 with the
Microsoft.Data.Sqlite provider, SQLite as the single local store, `Microsoft.Extensions.Hosting` for the
composition root, Serilog for diagnostics, xUnit + Shouldly for tests. The user delegated the stack choice
("choose the best language … for performance"), so the reasoning is recorded in
`docs/decisions/ADR-0001-stack.md` rather than treated as a user-specified constraint.

> Platform note: the schema's platform vocabulary is `web` / `ios` / `android` / `adaptive`. This product is a
> **native Windows desktop** application (WPF), which the vocabulary does not name. It is recorded here as
> `windows-desktop` because it is a native platform: the web-only paths (live browser mode, browser detector,
> Tailwind/`@theme` integration) do not apply, while the token, contrast, state-matrix, accessibility and
> copy rules do. iOS and Android platform references are explicitly **not** applicable.

## Users

Primary: the **shop owner / manager** of a single small-to-medium retail store, working at the counter or
behind the same PC, often in Arabic, needing to know what sold today, what is running out, and what the
shop is worth — without a bookkeeper.

Secondary: the **cashier**, who needs to complete a scan-and-pay sale in seconds while standing, using a USB
barcode scanner and an 80 mm thermal receipt printer, with no mouse and minimal reading.

Tertiary: the **inventory clerk**, who receives deliveries and counts stock, and who by default must not see
cost margins.

All three are assumed to be on one Windows 11 PC in one store. Assumption: no multi-terminal operation
(PRD §2.3 excludes it) and no customer-facing surfaces.

## Product Purpose

One offline application that owns the shop's commercial truth: what exists, what is in stock, what was sold,
what it cost, what was returned, and who did it. Success is a cashier finishing a sale without leaving the
keyboard, and an owner who can trust their stock number and their month-end numbers without reconciling
paper.

## Positioning

A single-store, offline-first **ledger of record** rather than a cloud POS: it never requires a network, it
keeps an append-only stock ledger that reconciles to the penny, and it is **rebrandable by configuration**
(title, logo, colours, receipt, currency, tax, locale) so the same binary can be sold to another shop
unchanged. A neighbouring cloud POS can claim convenience; this one claims *durable, auditable,
shop-owned* truth with verified backups.

## Operating Context

* A shop PC that may lose power mid-sale, and whose disk may fail outright.
* A USB HID / keyboard-wedge barcode scanner that types characters and presses Enter.
* An 80 mm thermal receipt printer and an A4 printer, driven through Windows print drivers.
* Paper receipts as the customer's only artefact; Egyptian retail habits (cash rounding, ج.م pricing).
* Bilingual operation: Arabic labels and messages, English SKUs, barcodes and invoices.
* The owner is the backup: there is no IT department, so backup configuration, verification and the
  restore procedure must be usable by a non-technical person.
* Backups must reach somewhere that survives the failure of this PC (USB media or a cloud account).

## Capabilities and Constraints

Confirmed (PRD v1.0 P0 scope):

* Local authentication with role-based permissions enforced in the application layer, not the UI.
* Products, categories, units, suppliers, barcodes, cost and sale price.
* Purchase receipts (draft → post), stock ledger, balances, low-stock alerts, stock counts, adjustments.
* POS: scan, cart, line and invoice discounts, tax, payment methods, idempotent checkout, receipts.
* Posted invoices, controlled partial/full returns, reprint that never re-posts.
* Eleven reports with date filters and CSV export (UTF-8 with BOM).
* Backups, verified restore, and an audit trail.

Constraints that bind the design:

* Offline-first: every P0 workflow works with the network cable pulled.
* Money is exact: integer minor units, never floating point.
* Posted documents are immutable; corrections are reversing entries.
* Stock never goes negative, and the ledger always reconciles to the cached balance.
* Arabic is the first locale; the interface must be usable right-to-left.
* The visual layer is entirely configurable from a versioned JSON theme file.

Explicitly undecided / out of scope for MVP: customer credit and receivables, shifts and cash-drawer
variance, expiry/lot tracking, serial numbers, variants, multiple branches, e-invoicing compliance, and
any hosted or multi-device component.

## Brand Commitments

* Working product name **StoreFlow**, a default rather than a fixed identity — it is data, overridable at
  runtime without recompiling.
* No invented claims: the app must not present itself as tax-authority compliant (PRD §2.3 compliance note),
  and the receipt is a locally generated invoice, not an e-receipt.
* Voice: plain, practical, and actionable. Errors say what to do next, never "an error occurred" and never a
  raw exception string.
* Arabic copy uses Egyptian commercial vocabulary (فاتورة، بيع، مرتجع، مخزون، ج.م).

## Evidence on Hand

* `StoreFlow_Retail_Management_PRD_EN.md` — the product requirements, acceptance criteria, data model and
  UAT set. This is the only confirmed evidence in the repository.
* The PRD's own worked example of moving weighted average (10 × 50 + 10 × 70 → 60 per unit).

Absences future work must not fabricate: no brand assets, logo, photographs, customer testimonials, store
photography, benchmark measurements, or printer/scanner model have been supplied. Performance figures in the
PRD are **targets**, not measurements, and no report may present them as achieved results.

## Product Principles

1. **The ledger is the truth.** Balances are a cache of an append-only movement ledger, and every screen that
   shows a number must be reconcilable to it.
2. **A sale is atomic and idempotent.** It either happens completely or not at all, and a double click never
   creates two invoices.
3. **Offline is the default, not a degraded mode.** No core workflow may require a network.
4. **Configurable identity.** Branding, colours, wording, currency and locale are data, so the product can be
   re-skinned and re-sold without touching code.
5. **Bilingual by construction, not by translation pass.** Arabic and English are equal citizens in the
   resource layer, and right-to-left is a first-class layout mode.

## Accessibility & Inclusion

* Right-to-left is a first-class layout, not a mirrored afterthought.
* Status is never communicated by colour alone — every state carries an icon and a text label.
* Full keyboard operability: the POS flow completes without a mouse, and every control is reachable by Tab
  with a visible focus ring.
* Minimum interactive target size 44 px on the POS and 32 px rows in management tables.
* Windows text scaling (100 %–200 %) must not clip the POS totals or the receipt preview.
* Contrast targets WCAG 2.1 AA for body text and controls; the theme editor validates contrast live and
  warns below AA, flags below AAA.
