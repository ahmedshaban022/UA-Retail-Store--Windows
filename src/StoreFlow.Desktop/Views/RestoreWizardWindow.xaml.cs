using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The restore wizard. It closes itself once the owner has finished with it, and reports back
/// whether the shop was restored.
/// </summary>
public partial class RestoreWizardWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public RestoreWizardWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.RestoreWizardViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        DialogResult = DataContext is ViewModels.RestoreWizardViewModel viewModel && viewModel.IsRestored;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ViewModels.RestoreWizardViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
