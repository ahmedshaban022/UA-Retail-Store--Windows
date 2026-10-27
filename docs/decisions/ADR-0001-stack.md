# ADR-0001 — Application stack: C# / .NET 10 / WPF

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-25 |
| **Deciders** | Implementation agent (decision delegated by the product owner with the brief "choose the best language … for performance") |
| **Phase** | 0 — Foundation |
| **Supersedes** | — |

## Context

StoreFlow is an offline-first Windows desktop application for a single retail store. It must:

- drive **USB HID barcode scanners** (keyboard wedge), **80 mm thermal receipt printers**, and **A4 printers**;
- render **Arabic right-to-left** text correctly, with ligatures and mixed-direction isolation;
- hold an **append-only monetary ledger** that reconciles to the piastre;
- run entirely offline, on a shop PC, with no runtime to install and no network dependency;
- be **rebrandable** (name, logo, colours, receipt, currency, locale) without recompiling.

The brief asked for "the best language (C#, Rust, …) for performance". The performance targets taken from the
PRD are: product list ≤ 2 s at 10 000 products, scan-to-cart < 1 s, five-line sale ≤ 30 s. Every one of those
is **I/O- and UI-bound**, not CPU-bound: a list query is a SQLite index scan, a scan is a hash lookup plus a
grid update, a sale is a transaction with a handful of writes. The workload is dominated by SQLite, by the
Windows print spooler, and by the compositor — not by arithmetic in the language runtime.

Meanwhile, the two hardest requirements — printing and Arabic shaping — are the two areas where the "fast
language" option is weakest:

- **Printing.** WPF gives first-class Windows printing: `PrintDocument`/GDI+, `System.Printing` for paper
  sizes and queues, and direct `CreateFile` access to a thermal printer's USB share for ESC/POS. A web-view
  shell (Tauri, Electron) reaches printers only through a JS printer bridge or a driver-driven virtual
  printer, and raw ESC/POS over `\\host\printer` needs `FILE_SHARE_WRITE` semantics that a sandboxed webview
  does not offer.
- **Arabic shaping.** Windows text shaping is a solved, tuned problem in DirectWrite/Uniscribe, exposed to
  .NET for free. A Rust or C++ GUI toolkit without its own shaping engine either renders unshaped, reversed
  Arabic or ships a HarfBuzz port — a second, permanent maintenance and correctness surface.

The rest of the stack was unconstrained, so the choice could be made on those two axes plus delivery risk.

## Decision

Use **C# 14 on .NET 10 LTS with WPF**, structured as Clean Architecture:

| Layer | Project | Target framework | Responsibility |
|---|---|---|---|
| Domain | `StoreFlow.Domain` | `net10.0` | Money, quantity, tax, valuation, entities, permissions. **References nothing.** |
| Application | `StoreFlow.Application` | `net10.0` | Use cases, ports, authorisation, validation, theme schema. References Domain only. |
| Infrastructure | `StoreFlow.Infrastructure` | `net10.0-windows` | EF Core + SQLite, backup engine, cloud targets, printing, theming store, logging. References Application. |
| Desktop | `StoreFlow.Desktop` | `net10.0-windows` | WPF views, view models, theme engine, localisation, composition root. References Application + Infrastructure. |

Supporting choices, all in the same spirit:

- **CommunityToolkit.Mvvm 8.4.2** for observable view models and commands — source-generated, no runtime
  reflection cost.
- **EF Core 10 + Microsoft.Data.Sqlite 10** for schema, migrations and typed queries, with **raw parameterised
  SQL** for the posting hot path, report aggregation, and the SQLite Online Backup API. One model, one
  migration story.
- **SQLite** as the single local store (file-based, WAL). It is the only store that gives transactional
  atomicity with zero server, zero admin, and zero network on an offline shop PC.
- **Serilog** for structured diagnostics to a rolling file.
- **xUnit 2.9.3 + Shouldly** for tests (see ADR note below on FluentAssertions).

The dependency rule is enforced by project references, not by convention: `Desktop → Application → Domain`,
`Infrastructure → Application`, and `Domain` references nothing.

## Consequences

**Positive**

- Printing, USB access, DPI/per-monitor-v2, and Arabic shaping all work through first-class Windows APIs with
  no bridging layer.
- The offline story is trivial: a self-contained `win-x64` publish, a local SQLite file, no service, no
  runtime prerequisite on the shop PC.
- The performance targets are comfortably reachable — the hot paths are a `FrozenDictionary` lookup and an
  indexed SQLite query, and the dominant cost is rendering, which WPF does on the GPU.
- .NET 10 is an LTS line, so the shop PC is not on a support treadmill.

**Negative**

- WPF does not trim, so a self-contained publish is roughly 150–200 MB. Accepted: the MSI compresses it and a
  shop PC has the disk.
- The C# compiler is not as fast as Rust's, so compile times are longer. Accepted: this is a desktop product
  with a build pipeline, not a service with a 30-second feedback loop.
- WPF's XAML is verbose and its designer story is dated. Accepted in exchange for the printing and shaping
  capabilities above; the rest of the app is MVVM with view models that are plain, testable C#.

**Explicitly rejected**

- **Rust + Tauri** — the strongest runner-up on raw performance, which this workload does not need. It loses on
  ESC/POS access to a USB printer share, on Arabic shaping without shipping HarfBuzz, and on the breadth of
  the Windows printing and DPI APIs.
- **Rust + egui/slint** — no first-class Arabic shaping, no Windows printing story, and a much larger
  long-term correctness surface for a product whose hardest requirements are exactly those two.
- **WinUI 3 / Avalonia** — Avalonia has no Windows printing story at all, which breaks the POS receipt
  requirement outright. WinUI 3's deployment story adds a Windows App SDK runtime dependency on an offline
  shop PC.
- **Electron / web stack** — requires a Chromium runtime on the shop PC, has the same printing weakness as
  Tauri, and is the largest binary for the least benefit here.

## Notes

**Test framework.** xUnit 2.9.3 with Shouldly. FluentAssertions 8 was rejected because its licence carries
commercial terms; xUnit v3 was avoided in favour of the stability of `dotnet test` in this toolchain.

**Environment note.** The development machine had no .NET SDK. `winget install Microsoft.DotNet.SDK.10`
stalled in the WiX Burn engine and was abandoned; the SDK was installed instead as a portable extraction of
`dotnet-sdk-10.0.402-win-x64.zip` at `D:\dotnet`, with `DOTNET_ROOT` set and `D:\dotnet` prepended to `PATH`.
This affects how the repository is built on this machine, not what it builds — see `README.md`.

## References

- `StoreFlow_Retail_Management_PRD_EN.md` §8.1 (recommended stack), §8.2 (performance targets), §7.1 (printing,
  localisation), §2.3 (explicitly out of scope).
- `PRODUCT.md` — product truth and operating context.
- `DESIGN.md` — the visual system this stack renders.
