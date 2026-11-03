using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The recovery wizard. It closes itself once the owner has finished with it, and reports back
/// whether the shop was restored.
/// </summary>
public partial class RecoveryWizardWindow : Window
{
    private bool _closing;
    private bool _restart;

    /// <summary>Creates the window.</summary>
    public RecoveryWizardWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.RecoveryWizardViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
            viewModel.RestartRequested += OnRestartRequested;
        }
    }

    private void OnRestartRequested(object? sender, EventArgs e) => _restart = true;

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        DialogResult = DataContext is ViewModels.RecoveryWizardViewModel viewModel && viewModel.IsRestored;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ViewModels.RecoveryWizardViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.RestartRequested -= OnRestartRequested;
        }

        base.OnClosed(e);

        if (_restart)
        {
            ApplicationRestart.Restart();
        }
    }
}
