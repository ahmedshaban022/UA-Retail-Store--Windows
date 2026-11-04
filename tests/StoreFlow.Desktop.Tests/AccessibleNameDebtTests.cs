using System.IO;
using System.Xml.Linq;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>Tracks the source-level accessible-name debt on identified controls.</summary>
public sealed class AccessibleNameDebtTests
{
    [Fact]
    public void Completed_screens_keep_their_localized_names()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreFlow.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        string views = Path.Combine(root.FullName, "src", "StoreFlow.Desktop", "Views");
        (string File, string Id, string Binding)[] expected =
        [
            ("AuditView.xaml", "Audit.Search", "SearchPlaceholder"),
            ("AuditView.xaml", "Audit.EntityType", "EntityTypeLabel"),
            ("AuditView.xaml", "Audit.From", "FromLabel"),
            ("AuditView.xaml", "Audit.To", "ToLabel"),
            ("AuditView.xaml", "Audit.Actor", "ActorLabel"),
            ("AuditView.xaml", "Audit.Grid", "Title"),
            ("CategoriesView.xaml", "Categories.Grid", "Title"),
            ("UnitsView.xaml", "Units.Grid", "Title"),
            ("SuppliersView.xaml", "Suppliers.Search", "SearchPlaceholder"),
            ("SuppliersView.xaml", "Suppliers.Grid", "Title"),
            ("SalesView.xaml", "SalesSearch", "SearchPlaceholder"),
            ("SalesView.xaml", "SalesFrom", "FromLabel"),
            ("SalesView.xaml", "SalesTo", "ToLabel"),
            ("SalesView.xaml", "SalesCashier", "CashierLabel"),
            ("SalesView.xaml", "SalesRegister", "Title"),
            ("PurchasesView.xaml", "Purchases.Search", "SearchPlaceholder"),
            ("PurchasesView.xaml", "Purchases.From", "FromLabel"),
            ("PurchasesView.xaml", "Purchases.To", "ToLabel"),
            ("PurchasesView.xaml", "Purchases.Supplier", "SupplierFilterLabel"),
            ("PurchasesView.xaml", "Purchases.Grid", "Title"),
            ("ApprovalsView.xaml", "Approvals.Grid", "Title"),
            ("BackupView.xaml", "Backup.TargetPicker", "DestinationLabel"),
            ("BackupView.xaml", "Backup.DailyTime", "DailyTimeLabel"),
            ("BackupView.xaml", "Backup.DailyRetention", "DailyRetentionLabel"),
            ("BackupView.xaml", "Backup.WeeklyDay", "WeeklyDayLabel"),
            ("BackupView.xaml", "Backup.WeeklyTime", "WeeklyTimeLabel"),
            ("BackupView.xaml", "Backup.WeeklyRetention", "WeeklyRetentionLabel"),
            ("BackupView.xaml", "Backup.Directory", "BackupDirectoryLabel"),
            ("BackupView.xaml", "Backup.FilterTarget", "FilterTargetLabel"),
            ("BackupView.xaml", "Backup.FilterResult", "FilterResultLabel"),
            ("BackupView.xaml", "Backup.From", "FromLabel"),
            ("BackupView.xaml", "Backup.To", "ToLabel"),
            ("BackupView.xaml", "Backup.Grid", "LogLabel"),
            ("CsvImportWindow.xaml", "CsvImport.FilePath", "FileLabel"),
            ("CsvImportWindow.xaml", "CsvImport.Grid", "Title"),
            ("DashboardView.xaml", "Dashboard.Date", "DateLabel"),
            ("InventoryView.xaml", "Inventory.AsOfDate", "AsOfDateLabel"),
            ("InventoryView.xaml", "Inventory.Search", "SearchPlaceholder"),
            ("InventoryView.xaml", "Inventory.Balances", "StockOnHandText"),
            ("InventoryView.xaml", "Inventory.MovementFrom", "FromLabel"),
            ("InventoryView.xaml", "Inventory.MovementTo", "ToLabel"),
            ("InventoryView.xaml", "Inventory.Movements", "MovementsText"),
            ("InventoryView.xaml", "Inventory.Counts", "CountsText"),
            ("OpeningStockWindow.xaml", "OpeningStock.Date", "DateLabel"),
            ("OpeningStockWindow.xaml", "OpeningStock.Notes", "NotesLabel"),
            ("OpeningStockWindow.xaml", "OpeningStock.Lines", "Title"),
            ("OpeningStockWindow.xaml", "OpeningStock.Line.Product", "DataContext.ProductLabel, Source={x:Reference LinesGrid}"),
            ("OpeningStockWindow.xaml", "OpeningStock.Line.Quantity", "DataContext.QuantityLabel, Source={x:Reference LinesGrid}"),
            ("OpeningStockWindow.xaml", "OpeningStock.Line.UnitCost", "DataContext.UnitCostLabel, Source={x:Reference LinesGrid}"),
            ("ProductEditorWindow.xaml", "ProductEditor.Sku", "SkuLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.Name", "NameLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.NameAr", "NameArLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.Description", "DescriptionLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.Category", "CategoryLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.Unit", "UnitLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.TaxCategory", "TaxCategoryLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.SalePrice", "SalePriceLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.CostPrice", "CostPriceLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.ReorderLevel", "ReorderLevelLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.Barcode", "BarcodeLabel"),
            ("ProductEditorWindow.xaml", "ProductEditor.NewBarcode", "AdditionalBarcodesLabel"),
            ("ProductsView.xaml", "Products.Search", "SearchPlaceholder"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Supplier", "SupplierLabel"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Date", "DateLabel"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Discount", "DiscountLabel"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Notes", "NotesLabel"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Lines", "Title"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Line.Product", "DataContext.ProductLabel, Source={x:Reference LinesGrid}"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Line.Quantity", "DataContext.QuantityLabel, Source={x:Reference LinesGrid}"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Line.UnitCost", "DataContext.UnitCostLabel, Source={x:Reference LinesGrid}"),
            ("PurchaseEditorWindow.xaml", "PurchaseEditor.Line.Tax", "DataContext.TaxLabel, Source={x:Reference LinesGrid}"),
            ("ReasonPromptWindow.xaml", "ReasonPrompt.Reason", "Label"),
            ("ReportsView.xaml", "Reports.Picker", "KindLabel"),
            ("ReportsView.xaml", "Reports.From", "FromLabel"),
            ("ReportsView.xaml", "Reports.To", "ToLabel"),
            ("ReportsView.xaml", "Reports.Cashier", "CashierLabel"),
            ("ReportsView.xaml", "Reports.Grid", "Title"),
            ("ReportsView.xaml", "Reports.DrawerOpening", "DrawerOpeningLabel"),
            ("ReportsView.xaml", "Reports.DrawerCounted", "DrawerCountedLabel"),
            ("RestoreWizardWindow.xaml", "Restore.Artifact", "ChooseLabel"),
            ("ReturnDialogWindow.xaml", "ReturnDialogLines", "Title"),
            ("ReturnDialogWindow.xaml", "ReturnDialogQuantity", "DataContext.ReturnQuantityHeader, Source={x:Reference ReturnLinesGrid}"),
            ("ReturnDialogWindow.xaml", "ReturnDialogReason", "ReasonLabel"),
            ("ReturnDialogWindow.xaml", "ReturnDialogMethod", "MethodLabel"),
            ("ReverseDialogWindow.xaml", "ReverseDialogReason", "ReasonLabel"),
            ("ReverseDialogWindow.xaml", "ReverseDialogMethod", "MethodLabel"),
            ("StockAdjustmentWindow.xaml", "StockAdjustment.Product", "ProductLabel"),
            ("StockAdjustmentWindow.xaml", "StockAdjustment.Quantity", "QuantityLabel"),
            ("StockAdjustmentWindow.xaml", "StockAdjustment.UnitCost", "UnitCostLabel"),
            ("StockAdjustmentWindow.xaml", "StockAdjustment.Reason", "ReasonLabel"),
            ("StockCountWindow.xaml", "StockCount.Date", "DateLabel"),
            ("StockCountWindow.xaml", "StockCount.Lines", "Title"),
            ("StockCountWindow.xaml", "StockCount.Line.Product", "DataContext.ProductLabel, Source={x:Reference LinesGrid}"),
            ("StockCountWindow.xaml", "StockCount.Line.Counted", "DataContext.CountedLabel, Source={x:Reference LinesGrid}"),
            ("UsersView.xaml", "Users.Grid", "UsersLabel"),
            ("UsersView.xaml", "Users.Roles", "RolesLabel"),
            ("UsersView.xaml", "Users.ChangeRolePicker", "ChangeRoleLabel"),
            ("UsersView.xaml", "Users.NewUsername", "NewUsernameLabel"),
            ("UsersView.xaml", "Users.NewDisplayName", "NewDisplayNameLabel"),
            ("UsersView.xaml", "Users.NewPassword", "NewPasswordLabel"),
            ("UsersView.xaml", "Users.NewRole", "NewRoleLabel"),
            ("ReferenceEditorWindow.xaml", "{Binding AutomationId}", "Label"),
            ("ReferenceEditorWindow.xaml", "{Binding AutomationId}", "Label"),
            ("ReferenceEditorWindow.xaml", "{Binding AutomationId}", "Label"),
            ("InventoryView.xaml", "Inventory.MovementProduct", "ProductLabel"),
            ("MainWindow.xaml", "LanguagePicker", "LanguageLabel"),
            ("PosView.xaml", "Pos.AddSearchResult", "DataContext.AddToCartLabel, RelativeSource={RelativeSource AncestorType=UserControl}"),
            ("ProductsView.xaml", "Products.Grid", "Title"),
            ("RecoveryWizardWindow.xaml", "Recovery.Path", "ChooseLabel"),
        ];
        foreach (IGrouping<(string File, string Id, string Binding), (string File, string Id, string Binding)> group in expected.GroupBy(entry => entry))
        {
            (string file, string id, string binding) = group.Key;
            XElement[] controls = XDocument.Load(Path.Combine(views, file)).Descendants()
                .Where(element => (string?)element.Attribute("AutomationProperties.AutomationId") == id).ToArray();
            controls.Length.ShouldBe(group.Count());
            foreach (XElement control in controls)
            {
                ((string?)control.Attribute("AutomationProperties.Name")).ShouldBe($"{{Binding {binding}}}");
            }
        }
    }

    [Fact]
    public void Identified_interactive_controls_without_a_declared_or_content_name_do_not_increase()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreFlow.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        string views = Path.Combine(root.FullName, "src", "StoreFlow.Desktop");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string[] interactive = ["TextBox", "PasswordBox", "ComboBox", "DatePicker", "DataGrid", "ListBox", "ListView", "Button", "CheckBox", "RadioButton", "ToggleButton"];
        string[] namingAttributes = ["AutomationProperties.Name", "AutomationProperties.LabeledBy", "Content", "Header"];
        string[] unnamed = Directory.GetFiles(views, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Name.Namespace == presentation && interactive.Contains(element.Name.LocalName, StringComparer.Ordinal))
                .Where(element => element.Attribute("AutomationProperties.AutomationId") is not null)
                .Where(element => !namingAttributes.Any(name => !string.IsNullOrWhiteSpace((string?)element.Attribute(name))))
                .Select(element => $"{Path.GetFileName(path)}: {(string?)element.Attribute("AutomationProperties.AutomationId")}"))
            .ToArray();

        unnamed.Length.ShouldBeLessThanOrEqualTo(0, string.Join(Environment.NewLine, unnamed));
    }
}
