using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Checks how a WPF picker writes the chosen entry back into the view model.
///
/// WPF can be told to write the chosen value back to its source only when the picker loses the focus.
/// Inside a table cell that event may never arrive, so a line of a purchase or of a stock count
/// never learns which product was chosen and the document cannot be saved. The bindings therefore
/// carry <c>UpdateSourceTrigger=PropertyChanged</c>, and these tests hold that in place.
/// </summary>
/// <remarks>
/// <para><b>Every test in this file owns the keyboard focus.</b> WPF pushes the chosen value to the
/// source on the selection itself when the picker happens to hold the focus, and holds it back when
/// the picker does not, so a test that reads the focus state of the machine is a test that fails
/// sometimes. Each test below states where the focus is, checks it, and produces the focus changes
/// it needs on purpose.</para>
/// </remarks>
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
    /// belongs to. This is the shape the purchase lines and the stock count lines use, and the
    /// choice is made the way a person makes it: by picking an entry from the open drop-down.
    /// </summary>
    [Fact]
    public void A_picker_in_a_table_cell_writes_the_choice_back()
    {
        RunOnAStaThread(() =>
        {
            var row = new Row();

            CellHarness harness = BuildCellPicker(row, immediate: true);

            try
            {
                Pump();

                ChooseFromTheDropDown(harness.Picker);

                row.ProductId.ShouldBe(1, "the chosen identifier must reach the row");
            }
            finally
            {
                harness.Window.Close();
            }
        });
    }

    /// <summary>
    /// A picker in a table cell writes the choice back whether or not it holds the keyboard focus.
    /// </summary>
    /// <remarks>
    /// The second half of this is the one that matters: the focus of a cell is not the focus of the
    /// shopkeeper's hands. A binding that writes back only when the focus happens to leave would
    /// leave the row empty whenever the picker did not hold the focus when the choice was made.
    /// </remarks>
    [Fact]
    public void A_picker_in_a_table_cell_writes_the_choice_back_without_the_focus()
    {
        RunOnAStaThread(() =>
        {
            var row = new Row();

            CellHarness harness = BuildCellPicker(row, immediate: true);

            try
            {
                Pump();

                MoveTheFocusToTheSink(harness);

                harness.Picker.IsKeyboardFocusWithin.ShouldBeFalse(
                    "the picker must not hold the keyboard focus for this control to mean anything");

                harness.Picker.SelectedItem = harness.Picker.Items[0];

                Pump();

                row.ProductId.ShouldBe(1, "the chosen identifier must reach the row");
            }
            finally
            {
                harness.Window.Close();
            }
        });
    }

    /// <summary>
    /// A picker in a table cell that waits for the focus writes nothing back when the choice is
    /// made, and writes the choice back only once the focus has actually left it. This is the
    /// negative control: it is why the bindings say to write at once.
    /// </summary>
    /// <remarks>
    /// <para>The control is deterministic because the test owns the focus. It states that the picker
    /// does not hold the keyboard focus, and it checks that it does not, so the first assertion can
    /// only be explained by the binding's trigger. It then produces the focus loss itself instead of
    /// waiting for the machine to produce one.</para>
    /// </remarks>
    [Fact]
    public void A_picker_in_a_table_cell_that_waits_for_the_focus_writes_nothing_back()
    {
        RunOnAStaThread(() =>
        {
            var row = new Row();

            CellHarness harness = BuildCellPicker(row, immediate: false);

            try
            {
                Pump();

                MoveTheFocusToTheSink(harness);

                harness.Picker.IsKeyboardFocusWithin.ShouldBeFalse(
                    "the picker must not hold the keyboard focus for this control to mean anything");

                harness.Picker.SelectedItem = harness.Picker.Items[0];

                Pump();

                row.ProductId.ShouldBe(0, "a picker that waits for the focus leaves the row empty");

                // The focus loss the binding is waiting for is produced here, on purpose.
                Keyboard.Focus(harness.Picker);

                Pump();

                Keyboard.Focus(harness.FocusSink);

                Pump();

                row.ProductId.ShouldBe(1, "the choice must reach the row once the focus has left the picker");
            }
            finally
            {
                harness.Window.Close();
            }
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

    /// <summary>
    /// The window the cell picker lives in, the picker itself, and a field the test can move the
    /// keyboard focus to.
    /// </summary>
    private sealed record CellHarness(ComboBox Picker, Window Window, TextBox FocusSink);

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
    private static CellHarness BuildCellPicker(Row row, bool immediate)
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

        // A field the test can move the keyboard focus to, so that a focus loss is something the
        // test produces rather than something the machine decides.
        var focusSink = new TextBox { Width = 10 };

        var panel = new StackPanel();

        panel.Children.Add(grid);
        panel.Children.Add(focusSink);

        var window = new Window { Content = panel, Width = 400, Height = 300 };
        window.Show();

        Pump();

        row.Products.Add(new Option(1, "RICE-001 - Rice 1kg"));

        Pump();
        grid.UpdateLayout();
        Pump();

        DataGridRow rowContainer = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0)!;

        return new CellHarness(Picker(rowContainer), window, focusSink);
    }

    /// <summary>
    /// Picks an entry the way a person picks one: by choosing it in the open drop-down.
    /// </summary>
    private static void ChooseFromTheDropDown(ComboBox picker)
    {
        picker.IsDropDownOpen = true;

        Pump();

        picker.ApplyTemplate();
        picker.UpdateLayout();

        ComboBoxItem? container = picker.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem;

        container.ShouldNotBeNull("the picker must have produced an entry to choose from");

        container.IsSelected = true;

        Pump();
    }

    /// <summary>
    /// Puts the keyboard focus on the sink, so that the picker is left without it.
    /// </summary>
    private static void MoveTheFocusToTheSink(CellHarness harness)
    {
        Keyboard.Focus(harness.FocusSink);

        Pump();
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
