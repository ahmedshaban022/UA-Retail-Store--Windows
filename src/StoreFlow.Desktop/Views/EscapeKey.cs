using System.Windows;
using System.Windows.Input;

namespace StoreFlow.Desktop.Views;

/// <summary>
/// Lets a window be dismissed with the Escape key.
///
/// The key does not close the window by itself. It runs the same command the Cancel button runs,
/// so the window is dismissed exactly the way the clerk expects: the reason is not given, the
/// count is not saved, the import is abandoned.
///
/// The button itself is left alone on purpose. WPF's own <c>IsCancel</c> closes the window as
/// well as raising the click, and these windows set their <c>DialogResult</c> on the way out.
/// Closing twice is a fault, so the key is wired to the command instead.
/// </summary>
public static class EscapeKey
{
    /// <summary>The command run when Escape is pressed.</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(EscapeKey),
        new PropertyMetadata(null, OnCommandChanged));

    /// <summary>Sets the command that Escape runs.</summary>
    /// <param name="element">The window.</param>
    /// <param name="value">The command.</param>
    public static void SetCommand(DependencyObject element, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(CommandProperty, value);
    }

    /// <summary>Reads the command that Escape runs.</summary>
    /// <param name="element">The window.</param>
    /// <returns>The command.</returns>
    public static ICommand? GetCommand(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (ICommand?)element.GetValue(CommandProperty);
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
        {
            return;
        }

        window.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is ICommand)
        {
            window.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled)
        {
            return;
        }

        if (sender is not Window window)
        {
            return;
        }

        if (window.GetValue(CommandProperty) is not ICommand command || !command.CanExecute(null))
        {
            return;
        }

        e.Handled = true;

        command.Execute(null);
    }
}
