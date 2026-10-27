using System.Windows;
using System.Windows.Controls;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Makes a <see cref="PasswordBox"/> bindable.
/// </summary>
/// <remarks>
/// <para><see cref="PasswordBox.Password"/> is a plain CLR property, not a dependency property, so XAML
/// refuses to bind to it ("A 'Binding' cannot be set on the 'Password' property"). This attached property
/// bridges that gap: the view model pushes its value in, and every keystroke is pushed back out, without
/// the view having to know about the view model at all.</para>
/// <para>The password is deliberately never read back from the control into a plain string property on a
/// view model that also keeps it in memory longer than needed; the view models in this product clear their
/// password field as soon as it is no longer needed.</para>
/// </remarks>
public static class PasswordBoxBehavior
{
    /// <summary>The attached property that carries the bound password.</summary>
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password",
        typeof(string),
        typeof(PasswordBoxBehavior),
        new FrameworkPropertyMetadata(
            // The default must not be the empty string. A binding that transfers an empty value
            // over an equally empty default changes nothing, so the property-changed callback never
            // runs, the PasswordChanged handler is never attached, and every keystroke is lost.
            // A null default guarantees the first bind is a real change.
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnPasswordPropertyChanged),
        null);

    /// <summary>Sets the bound password.</summary>
    public static void SetPassword(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PasswordProperty, value);
    }

    /// <summary>Gets the bound password.</summary>
    public static string? GetPassword(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(PasswordProperty);
    }

    /// <summary>Re-applies the bound value to the control and hooks the change event.</summary>
    private static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        box.PasswordChanged -= OnBoxPasswordChanged;
        box.PasswordChanged += OnBoxPasswordChanged;

        string incoming = e.NewValue as string ?? string.Empty;

        if (!string.Equals(box.Password, incoming, StringComparison.Ordinal))
        {
            box.Password = incoming;
        }
    }

    /// <summary>Pushes what the user typed back to the bound source.</summary>
    private static void OnBoxPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        string typed = box.Password;

        if (string.Equals(GetPassword(box), typed, StringComparison.Ordinal))
        {
            // The bound value already agrees with what is on screen — this is the model pushing a
            // value down, not the owner typing.
            return;
        }

        // SetValue on a property with a two-way binding updates the source and leaves the binding
        // in place. SetCurrentValue does neither: it leaves the effective value alone, so the view
        // model would never hear what the owner typed.
        box.SetValue(PasswordProperty, typed);
    }
}
