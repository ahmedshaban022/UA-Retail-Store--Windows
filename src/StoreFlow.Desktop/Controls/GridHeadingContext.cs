using System.Windows;
using System.Windows.Controls;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Gives a table's column headings the words they have to show.
/// </summary>
/// <remarks>
/// <para>A column of a data grid is not part of the visual tree, so a heading cannot simply
/// inherit the context of the page the way everything else on the screen does. WPF hands the
/// heading a context of its own, and a heading that reads from that context finds nothing - the
/// column heading comes out blank.</para>
/// <para>Handing the heading the table's own context settles it. It has to be done when the table
/// is realised, because a table that sits on a tab the user opens later is realised long after the
/// heading was written down, and a heading that read too early never reads again.</para>
/// </remarks>
public static class GridHeadingContext
{
    /// <summary>Turns the behaviour on for a table.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(GridHeadingContext),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Turns the behaviour on for a table.</summary>
    /// <param name="element">The table.</param>
    /// <param name="value">Whether the behaviour is on.</param>
    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(IsEnabledProperty, value);
    }

    /// <summary>Whether the behaviour is on for a table.</summary>
    /// <param name="element">The table.</param>
    /// <returns>Whether the behaviour is on.</returns>
    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(IsEnabledProperty);
    }

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not DataGrid grid)
        {
            return;
        }

        grid.Loaded += OnTableRealised;
        grid.DataContextChanged += OnTableContextChanged;

        HandTheWords(grid);
    }

    private static void OnTableRealised(object sender, RoutedEventArgs e)
    {
        HandTheWords((DataGrid)sender);
    }

    private static void OnTableContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        HandTheWords((DataGrid)sender);
    }

    private static void HandTheWords(DataGrid grid)
    {
        foreach (DataGridColumn column in grid.Columns)
        {
            if (column.Header is FrameworkElement heading)
            {
                heading.DataContext = grid.DataContext;
            }
        }
    }
}
