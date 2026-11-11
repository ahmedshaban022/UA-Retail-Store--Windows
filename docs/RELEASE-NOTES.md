# StoreFlow Desktop v1.0.2 — release notes

**Release status: `published`.**

StoreFlow is an offline shop programme for keeping a catalogue, tracking stock and purchases, making sales and returns, reviewing reports, and taking backups. It can be used in Arabic or English.

**Version 1.0.2.** This is a repair release. It fixes a fault that could stop a shop from opening at
all, and a second fault behind it that could stop the programme from starting. Nothing about how
the shop works day to day has changed.

**You do not need to hold off using v1.0.2.** If your shop opened normally on v1.0.1, this release
changes nothing for you and you can keep using what you have. If your shop **refused to open**
after an upgrade, this is the release that fixes it.

## What changed

### A shop that is behind the migrations can be migrated again

A shop's database is brought up to date by applying migrations, one after another. Some of those
migrations change the shape of a table, and SQLite does that by making a new table, copying the
rows, dropping the old one and renaming the new one.

**SQLite will not drop a table that a trigger still refers to**, and StoreFlow keeps guard
triggers on the ledger's tables — the ones that refuse a negative stock balance or a change to a
posted document. So a shop that had already run the programme could not be migrated at all: the
migration stopped with *"no such table: main.SalesInvoices"*, the shop would not open, and the
recovery wizard had nothing to offer but a way out.

The guards are now taken down before a migration runs and put back immediately afterwards, so a
shop that is behind can be brought up to date again. The guards are back in place before the shop
can do anything at all: they are re-applied on every start.

### The programme no longer asks a connection it cannot write to change the database file

This one is worth describing, because it is the kind of fault that looks like anything but what it
is.

Before migrating, the framework checks whether the database exists — and it opens that check
**read-only**, so that the check does not bring a database into being as a side effect. StoreFlow
applied its SQLite settings to every connection it opened, and one of those settings — putting the
database into the write-ahead log — is a change **to the file**, not to the connection. On a
connection that cannot write, that change fails, and the failure comes out of the open and stops
the programme from starting.

The settings that belong to the connection — foreign keys on, the busy timeout, the full
synchronous journal — are still applied to every connection, read-only or not. The journal mode is
a property of the database file, so it is now asked for in a way that tolerates a connection that
cannot write, and the writable connections set it.

### Two honest limits, unchanged

- **An interrupted migration still cannot heal itself.** SQLite's own table rebuild runs outside
  the migration's transaction, so a programme that is killed half way through leaves work behind,
  and the next start stops on the first piece it finds. The repair above removes the *trigger*
  obstacle, so a shop with its guards in place can now be migrated — but a genuinely half-applied
  migration still needs the copy the programme takes before every migration, which is kept under
  `%APPDATA%\StoreFlow\data\restore-points`.
- **The recovery wizard still has no way out.** When the shop's data cannot be read and no backup
  is restored, the wizard offers only **Close**, and the programme then refuses to start. A way to
  start again with a fresh shop, keeping the old folder aside, is a new screen and is not in this
  release.

### Still open from before

- **Backup files (`.sfb`) are still not encrypted.** They hold the shop's data in the clear. The
  full design has been written down and reviewed
  (`docs/decisions/ADR-0005-archive-encryption.md`) but is deliberately **not** in this release: a
  format change on the backup and restore path could leave a shop without a usable copy of its
  data. Until it arrives, keep the backup files on a protected disk, control who can read them,
  and use full-disk encryption (for example BitLocker) on the disk they are on.
- When a price override is not allowed, StoreFlow refuses the sale instead of asking a manager to
  approve it.
- Stock counts are not blind: the person counting can see the recorded quantity, which can
  influence the count.
- The installer is not signed with a publisher's certificate. The SHA-256 proves that the file is
  the one that was published and has not been changed since; it does not prove who published it.

## Installing

The installer is the `StoreFlow-1.0.2-x64.msi` file. On Windows, double-click it and follow the
steps. It installs for the current user and does not need an administrator account. The default
programme location is `%LOCALAPPDATA%\Programs\StoreFlow`.

**Upgrading from v1.0.0 or v1.0.1:** run the new installer. It replaces the programme in place and
leaves `%APPDATA%\StoreFlow` alone, so the shop's catalogue, ledger, accounts and backups carry
over untouched. A shop that is already on v1.0.1 can also let the programme do it: open
**Settings**, find **About this programme**, press **Check for updates**, and press **Install now**
when it offers v1.0.2.

**If the installer refuses with "another version of this product is already installed":** that is
Windows Installer saying it will not put the same version over itself. Remove the installed copy
from Windows' *Installed apps* and run the new installer again. The shop's data is not part of the
installation and is left alone.

The shop's data is kept in `%APPDATA%\StoreFlow`, separately from the programme. Installing,
upgrading or removing the programme does not remove that data. Keep your own backups as well.

## Taking the first backup

Once you have completed setup and signed in, open **Backup**, choose the backup target, and press
**Back up now**. The copy goes to the folder or cloud target you selected. The shop's automatic
local backups are kept under `%APPDATA%\StoreFlow\backups`. Keep a copy on a protected disk away
from the shop computer too.

## Check the installer

The installer is published together with a file that holds its SHA-256, and the same hash is
printed on the release's own page, next to the installer. Compare the two before you install: if
they are not the same, the file you were given was changed on the way.

For help, contact the person who supplied or supports your StoreFlow installation.
