using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using StoreFlow.Desktop.ViewModels;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// The reports screen. The columns of the grid are built from the report that is on screen, because
/// every report has its own columns; the values themselves are bound to the rows the report reader
/// produced, so the screen and the exported file cannot disagree.
/// </summary>
public partial class ReportsView : UserControl
{
    private ReportsViewModel? _viewModel;

    /// <summary>Creates the screen.</summary>
    public ReportsView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Columns.CollectionChanged -= OnColumnsChanged;
        }

        _viewModel = e.NewValue as ReportsViewModel;

        if (_viewModel is not null)
        {
            _viewModel.Columns.CollectionChanged += OnColumnsChanged;
        }

        RebuildColumns();
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildColumns();

    /// <summary>Builds the grid's columns from the report on screen.</summary>
    private void RebuildColumns()
    {
        ReportGrid.Columns.Clear();

        if (_viewModel is null)
        {
            return;
        }

        foreach (ReportColumnView column in _viewModel.Columns)
        {
            Binding binding = new(column.PropertyName)
            {
                Mode = BindingMode.OneWay,
                Converter = ConverterFor(column.Kind),
            };

            var textColumn = new DataGridTextColumn
            {
                Header = column.Header,
                Binding = binding,
                Width = column.Kind == ReportColumnKind.Text
                    ? DataGridLength.SizeToHeader
                    : new DataGridLength(120),
                MinWidth = 80,
                ElementStyle = (Style)FindResource("Type.Numeric"),
            };

            if (column.Kind == ReportColumnKind.Text)
            {
                textColumn.ElementStyle = (Style)FindResource("Type.LeftToRight");
            }

            ReportGrid.Columns.Add(textColumn);
        }
    }

    /// <summary>The converter that writes the values of a column the way they are meant to be read.</summary>
    private IValueConverter? ConverterFor(ReportColumnKind kind) => kind switch
    {
        ReportColumnKind.Money => (IValueConverter?)FindResource("Money"),
        ReportColumnKind.Timestamp => (IValueConverter?)FindResource("Timestamp"),
        _ => null,
    };
}
