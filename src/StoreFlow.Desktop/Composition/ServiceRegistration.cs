using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Identity;
using StoreFlow.Application.Reporting;
using StoreFlow.Application.Scanning;
using StoreFlow.Application.Security;
using StoreFlow.Application.Setup;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Infrastructure;
using StoreFlow.Infrastructure.Export;
using StoreFlow.Infrastructure.Identity;
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

        return services;
    }
}
