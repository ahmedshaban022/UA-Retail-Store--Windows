using System.Globalization;
using System.Reflection;

namespace StoreFlow.Desktop.Localization;

/// <summary>A language the application can run in.</summary>
/// <param name="Code">The culture code, for example <c>ar</c> or <c>en</c>.</param>
/// <param name="NativeName">The language's own name, shown in the language picker.</param>
public sealed record LanguageOption(string Code, string NativeName)
{
    /// <summary>
    /// The language's own name.
    /// </summary>
    /// <remarks>
    /// A select shows the selected item's own text. Without this override the closed control would
    /// show the record's generated <c>ToString</c>, which reads as
    /// <c>LanguageOption { Code = ar, NativeName = ... }</c> instead of the language name.
    /// </remarks>
    public override string ToString() => NativeName;
}

/// <summary>
/// Switches the interface language at run time. The resource sets are satellite assemblies, so a
/// change is instant and does not disturb the open windows beyond the text that changes.
/// </summary>
public sealed class LanguageService
{
    private static readonly LanguageOption[] Options =
    [
        new("ar", "العربية"),
        new("en", "English"),
    ];

    private readonly List<WeakListener> _listeners = [];
    private readonly object _gate = new();

    private string _currentLanguage = "ar";

    /// <summary>Creates the service and applies the default language's culture.</summary>
    /// <remarks>
    /// The culture has to be in place before any view model reads a string, because the view
    /// models take their labels at construction time. Applying it here means the very first
    /// screen is already in the right language.
    /// </remarks>
    public LanguageService() => ApplyCulture(_currentLanguage);

    /// <summary>Raised after the language changes.</summary>
    /// <remarks>
    /// The listeners are held weakly, and that is the whole point of the event.
    /// <para>
    /// A screen is built fresh every time the owner opens it, and this service lives for as long
    /// as the programme does. A plain event would therefore keep every screen the owner has ever
    /// opened alive for the rest of the session, and every change of language would go on
    /// refreshing screens nobody is looking at — which is database work for nothing, on a shop
    /// computer that is already busy.
    /// </para>
    /// <para>
    /// Holding the listener weakly means the service forgets a screen as soon as the screen
    /// itself is gone. A listener that is still there is still told.
    /// </para>
    /// </remarks>
    public event EventHandler? LanguageChanged
    {
        add
        {
            if (value is null)
            {
                return;
            }

            lock (_gate)
            {
                _listeners.Add(new WeakListener(value));
            }
        }

        remove
        {
            if (value is null)
            {
                return;
            }

            lock (_gate)
            {
                for (int index = 0; index < _listeners.Count; index++)
                {
                    if (_listeners[index].Matches(value))
                    {
                        _listeners.RemoveAt(index);
                        return;
                    }
                }
            }
        }
    }

    /// <summary>The languages the application offers.</summary>
    public static IReadOnlyList<LanguageOption> Available => Options;

    /// <summary>The language currently in use.</summary>
    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage == value)
            {
                return;
            }

            _currentLanguage = value;
            ApplyCulture(value);
            RaiseLanguageChanged();
        }
    }

    /// <summary>The culture that matches the current language.</summary>
    /// <remarks>
    /// Strings are resolved against this culture explicitly rather than against the ambient thread
    /// culture, so a screen cannot end up in a different language from the application.
    /// </remarks>
    public CultureInfo CurrentCulture => new(_currentLanguage);

    /// <summary>Sets the interface language.</summary>
    public void SetLanguage(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalised = code.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";

        // The culture is applied unconditionally: the language may already be the requested one and
        // the culture may still not have been applied to this thread.
        ApplyCulture(normalised);

        CurrentLanguage = normalised;
    }

    /// <summary>Applies a language to the current thread and to every thread created afterwards.</summary>
    public static void ApplyCulture(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        CultureInfo culture = new(code);

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    /// <summary>Tells the listeners that are still there that the language has changed.</summary>
    private void RaiseLanguageChanged()
    {
        List<WeakListener> live = [];

        lock (_gate)
        {
            List<WeakListener> gone = [];

            foreach (WeakListener listener in _listeners)
            {
                if (listener.IsAlive)
                {
                    live.Add(listener);
                }
                else
                {
                    gone.Add(listener);
                }
            }

            // A screen that has been left behind is taken off the list, so the list does not grow
            // for ever as the owner opens more and more screens.
            foreach (WeakListener listener in gone)
            {
                _listeners.Remove(listener);
            }
        }

        foreach (WeakListener listener in live)
        {
            listener.Raise(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// A listener the service does not keep alive.
    /// </summary>
    /// <remarks>
    /// What has to be held weakly is the object the handler belongs to, because that object is a
    /// screen the application builds and then throws away. The method itself is held strongly:
    /// a method does not keep its object alive, and holding it is what lets the handler be raised
    /// again for as long as the object is still there.
    /// </remarks>
    private sealed class WeakListener
    {
        private readonly WeakReference<object?>? _target;
        private readonly MethodInfo _method;
        private readonly EventHandler? _stateless;

        public WeakListener(EventHandler handler)
        {
            _method = handler.Method;
            object? target = handler.Target;

            if (target is null)
            {
                // A handler that belongs to no object keeps nothing alive, so it can be held as it is.
                _stateless = handler;
            }
            else
            {
                _target = new WeakReference<object?>(target);
            }
        }

        /// <summary>Whether the object behind the handler is still there.</summary>
        public bool IsAlive => _stateless is not null || _target!.TryGetTarget(out _);

        /// <summary>Calls the handler, if the object behind it is still there.</summary>
        public void Raise(object? sender, EventArgs e)
        {
            if (_stateless is not null)
            {
                _stateless(sender, e);
                return;
            }

            if (_target!.TryGetTarget(out object? target))
            {
                EventHandler handler = (EventHandler)_method.CreateDelegate(typeof(EventHandler), target);
                handler(sender, e);
            }
        }

        /// <summary>Whether this listener is the one behind the given handler.</summary>
        public bool Matches(EventHandler handler)
        {
            if (_method != handler.Method)
            {
                return false;
            }

            if (_stateless is not null)
            {
                return handler.Target is null;
            }

            return handler.Target is not null
                && _target!.TryGetTarget(out object? target)
                && ReferenceEquals(target, handler.Target);
        }
    }
}
