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
        ResourceDictionary resources)
    {
        _services = services;
        Paths = paths;
        Theme = theme;
        Resources = resources;
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
        await initializer.InitializeAsync();

        ResourceDictionary resources = System.Windows.Application.Current.Resources;

        return new AppHost(provider, paths, theme, resources);
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

    /// <summary>Called once the shell is on screen, to run the first page load.</summary>
    public void ShellReady()
    {
        if (Shell?.DataContext is ShellViewModel shell)
        {
            shell.NavigateCommand.Execute("Dashboard");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _services.Dispose();
        return ValueTask.CompletedTask;
    }
}
