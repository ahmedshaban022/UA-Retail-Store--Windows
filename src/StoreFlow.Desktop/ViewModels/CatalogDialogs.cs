using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Desktop.Views;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Shows the catalogue's dialogs. The window is created here, so the screen logic never has to know
/// what a dialog looks like.
/// </summary>
public sealed class CatalogDialogs(IServiceProvider services) : ICatalogDialogs
{
    /// <inheritdoc />
    public async Task<bool> ShowProductEditorAsync(int? productId, CancellationToken cancellationToken = default)
    {
        ProductEditorViewModel viewModel = services.GetRequiredService<ProductEditorViewModel>();

        await viewModel.LoadAsync(productId, cancellationToken);

        var window = new ProductEditorWindow { DataContext = viewModel };
        Prepare(window);

        return window.ShowDialog() == true;
    }

    /// <inheritdoc />
    public async Task<bool> ShowReferenceEditorAsync(
        CatalogReferenceKind kind,
        int? recordId,
        CancellationToken cancellationToken = default)
    {
        ReferenceEditorViewModel viewModel =
            ActivatorUtilities.CreateInstance<ReferenceEditorViewModel>(services, kind);

        await viewModel.LoadAsync(recordId, cancellationToken);

        var window = new ReferenceEditorWindow { DataContext = viewModel };
        Prepare(window);

        return window.ShowDialog() == true;
    }

    /// <inheritdoc />
    public async Task<bool> ShowCsvImportAsync(CancellationToken cancellationToken = default)
    {
        CsvImportViewModel viewModel = services.GetRequiredService<CsvImportViewModel>();

        await viewModel.RefreshPreviewAsync(cancellationToken);

        var window = new CsvImportWindow { DataContext = viewModel };
        Prepare(window);

        return window.ShowDialog() == true;
    }

    private static void Prepare(Window window)
    {
        if (System.Windows.Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }
    }
}
