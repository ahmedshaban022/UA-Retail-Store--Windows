using System.ComponentModel;
using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>The first-run wizard. It closes itself once the setup has been written.</summary>
public partial class FirstRunWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public FirstRunWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.FirstRunViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModels.FirstRunViewModel.IsComplete) ||
            sender is not ViewModels.FirstRunViewModel viewModel ||
            !viewModel.IsComplete ||
            _closing)
        {
            return;
        }

        _closing = true;
        DialogResult = true;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ViewModels.FirstRunViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnClosed(e);
    }
}
