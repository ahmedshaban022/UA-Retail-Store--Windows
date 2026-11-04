# ADR-0005 — Encrypted backup archives: the design, and why it is not in v1.0.1

| | |
|---|---|
| **Status** | Proposed — designed here, to be built and proved in v1.1 |
| **Date** | 2026-11-04 |
| **Deciders** | Implementation agent |
| **Phase** | v1.0.1 — the release-cadence phase |
| **Supersedes** | — |
| **Nature** | A policy choice about the shop's data, pinned here so the change is made once, deliberately, and tested as a whole. |

## Context

A `.sfb` backup holds the shop's entire database — the catalogue, every invoice, every payment, every
customer's stock movement — together with the shop's pictures and settings. Today it is a plain zip.
**Anyone who gets the file can read all of it.** PRD §8.5 (line 472) says:

> Encrypt portable backup archives with a strong password where feasible (recommended P1 if not
> included in MVP).

Phase 6 deferred this to the release phase with the reason written down. Phase 8 declined it again and
published the consequence in the v1.0.0 release notes, which every shop owner reads:

> Backup files (`.sfb`) are not encrypted. They contain the shop's data in the clear, so keep them on
> a protected disk and control who can access them.

So the shop is told, in its own release notes, that its backups are open. That is honest and it is not
good enough. This ADR specifies the design so that v1.1 can build it once, properly, instead of it
being re-litigated in every release.

### Why not in v1.0.1

v1.0.1 is the **first release after v1.0.0**. It exists to prove the release cadence — that a shop's
installed copy can find a new release, download it, check it and upgrade itself. It also carries CI,
the fix for the unexplained restore failure, and the accessible names.

Encryption is not a patch. It changes the *format of the shop's backups*, and it lands on the two
paths that matter most when a shop's computer dies:

1. **The scheduled backup.** It runs unattended. If the passphrase is not available at 03:45, the
   programme must decide what to do — and the two available answers are "write nothing" (the shop
   silently stops taking backups) or "write an unencrypted copy" (the shop silently loses the
   protection it thinks it has). Both are bad. Getting that right needs a decision about where the
   passphrase lives, and that decision has to be made together with the format change.
2. **The restore.** A shop that upgrades to v1.1 and then restores an archive written by v1.0.0 must
   still get its shop back. A partial format change at v1.0.1 — where the writer understands format 2
   but the reader has an edge case, or the scheduled backup writes a format-2 file and the restore
   path does not recognise it — leaves a shop **without a usable copy of its own data**. That is the
   one failure this product must never have.

The decision is therefore: **the design is settled now, in this ADR; the change is built, proved and
released as v1.1**, with the format change, the unattended path, the migration and the lost-passphrase
rules landing together.

## Decision

### 1. The container stays a zip

The `.sfb` remains a standard zip archive. The shop's own tools can list it, and a shop owner can
open it on any Windows PC. A bespoke binary container would be one more thing to explain and one more
thing to get wrong.

### 2. A small, unencrypted marker says what the archive is

Every format-2 archive carries one extra entry, `storeflow-backup.json`, which is **not encrypted** and
contains no shop data:

| Field | Why |
|---|---|
| `formatVersion` (2) | Tells the reader whether this is the old format or the new one. |
| `cipher` | `AES-256-GCM`. |
| `kdf` | `PBKDF2-HMAC-SHA256`. |
| `iterations` | 600,000. |
| `salt` | 16 random bytes, base64. |
| `createdUtc` | When the archive was written. |

The marker exists so that the programme can tell **"this is an encrypted StoreFlow backup"** from
**"this is a file that is not a StoreFlow backup"** without a passphrase, and can tell the owner the
right thing. It leaks the fact that a backup exists and when it was made. It leaks nothing about the
shop.

### 3. Every part is encrypted, and the manifest is one of the parts

Each part — the database, the settings, every picture — is encrypted with **AES-256-GCM** under a
**fresh 96-bit nonce**, and the part's path inside the archive is bound into the GCM *additional
authenticated data*. That binding matters: it means a part cannot be swapped for another part of the
same archive without the tag failing, so an attacker cannot move the shop's database into the slot of
a picture.

The **manifest is encrypted as a part** rather than left in the clear. It records the schema version,
the app version, the machine name and the SHA-256 of every part; leaving it readable would tell a
reader what the shop runs and what it looks like, for no benefit.

The integrity story does not change: the manifest still records the SHA-256 of every part, computed
over the **decrypted** bytes, and the restore still refuses an archive whose parts do not agree with
its own manifest. The whole-file SHA-256 that the programme publishes beside the file still covers the
file's bytes. It proves the file arrived intact. It does not prove who published it, and it never did.

### 4. The key comes from the owner's passphrase, stretched the slow way

The key is derived from the passphrase the owner chooses with **PBKDF2-HMAC-SHA256, 600,000
iterations**, over a 16-byte random salt per archive. 600,000 is OWASP's current figure for
PBKDF2-HMAC-SHA256.

PBKDF2 is chosen over Argon2id even though this project already uses Argon2id for passwords
(`PasswordHasher`), because PBKDF2 is in the platform, needs no extra dependency, and is what every
tooling ecosystem can verify. That is a deliberate trade of a stronger-but-optional primitive for a
weaker-but-universally-available one, and it is recorded here rather than left implicit.

**The passphrase is never written into the repository, the settings table, the log, or the archive.**

### 5. The unattended backup gets the passphrase from Windows, not from the owner

The scheduled backup runs without anybody there, so the passphrase has to be available to the Windows
account that runs the shop's programme. It is stored with **DPAPI, `CurrentUser` scope** — readable by
that account and nobody else, and unreadable if the account is replaced.

The rule that matters:

> **If the passphrase cannot be read, the scheduled backup writes NOTHING and says so in the log.**
> It never falls back to an unencrypted archive.

A silent fallback would be worse than no backup at all, because the shop would believe it was
protected when it was not. A loud failure is visible in the backup log, and the shop's own screen
says the same.

### 6. A lost passphrase loses that archive, and the programme will not make it worse

A passphrase cannot be recovered. That is the price of the protection, and it must be said plainly to
the owner.

The rules:

- A wrong or missing passphrase **never** deletes, moves, truncates or overwrites the archive.
- The archive stays exactly where it is, byte for byte, so that a copy elsewhere — or a recovery
  expert — can still be used.
- The owner is told, in the shop's own language, that this archive needs a passphrase and that the
  passphrase cannot be recovered by the programme.

The owner is told at the moment they set it, too: **the passphrase is the archive's only key, and it
is not written down anywhere by the programme.** A shop that keeps the passphrase on a label on the
USB stick that holds the backup has protected nothing, and the release notes for v1.1 must say so.

### 7. The old archives still restore

The reader decides by looking for the marker:

- **No marker** → the archive is format 1, the format every backup written so far is. It restores
  exactly as it does today, with **no passphrase**. Every backup already on a shop's disk keeps
  working.
- **Marker present** → the archive is format 2 and needs the owner's passphrase.

A format-1 archive is never rewritten in place and never silently converted. If the shop wants its
backups encrypted, it takes new ones; the old ones keep restoring until the shop deletes them itself.

### 8. What has to be true before v1.1 ships it

The change is not done until all of these are proved:

- A format-2 archive written and read back gives the same shop, with the same figures.
- A **wrong passphrase** is refused, and the archive is left untouched.
- A **format-1 archive still restores** with no passphrase, on the same machine and in a second
  profile.
- A **swapped part** is refused (the additional authenticated data does its job).
- The marker leaks nothing about the shop: no file names, no schema version, no machine name.
- The **scheduled** backup writes nothing when the passphrase cannot be read, and the log says why.
- A **lost passphrase** leaves the archive on disk, unchanged, with the owner told.

## Consequences

- The archive is no longer openable with a zip tool. That is the point, and it is stated in the
  release notes.
- The shop must remember a passphrase. The user guide gains a section on choosing one, and on what to
  do when it is forgotten.
- The passphrase has to reach a second machine when the shop's computer dies, or the backup is
  useless there. The recovery runbook gains that step.
- The failure mode of "the scheduled backup stopped" becomes visible in the log rather than silent.

## Status

**Not implemented.** This ADR is the specification, not a claim. v1.0.0 and v1.0.1 ship unencrypted
archives, and both say so in the notes the shop owner reads. The build lands in v1.1 with the tests
listed in section 8, and until then the honest instruction to a shop owner is the one already
published: keep the copies on protected storage and control who can reach them.
