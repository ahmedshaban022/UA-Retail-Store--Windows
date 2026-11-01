using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The reason prompt. It closes itself once the owner has given a reason, or once they have
/// walked away from it.
/// </summary>
public partial class ReasonPromptWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public ReasonPromptWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.ReasonPromptViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, string? reason)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        DialogResult = reason is null ? false : true;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ViewModels.ReasonPromptViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
