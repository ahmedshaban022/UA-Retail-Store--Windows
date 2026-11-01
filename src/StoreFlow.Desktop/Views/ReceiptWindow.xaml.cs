using System.Windows;
using StoreFlow.Desktop.Controls;
using StoreFlow.Desktop.ViewModels;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The receipt of a sale. What is shown here is drawn from the same layout the printer uses, so the
/// preview is the paper and not a picture of it.
/// </summary>
public partial class ReceiptWindow : Window
{
    /// <summary>Creates the window.</summary>
    public ReceiptWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReceiptViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }

        Draw();
    }

    /// <summary>Closes the receipt window when the view model asks for it.</summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The event arguments.</param>
    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void Draw()
    {
        if (DataContext is not ReceiptViewModel viewModel)
        {
            return;
        }

        ReceiptPreview.Fill(ReceiptCanvas, viewModel.CurrentDocument, viewModel.CurrentContentWidth);

        ReceiptCanvas.Width = viewModel.CurrentContentWidth * ReceiptPreview.UnitsPerPoint;
        ReceiptCanvas.Height = ReceiptPreview.MeasureHeight(viewModel.CurrentDocument, viewModel.CurrentContentWidth);
    }
}
