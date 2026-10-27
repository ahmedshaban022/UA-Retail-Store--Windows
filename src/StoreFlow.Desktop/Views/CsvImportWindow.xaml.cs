using System.Windows;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The CSV import window. It closes itself once the import has run, or once the owner has walked
/// away from it.
/// </summary>
public partial class CsvImportWindow : Window
{
    private bool _closing;

    /// <summary>Creates the window.</summary>
    public CsvImportWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);

        if (DataContext is ViewModels.CsvImportViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, bool imported)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        DialogResult = imported;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ViewModels.CsvImportViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }
}
