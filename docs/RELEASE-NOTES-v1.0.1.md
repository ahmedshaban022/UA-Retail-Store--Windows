# StoreFlow Desktop v1.0.1 — release notes

StoreFlow is an offline shop programme for keeping a catalogue, tracking stock and purchases, making sales and returns, reviewing reports, and taking backups. It can be used in Arabic or English.

**Version 1.0.1.** This is a repair and an honesty release. It closes the two problems the v1.0.0
notes still had open, explains the one that is still open, and proves for the first time that a
released copy of StoreFlow can update itself.

**You do not need to hold off using v1.0.1.** The v1.0.0 notes said that the restore confirmation
had failed once and that the cause was unknown, and asked that this be investigated before the
programme was handed to a shop. It has now been investigated, the cause was a fault in the test
harness rather than in the programme, and it has been fixed. The restore confirmation behaves
correctly.

## What changed

### The audit trail can no longer lose an entry

Every change that alters the shop's data — a catalogue edit, a stock movement, a purchase, a
count, a suspended basket, a discount approval, an account change, a sale, a return, a reversal,
a drawer count, the first-run setup — now writes its audit entry **in the same write as the change
itself**. The entry and the change are made permanent together, or not at all. Before this
release, most commands wrote the entry in a second, separate write after the change had already
been committed: if the computer had lost power in between, the shop could have a change on the
books with no record of who made it. That window is closed for everything listed above.

Two places still write their entry separately, and it is worth knowing which:

- **Restoring a backup.** A restore replaces the shop's whole database file. There is no
  transaction that spans replacing a file and inserting a row into it, so the entry that records
  the restore is written to the restored shop immediately afterwards. A crash in that moment leaves
  a restored shop whose trail does not mention the restore; the restore point itself still holds
  the previous state.
- **Changing the backup schedule.** The backup schedule is written by the settings store, which
  owns its own write. Its entry is written immediately afterwards, in the same way.

Neither of the two can leave the shop's *stock, money or catalogue* in a state it should not be
in; the only thing at risk in either case is the presence of one line in the audit trail.

### Every control has a name a screen reader can read

All 88 controls that had no accessible name now have one. A screen-reader user can now identify
every button, field and list on every screen. This was the second thing the v1.0.0 notes listed as
open, and it is now closed.

### The programme can update itself, and this release proves it

The updater that shipped with v1.0.0 had never been used against a real published release. v1.0.1
is the first release where that was done end to end: a shop with v1.0.0 installed found v1.0.1,
downloaded it, checked it, installed it, and kept all of its data.

Here is exactly what the updater does, so that a shop can decide what it thinks of it.

- **What is checked.** The public list of StoreFlow Desktop releases on GitHub, and nothing else.
  The request asks one question: "is there a release newer than the one running?" It is an
  anonymous request. Nothing about the shop is sent with it — no shop name, no licence, no
  counter, no identifier, no hardware list, no file names. The shop cannot be singled out by the
  request, because the request carries nothing that could single it out.
- **When.** Once, in the background, shortly after the programme opens. The programme never waits
  for it: the till opens and the sale screen works exactly as before while the check is in
  flight. It never repeats while the programme is open.
- **What is downloaded.** Nothing, until the shop presses the button. When the shop presses
  **Install now**, the updater downloads two files: the installer, and the small `.sha256` file
  published beside it that holds the installer's SHA-256. It then computes the SHA-256 of what it
  downloaded and compares it with the published hash. **If they differ, the download is thrown away
  and the update is refused.** The files are kept in a temporary folder (`%TEMP%\StoreFlow`) so that
  the installer can run from it. A download that does not match is deleted at once; a download that
  does match is left there until Windows clears the temporary folder.
- **What is not touched.** The updater never writes inside the shop's data folder. It does not
  open the ledger, the catalogue, the backups or the settings. The upgrade replaces the
  *programme*; `%APPDATA%\StoreFlow` is not part of the programme and is left exactly as it was.
  This was measured on a real upgrade: the shop's database held the same tables with the same rows
  before and after, and the shop's folder held the same files with the same contents. The *content*
  is what was compared, because the bytes of a database file move when the programme closes even
  when nothing was written to it.
- **How to turn it off.** Nothing is ever installed unless the shop presses the button — that is
  the switch the shop already has, and it is the reason a shop that never wants an update never
  gets one. To stop even the single background check, block `api.github.com` in the shop's
  firewall or proxy. The programme then carries on working exactly as before: a check that cannot
  reach the release list is not an error, so no message appears and nothing is written.

One honest limit, as in v1.0.0: the installer is not signed with a publisher's certificate. The
SHA-256 proves that the file is the one that was published and has not been changed since; it does
not prove who published it. A shop that needs that guarantee should check the installer against
the hash printed at the end of these notes, from a source it trusts.

## Known open items

- **Backup files (`.sfb`) are still not encrypted.** They hold the shop's data in the clear. This
  was the third item in the v1.0.0 notes and it is still open: encrypting the archive is a change
  to the file format, and the full design has been written down and reviewed
  (`docs/decisions/ADR-0005-archive-encryption.md`) but is deliberately **not** in this release.
  The reason is a plain one: a format change on the backup and restore path, in the first release
  after v1.0.0, could leave a shop without a usable copy of its data. Until it arrives, keep the
  backup files on a protected disk, control who can read them, and use full-disk encryption (for
  example BitLocker) on the disk they are on.
- When a price override is not allowed, StoreFlow refuses the sale instead of asking a manager to
  approve it.
- Stock counts are not blind: the person counting can see the recorded quantity, which can
  influence the count.

## Installing

The installer is the `StoreFlow-1.0.1-x64.msi` file. On Windows, double-click it and follow the
steps. It installs for the current user and does not need an administrator account. The default
programme location is `%LOCALAPPDATA%\Programs\StoreFlow`.

**Upgrading from v1.0.0:** run the new installer. It replaces the programme in place and leaves
`%APPDATA%\StoreFlow` alone, so the shop's catalogue, ledger, accounts and backups carry over
untouched. A shop that is already on v1.0.0 can also let the programme do it: open **Settings**,
find **About this programme**, press **Check for updates**, and press **Install now** when it
offers v1.0.1.

The shop's data is kept in `%APPDATA%\StoreFlow`, separately from the programme. Installing,
upgrading or removing the programme does not remove that data. Keep your own backups as well.

## Taking the first backup

Once you have completed setup and signed in, open **Backup**, choose the backup target, and press
**Back up now**. The copy goes to the folder or cloud target you selected. The shop's automatic
local backups are kept under `%APPDATA%\StoreFlow\backups`. Keep a copy on a protected disk away
from the shop computer too.

## Check the installer

The installer is published together with a file that holds its SHA-256, and the same hash is
printed at the end of these notes. Compare the two before you install: if they are not the same,
the file you were given was changed on the way.

For help, contact the person who supplied or supports your StoreFlow installation.
