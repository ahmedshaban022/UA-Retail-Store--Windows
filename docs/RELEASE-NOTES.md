# StoreFlow Desktop v1.0 — release notes

StoreFlow is an offline shop programme for keeping a catalogue, tracking stock and purchases, making sales and returns, reviewing reports, and taking backups. It can be used in Arabic or English.

**Release candidate: not ready for shop use.** During the installed release checks, first-run setup could not open. The problem must be fixed and the installed programme checked again before handing it to a shop.

## What v1.0 does not do

A sale cannot have a customer attached or be paid using split tender. Each purchase has one supplier and one currency. StoreFlow does not offer a blind stock count or ask a manager to approve a price override.

## Known open items

- Backup files (`.sfb`) are not encrypted. They contain the shop's data in the clear, so keep them on a protected disk and control who can access them.
- Some controls have no accessible name. A screen-reader user may be unable to identify or use them.
- A sale is saved just before its audit entry is written. In a very small window, an interruption can leave a sale without that entry.
- When a price override is not allowed, StoreFlow refuses the sale instead of asking a manager to approve it.
- Stock counts are not blind: the person counting can see the recorded quantity, which can influence the count.

## Installing

The installer is the `StoreFlow-1.0.0-x64.msi` file. On Windows, double-click it and follow the steps. It installs for the current user and does not need an administrator account. The default programme location is `%LOCALAPPDATA%\Programs\StoreFlow`.

The shop's data is kept in `%APPDATA%\StoreFlow`, separately from the programme. Installing, upgrading or removing the programme does not remove that data. Keep your own backups as well.

## Taking the first backup

Once setup works and you have signed in, open **Backup**, choose the backup target, and press **Back up now**. The copy goes to the folder or cloud target you selected. The shop's automatic local backups are kept under `%APPDATA%\StoreFlow\backups`. Keep a copy on a protected disk away from the shop computer too.

## Check the installer

The SHA-256 of this release candidate's installer is:

`E751055E2B86EB81ACEAC6E836790BD99681C3E927C948A5E103810E3FC753DE`

For help, contact the person who supplied or supports your StoreFlow installation.
