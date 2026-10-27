using System.ComponentModel;
using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>The sign-in window. It closes itself once the user has been authenticated.</summary>
public partial class LoginWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public LoginWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.LoginViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModels.LoginViewModel.IsAuthenticated) ||
            sender is not ViewModels.LoginViewModel viewModel ||
            !viewModel.IsAuthenticated ||
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
        if (DataContext is ViewModels.LoginViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnClosed(e);
    }
}
