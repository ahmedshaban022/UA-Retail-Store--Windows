using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Application.Inventory;
using StoreFlow.Application.Reporting;
using StoreFlow.Application.Scanning;
using StoreFlow.Application.Security;
using StoreFlow.Application.Setup;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Infrastructure;
using StoreFlow.Infrastructure.Catalog;
using StoreFlow.Infrastructure.Export;
using StoreFlow.Infrastructure.Identity;
using StoreFlow.Infrastructure.Inventory;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Reporting;
using StoreFlow.Infrastructure.Security;
using StoreFlow.Infrastructure.Services;
using StoreFlow.Infrastructure.Setup;
using StoreFlow.Infrastructure.Theming;

namespace StoreFlow.Desktop.Composition;

/// <summary>
/// Registers every service the application needs. The composition root is the only place that
/// knows which implementation is used for each port.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>The root namespace of the application assembly, used to locate the resource sets.</summary>
    public const string ApplicationName = "StoreFlow.Desktop";

    /// <summary>Adds the application's services to the container.</summary>
    public static IServiceCollection AddStoreFlowServices(
        this IServiceCollection services,
        AppPaths paths,
        ThemeDefinition theme,
        Serilog.ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(theme);

        services.AddSingleton(paths);

        services.AddDbContextFactory<StoreFlowDbContext>((provider, options) =>
        {
            options.UseSqlite($"Data Source={paths.DatabasePath}", sqlite =>
                sqlite.MigrationsAssembly(typeof(StoreFlowDbContext).Assembly.FullName));

            options.AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>());
        });

        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddSingleton<DatabaseInitializer>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IUserSession, InMemoryUserSession>();
        services.AddSingleton<IAuditWriter, EfAuditWriter>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IScanBuffer, ScanBuffer>();
        services.AddSingleton<IExportService, CsvExportService>();
        services.AddSingleton<IDashboardReader, EfDashboardReader>();

        services.AddSingleton<IThemeStore>(_ => new FileThemeStore(paths.ThemesDirectory));
        services.AddSingleton<ITokenVault>(_ => new DpapiTokenVault(paths.TokensDirectory));

        services.AddSingleton<IAuthorizationService, AuthorizationService>();

        services.AddSingleton<ICommandHandler<AuthenticateCommand, Result<UserSession>>, AuthenticateCommandHandler>();
        services.AddSingleton<ICommandHandler<CompleteSetupCommand, Result>, CompleteSetupCommandHandler>();
        services.AddSingleton<IQueryHandler<SetupStatusQuery, Result<SetupStatus>>, SetupStatusQueryHandler>();

        // The catalogue. Authorisation is enforced inside these handlers, so the interface never
        // decides who may do what (PRD §3).
        services.AddSingleton<IProductImageStore, ProductImageStore>();

        services.AddSingleton<ICommandHandler<SaveProductCommand, Result<int>>, SaveProductCommandHandler>();
        services.AddSingleton<ICommandHandler<SetProductActiveCommand, Result>, SetProductActiveCommandHandler>();
        services.AddSingleton<ICommandHandler<SaveCategoryCommand, Result<int>>, SaveCategoryCommandHandler>();
        services.AddSingleton<ICommandHandler<SetCategoryActiveCommand, Result>, SetCategoryActiveCommandHandler>();
        services.AddSingleton<ICommandHandler<SaveUnitCommand, Result<int>>, SaveUnitCommandHandler>();
        services.AddSingleton<ICommandHandler<SetUnitActiveCommand, Result>, SetUnitActiveCommandHandler>();
        services.AddSingleton<ICommandHandler<SaveSupplierCommand, Result<int>>, SaveSupplierCommandHandler>();
        services.AddSingleton<ICommandHandler<SetSupplierActiveCommand, Result>, SetSupplierActiveCommandHandler>();
        services.AddSingleton<ICommandHandler<ImportProductsCommand, Result<ImportSummary>>, ImportProductsCommandHandler>();
        services.AddSingleton<ICommandHandler<ExportProductsCommand, Result<int>>, ExportProductsCommandHandler>();

        services.AddSingleton<IQueryHandler<SearchProductsQuery, Result<ProductPage>>, SearchProductsQueryHandler>();
        services.AddSingleton<IQueryHandler<GetProductQuery, Result<ProductDetails>>, GetProductQueryHandler>();
        services.AddSingleton<IQueryHandler<ListCategoriesQuery, Result<IReadOnlyList<CategorySummary>>>, ListCategoriesQueryHandler>();
        services.AddSingleton<IQueryHandler<ListUnitsQuery, Result<IReadOnlyList<UnitSummary>>>, ListUnitsQueryHandler>();
        services.AddSingleton<IQueryHandler<ListSuppliersQuery, Result<IReadOnlyList<SupplierSummary>>>, ListSuppliersQueryHandler>();
        services.AddSingleton<IQueryHandler<GetCatalogReferenceDataQuery, Result<CatalogReferenceData>>, GetCatalogReferenceDataQueryHandler>();
        services.AddSingleton<IQueryHandler<PreviewProductImportQuery, Result<ProductImportPreview>>, PreviewProductImportQueryHandler>();
        services.AddSingleton<IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryView>>>, ListAuditEntriesQueryHandler>();
        services.AddSingleton<IQueryHandler<GetProductAuditQuery, Result<IReadOnlyList<AuditEntryView>>>, GetProductAuditQueryHandler>();

        // The inventory and purchase screens. Authorisation is enforced inside these handlers, so
        // the interface never decides who may do what (PRD §3).
        services.AddSingleton<ICommandHandler<SavePurchaseCommand, Result<int>>, SavePurchaseCommandHandler>();
        services.AddSingleton<ICommandHandler<PostPurchaseCommand, Result<int>>, PostPurchaseCommandHandler>();
        services.AddSingleton<ICommandHandler<ReversePurchaseCommand, Result<int>>, ReversePurchaseCommandHandler>();
        services.AddSingleton<ICommandHandler<ExportPurchasesCommand, Result<int>>, ExportPurchasesCommandHandler>();
        services.AddSingleton<ICommandHandler<RecordOpeningStockCommand, Result<int>>, RecordOpeningStockCommandHandler>();
        services.AddSingleton<ICommandHandler<PostStockAdjustmentCommand, Result<StockAdjustmentOutcome>>, PostStockAdjustmentCommandHandler>();
        services.AddSingleton<ICommandHandler<ApproveStockAdjustmentCommand, Result<int>>, ApproveStockAdjustmentCommandHandler>();
        services.AddSingleton<ICommandHandler<SaveStockCountCommand, Result<int>>, SaveStockCountCommandHandler>();
        services.AddSingleton<ICommandHandler<SubmitStockCountCommand, Result>, SubmitStockCountCommandHandler>();
        services.AddSingleton<ICommandHandler<ApproveStockCountCommand, Result>, ApproveStockCountCommandHandler>();
        services.AddSingleton<ICommandHandler<PostStockCountCommand, Result<int>>, PostStockCountCommandHandler>();
        services.AddSingleton<ICommandHandler<CancelStockCountCommand, Result>, CancelStockCountCommandHandler>();

        services.AddSingleton<IQueryHandler<ListPurchasesQuery, Result<IReadOnlyList<PurchaseSummary>>>, ListPurchasesQueryHandler>();
        services.AddSingleton<IQueryHandler<GetPurchaseQuery, Result<PurchaseDetails>>, GetPurchaseQueryHandler>();
        services.AddSingleton<IQueryHandler<ListStockBalancesQuery, Result<IReadOnlyList<StockBalanceSummary>>>, ListStockBalancesQueryHandler>();
        services.AddSingleton<IQueryHandler<ListStockMovementsQuery, Result<IReadOnlyList<StockMovementSummary>>>, ListStockMovementsQueryHandler>();
        services.AddSingleton<IQueryHandler<ListLowStockQuery, Result<IReadOnlyList<LowStockItem>>>, ListLowStockQueryHandler>();
        services.AddSingleton<IQueryHandler<GetInventoryValuationQuery, Result<InventoryValuation>>, GetInventoryValuationQueryHandler>();
        services.AddSingleton<IQueryHandler<ListStockCountsQuery, Result<IReadOnlyList<StockCountSummary>>>, ListStockCountsQueryHandler>();
        services.AddSingleton<IQueryHandler<GetStockCountQuery, Result<StockCountDetails>>, GetStockCountQueryHandler>();
        services.AddSingleton<IQueryHandler<GetInventoryReferenceDataQuery, Result<InventoryReferenceData>>, GetInventoryReferenceDataQueryHandler>();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();

            if (logger is not null)
            {
                logging.AddSerilog(logger, dispose: false);
            }
        });

        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddSingleton<IStrings, Strings>();
        services.AddSingleton<LanguageService>();

        services.AddSingleton<IThemeService>(provider =>
            new WpfThemeService(
                System.Windows.Application.Current.Resources,
                theme,
                provider.GetRequiredService<LanguageService>()));

        services.AddTransient<ShellViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<FirstRunViewModel>();

        // The catalogue's screens. Each one is built fresh when the owner opens it, so it always
        // shows what the database holds at that moment.
        services.AddSingleton<ICatalogDialogs, CatalogDialogs>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<CategoriesViewModel>();
        services.AddTransient<UnitsViewModel>();
        services.AddTransient<SuppliersViewModel>();
        services.AddTransient<AuditViewModel>();
        services.AddTransient<ProductEditorViewModel>();
        services.AddTransient<CsvImportViewModel>();

        // The purchase and inventory screens, and the dialogs they open.
        services.AddSingleton<IInventoryDialogs, InventoryDialogs>();
        services.AddTransient<PurchasesViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<PurchaseEditorViewModel>();
        services.AddTransient<StockAdjustmentViewModel>();
        services.AddTransient<OpeningStockViewModel>();
        services.AddTransient<StockCountViewModel>();

        return services;
    }
}
