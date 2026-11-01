using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Application.Sales;
using StoreFlow.Desktop.Views;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Shows the sales register's dialogs. The window is created here, so the screen logic never has to
/// know what a dialog looks like.
/// </summary>
public sealed class SalesDialogs(IServiceProvider services) : ISalesDialogs
{
    /// <inheritdoc />
    public Task<ReturnDialogResult?> AskForReturnAsync(SalesInvoiceDetails invoice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        ReturnDialogViewModel viewModel = services.GetRequiredService<ReturnDialogViewModel>();

        viewModel.Load(invoice);

        var window = new ReturnDialogWindow { DataContext = viewModel };
        Prepare(window);

        ReturnDialogResult? result = null;

        viewModel.CloseRequested += (_, value) => result = value;

        window.ShowDialog();

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ReverseDialogResult?> AskForReverseAsync(SalesInvoiceDetails invoice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        ReverseDialogViewModel viewModel = services.GetRequiredService<ReverseDialogViewModel>();

        viewModel.Load(invoice);

        var window = new ReverseDialogWindow { DataContext = viewModel };
        Prepare(window);

        ReverseDialogResult? result = null;

        viewModel.CloseRequested += (_, value) => result = value;

        window.ShowDialog();

        return Task.FromResult(result);
    }

    /// <summary>Gives the dialog an owner, so it sits over the shell and not behind it.</summary>
    /// <param name="window">The dialog.</param>
    private static void Prepare(Window window)
    {
        if (System.Windows.Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }
    }
}
