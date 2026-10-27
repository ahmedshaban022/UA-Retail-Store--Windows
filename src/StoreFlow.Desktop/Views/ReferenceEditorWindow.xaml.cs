using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The editor for the catalogue's reference records. It closes itself once the record has been
/// saved, or once the owner has walked away from it.
/// </summary>
public partial class ReferenceEditorWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public ReferenceEditorWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.ReferenceEditorViewModel viewModel)
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
        if (DataContext is ViewModels.ReferenceEditorViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
