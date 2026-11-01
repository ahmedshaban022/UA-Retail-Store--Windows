using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The reversal dialog. It closes itself once the reversal has been put together, or once the owner
/// has walked away from it.
/// </summary>
public partial class ReverseDialogWindow : Window
{
    private bool _closing;

    /// <summary>Creates the dialog.</summary>
    public ReverseDialogWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.ReverseDialogViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, ViewModels.ReverseDialogResult? result)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        DialogResult = result is not null;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ViewModels.ReverseDialogViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
