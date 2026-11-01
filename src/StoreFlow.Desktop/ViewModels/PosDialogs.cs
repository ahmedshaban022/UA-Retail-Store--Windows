using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Application.Pos;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Views;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>Shows the till's dialogs.</summary>
public sealed class PosDialogs(IServiceProvider services) : IPosDialogs
{
    /// <inheritdoc />
    public Task ShowReceiptAsync(SaleReceipt receipt, bool isCopy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var viewModel = new ReceiptViewModel(
            receipt,
            isCopy,
            services.GetRequiredService<StoreFlow.Application.Abstractions.IPrintService>(),
            services.GetRequiredService<StoreFlow.Desktop.Printing.ReceiptBuilder>(),
            services.GetRequiredService<IStrings>(),
            services.GetRequiredService<StoreFlow.Application.Abstractions.IUserSession>());

        var window = new ReceiptWindow { DataContext = viewModel };

        Prepare(window);

        window.ShowDialog();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> AskForLabelAsync(string title, string label, CancellationToken cancellationToken = default)
    {
        var viewModel = new ReasonPromptViewModel(services.GetRequiredService<IStrings>())
        {
            Title = title,
            Label = label,
        };

        var window = new ReasonPromptWindow { DataContext = viewModel };

        Prepare(window);

        string? answer = null;

        viewModel.CloseRequested += (_, value) => answer = value;

        window.ShowDialog();

        return Task.FromResult(answer);
    }

    private static void Prepare(Window window)
    {
        if (System.Windows.Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }
    }
}
