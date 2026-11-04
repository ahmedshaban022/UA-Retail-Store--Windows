using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;
using StoreFlow.Application.Setup;
using StoreFlow.Application.Theming;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using StoreFlow.Desktop.Views;
using StoreFlow.Infrastructure;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Theming;
using System.IO;
using System.Windows;

namespace StoreFlow.Desktop.Composition;

/// <summary>
/// The composition root. It builds the container, prepares the data folder and the database, loads
/// the saved theme, and decides which window to show first.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private AppHost(
        ServiceProvider services,
        AppPaths paths,
        ThemeDefinition theme,
        ResourceDictionary resources,
        bool databaseReady)
    {
        _services = services;
        Paths = paths;
        Theme = theme;
        Resources = resources;
        _databaseReady = databaseReady;
    }

    /// <summary>The known locations on disk.</summary>
    public AppPaths Paths { get; }

    /// <summary>The theme the application starts with.</summary>
    public ThemeDefinition Theme { get; private set; }

    /// <summary>The application's resource dictionary, where the live theme is installed.</summary>
    public ResourceDictionary Resources { get; }

    /// <summary>The service container, so a view can reach a service it needs.</summary>
    public IServiceProvider Services => _services;

    /// <summary>The main shell, once the user has signed in.</summary>
    public MainWindow? Shell { get; private set; }

    /// <summary>
    /// True when the shop's database could be prepared. When it could not, the owner is offered the
    /// recovery wizard instead of the sign-in window: there is no shop to sign in to (PRD §8.5).
    /// </summary>
    private bool _databaseReady;

    /// <summary>The scheduler that takes the shop's backups while it is open.</summary>
    private BackupScheduler? _scheduler;

    /// <summary>Builds the host and prepares the database.</summary>
    public static async Task<AppHost> CreateAsync()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        // The theme is loaded before the container is built, because the theme service needs it and
        // because the theme file is the product's rebrand surface.
        var themeStore = new FileThemeStore(paths.ThemesDirectory);
        ThemeDefinition theme = await themeStore.LoadAsync();

        Serilog.ILogger logger = BuildLogger(paths);

        var services = new ServiceCollection();
        services.AddStoreFlowServices(paths, theme, logger);

        var provider = services.BuildServiceProvider();

        // The language is restored from the theme and the culture applied before anything reads a
        // string, because the view models take their labels at construction time.
        LanguageService language = provider.GetRequiredService<LanguageService>();
        language.SetLanguage(theme.Locale.Default);

        DatabaseInitializer initializer = provider.GetRequiredService<DatabaseInitializer>();

        bool databaseReady = true;

        try
        {
            await initializer.InitializeAsync();
        }
        catch (SqliteException ex)
        {
            // The shop's own data cannot be read. The owner is offered the recovery wizard rather
            // than a sign-in window they cannot get past.
            databaseReady = false;

            StartupDiagnostics.Write("The shop's database could not be prepared.", ex);
        }
        catch (DbUpdateException ex)
        {
            databaseReady = false;

            StartupDiagnostics.Write("The shop's database could not be brought up to date.", ex);
        }

        ResourceDictionary resources = System.Windows.Application.Current.Resources;

        return new AppHost(provider, paths, theme, resources, databaseReady);
    }

    /// <summary>Builds the rolling file logger the application writes its diagnostics to.</summary>
    private static Serilog.ILogger BuildLogger(AppPaths paths)
    {
        try
        {
            return new Serilog.LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(paths.LogsDirectory, "storeflow-.log"),
                    rollingInterval: Serilog.RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true)
                .CreateLogger();
        }
#pragma warning disable CA1031 // A logger that throws would prevent the application from starting.
        catch (Exception)
        {
            return Serilog.Core.Logger.None;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Shows the window the application should open with: the first-run wizard when the installation
    /// has no administrator yet, then the sign-in window, and finally the shell.
    /// </summary>
    public async Task<Window> ShowStartupWindowAsync()
    {
        if (!_databaseReady)
        {
            bool recovered = await ShowRecoveryAsync();

            if (!recovered)
            {
                throw new InvalidOperationException(
                    "The shop's data cannot be read and no backup was restored, so StoreFlow cannot start.");
            }
        }

        var statusHandler = _services.GetRequiredService<IQueryHandler<SetupStatusQuery, Result<SetupStatus>>>();
        Result<SetupStatus> status = await statusHandler.HandleAsync(new SetupStatusQuery(), CancellationToken.None);

        if (status.IsSuccess && !status.Value.IsComplete)
        {
            var wizard = new FirstRunWindow
            {
                DataContext = _services.GetRequiredService<FirstRunViewModel>(),
            };

            wizard.ShowDialog();
        }

        var login = new LoginWindow
        {
            DataContext = _services.GetRequiredService<LoginViewModel>(),
        };

        login.ShowDialog();

        Shell = new MainWindow
        {
            DataContext = _services.GetRequiredService<ShellViewModel>(),
        };

        return Shell;
    }

    /// <summary>
    /// Offers the recovery wizard, which is how a shop whose own data cannot be read is put back.
    /// </summary>
    /// <returns>True when the shop was restored.</returns>
    private async Task<bool> ShowRecoveryAsync()
    {
        var viewModel = _services.GetRequiredService<RecoveryWizardViewModel>();

        var window = new RecoveryWizardWindow { DataContext = viewModel };

        window.ShowDialog();

        if (!viewModel.IsRestored)
        {
            return false;
        }

        DatabaseInitializer initializer = _services.GetRequiredService<DatabaseInitializer>();

        await initializer.InitializeAsync();

        _databaseReady = true;

        StartupDiagnostics.Write("The shop was recovered from a backup and the database is ready again.");

        return true;
    }

    /// <summary>Called once the shell is on screen, to run the first page load.</summary>
    public void ShellReady()
    {
        if (Shell?.DataContext is ShellViewModel shell)
        {
            shell.NavigateCommand.Execute("Dashboard");

            // The update check runs in the background. The shop's programme never waits for the
            // internet, and a shop that is offline simply sees no banner.
            _ = shell.CheckForUpdatesAsync();
        }

        // The scheduler starts once the shop is open, so that the backups it takes are taken from
        // a shop that is ready.
        _scheduler = new BackupScheduler(_services);
        _scheduler.Start();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _scheduler?.Dispose();

        _services.Dispose();
        return ValueTask.CompletedTask;
    }
}
