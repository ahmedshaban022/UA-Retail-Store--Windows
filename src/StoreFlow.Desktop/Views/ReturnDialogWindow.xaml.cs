using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The return dialog. It closes itself once the return has been put together, or once the owner
/// has walked away from it.
/// </summary>
public partial class ReturnDialogWindow : Window
{
    private bool _closing;

    /// <summary>Creates the dialog.</summary>
    public ReturnDialogWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.ReturnDialogViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, ViewModels.ReturnDialogResult? result)
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
        if (DataContext is ViewModels.ReturnDialogViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
