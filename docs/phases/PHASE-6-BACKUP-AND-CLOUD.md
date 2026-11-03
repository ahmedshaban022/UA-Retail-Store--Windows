# Phase 6 — Backup and cloud

The shopkeeper's question at the worst moment: *the computer died — is my shop still there?*

Phase 6 answers it. The shop takes a copy of itself on a schedule without anyone remembering to,
the copy is a **single file** that can be carried on a USB drive or written to an object store, the
file proves its own integrity before anything is touched with it, and the shop can be put back from
it — on the same machine or on another one — with the data, the settings and the pictures exactly as
they were.

It also builds the way back in when the shop's data cannot be read at all: the **recovery wizard**,
which is what the application shows instead of the sign-in window when the database will not open.

Every rule below was proved by the automated suite, and then proved again by driving the real
application through the backup screen, the restore wizard and the recovery wizard, and taking the
pictures the application draws of itself.

---

## What was built

### One file that carries the whole shop

A backup is a **`.sfb` file** — a zip container with a fixed shape:

```
manifest.json                  the description of the copy, written last
database/storeflow.db          the shop's data, taken with SQLite's own online backup
settings/store-settings.json   the shop's settings and its details
media/**                       the product pictures
```

The **manifest is written last**, and it carries a SHA-256 for every part. That ordering is the whole
integrity story: a file that was cut short by a power cut has no manifest, or has a manifest that
does not match its parts, and is refused. A file that was changed after it was written — one part
altered, or one part added — is refused as well, because the manifest says exactly which parts the
copy has and what each of them hashes to.

`BackupArchive.Inspect` is the only way in. The restore never opens an archive directly; it asks the
archive what it holds, and it acts only on the answer.

**The archive is not encrypted.** PRD §8.5 asks for it and it is deferred to Phase 7 on purpose: an
encrypted archive needs a key, and a key that is stored on the same machine as the copy is not a
secret. Until there is a passphrase the owner chooses, a `.sfb` file holds the shop's commercial data
in the clear, and that is written down here rather than left to be discovered.

### The capture cannot catch the shop halfway through a sale

The copy is taken with **SQLite's own online backup API** (`SqliteConnection.BackupDatabase`), not by
copying the file. A file copy of a live SQLite database catches whatever the write-ahead log happens
to hold at that instant; the online backup takes a consistent snapshot of the whole database while
the shop keeps trading.

The copy is then checked with `PRAGMA integrity_check` **before** it is put in the archive. A capture
that does not pass the check is not written anywhere.

One small thing the tests caught: the temporary copy could not be deleted afterwards, because the
connection that wrote it was still pooled and still holding the handle. The capture now opens its
temporary database with pooling switched off, so the file can be removed the moment it is done with.

### Two destinations, and one that says so when it is not there

| Destination | What it is |
|---|---|
| **Local folder** | A folder on the machine or on a USB drive. It is the default, and it needs nothing but a path. |
| **S3-compatible object store** | Any service that speaks the S3 API — AWS S3, MinIO, or a NAS that presents itself as one. The AWS SDK is already referenced by the project. |

Google Drive, OneDrive and Dropbox are **deferred**, and the reason is worth stating: each one needs
its own OAuth application registration, its own token refresh and its own consent screen before it
can be offered to a shopkeeper. The S3 target already speaks to the object stores that are the
common answer in a shop's own server room, and it does so without a cloud account of our own.

A destination that is not configured says so on the screen — "not configured" — rather than failing
at the moment the owner presses the button. The **Test** button beside each destination writes that
answer down without taking a copy of anything.

### The schedule runs as the shop, not as a person

The scheduler is an in-process timer: it looks every thirty minutes, and it looks once twenty
seconds after the shell comes up. It needs **no signed-in user**, because a daily backup that waits
for somebody to log in is a daily backup that does not happen when the shop opens.

- The **daily** backup runs at the time the owner sets.
- The **weekly** backup runs on the day and at the time the owner sets, and **stays due** until it
  has been taken — so a shop that was shut on Saturday takes it on Monday morning.
- When the shop was closed over an occurrence, the catch-up is **one** daily and **one** weekly, not
  one for every missed day. A shop that was shut for a week comes back with one fresh daily copy and
  one fresh weekly copy; the older days are covered by the copies already on disk.

All of it is worked out in **wall-clock time**. "Eleven at night" means eleven at night where the shop
is, and no time zone is applied twice — a rule the tests pin, because the first version of the policy
mixed `DateTime` and `DateTimeOffset` and shifted the hour.

### The retention keeps seven dailies and four weeklies

The shop's copies are pruned by the same rule that decides what to keep: **seven daily copies and
four weekly copies**. Two things are never pruned:

- **The newest copy is never deleted**, whatever the policy says.
- **A copy the owner asked for is never deleted** by the scheduler. A manual backup is the one the
  owner took on purpose; the scheduler does not decide that it has had its turn.

### The restore refuses to do anything unsafe

The restore wizard is where the risk is, so the rules are strict:

1. **The archive is checked before anything is touched.** If the integrity check fails, nothing
   happens at all.
2. **The database that is about to be replaced is kept** as a **restore point** in
   `data\restore-points\`, so the restore itself can be undone.
3. **Only an administrator may restore** (`Permission.BackupRestore`). A manager may take a copy — the
   seeded manager role holds `BackupCreate` — but may not put one back. A test pins this in both
   directions.
4. **A restore is refused while a sale is being posted.** The sale path is instrumented by
   `SaleActivityMonitor`; a sale that is half written and a restore that lands at the same moment
   would lose money, and money is not worth the convenience.
5. **The schema has to be one this version understands.** A backup from an older known version is
   allowed and is brought forward by the migrations; a backup from an unknown or a newer version is
   refused with a message that says so, rather than half-restored.

The pictures come back with the shop, and a picture that was taken after the copy was made is
removed, so that the shop looks the way the copy says it should.

The restore says **"StoreFlow will restart now"**, because it must: the database underneath the
running application has been replaced. The restart itself is Phase 7's business; the phase that
builds backups says plainly what the owner has to do.

### The way back in when the shop's data cannot be read

The **recovery wizard** is the answer to the case where the database will not open at all. The
application catches a failed `DatabaseInitializer` and shows the recovery wizard **instead of** the
sign-in window — because a sign-in window in front of a database that cannot be read is a dead end.

The recovery restore is allowed **without a signed-in administrator**, and only then: when the shop's
own database cannot be read at all (`SqliteSnapshot.IsHealthy` is false, or the file is missing).
That is the only situation in which the rule "only an administrator may restore" is set aside, and
it is set aside precisely because there is nobody left to sign in.

### A backup before the shape of the data changes

A migration changes the shape of the database. If one goes wrong, the shop needs yesterday.

`MigrationSafety.SafeMigrationIds` is the allow-list of migrations that have been reviewed. Any
pending migration that is **not** on the list takes a backup first — the default is to back up, and
to be told a migration was reviewed. A failure of the hook is written to the backup log and the
migration still proceeds: a backup that cannot be taken is a reason to be careful, not a reason to
leave the shop unable to open.

### The sale path still does not touch the network

The till's promise from Phase 3 still holds, and it is now **pinned by a test that fails if it stops
holding**:

- The Domain and Application assemblies reference no network assembly at all.
- A **type-closure walk** over `PostSaleCommandHandler` proves that no HTTP client can be reached
  from the sale path — not by a field, not by a local function, not by a captured variable.
- The Desktop assembly is checked the same way over `PosViewModel`.

This matters more in this phase than in any other, because this is the phase that adds a network
client to the application. The S3 target is a network client. It lives on the other side of the
`IBackupTarget` port, and the test walks the type closure to prove the till cannot reach it.

---

## What running it found

Nine defects, found by the suite and by the smoke run, all fixed and all pinned by tests.

| What to check | What was wrong | What was found |
|---|---|---|
| The shop's settings, after a restart | The installation **wrote over the shop's settings on every start**. The backup folder, the retention, the discount the shop allows, the scan suffix, the shop's own name and address — all of them were reset to their defaults the moment the application was started again | The shop's settings and details are laid down once, when the shop is new, and are the shop's from then on. Three tests: the settings survive a restart, the details survive a restart, and a new shop still starts with the defaults |
| The **backup screen** | The destination the copy went to was chosen **in the code**, not on the screen — so "Back up now" wrote somewhere the owner could neither see nor change | A picker at the top of the screen says where the copy goes, and the copy goes where the owner says |
| The **backup screen** again | The screen computed what the last command did — "the schedule was saved", "nothing is waiting", every error message — and **threw it away** | A line under the buttons says what happened |
| The **restore wizard** | The wizard could check a backup and read the verdict, but **the button that puts the shop back was never bound to anything**, so a restore could not be started from the interface at all | "Restore the shop" is there, and it is only live once the backup has been checked |
| The **schedule** | The schedule was written in one spelling and read in another, so the daily backup **silently switched itself off** | The two spellings are the same, and the round trip is tested |
| The **destination card**, in Arabic | The card said "Backups are written to …" and "The cloud target has not been set up yet." **in English**, whatever language the shop was running in | The destination's state is carried as a code and the screen writes the sentence in the shop's language |
| The **backup log** | The destination column showed the raw identifier — `LocalFolder` — where the owner should read "Local folder" | The column is written in the shop's language |
| The **navigation rail** | The rail entry said "Backup and restore" and the screen said "Backup and cloud" — the same screen under two names | They are called the same thing now, in both languages, and a test says so |
| The **schedule card** | The field for the weekly copies kept was **cut off by the edge of the card**, in English and in Arabic alike | The card is given the room it needs, and the labels are short enough to fit in either language |

Three more were found by the tests before the application was ever run: the captured temporary file
could not be deleted because a pooled connection still held it, the schedule policy applied the time
zone twice, and the S3 destination threw when the bucket was empty — the SDK answers an empty bucket
with no object list at all, and the code expected one.

### What was deliberately left in English

The **log of the copies** and the **audit trail** keep the words that were written when the event
happened. A log is a record, and a record does not change its language because somebody changed the
interface language afterwards. The screen around the log — its headings, its filters, the
destination column, the outcome — is written in the shop's language, exactly as the rest of the
application is.

---

## The PRD §11 rows this phase answers

| PRD §11 row | What it needs | Where it stands |
|---|---|---|
| UAT-16 | A backup and a restore on a clean machine | **Proved in substance**, by the automated suite and by the smoke run: the copy is taken, the file carries the checksum the screen shows, the wizard reads that file back, the shop is put back with its invoices, its stock and its settings, and **the same copy restores into a different, empty shop folder** — the same thing a second computer would be. What is still a person's job is the hardware: the second PC, the USB drive, the power cut |
| UAT-17 | A sale with the internet disconnected | **Proved for the path**, by the type-closure test that fails if any HTTP client can be reached from the sale. The manual step — unplugging the cable and watching the till carry on — is still a person's job |

---

## What this phase did not reach

| What | Why |
|---|---|
| **Per-entity export and import** | Deferred, and the delivery plan's Phase 6 row has been amended to say so. A transactional import of products or invoices would have to bypass the posting and approval rules that make the ledger trustworthy, and a CSV import that writes straight to the tables is worse than no import at all. The **whole-shop backup and restore** is the honest answer to the same need |
| **An encrypted archive** | Deferred to Phase 7, on purpose — see the note on the `.sfb` format above |
| **Google Drive, OneDrive, Dropbox** | Each needs its own OAuth application registration and consent flow. The S3-compatible target is the answer this phase ships |
| **The restart after a restore** | The restore says "StoreFlow will restart now"; the restart itself belongs with the crash-recovery work in Phase 7 |
| **The second computer** | The restore into a **different shop folder** is proved, in the suite and in the smoke run. A different *machine* is the manual step in `docs/uat/UAT-PLAN.md`, and it is the one part of UAT-16 this phase cannot prove by itself |

---

## Verification

### The build

```
StoreFlow.sln — 0 warnings, 0 errors
```

Analyzers are on and warnings are errors, so a clean build is a clean build.

### The suite, three times over

The whole solution was tested **three times**, and every run was green.

| Run | Domain | Application | Infrastructure | Desktop | Total | Failures |
|---|---|---|---|---|---|---|
| 1 | 25 | 137 | 140 | 121 | 423 | 0 |
| 2 | 25 | 137 | 140 | 121 | 423 | 0 |
| 3 | 25 | 137 | 140 | 121 | 423 | 0 |

### The checks that carry the gate

| What has to be true | Where it is pinned |
|---|---|
| The copy is taken with SQLite's own online backup, and the capture is checked before it is written anywhere | `SqliteBackupCaptureService`, `SqliteSnapshot` |
| The file carries the checksum the screen shows, and the log row carries it too | the smoke run, `The_backup_is_written_to_the_log_with_the_checksum_that_proves_it` |
| The archive says what it holds, part by part | `An_archive_carries_a_manifest_that_describes_every_part`, `The_manifest_records_the_checksum_of_every_part` |
| A copy that was changed after it was written is refused | `A_part_that_was_changed_after_the_archive_was_written_is_refused`, `A_backup_that_was_changed_after_it_was_written_is_refused_by_the_restore` |
| A file that is not a copy is refused | `An_archive_without_a_manifest_is_refused`, `A_part_that_was_added_after_the_archive_was_written_is_refused` |
| A restore without the permission changes nothing | `A_restore_without_the_permission_is_refused_by_the_application_layer` |
| A manager may take a copy but may not put one back | `The_manager_may_back_the_shop_up_but_may_not_put_it_back` |
| A restore is refused while a sale is being posted | `A_restore_is_refused_while_a_sale_is_being_posted` |
| The restore gives a different shop the same data | `A_restore_into_a_second_profile_gives_the_same_shop` |
| The data a restore replaced is kept | `Restoring_over_a_live_database_leaves_the_old_database_recoverable` |
| The retention keeps seven dailies and four weeklies, and never the newest | `The_copy_past_the_number_the_shop_keeps_is_the_one_that_goes`, `The_newest_copy_is_never_removed`, `A_backup_the_owner_took_by_hand_is_never_removed_by_the_scheduler` |
| A migration nobody has reviewed takes a copy first | `A_migration_that_nobody_has_reviewed_takes_a_backup_first`, `The_migration_hook_writes_a_backup_before_a_migration_that_needs_one` |
| The scheduler catches up once, not once per missed day | `A_day_that_was_missed_is_caught_up_once_and_not_once_a_day`, `The_scheduler_takes_one_backup_when_the_shop_was_closed`, `The_scheduler_takes_one_backup_when_a_day_was_missed` |
| The sale path cannot reach a network client | `The_core_of_the_shop_never_references_the_network`, `The_sale_path_is_made_of_no_network_client` |
| The till cannot reach a network client | `The_desktop_assembly_never_references_the_network`, `The_till_is_made_of_no_network_client` |
| The shop's settings survive a restart | `The_shop_s_settings_survive_a_restart`, `The_shop_s_details_survive_a_restart` |
| The S3 destination really speaks the S3 API | `A_backup_is_written_to_the_object_store_and_read_back`, `A_cloud_target_that_was_never_set_up_says_so` |
| The backup screen is written in the shop's language | `The_backup_screen_is_written_in_both_languages`, `A_destination_is_named_in_the_language_the_shop_reads` |

### The design check

The mechanical design detector was run over every new surface — the backup screen, the two wizards,
the converters and their view models — and it reported **no findings**. The rules it checks that
matter most here were checked by hand as well: every colour on the new screens comes from a semantic
token, the Arabic layout mirrors through `FlowDirection`, the copy is written in the shop's language
in both, and no font size is written into a view.

### The application, driven

`tools/smoke-backup.ps1` drove the running application through the whole story: choose a
destination, set the schedule, create a product, take a copy, read the file back from the disk,
check it in the restore wizard, put the shop back, start the application again and find the product
and the schedule still there — then find the same copy in a second, empty shop folder through the
file dialog and put it in there too.

**38 checks, 0 failures.** The pictures it took are in `docs/phases/phase6-screens/`.

Two of the checks were waiting for a **value** and not for a count: the checksum the screen must
reach, and the message the wizard must reach. The number of rows in the log is no use for either —
the log is empty before a backup and holds one row after it, and a count that is already right
proves nothing.

The first run of the script failed six of its checks. Four of them were the script's own fault: it
had restored the **scheduler's** copy instead of the copy the owner asked for, and compared the wrong
checksums against it. The other two were the application, and they were the same defect twice — the
shop's settings had not survived the restore, because the installation wrote over them on every
start. That is the first row of the defect table above.

Then the pictures were looked at, and the screen was found saying things in English to an Arabic
shop, calling itself two different names, and cutting the field for the weekly copies off at the
edge of the card. Those are rows six to nine.

---

The phase is complete, and the shop's data can be carried away and brought back. Every copy says
what it holds and proves it is what it says, the copy happens without anyone remembering to make
it, the restore asks first and keeps what it replaces, and the way back in exists for the day the
database will not open at all.
