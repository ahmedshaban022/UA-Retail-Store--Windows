using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StoreFlow.Desktop.ViewModels;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The till. The scanner is the only thing the cashier has to touch: Enter finishes a scan, and
/// the rest of the screen follows from that.
/// </summary>
public partial class PosView : UserControl
{
    /// <summary>Creates the view.</summary>
    public PosView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PosViewModel viewModel)
        {
            viewModel.RefreshHeldCartsCommand.Execute(null);
        }

        FocusScanner();
    }

    /// <summary>
    /// Handles the page being shown. The cashier who comes back to the till expects to scan
    /// straight away, so the caret goes back into the scan field every time the page is shown and
    /// not only the first time it is loaded.
    /// </summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The change that was made.</param>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusScanner));
        }
    }

    /// <summary>
    /// Handles a key in the scan field. Enter finishes the scan; everything else is left to the
    /// text box, because a barcode scanner types the code and then presses Enter.
    /// </summary>
    private void OnScanKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        if (DataContext is PosViewModel viewModel && viewModel.ScanCommand.CanExecute(null))
        {
            viewModel.ScanCommand.Execute(null);
        }

        FocusScanner();
    }

    /// <summary>Put the caret back in the scan field, so the next scan goes straight in.</summary>
    private void FocusScanner()
    {
        if (DataContext is PosViewModel)
        {
            var scanner = FindName("ScanBox") as TextBox;

            scanner?.Focus();
        }
    }
}
