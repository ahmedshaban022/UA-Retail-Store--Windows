using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Checks how a WPF picker writes the chosen entry back into the view model.
///
/// WPF writes the chosen value back to its source only when the picker loses the focus. Inside a
/// table cell that never happens, so a line of a purchase or of a stock count never learns which
/// product was chosen and the document cannot be saved. The bindings therefore carry
/// <c>UpdateSourceTrigger=PropertyChanged</c>, and these tests hold that in place.
/// </summary>
public sealed class ComboBoxBindingTests
{
    /// <summary>
    /// A picker bound by identifier writes the chosen identifier back to the source.
    /// </summary>
    [Fact]
    public void A_picker_bound_by_identifier_writes_the_choice_back()
    {
        RunOnAStaThread(() =>
        {
            var source = new Source();
            var combo = BuildPicker(source, source.Products);

            Pump();

            combo.SelectedItem = source.Products[0];

            Pump();

            source.ProductId.ShouldBe(1, "the chosen identifier must reach the view model");
        });
    }

    /// <summary>
    /// A picker bound by the entry itself writes the chosen entry back to the source.
    /// </summary>
    [Fact]
    public void A_picker_bound_by_entry_writes_the_choice_back()
    {
        RunOnAStaThread(() =>
        {
            var source = new Source();
            var combo = BuildPicker(source, source.Products);

            BindingOperations.SetBinding(
                combo,
                ComboBox.SelectedItemProperty,
                new Binding(nameof(Source.Product)) { Mode = BindingMode.TwoWay });

            Pump();

            combo.SelectedItem = source.Products[0];

            Pump();

            source.Product.ShouldNotBeNull("the chosen entry must reach the view model");
            source.Product!.Id.ShouldBe(1);
        });
    }

    /// <summary>
    /// A picker in a table cell that is bound by identifier writes the choice back to the row it
    /// belongs to. This is the shape the purchase lines and the stock count lines use.
    /// </summary>
    [Fact]
    public void A_picker_in_a_table_cell_writes_the_choice_back()
    {
        RunOnAStaThread(() =>
        {
            Row row = new Row();

            ComboBox picker = BuildCellPicker(row, immediate: true);

            Pump();

            picker.IsDropDownOpen = true;

            Pump();

            ComboBoxItem container = Container(picker);

            container.IsSelected = true;

            Pump();

            row.ProductId.ShouldBe(1, "the chosen identifier must reach the row");
        });
    }

    /// <summary>
    /// A picker in a table cell that waits for the focus to leave writes nothing back, because the
    /// focus never leaves the cell. This is why the bindings say to write at once.
    /// </summary>
    [Fact]
    public void A_picker_in_a_table_cell_that_waits_for_the_focus_writes_nothing_back()
    {
        RunOnAStaThread(() =>
        {
            Row row = new Row();

            ComboBox picker = BuildCellPicker(row, immediate: false);

            Pump();

            picker.IsDropDownOpen = true;

            Pump();

            ComboBoxItem container = Container(picker);

            container.IsSelected = true;

            Pump();

            row.ProductId.ShouldBe(0, "a picker that waits for the focus leaves the row empty");
        });
    }

    /// <summary>The source of the binding.</summary>
    private sealed class Source
    {
        public int ProductId { get; set; }

        public Option? Product { get; set; }

        public ObservableCollection<Option> Products { get; } = [];
    }

    /// <summary>One entry in the picker.</summary>
    private sealed record Option(int Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>One row of the table.</summary>
    private sealed class Row
    {
        public int ProductId { get; set; }

        public ObservableCollection<Option> Products { get; } = [];
    }

    private static ComboBox BuildPicker(Source source, ObservableCollection<Option> items)
    {
        var combo = new ComboBox
        {
            DisplayMemberPath = nameof(Option.DisplayName),
            SelectedValuePath = nameof(Option.Id),
            ItemsSource = items,
            DataContext = source,
        };

        BindingOperations.SetBinding(
            combo,
            ComboBox.SelectedValueProperty,
            new Binding(nameof(Source.ProductId))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });

        items.Add(new Option(1, "RICE-001 - Rice 1kg"));
        items.Add(new Option(2, "OIL-001 - Olive Oil 1L"));

        return combo;
    }

    /// <summary>
    /// Builds the table the purchase lines and the stock count lines use: a picker inside a cell,
    /// bound to the row it belongs to.
    /// </summary>
    private static ComboBox BuildCellPicker(Row row, bool immediate)
    {
        var rows = new ObservableCollection<Row> { row };

        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false };

        var column = new DataGridTemplateColumn();

        var template = new DataTemplate();

        FrameworkElementFactory combo = new FrameworkElementFactory(typeof(ComboBox));
        combo.SetBinding(ComboBox.ItemsSourceProperty, new Binding("Products"));
        combo.SetBinding(
            ComboBox.SelectedValueProperty,
            new Binding("ProductId")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = immediate
                    ? UpdateSourceTrigger.PropertyChanged
                    : UpdateSourceTrigger.LostFocus,
            });
        combo.SetValue(ComboBox.DisplayMemberPathProperty, "DisplayName");
        combo.SetValue(ComboBox.SelectedValuePathProperty, "Id");

        template.VisualTree = combo;

        column.CellTemplate = template;

        grid.Columns.Add(column);

        var window = new Window { Content = grid, Width = 400, Height = 300 };
        window.Show();

        Pump();

        row.Products.Add(new Option(1, "RICE-001 - Rice 1kg"));

        Pump();
        grid.UpdateLayout();
        Pump();

        DataGridRow rowContainer = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0)!;

        return Picker(rowContainer);
    }

    private static ComboBoxItem Container(ComboBox combo)
    {
        combo.ApplyTemplate();
        combo.UpdateLayout();

        ComboBoxItem? container = combo.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem;

        container.ShouldNotBeNull("the picker must have produced an entry to choose from");

        return container;
    }

    private static ComboBox Picker(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);

            if (child is ComboBox combo)
            {
                return combo;
            }

            ComboBox? found = Picker(child);

            if (found is not null)
            {
                return found;
            }
        }

        throw new InvalidOperationException("The table holds no picker.");
    }

    private static void RunOnAStaThread(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // The failure has to travel back to the test thread.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
