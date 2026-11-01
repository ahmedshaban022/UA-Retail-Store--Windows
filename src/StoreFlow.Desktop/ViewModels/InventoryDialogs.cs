using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Views;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Shows the inventory screens' dialogs. The window is created here, so the screen logic never has
/// to know what a dialog looks like.
/// </summary>
public sealed class InventoryDialogs(IServiceProvider services) : IInventoryDialogs
{
    /// <inheritdoc />
    public async Task<bool> ShowPurchaseEditorAsync(int? purchaseId, CancellationToken cancellationToken = default)
    {
        PurchaseEditorViewModel viewModel = services.GetRequiredService<PurchaseEditorViewModel>();

        await viewModel.LoadAsync(purchaseId, cancellationToken);

        var window = new PurchaseEditorWindow { DataContext = viewModel };
        Prepare(window);

        return window.ShowDialog() == true;
    }

    /// <inheritdoc />
    public Task<string?> AskForReasonAsync(string title, string label, CancellationToken cancellationToken = default)
    {
        var viewModel = new ReasonPromptViewModel(services.GetRequiredService<IStrings>())
        {
            Title = title,
            Label = label,
        };

        var window = new ReasonPromptWindow { DataContext = viewModel };
        Prepare(window);

        string? reason = null;

        viewModel.CloseRequested += (_, value) => reason = value;

        window.ShowDialog();

        return Task.FromResult(reason);
    }

    /// <inheritdoc />
    public async Task<bool> ShowStockAdjustmentAsync(CancellationToken cancellationToken = default)
    {
        StockAdjustmentViewModel viewModel = services.GetRequiredService<StockAdjustmentViewModel>();

        await viewModel.LoadAsync(cancellationToken);

        var window = new StockAdjustmentWindow { DataContext = viewModel };
        Prepare(window);

        return window.ShowDialog() == true;
    }

    /// <inheritdoc />
    public async Task<bool> ShowOpeningStockAsync(CancellationToken cancellationToken = default)
    {
        OpeningStockViewModel viewModel = services.GetRequiredService<OpeningStockViewModel>();

        await viewModel.LoadAsync(cancellationToken);

        var window = new OpeningStockWindow { DataContext = viewModel };
        Prepare(window);

        return window.ShowDialog() == true;
    }

    /// <inheritdoc />
    public async Task<bool> ShowStockCountAsync(CancellationToken cancellationToken = default)
    {
        StockCountViewModel viewModel = services.GetRequiredService<StockCountViewModel>();

        await viewModel.LoadAsync(cancellationToken);

        var window = new StockCountWindow { DataContext = viewModel };
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
