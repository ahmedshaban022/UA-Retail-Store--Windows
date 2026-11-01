using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The stock adjustment dialog. It closes itself once the adjustment has been posted, or once the
/// owner has walked away from it.
/// </summary>
public partial class StockAdjustmentWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public StockAdjustmentWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.StockAdjustmentViewModel viewModel)
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
        if (DataContext is ViewModels.StockAdjustmentViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
