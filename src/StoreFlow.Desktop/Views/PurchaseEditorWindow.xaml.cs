using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The purchase editor. It closes itself once the document has been saved, or once the owner has
/// walked away from it.
/// </summary>
public partial class PurchaseEditorWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public PurchaseEditorWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.PurchaseEditorViewModel viewModel)
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
        if (DataContext is ViewModels.PurchaseEditorViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
