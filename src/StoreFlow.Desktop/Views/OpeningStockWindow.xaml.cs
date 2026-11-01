using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The opening stock dialog. It closes itself once the opening balances are recorded, or once the
/// owner has walked away from it.
/// </summary>
public partial class OpeningStockWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public OpeningStockWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.OpeningStockViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, bool saved)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        DialogResult = saved;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ViewModels.OpeningStockViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
