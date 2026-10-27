using System.Windows;
using StoreFlow.Application.Theming;

namespace StoreFlow.Desktop.Theming;

/// <summary>
/// The live theme dictionary. It is a placeholder in XAML that the theme engine fills in at run
/// time, so a theme change repaints the whole application without recreating the resource tree.
/// </summary>
public sealed class ThemeResourceDictionary : ResourceDictionary
{
    /// <summary>Replaces the contents of this dictionary with the given one.</summary>
    public void Replace(ResourceDictionary source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Clear();

        foreach (object key in source.Keys)
        {
            this[key] = source[key];
        }
    }

    /// <summary>Builds and installs a theme in one step.</summary>
    public void Install(ThemeDefinition theme) => Replace(ThemeEngine.Build(theme));
}
