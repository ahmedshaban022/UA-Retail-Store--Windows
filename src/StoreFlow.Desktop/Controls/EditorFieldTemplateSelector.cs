using System.Windows;
using System.Windows.Controls;
using StoreFlow.Desktop.ViewModels;

namespace StoreFlow.Desktop.Controls;

/// <summary>
/// Picks the control that matches a field in the reference editor: a line of text, several lines of
/// text, or a choice from a list.
/// </summary>
public sealed class EditorFieldTemplateSelector : DataTemplateSelector
{
    /// <summary>The template used for a single line of text.</summary>
    public DataTemplate? TextTemplate { get; set; }

    /// <summary>The template used for several lines of text.</summary>
    public DataTemplate? MultilineTemplate { get; set; }

    /// <summary>The template used for a choice from a list.</summary>
    public DataTemplate? ChoiceTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        EditorField { Kind: EditorFieldKind.Text } => TextTemplate,
        EditorField { Kind: EditorFieldKind.Multiline } => MultilineTemplate,
        EditorField { Kind: EditorFieldKind.Choice } => ChoiceTemplate,
        _ => null,
    };
}
