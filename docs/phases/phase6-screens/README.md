# Phase 6 — the backup, the restore and the recovery, as they were proved

Every picture in this folder was taken by `tools/smoke-backup.ps1` while it drove the real
application. Nothing here is a mock-up and nothing was drawn by hand.

**The application draws its own windows.** The script asks the running application, over a capture
pipe, to draw one of its own windows, and the application writes the picture itself. This matters:
a picture taken from outside the process comes back mirrored for a right-to-left window, and the
mirrored version of an Arabic screen is a lie about the screen. The pictures below are what the
owner sees.

The run told one story, in two shops. In the first shop it chose where the copy would go, set the
schedule, created a product, took a copy, and read the file back from the disk. It then opened the
restore wizard, checked that copy against its own checksum, and put the shop back — and it started
the application again to find the product and the schedule still there. In the second shop, an
**empty** shop folder on the same machine, it found the same copy through the file dialog and put it
in, and started that shop again to find the first shop's product in it.

Every number the run checked was a value the screen had to reach — the checksum of a copy, the
message of a wizard, the schedule that came back — and never the number of rows in a table, which
proves nothing when the count is already right.

| Picture | What it shows |
|---|---|
| `backup-screen.png` | The backup screen in English: the destinations and the picker that says where the copy goes, the schedule, the log of the copies that have been taken, and the checksum of the copy that was just taken |
| `restore-wizard-checked.png` | The restore wizard with a copy chosen and checked: the integrity verdict, the size of the file, its checksum, the version of the application that wrote it and the schema it carries |
| `restore-wizard-confirm.png` | The same wizard, after "Restore the shop": what is about to be replaced, and the buttons that confirm or step back |
| `restore-wizard-restored.png` | The wizard after the restore: the shop was put back, and the path of the restore point — the data it replaced — is on the screen |
| `recovery-wizard.png` | The recovery wizard: the runbook for the day the shop's data cannot be read, and the button that puts the shop back without a signed-in administrator |
| `backup-screen-arabic.png` | The backup screen in Arabic, with the same copy, the same schedule and the same log |
| `restore-wizard-arabic.png` | The restore wizard in Arabic |
| `recovery-wizard-arabic.png` | The recovery wizard in Arabic |
| `restore-wizard-other-profile.png` | The restore wizard in the **second** shop folder, with the copy of the first shop chosen through the file dialog and checked there |

What these pictures were taken to prove:

- **The destination is on the screen** — the picker at the top of the backup screen says where the
  copy goes. It used to be decided in the code, where the owner could neither see it nor change it.
- **The screen says what happened** — the line under the buttons is where "the schedule was saved"
  and every error message appear.
- **The copy is checked before anything is touched** — `restore-wizard-checked.png` carries the
  verdict and the checksum, and the checksum is the same one the backup screen showed for the same
  file. A copy that does not match its own checksum is refused.
- **The restore asks first** — `restore-wizard-confirm.png` is the moment between the check and the
  action, and the restore point is named on the screen afterwards.
- **The shop comes back whole** — after the restore the application was started again, and the
  product, the daily time, the retention and the backup folder were all still there.
- **The same copy restores into another shop** — `restore-wizard-other-profile.png` is the wizard in
  the second, empty shop folder, and after that restore the second shop held the first shop's
  product.

**One flaw in the pictures themselves:** in `restore-wizard-other-profile.png` and
`restore-wizard-arabic.png` the "Choose a backup" line is cut off at the top, because those two
windows were captured shorter than their content and the label went under the header. It is a flaw
in the evidence, not in the screen — the same line is whole in `restore-wizard-checked.png`, and the
wizard is the same window in all three.
