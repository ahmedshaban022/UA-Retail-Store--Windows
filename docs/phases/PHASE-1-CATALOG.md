# Phase 1 — Catalogue

**Status:** complete · **Commits:** `82336a7` (the phase itself) · `424a6c3` (the twelve defects) · **Date:** 2026-10-27

The second of eight phases. This phase ends with a shop's catalogue in place: products, categories,
units and suppliers, each with its own screen, a product editor that refuses bad data in plain
words, a search that finds a product by its name, its code or its barcode, a spreadsheet in and a
spreadsheet out, and a trail of who changed what.

---

## What was built

### The catalogue in the application layer

The catalogue is a set of commands and queries that mean the same thing wherever they are called
from, and they live in `src/StoreFlow.Application/Catalog`:

| File | What it holds |
|---|---|
| `CatalogCommands.cs` | The writes: save a product, activate or deactivate one, the same for categories, units and suppliers, import a spreadsheet, export one |
| `CatalogQueries.cs` | The reads: search the products, read one product, list the reference records, read the reference data an editor needs, preview an import, read the audit trail |
| `CatalogValidation.cs` | Every rule about what a valid record is, as a pure function that returns a list of issues with a stable code |
| `ProductCsvMapper.cs` | The spreadsheet contract: the thirteen fields, the headings that mean each of them, and how a row becomes a product |
| `CatalogDtos.cs` | The shapes that cross the boundary, including the money and quantity values the screens show |

The rules are **pure and separate from the database**. `CatalogValidation` does not know what a
database is; it answers "is this name usable" and "is this price sane" with a list of problems, each
tagged with a code such as `catalog.duplicate_sku`. The infrastructure turns those codes into
failures, and the desktop turns them into sentences in the shopkeeper's language. The same rule is
therefore testable without a database and readable in one place.

`CsvReader` (`src/StoreFlow.Application/Common/CsvReader.cs`) is a plain text reader: quoted
fields, doubled quotes, both line endings, a byte-order mark, the delimiter guessed from the
header, and — because shopkeepers get their data out of Excel on a Windows machine — **Windows-1256
as a fallback when the file is not valid UTF-8**. Arabic names in an Arabic spreadsheet survive the
round trip.

### The catalogue in the infrastructure

`src/StoreFlow.Infrastructure/Catalog` holds the adapters:

- **Product writes** — save a product, activate or deactivate one. A duplicate code or barcode is
  refused with the code the application layer produced, so the message the shopkeeper reads is the
  same wherever it came from.
- **Reference writes** — categories, units and suppliers. A unit's precision cannot be changed while
  products are still using it (`catalog.precision_in_use`), because the quantities already recorded
  against that unit would stop meaning what they meant.
- **Reads** — the product search, one product with its category, unit and tax category, the three
  reference lists with the number of products behind each row.
- **The audit trail** — every change to the catalogue writes a row: what happened, to which record,
  when, and by whom. The state before the change is kept as a **hash**, never as a copy, so the
  trail stays small and cannot be mistaken for a second source of truth.
- **The image store** — a product picture is copied into `<root>/media/products/` under a name of
  its own, the format is recognised from the first bytes rather than from the extension, and a path
  that tries to climb out of the folder is refused.
- **Import and export** — the preview reads a spreadsheet and says what it would do, row by row,
  before anything is written.

### Barcode uniqueness is enforced by the database, not only by the application

A barcode is a promise to the shopkeeper: one scan, one product. Keeping that promise in the
application layer alone would leave a hole for anything that writes to the database another way —
a future import, a repair script, a second application.

Four SQLite triggers in `src/StoreFlow.Infrastructure/Persistence/SchemaTriggers.cs` now enforce it
across the two places a barcode can live, `Products.Barcode` and `ProductBarcodes.Barcode`, in both
directions and without regard to case. The application still checks first and still produces the
friendly message; the database is the backstop.

A migration, `CatalogCaseInsensitiveIdentifiers`, also makes the code, the barcode and the reference
names case-insensitive. `rice-001` and `RICE-001` are the same product code, and the database now
says so.

### The catalogue on the desktop

Five screens and three dialogs:

| Screen | What it is for |
|---|---|
| **Products** | The catalogue itself: a search box, a list, a new-product button, and the import and export commands |
| **Categories / Units / Suppliers** | The reference records, each on its own screen |
| **Audit** | Who changed what, and when |
| **Product editor** | A dialog for one product: code, names, category, unit, tax category, prices, reorder level, barcode and extra barcodes, picture, description |
| **Reference editor** | One dialog for all three reference records, built from a description of the fields |
| **CSV import** | Choose a file, read the preview, then import it |

Products, categories, units and suppliers are **separate entries in the navigation rail** rather
than tabs inside one screen, because the plan lists them as separate screens and because a shop
with a thousand products and four categories is two different kinds of work.

The editors are **modal dialogs**. A product needs a long form, a picture and its history, which
does not belong in a list; a reference record is a name and a name in Arabic, which does.

The reference editor is **one dialog for three record types**, driven by a description of its
fields. Adding a fourth kind of reference record is a matter of describing its fields, not of
writing a new window.

### The tables

The catalogue is read in tables, and the tables follow the design system's rule: **a grid keeps a
minimum column width and scrolls sideways below it**. Every column says what it holds, the name
and the Arabic name are the two that grow, and the money columns are set in tabular figures at the
leading edge so the digits stack up and can be compared down the column.

**A heading stands over the values it names.** A column of prose and a column of figures do not
stand in the same place: prose follows the direction of the page, and figures stand against the
right-hand edge of their cell, so the digits can be compared down the column. The heading of each
column stands where that column's values stand.

A code — a product's code, a barcode, a telephone number, the moment a change happened — is
written left to right wherever it is read, so the cell that holds it is given that direction. The
hyphen in a code, and the bracket at the end of a summary, are part of the text and are not moved
to the other end of the line.

A heading is a label rather than a piece of text, and the label is bound to the grid it belongs
to. The reason is written down under the bugs below, because the shape that looks obvious is the
one that cannot work.

### The spreadsheet contract

Thirteen fields, and the headings that mean them:

```
sku  name  nameAr  category  unit  taxCategory  salePrice  costPrice
reorderLevel  barcode  additionalBarcodes  description  isActive
```

The headings are **localised**: the exporter writes the headings of the language the interface is
in, and the importer accepts those headings in either language, plus a set of common synonyms and
the Arabic names for the same fields. A file this application wrote can be read straight back.

A product with no category exports with an **empty** category cell rather than a placeholder, so
the round trip loses nothing. The selling price is required on import; the cost price and the
reorder level default to zero. No price and no quantity may be negative — the importer holds to
the same rule as the editor. The import runs in one of two modes: **create only**, which refuses
a row whose code already exists, or **update by code**, which changes what is there.

### The strings

One hundred and thirty new keys, added to `Strings.resx` and `Strings.ar.resx` together, taking
both files to **one hundred and ninety keys each with matching names**. Every word the catalogue
shows — in Arabic or in English — comes from one of those two files; no screen carries a literal
sentence. The Arabic file is UTF-8 without a byte-order mark and keeps its line endings, and the
Arabic is checked after every change rather than assumed.

---

## What was verified, and how

### The build and the tests

```
dotnet build   →  succeeded, 0 warnings, 0 errors
dotnet test    →  Passed!  Failed: 0, Passed: 25 (domain) / 71 (application) / 56 (desktop) / 36 (infrastructure)
```

**One hundred and eighty-eight tests.** The build treats warnings as errors and runs the analyzers,
so a clean build is a real result and not a quiet one.

Seventy-two of those tests are new in this phase: the CSV reader against quoted fields, Arabic
spreadsheets and Windows-1256 files; the validation rules; the spreadsheet mapper; the catalogue
handlers against a real SQLite database with its migrations and its triggers; and the behaviours
described below that were found by running the application.

### The real application, driven end to end

`tools/smoke-catalog.ps1` drives the actual application through UI Automation: it completes the
first-run wizard in Arabic, signs in, opens every catalogue screen, creates a product, tries to
create a second product with the same barcode, tries again with the same code, creates a category,
a unit and a supplier, reads the audit trail, searches, exports a spreadsheet, imports a
spreadsheet, and then switches the interface to English and does it all over again.

Every control is found by its **automation identifier**, not by its label, so the script does not
depend on the wording of the interface in either language. The application is pointed at a
throwaway folder through `STOREFLOW_HOME`, so the shop's own data is never touched.

The pictures in [`phase1-screens/`](phase1-screens/) are taken by asking each window to draw itself.
A right-to-left window comes out mirrored when it is captured from outside its own process, so the
Arabic pictures are flipped back; the English ones are left as they are, because flipping a
left-to-right window would be a lie about the screen.

The last run of the script made **forty checks and reported zero failures**. It wrote
`failures: 0` at the end and exited cleanly.

### What the run proved

| Check | Result |
|---|---|
| The wizard completes in Arabic and the shopkeeper signs in | pass |
| The navigation rail is on the **right** in Arabic — the interface reads right to left | pass |
| A product is created and appears in the list | pass |
| A second product with the same **barcode** is refused: *"الباركود 6221000000017 مستخدم بالفعل لمنتج آخر."* | pass |
| A second product with the same **code** is refused: *"الكود RICE-001 مستخدم بالفعل لمنتج آخر."* | pass |
| The editor stays open when the data is refused, and says which field is at fault | pass |
| A category, a unit and a supplier are created and appear in their lists | pass |
| The audit trail records the changes | pass |
| The search finds a product by its code and leaves out what does not match | pass |
| The export writes a spreadsheet that holds the product | pass |
| The import reads a spreadsheet, shows the row it would create **and the row it would refuse**, and imports the first | pass |
| The refused row — the one with the negative selling price — was not written | pass |
| The import preview says what is wrong with a row, in the language of the interface | pass |
| The audit trail's type filter shows the name of the choice, not the record | pass |
| The money reads in the language of the interface: `EGP` in English | pass |
| The same screens in English | pass |

**The import caught a broken spreadsheet before anything was written.** The verification script
builds its test spreadsheet in PowerShell, and PowerShell binds the comma tighter than the plus
sign inside a comma-separated array — so `'a' + $name + 'c'` arrived as three pieces and the
Arabic name landed on a line of its own. The importer read the file exactly as it was written,
showed the owner that six of the seven rows had problems, and imported the one row that was
whole. The script was wrong and the application was right, which is the only way round that leaves
a spreadsheet trustworthy.

---

## Bugs found by running it

### 1. The reference editor fell over when it opened a unit

The unit editor has a field that offers a list of choices — the precision, from zero to six. The
list was keyed by number, and the field holds text. WPF matches a value against a list by
converting the value to the type of the item's key first, so an empty value could not be read as a
number, and the window threw while it was being built.

**The editor never appeared. The application reported the failure and every screen behind it
stopped answering.**

The fix is a type of its own for the choices, keyed by text, so the value and the key are always
the same kind of thing. The trap is written down as a test, in
`tests/StoreFlow.Desktop.Tests/ReferenceEditorChoiceTests.cs`: one test shows that a list keyed by
text can be built with nothing chosen yet, and another shows that the old shape cannot.

### 2. Every button in the application carried the page's ink

The primary button is petrol with white text, by the theme's own rule. It rendered petrol with
**near-black** text — a button nobody could read, and the only complaint the shopkeeper had.

The cause was not the button. It was the style for a **label**: the implicit style for `TextBlock`
set the colour of the text, and a label inside a button is a `TextBlock`. A colour set that way
wins over inheritance, so every label in the application was painted the page's ink, whatever it
was sitting on.

The style for a label now sets the type and leaves the colour alone, so a label takes the colour of
the thing it sits in; the windows carry the ink of the interface, and a label on the petrol accent
is light because the button says so. The primary button's label is also now **semi-bold**, and the
button carries the face and the size of the theme rather than the platform default.

Two tests pin it: one that a bare label carries no colour of its own, and one that the label of a
primary button is the colour the theme reserves for text on the accent.

### 3. The audit trail could not be ordered by the moment it happened

SQLite cannot sort by a `DateTimeOffset` column, so reading the trail in the order it was written
threw. The trail is now ordered by the row's own number, which is the order it was written in.

### 4. The tax picker showed the record instead of the name

A picker that shows `CatalogOptionItem { Id = 0, DisplayName = … }` instead of the name is the
exact trap Phase 0 fell into with the language picker. The options now say what they are called.

### 5. A byte-order mark survived the first reading of a spreadsheet

Excel writes one at the start of a UTF-8 file, and it became part of the first heading, so the
first column was never recognised. It is now stripped on the way in, in both the file reading and
the string reading.

### 6. The tables had no headings, and the product's name was crushed to nothing

Every catalogue table rendered with an **empty band** where the column headings should have been,
and the product's name was not in the row at all.

Both came from the same place. A heading was written as a binding on the column itself:

```xml
<DataGridTextColumn Header="{Binding SkuHeader}" … />
```

**A data grid column is not part of the visual tree.** It inherits no data context of its own, so
the binding had nothing to read and the heading came out empty. The heading is now a label inside
the column, and the label says which grid it reads from, because a heading does not inherit the
grid's data context either:

```xml
<DataGridTextColumn.Header>
  <TextBlock Text="{Binding DataContext.SkuHeader, RelativeSource={RelativeSource AncestorType={x:Type DataGrid}}}" … />
</DataGridTextColumn.Header>
```

The crushed name was the second half of the same screen: the columns had fixed widths that added
up to more than the space the window had, so the two columns sized with a star — the name and the
Arabic name — were given what was left, which was **fifteen pixels**. The names now carry a
minimum width, and the design system's own rule applies: below the width the columns need, the
table scrolls sideways rather than crushing a column until it cannot be read.

Three tests pin it: one that a heading that is a label reads its label from the grid, one that a
binding written straight on the column reads nothing, and one that reads the views themselves and
fails if a heading ever goes back to the shape that cannot work.

### 7. The money was written the wrong way round in Arabic

The price in the table read `450.00 ج.م`, while the same figure on the dashboard read `ج.م 0.00`.
Only one of them can be right, and in Arabic the answer is the second: the **amount is read first
and the currency after it**.

The style for a money column forced the text to read left to right, which put the currency on the
wrong side of the amount in the Arabic interface. It now keeps the direction of the page, so the
two screens agree.

### 8. An identifier in the middle of an Arabic sentence

The message that refuses a duplicate barcode reads *"الباركود … مستخدم بالفعل لمنتج آخر."*, with
the barcode standing in the middle of it. An identifier is wrapped in a **left-to-right isolate**
(`U+2066` … `U+2069`), the invisible marks that say where the identifier begins and ends, so it
cannot be read as part of the words around it. Only identifiers are isolated; a name is the
owner's own writing and may be Arabic, and forcing it left to right would be worse than the
problem it solves.

**What the isolate is worth, measured.** The marks are in the string and they are invisible to the
reader, but WPF's text engine **does not read them**: it treats them as marks to be ignored, so the
text they surround is laid out as if they were not there. A rendering test
(`tests/StoreFlow.Desktop.Tests/BidiRenderingTests.cs`) draws the same text four ways and compares
the ink:

| | draws the same as the English cell? |
|---|---|
| the text alone in an Arabic cell | no |
| the text with the isolate marks in an Arabic cell | no — the marks change nothing |
| the text with a left-to-right embedding in an Arabic cell | no |
| **the cell given its own direction** | **yes** |

So the marks stay in the sentences, where they cost nothing and say what is meant, but the tables
do not rely on them: **what keeps a code in order in a table is the direction of the cell itself.**
Bug 12 is what that cost before it was measured.

### 9. The import took a product with a negative selling price

The editor refuses a price below zero, and the rule lives in `CatalogValidation`. The **import did
not ask it**: it checked that the price was a number and stopped there. A spreadsheet with a
selling price of −5.00 was previewed as ready, imported, and written into the catalogue.

The importer now applies the same rule as the editor — the same code, the same message — for the
selling price, the cost price and the reorder level. An import is another way into the catalogue,
so it is held to the same standard.

The preview also had nothing to say. Every problem the importer found carried a code, and **not one
of those codes had a message**: each of them fell through to the catch-all sentence that says
something went wrong, so a row with a missing price, an unknown category or a code that already
existed all said the same useless thing. The codes now have messages of their own in both
languages, the problem carries the value that caused it, and the interface names the field in the
language the shopkeeper is reading. A code or a barcode in a sentence is isolated, as it is
everywhere else.

### 10. The English interface showed the Arabic currency

Switching the interface to English left the money reading `180.00 ج.م`. The symbol was chosen by
the **theme's** language, not by the language the interface was in, so a shopkeeper reading English
saw amounts in a currency written in the other language. The symbol now follows the language of the
interface.

### 11. The audit trail's filter showed the record instead of the name

The type filter in the audit trail read `AuditEntityTypeOption { Key = , DisplayName = All }` — a
line of code where a word should be. This is the same trap as the tax picker in bug 4, in a second
place: a closed select shows the selected item's own text, so an option that goes into a select
has to say what it is called. The option now does, and a test in
`tests/StoreFlow.Desktop.Tests/SelectShowsTheNameTests.cs` builds a select with the
application's own theme, opens it, and fails if the record shows instead of the name.

### 12. The codes in the tables stood away from their heading

A code in a table — the product's code, the barcode, the moment a change happened — was written
left to right by turning the whole cell left to right. The cell then sat at the **left** of its
column while the heading that named it sat at the right, so the two were not over each other.

The obvious repair — wrap the code in a directional isolate and let the cell keep the direction of
the page — **made things worse**, and the run showed it: the audit trail's moment in time came out
as `07:53 2026-11-01`, with the date and the time in two pieces, and the summary came out as
`(Rice 1kg (RICE-001`, with its closing bracket moved to the other end of the line. The reason is
in bug 8: the text engine does not read the marks.

A cell that is written left to right as a whole is now **given that direction**, and it stands
against the right-hand edge of its cell, where the heading that names it stands. The money columns
had the same problem the other way round, and only in English: the figures stood against the
right-hand edge of their cell while their heading stood at the left. A heading now stands over the
values it names, wherever they stand.

---

## What is deliberately not in this phase

### Records are deactivated, never deleted

A category, a unit, a supplier and a product are all **deactivated** rather than deleted, in line
with PRD-01. Deactivating keeps the history intact: a receipt from last year still names the unit
it was sold in, and the product is simply not offered for sale any more. The UAT row for this
(UAT-18) is therefore only partly provable in this phase — see below.

### No full-text search

The search covers the name, the Arabic name, the code, the barcode and the extra barcodes, in one
composed query with the wildcards escaped. A catalogue of a few tens of thousands of products does
not need FTS5, and a unique index on the code and the barcode is already there. Should the
catalogue grow past what a composed `LIKE` can serve, the search is the thing to change.

### The product list is not paged on the server

The plan's performance table asks for a product list that answers in two seconds at ten thousand
products, served **50 rows to a page** with the filtering done in the database. The catalogue
reads its list in one query and filters it in the application, which is comfortable for the few
hundreds of products a shop actually has and is the thing to change when the catalogue grows. The
performance harness that proves the two seconds belongs to Phase 7, and the paging belongs with
it: changing the shape of the read now would mean changing it again in a month.

### The exit gate is partly open, and here is exactly where

The gate for this phase is UAT-01, UAT-02 and UAT-18.

- **UAT-02 is proved.** A duplicate barcode is refused, the message names the barcode and says it
  belongs to another product, the editor stays open, and the first product is untouched. The
  database would refuse it too, even if the application did not.
- **UAT-01 is proved as far as this phase goes.** A product is found by its name, its code and its
  barcode. The rest of that row — a scan at the till resolving to one line — is Phase 3.
- **UAT-18 is proved as far as this phase goes.** A product can be deactivated and it disappears
  from the catalogue while its history stays. The rest of that row — the product not being
  selectable in a new sale, and an old invoice opening with the values it was raised with — needs a
  posted sale, which is Phase 3 and Phase 4.

The gate is therefore **not fully closed**. Two of the three rows depend on a till and a posted
invoice that do not exist yet. This is stated rather than dressed up: the honest position is that
the catalogue rules are proved and the till rows are not yet reachable.

---

## Known papercuts

- **The name in the top bar shows the login name where the role belongs.** The shopkeeper's name is
  followed by the name they sign in with, not by the job they do. The session does not carry the
  role, so the fix is to carry the role's code and let the interface name it in the language it is
  written in. It was left alone here because it belongs to the shell, which Phase 0 built, and
  because it is the sort of small thing that is easier to change once there are three roles on
  screen rather than one.
- **`StoreFlow.slnx` is still untracked.** The solution is built from `StoreFlow.sln`; the `.slnx`
  file is left where it is rather than deleted, because deleting files is not something this
  workspace will do. It is a leftover from the scaffold and should be removed by hand.
- **The window is taller than a small screen.** The shell is 1280 × 820 and the screen it was
  verified on is 1366 × 768, so the bottom of the window sits below the taskbar. It was left
  alone because it is a Phase 0 decision and the fix is a size that suits the smallest screen the
  product claims to support — but it should be settled before the till is built, where the screen
  is fixed.
- **The theme's live dictionary is written to the log at start-up.** This was added while chasing
  the button bug and is worth keeping: a missing colour role shows up as a control quietly falling
  back to another colour, and on a shop PC there is no debugger.
- **A file was left in the shopkeeper's own documents.** An early run of the verification could not
  make the Windows file dialog take the folder it was told to, so the export landed in the
  documents folder instead of the throwaway folder the run uses. The driver now types the path into
  the dialog and the file goes where it is told, but that one file is still there and should be
  deleted by hand.
- **The catalogue tables turn column virtualisation off.** The design system asks for it always, and
  row virtualisation is on: a catalogue of ten thousand products scrolls without building ten
  thousand rows. **Column** virtualisation is off in the four catalogue tables, because WPF ignores
  the minimum width of a column that sizes itself with a star, and a column squeezed to fifteen
  pixels is the bug this phase already had once. The rule and the exception are both real; the
  exception belongs to the tables that size themselves, and it is revisited with the paging in
  Phase 7, which is where a ten-thousand-row list has to be proved.

---

## The phase gate

| Row | What it needs | Where it stands |
|---|---|---|
| UAT-01 | A product is found by its name, its code and its barcode | **Proved for the catalogue**; the till half belongs to Phase 3 |
| UAT-02 | A duplicate barcode is refused with a readable message and nothing changes | **Proved**, by the application and by the database |
| UAT-18 | A deactivated product is not sold again, and old documents keep their values | **Proved for the catalogue**; the sale half belongs to Phase 3 and Phase 4 |

**The phase is complete, and the gate is partly open.** Everything the catalogue is responsible for
is built, proved by tests, and proved again by driving the real application. The two rows that
need a till cannot be run until Phase 3 exists, and saying otherwise would be a lie.

The next phase is **Phase 2 — Inventory**: purchase drafts, receiving, the stock ledger, and
reordering.
