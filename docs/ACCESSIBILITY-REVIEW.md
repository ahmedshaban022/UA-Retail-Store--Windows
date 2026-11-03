# Accessibility review — Phase 7

The application is Arabic-first and is used at a till, by people who are working fast, in a shop
where a screen reader is not an unusual thing to need. This review looks at the four things PRD
NFR-11 asks for: contrast, accessible names, keyboard reachability, and Arabic at large font
sizes.

Everything here was found by reading the code, not by using the application. Where a conclusion
depends on how a control behaves at run time rather than on what the source says, it says so.

---

## 1. What was checked

| Area | How |
|---|---|
| The automation contract between the smoke scripts and the screens | Every string the eleven `tools\smoke-*.ps1` scripts look for, matched against the XAML |
| Accessible names | Every interactive control in the thirty-two XAML files in `src\StoreFlow.Desktop` |
| Keyboard | Every `Focusable`, `IsTabStop`, `KeyboardNavigation`, `InputBinding`, `KeyBinding`, `KeyGesture` and `AccessKey` in `src`, plus every `KeyDown` handler |
| Font scaling | Every `FontSize`, every fixed `Height`, and the way the theme publishes its sizes |
| Arabic | The embedded font, the flow direction, and the places where a fixed height could cut the text off |
| Contrast | `ThemeContrastValidator` run over every built-in theme |

## 2. The automation contract is intact

**389 lookups across the eleven smoke scripts. Every one of them resolves to a control that
exists.** The scripts find controls by `AutomationProperties.AutomationId`, and the thirty-two XAML
files carry 304 of them. Nothing in the scripts is unreachable.

Two things were worth knowing:

- Six of the lookups are by *name* (`Next`, `Finish`, `Sign in`) rather than by identifier, and
  they are English only. In Arabic mode the button's name is the Arabic word, so those lookups
  would not match. They are not broken — the scripts run in English mode — but they are not
  bilingual, and the review says so rather than leaving it to be discovered.
- The text lookups (`RICE-001`, `INV-`, `customer waiting`) match runtime data, not names.

## 3. Accessible names were the real gap

At the start of this phase the whole application had **two** `AutomationProperties.Name`
declarations, both on the login screen. Everything else was found by its identifier, which is not
what a screen reader reads out.

An `AutomationId` is UIA's *AutomationId* property. It is how the smoke scripts find a control. It
is **not** the UIA *Name* property, and a control with only an identifier and no textual content
announces itself as an unnamed edit box.

### 3.1 Fixed in this phase

**The worst cases: fields with neither a name nor an identifier.** A screen reader announced
nothing at all, and the smoke scripts could not reach them either.

| Screen | Control | Now named by |
|---|---|---|
| `FirstRunWindow.xaml` | store name, store name in Arabic, address, phone | `StoreNameLabel`, `AddressLabel`, `PhoneLabel` |
| `FirstRunWindow.xaml` | display name, username | `DisplayNameLabel`, `UsernameLabel` |
| `FirstRunWindow.xaml` | password, confirm password | `PasswordLabel`, `ConfirmPasswordLabel` |
| `FirstRunWindow.xaml` | language picker, appearance picker | `LanguageLabel`, `ThemeLabel` |
| `SettingsView.xaml` | product name in English and in Arabic, appearance picker, language picker | `ProductNameLabel`, `ThemeLabel`, `LanguageLabel` |

`SettingsView.xaml` was the only view in the application with no `AutomationProperties` at all.
It now carries identifiers as well as names: `Settings.ProductNameEn`, `Settings.ProductNameAr`,
`Settings.Theme`, `Settings.Language`, `Settings.Save`, `Settings.Status`.

**The point of sale**, where the cashier spends the day:

| Control | Now named by |
|---|---|
| `Pos.Scan` | `ScanLabel` |
| `Pos.Cart` | `CartLabel` |
| `Pos.HeldCarts` | `HeldCartsLabel` |
| `Pos.Quantity` | `QuantityColumnLabel` |
| `Pos.CashReceived` | `CashReceivedLabel` |
| `Pos.InvoiceDiscount` | `InvoiceDiscountLabel` |

### 3.2 Stage D decision and remaining debt

The Phase 7 text said 96, but its list actually enumerated **107** identified controls (including
three dynamically identified reference-editor fields). Stage D named 20 on six complete screens:
all six Audit controls, the Categories and Units grids, both Suppliers controls, and all five
controls on each of Sales and Purchases. It also named the Approvals grid, outside the original list. The names use
their existing localised labels or page titles; automation identifiers and visible text are unchanged.
**The remaining 87 listed controls are accepted release debt: screen-reader users may be unable to
identify and use them independently.** Their labels and grid contexts need screen-by-screen localised
work and runtime verification; guessed names are not a safe substitute.

A conservative source guard counts **88** identified interactive elements with no explicit name,
label association, content attribute or header attribute. Its scope includes the 87 listed controls,
plus `Pos.AddSearchResult`, which contains child text, so this is a candidate
count rather than a claim of 88 runtime UI Automation names missing. The guard is not a screen-reader
test and does not cover controls without identifiers. It fails if its count rises above 88.

Remaining list, by screen:

| Screen | Controls |
|---|---|
| `BackupView.xaml` | `Backup.TargetPicker`, `Backup.DailyTime`, `Backup.DailyRetention`, `Backup.WeeklyDay`, `Backup.WeeklyTime`, `Backup.WeeklyRetention`, `Backup.Directory`, `Backup.FilterTarget`, `Backup.FilterResult`, `Backup.From`, `Backup.To`, `Backup.Grid` |
| `CsvImportWindow.xaml` | `CsvImport.FilePath`, `CsvImport.Grid` |
| `DashboardView.xaml` | `Dashboard.Date` |
| `InventoryView.xaml` | `Inventory.AsOfDate`, `Inventory.Search`, `Inventory.Balances`, `Inventory.MovementProduct`, `Inventory.MovementFrom`, `Inventory.MovementTo`, `Inventory.Movements`, `Inventory.Counts` |
| `MainWindow.xaml` | `LanguagePicker` |
| `OpeningStockWindow.xaml` | `OpeningStock.Date`, `OpeningStock.Notes`, `OpeningStock.Lines`, `OpeningStock.Line.Product`, `OpeningStock.Line.Quantity`, `OpeningStock.Line.UnitCost` |
| `ProductEditorWindow.xaml` | `ProductEditor.Sku`, `.Name`, `.NameAr`, `.Description`, `.Category`, `.Unit`, `.TaxCategory`, `.SalePrice`, `.CostPrice`, `.ReorderLevel`, `.Barcode`, `.NewBarcode` |
| `ProductsView.xaml` | `Products.Search`, `Products.Grid` |
| `PurchaseEditorWindow.xaml` | `PurchaseEditor.Supplier`, `.Date`, `.Discount`, `.Notes`, `.Lines`, `.Line.Product`, `.Line.Quantity`, `.Line.UnitCost`, `.Line.Tax` |
| `ReasonPromptWindow.xaml` | `ReasonPrompt.Reason` |
| `RecoveryWizardWindow.xaml` | `Recovery.Path` |
| `ReferenceEditorWindow.xaml` | the three bound fields (`name`, `nameAr`, `phone`) |
| `ReportsView.xaml` | `Reports.Picker`, `.From`, `.To`, `.Cashier`, `.Grid`, `.DrawerOpening`, `.DrawerCounted` |
| `RestoreWizardWindow.xaml` | `Restore.Artifact` |
| `ReturnDialogWindow.xaml` | `ReturnDialogLines`, `.Quantity`, `.Reason`, `.Method` |
| `ReverseDialogWindow.xaml` | `ReverseDialogReason`, `.Method` |
| `StockAdjustmentWindow.xaml` | `StockAdjustment.Product`, `.Quantity`, `.UnitCost`, `.Reason` |
| `StockCountWindow.xaml` | `StockCount.Date`, `StockCount.Lines`, `StockCount.Line.Product`, `StockCount.Line.Counted` |
| `UsersView.xaml` | `Users.Grid`, `Users.Roles`, `Users.ChangeRolePicker`, `Users.NewUsername`, `Users.NewDisplayName`, `Users.NewPassword`, `Users.NewRole` |

Nine data-grid columns have no header at all, which is both a visual and an accessibility fault:
`SalesView.xaml:363-365` (product, quantity, total), `UsersView.xaml:78-79` (role code, role
user count), and the action columns in `OpeningStockWindow.xaml:114`, `PosView.xaml:230`,
`ProductsView.xaml:100`, `PurchaseEditorWindow.xaml:166`, `StockCountWindow.xaml:88`.

The remaining naming and column-header work is accepted release debt, not a claim of NFR-11 conformance.

## 4. Keyboard

### 4.1 What there was

**No keyboard shortcuts anywhere in the application.** A repository-wide search for
`InputBindings`, `KeyBinding`, `KeyGesture` and `AccessKey=` returns one hit: the
`RecognizesAccessKey` setting on the button template in `Themes/Controls.xaml:48`. No element
declares an access key.

**Escape did nothing.** No `Key.Escape` handler exists anywhere, and no button was marked as a
cancel button. Every dialog — thirteen of them — could only be closed with the mouse or by
tabbing to its Cancel button.

**Enter in the scan box did not confirm the sale.** `PosView.xaml.cs:58-73` handles Enter on the
scan field and sets `e.Handled = true`, which is correct for a barcode scanner. But the Confirm
button was not the window's default button, so a cashier who pressed Enter in the scan field could
not confirm the sale without first tabbing out of the field.

### 4.2 What was done

**Escape now closes a dialog.** `src\StoreFlow.Desktop\Views\EscapeKey.cs` is an attached
property. A window that sets `EscapeKey.Command` runs that command when Escape is pressed. It is
wired on all thirteen dialogs:

`CsvImportWindow`, `OpeningStockWindow`, `ProductEditorWindow`, `PurchaseEditorWindow`,
`ReasonPromptWindow`, `ReceiptWindow`, `RecoveryWizardWindow`, `ReferenceEditorWindow`,
`RestoreWizardWindow`, `ReturnDialogWindow`, `ReverseDialogWindow`, `StockAdjustmentWindow`,
`StockCountWindow`.

The key runs the *same command the Cancel button runs*, rather than closing the window itself.
This is deliberate: WPF's own `IsCancel` closes the window as well as raising the click, and these
windows set their `DialogResult` on the way out. Closing twice is a fault, so the key is wired to
the command instead and the button is left alone.

**The scan box still swallows Enter.** That is right for a scanner, and the scan box taking the
focus back after every scan is right for a till. What is missing is a way to confirm the sale
without reaching for the mouse. The Confirm button is reachable by Tab and Space, so the sale
*can* be made from the keyboard; it just takes more keys than it should.

### 4.3 What is still to do

- The Confirm-Sale button should be the window's default button, so Enter confirms once the
  cashier is out of the scan field. `IsDefault="True"` on `PosView.xaml:406-413`.
- No window sets `KeyboardNavigation.TabNavigation`. A dialog whose content is taller than the
  window lets the focus wander behind it. `Cycle` on the modal dialogs would fix that.
- There are no access keys. `Alt+F` and friends would help a keyboard-only cashier.

## 5. Arabic and large fonts

### 5.1 The embedded font is right

The requirement was that the Arabic family comes from `Fonts.GetFontFamilies` over
`pack://application:,,,/StoreFlow.Desktop;component/Fonts/`. That is what `ThemeEngine.cs:244-260`
does, and `ResolveUiFontFamily` (`ThemeEngine.cs:303-340`) returns the embedded family for a
right-to-left language whose theme names `Tajawal`. The fonts are `Fonts/Tajawal-Regular.ttf` and
`Fonts/Tajawal-Bold.ttf`.

### 5.2 The defect that was real

`ThemeTypography.Scale` multiplies the **font sizes** only. The row heights and the control
heights come from `ThemeDensity`, which knew nothing about the font scale. So raising the scale
grew the text and left every box exactly the same size — which is how Arabic gets cut off at the
bottom of the field it sits in.

**Fixed.** `ThemeEngine.cs:114-126` now multiplies the row height, the control height, the editor
row height, the spacing unit and the point-of-sale hit target by the font scale, and publishes a
new `Size.MultilineFieldHeight` for the boxes that take a paragraph.

The six fixed-height text boxes now use that resource instead of a literal number:
`ProductEditorWindow.xaml:72`, `ReasonPromptWindow.xaml:37`, `ReverseDialogWindow.xaml:41`,
`StockAdjustmentWindow.xaml:70`, `ReturnDialogWindow.xaml:152`, `ReferenceEditorWindow.xaml:29`.

Four hard-coded font sizes bypass the scale and were left alone: `PosView.xaml:30` (16), `:411`
(15), `:85` (11) and `:186` (11). They are deliberate — the scan box is read at a glance and the
total is read from across the counter.

### 5.3 What is still to do

- `Typography.Scale` is not wired to any setting. It can only be changed by writing a theme file.
  A shop that wants larger type has to hand-edit JSON. There should be a text-size setting in
  **Settings**.
- The implicit `TextBlock` style trims with `CharacterEllipsis` everywhere
  (`Themes/Base.xaml:87`). For an Arabic run, WPF's ellipsis trimming can cut the leftmost letter.
  Whether it does in practice was not tested here.

## 6. Contrast

`ThemeContrastValidator` was checking sixteen role pairs. It now checks **thirty-five**. Added:

- the four states on `surface.raised` and on `surface.sunken`
- `accent.on` on `state.info`
- `state.*` on the matching `state.*.subtle` background
- `selection.foreground` on `selection.background`
- `text.primary` on `surface.overlay`
- `text.secondary` and `text.muted` on `surface.raised`
- `focus.ring` and `border.strong` on `surface.raised`
- `accent.default` on `surface.canvas`

**All three built-in themes still pass.** `ThemeStoreTests.The_built_in_presets_all_satisfy_the_contrast_rules`
checks every theme in `BuiltInThemes.All` and fails on any error-severity finding; it is green.

Two gaps remain in the validator itself:

- `NormalTextAaa` (7.0) is declared but never used, so the claim that the high-contrast theme
  meets AAA is not machine-enforced.
- `selection.foreground` against `accent.default` is not checked, and the selection colours are
  only checked against each other.

## 7. What this phase changed, in one list

| Change | Where |
|---|---|
| Accessible names on the first-run and settings fields | `FirstRunWindow.xaml`, `SettingsView.xaml` |
| Accessible names on the point-of-sale screen | `PosView.xaml` |
| Identifiers on the settings screen | `SettingsView.xaml` |
| Escape closes a dialog | `src\StoreFlow.Desktop\Views\EscapeKey.cs` and thirteen windows |
| The contrast validator checks thirty-five pairs instead of sixteen | `src\StoreFlow.Application\Theming\ThemeContrastValidator.cs` |
| The row and control heights follow the font scale | `src\StoreFlow.Desktop\Theming\ThemeEngine.cs` |
| The six paragraph boxes scale with the font | six XAML files |

## 8. What this phase did not change, and why

- **The scan box takes the focus back.** `PosView.xaml.cs:41-47` moves the focus to the scan box
  every time the point-of-sale screen becomes visible, and after every scan. At a till this is
  what a cashier wants. It does mean the keyboard-only cashier has to tab past the whole page to
  reach the cart. That trade is a decision for the shop, not for this phase.
- **The automation identifiers were not changed.** The smoke scripts depend on them and all 389
  lookups resolve. Nothing was renumbered.
- **The grid columns were not given headers.** Nine columns across four screens. Each header needs
  a string in two languages, and the strings do not exist yet.
