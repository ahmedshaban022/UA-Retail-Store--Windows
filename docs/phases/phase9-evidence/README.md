# Phase 9 evidence

The logs the [Phase 9 report](../PHASE-9-V1.0.1.md) points at. They are kept here because a
report that says "the evidence is in…" has to point at something a reader can open.

| File | What it shows |
|---|---|
| `backup-run-1.log`, `backup-run-2.log` | The backup harness against the development build after the restore-confirmation fix: **39 checks, 0 failures** on each run. |
| `backup-installed-run-1.log`, `backup-installed-run-2.log`, `backup-installed-run-3.log` | The same harness run **three times against the installed v1.0.1** — the published release, driven the way a shop would drive it: **39 checks, 0 failures** on each run. |
| `update-cycle-1.0.0-to-1.0.1.log` | The updater's first real cycle: the published v1.0.0 installed, found v1.0.1, downloaded it, checked the download against the published checksum, installed it, and came back as v1.0.1 with the shop's data measured before and after. |
| `proof1-the-trigger-drop-removed.log` | The proof that the migration tests catch the fault they were written for: with the guard triggers **not** taken down before the migration, the two tests fail with `no such table: main.SalesInvoices` — the error that stopped the shop from opening. |
| `proof2-the-read-only-tolerance-removed.log` | The proof that the pragma tests catch the second fault: with the tolerance for a connection that cannot write removed, the tests fail with `attempt to write a readonly database`. |
| `proof-the-real-shop-opens.log` | The end-to-end proof: the new build started against a **copy** of the real shop, the owner signed in, the shell opened, and the shop's content was measured before and after — 28 tables, the same rows, and only the sign-in's own audit entry added. |

The logs are produced by harnesses that live in the repository and can be run again:

- `tools/smoke-backup.ps1` — the backup, restore and recovery harness.
- `tools/update-cycle.ps1` — the release cycle.
