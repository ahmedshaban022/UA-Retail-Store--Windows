# StoreFlow Desktop v1.0 — release notes

StoreFlow is an offline shop programme for keeping a catalogue, tracking stock and purchases, making sales and returns, reviewing reports, and taking backups. It can be used in Arabic or English.

**Release candidate.** Backup and recovery checks passed against both the development programme and the installed copy. In an earlier development run, the restore confirmation did not appear; the cause has not been established, so this should be investigated before the programme is handed to a shop.

## What v1.0 does not do

A sale cannot have a customer attached or be paid using split tender. Each purchase has one supplier and one currency. StoreFlow does not offer a blind stock count or ask a manager to approve a price override.

## Known open items

- Backup files (`.sfb`) are not encrypted. They contain the shop's data in the clear, so keep them on a protected disk and control who can access them.
- Some controls have no accessible name. A screen-reader user may be unable to identify or use them.
- A sale and its audit entry are now written together, so a sale cannot exist without its audit entry. Some other actions still write their audit entry just after the action itself has been saved.
- When a price override is not allowed, StoreFlow refuses the sale instead of asking a manager to approve it.
- Stock counts are not blind: the person counting can see the recorded quantity, which can influence the count.

## Installing

The installer is the `StoreFlow-1.0.0-x64.msi` file. On Windows, double-click it and follow the steps. It installs for the current user and does not need an administrator account. The default programme location is `%LOCALAPPDATA%\Programs\StoreFlow`.

The shop's data is kept in `%APPDATA%\StoreFlow`, separately from the programme. Installing, upgrading or removing the programme does not remove that data. Keep your own backups as well.

## Taking the first backup

Once you have completed setup and signed in, open **Backup**, choose the backup target, and press **Back up now**. The copy goes to the folder or cloud target you selected. The shop's automatic local backups are kept under `%APPDATA%\StoreFlow\backups`. Keep a copy on a protected disk away from the shop computer too.

## Check the installer

The SHA-256 of this release candidate's installer is:

`13879A7237429097D8BD933CA994534612B6FED2870F1DBF71516EADB07E5ABD`

For help, contact the person who supplied or supports your StoreFlow installation.
