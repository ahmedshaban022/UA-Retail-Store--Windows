namespace StoreFlow.Desktop.Localization;

/// <summary>
/// The resource keys used by the interface. Keeping them as constants means a missing translation
/// is a compile error rather than a blank label at run time.
/// </summary>
public static class StringKeys
{
    public const string AppLoading = "App.Loading";
    public const string AppTitle = "App.Title";

    public const string AuditAction = "Audit.Action";
    public const string AuditEmpty = "Audit.Empty";
    public const string AuditEntityType = "Audit.EntityType";
    public const string AuditSearch = "Audit.Search";
    public const string AuditSummary = "Audit.Summary";
    public const string AuditTitle = "Audit.Title";
    public const string AuditWhen = "Audit.When";
    public const string AuditWho = "Audit.Who";

    public const string CategoriesEmpty = "Categories.Empty";
    public const string CategoriesName = "Categories.Name";
    public const string CategoriesNameAr = "Categories.NameAr";
    public const string CategoriesNew = "Categories.New";
    public const string CategoriesProducts = "Categories.Products";
    public const string CategoriesTitle = "Categories.Title";

    public const string CommonActivate = "Common.Activate";
    public const string CommonAdd = "Common.Add";
    public const string CommonAll = "Common.All";
    public const string CommonBack = "Common.Back";
    public const string CommonBrowse = "Common.Browse";
    public const string CommonCancel = "Common.Cancel";
    public const string CommonClose = "Common.Close";
    public const string CommonDeactivate = "Common.Deactivate";
    public const string CommonEdit = "Common.Edit";
    public const string CommonError = "Common.Error";
    public const string CommonExport = "Common.Export";
    public const string CommonFinish = "Common.Finish";
    public const string CommonImport = "Common.Import";
    public const string CommonLine = "Common.Line";
    public const string CommonNext = "Common.Next";
    public const string CommonOk = "Common.Ok";
    public const string CommonProblems = "Common.Problems";
    public const string CommonRemove = "Common.Remove";
    public const string CommonRetry = "Common.Retry";
    public const string CommonSave = "Common.Save";
    public const string CommonSearch = "Common.Search";
    public const string CommonShowInactive = "Common.ShowInactive";
    public const string CommonStatus = "Common.Status";

    public const string CsvAction = "Csv.Action";
    public const string CsvActionCreate = "Csv.ActionCreate";
    public const string CsvActionInvalid = "Csv.ActionInvalid";
    public const string CsvActionUpdate = "Csv.ActionUpdate";
    public const string CsvCategory = "Csv.Category";
    public const string CsvChooseFile = "Csv.ChooseFile";
    public const string CsvEmpty = "Csv.Empty";
    public const string CsvFile = "Csv.File";
    public const string CsvMode = "Csv.Mode";
    public const string CsvModeCreateOnly = "Csv.ModeCreateOnly";
    public const string CsvModeUpsert = "Csv.ModeUpsert";
    public const string CsvName = "Csv.Name";
    public const string CsvReady = "Csv.Ready";
    public const string CsvSalePrice = "Csv.SalePrice";
    public const string CsvSku = "Csv.Sku";
    public const string CsvTitle = "Csv.Title";
    public const string CsvUnit = "Csv.Unit";

    public const string DashboardInvoices = "Dashboard.Invoices";
    public const string DashboardLowStock = "Dashboard.LowStock";
    public const string DashboardNoSalesYet = "Dashboard.NoSalesYet";
    public const string DashboardTitle = "Dashboard.Title";
    public const string DashboardTodaySales = "Dashboard.TodaySales";
    public const string DashboardWelcome = "Dashboard.Welcome";

    public const string ErrorBarcodeInUse = "Error.BarcodeInUse";
    public const string ErrorDuplicateBarcode = "Error.DuplicateBarcode";
    public const string ErrorDuplicateName = "Error.DuplicateName";
    public const string ErrorDuplicateSku = "Error.DuplicateSku";
    public const string ErrorImageNotFound = "Error.ImageNotFound";
    public const string ErrorImageTooLarge = "Error.ImageTooLarge";
    public const string ErrorImageUnsupported = "Error.ImageUnsupported";
    public const string ErrorImageWriteFailed = "Error.ImageWriteFailed";
    public const string ErrorImportFileNotReadable = "Error.ImportFileNotReadable";
    public const string ErrorImportMissingValue = "Error.ImportMissingValue";
    public const string ErrorImportNotANumber = "Error.ImportNotANumber";
    public const string ErrorImportNotABoolean = "Error.ImportNotABoolean";
    public const string ErrorImportUnknownCategory = "Error.ImportUnknownCategory";
    public const string ErrorImportUnknownUnit = "Error.ImportUnknownUnit";
    public const string ErrorImportUnknownTaxCategory = "Error.ImportUnknownTaxCategory";
    public const string ErrorImportSkuExists = "Error.ImportSkuExists";
    public const string ErrorImportDuplicateSku = "Error.ImportDuplicateSku";
    public const string ErrorImportDuplicateBarcode = "Error.ImportDuplicateBarcode";
    public const string ErrorImportUnknownField = "Error.ImportUnknownField";
    public const string ErrorInvalidBarcode = "Error.InvalidBarcode";
    public const string ErrorInvalidPrecision = "Error.InvalidPrecision";
    public const string ErrorInvalidReference = "Error.InvalidReference";
    public const string ErrorNegativePrice = "Error.NegativePrice";
    public const string ErrorNegativeQuantity = "Error.NegativeQuantity";
    public const string ErrorNotAuthorized = "Error.NotAuthorized";
    public const string ErrorNotFound = "Error.NotFound";
    public const string ErrorPrecisionInUse = "Error.PrecisionInUse";
    public const string ErrorRequired = "Error.Required";
    public const string ErrorTooLong = "Error.TooLong";
    public const string ErrorUnexpected = "Error.Unexpected";

    public const string FirstRunAddress = "FirstRun.Address";
    public const string FirstRunConfirmPassword = "FirstRun.ConfirmPassword";
    public const string FirstRunDisplayName = "FirstRun.DisplayName";
    public const string FirstRunLanguage = "FirstRun.Language";
    public const string FirstRunPassword = "FirstRun.Password";
    public const string FirstRunPasswordMismatch = "FirstRun.PasswordMismatch";
    public const string FirstRunPhone = "FirstRun.Phone";
    public const string FirstRunStep1 = "FirstRun.Step1";
    public const string FirstRunStep2 = "FirstRun.Step2";
    public const string FirstRunStep3 = "FirstRun.Step3";
    public const string FirstRunStoreName = "FirstRun.StoreName";
    public const string FirstRunTheme = "FirstRun.Theme";
    public const string FirstRunThemeDark = "FirstRun.ThemeDark";
    public const string FirstRunThemeHighContrast = "FirstRun.ThemeHighContrast";
    public const string FirstRunThemeLight = "FirstRun.ThemeLight";
    public const string FirstRunTitle = "FirstRun.Title";

    public const string LoginDisabled = "Login.Disabled";
    public const string LoginInvalid = "Login.Invalid";
    public const string LoginLocked = "Login.Locked";
    public const string LoginPassword = "Login.Password";
    public const string LoginSignIn = "Login.SignIn";
    public const string LoginTitle = "Login.Title";
    public const string LoginUsername = "Login.Username";

    public const string NavAudit = "Nav.Audit";
    public const string NavBackup = "Nav.Backup";
    public const string NavCategories = "Nav.Categories";
    public const string NavDashboard = "Nav.Dashboard";
    public const string NavInventory = "Nav.Inventory";
    public const string NavPos = "Nav.Pos";
    public const string NavProducts = "Nav.Products";
    public const string NavPurchases = "Nav.Purchases";
    public const string NavReports = "Nav.Reports";
    public const string NavSales = "Nav.Sales";
    public const string NavSettings = "Nav.Settings";
    public const string NavSuppliers = "Nav.Suppliers";
    public const string NavUnits = "Nav.Units";
    public const string NavUsers = "Nav.Users";

    public const string ProductEditorActive = "ProductEditor.Active";
    public const string ProductEditorAddBarcode = "ProductEditor.AddBarcode";
    public const string ProductEditorAdditionalBarcodes = "ProductEditor.AdditionalBarcodes";
    public const string ProductEditorBarcode = "ProductEditor.Barcode";
    public const string ProductEditorCategory = "ProductEditor.Category";
    public const string ProductEditorChooseImage = "ProductEditor.ChooseImage";
    public const string ProductEditorCostPrice = "ProductEditor.CostPrice";
    public const string ProductEditorDescription = "ProductEditor.Description";
    public const string ProductEditorHistory = "ProductEditor.History";
    public const string ProductEditorHistoryEmpty = "ProductEditor.HistoryEmpty";
    public const string ProductEditorImage = "ProductEditor.Image";
    public const string ProductEditorName = "ProductEditor.Name";
    public const string ProductEditorNameAr = "ProductEditor.NameAr";
    public const string ProductEditorReorderLevel = "ProductEditor.ReorderLevel";
    public const string ProductEditorRemoveImage = "ProductEditor.RemoveImage";
    public const string ProductEditorSalePrice = "ProductEditor.SalePrice";
    public const string ProductEditorSku = "ProductEditor.Sku";
    public const string ProductEditorTaxCategory = "ProductEditor.TaxCategory";
    public const string ProductEditorTitleEdit = "ProductEditor.TitleEdit";
    public const string ProductEditorTitleNew = "ProductEditor.TitleNew";
    public const string ProductEditorUnit = "ProductEditor.Unit";

    public const string ProductsActivated = "Products.Activated";
    public const string ProductsActive = "Products.Active";
    public const string ProductsBarcode = "Products.Barcode";
    public const string ProductsCategory = "Products.Category";
    public const string ProductsCostPrice = "Products.CostPrice";
    public const string ProductsDeactivated = "Products.Deactivated";
    public const string ProductsEmpty = "Products.Empty";
    public const string ProductsExported = "Products.Exported";
    public const string ProductsInactive = "Products.Inactive";
    public const string ProductsName = "Products.Name";
    public const string ProductsNameAr = "Products.NameAr";
    public const string ProductsNew = "Products.New";
    public const string ProductsNoMatches = "Products.NoMatches";
    public const string ProductsSalePrice = "Products.SalePrice";
    public const string ProductsSearch = "Products.Search";
    public const string ProductsSku = "Products.Sku";
    public const string ProductsTitle = "Products.Title";
    public const string ProductsUncategorized = "Products.Uncategorized";
    public const string ProductsUnit = "Products.Unit";

    public const string SessionLock = "Session.Lock";
    public const string SessionSignOut = "Session.SignOut";

    public const string SettingsLanguage = "Settings.Language";
    public const string SettingsProductName = "Settings.ProductName";
    public const string SettingsRebrand = "Settings.Rebrand";
    public const string SettingsSaved = "Settings.Saved";
    public const string SettingsTheme = "Settings.Theme";

    public const string SuppliersEmpty = "Suppliers.Empty";
    public const string SuppliersName = "Suppliers.Name";
    public const string SuppliersNameAr = "Suppliers.NameAr";
    public const string SuppliersNew = "Suppliers.New";
    public const string SuppliersNotes = "Suppliers.Notes";
    public const string SuppliersPhone = "Suppliers.Phone";
    public const string SuppliersTitle = "Suppliers.Title";

    public const string UnitsEmpty = "Units.Empty";
    public const string UnitsName = "Units.Name";
    public const string UnitsNameAr = "Units.NameAr";
    public const string UnitsNew = "Units.New";
    public const string UnitsPrecision = "Units.Precision";
    public const string UnitsProducts = "Units.Products";
    public const string UnitsTitle = "Units.Title";
}
