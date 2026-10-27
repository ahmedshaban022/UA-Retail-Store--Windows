using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Shouldly;
using StoreFlow.Desktop.ViewModels;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// A map of how WPF behaves when a field offers a choice, written down as tests.
/// </summary>
/// <remarks>
/// <para>WPF matches a bound value against a list by converting the value to the type of the item's
/// key first. When the field holds text and the key is a number, an empty value cannot be converted
/// and the list throws while the window is being built.</para>
/// <para>The first version of the unit editor keyed its precision choices by number, and the editor
/// window fell over the moment it was opened. The window never appeared, the application reported
/// the failure, and every screen behind it stopped answering.</para>
/// </remarks>
public sealed class ReferenceEditorChoiceTests
{
    [Fact]
    public void A_choice_field_with_nothing_chosen_yet_can_be_built()
    {
        RunOnStaThread(() =>
        {
            var field = new EditorField(
                "precision",
                "Precision",
                EditorFieldKind.Choice,
                [new EditorChoice("0", "0"), new EditorChoice("3", "3")]);

            var combo = BuildChoice(field);

            // The list is built, and the field is still empty: nothing was thrown on the way.
            combo.ShouldNotBeNull();
            field.Value.ShouldBe(string.Empty);
        });
    }

    [Fact]
    public void A_choice_field_keeps_the_value_it_was_given()
    {
        RunOnStaThread(() =>
        {
            var field = new EditorField(
                "precision",
                "Precision",
                EditorFieldKind.Choice,
                [new EditorChoice("0", "0"), new EditorChoice("3", "3")]);
            field.Value = "3";

            ComboBox combo = BuildChoice(field);

            combo.SelectedValue.ShouldBe("3");
        });
    }

    [Fact]
    public void A_choice_shows_its_name_rather_than_the_record_behind_it()
    {
        RunOnStaThread(() =>
        {
            var field = new EditorField(
                "precision",
                "Precision",
                EditorFieldKind.Choice,
                [new EditorChoice("0", "0")]);

            ComboBox combo = BuildChoice(field);

            combo.Items.Cast<EditorChoice>().Single().ToString().ShouldBe("0");
            combo.Text.ShouldBe(string.Empty);
        });
    }

    [Fact]
    public void A_list_keyed_by_a_number_cannot_hold_an_empty_text_value()
    {
        RunOnStaThread(() =>
        {
            // This is the shape that broke the editor, written down so that the trap stays visible.
            // The value is text and the key is a number, so the list tries to read "" as a number and
            // throws while the window is being built.
            var field = new EditorField("precision", "Precision", EditorFieldKind.Choice);

            Should.Throw<FormatException>(() =>
            {
                var combo = new ComboBox
                {
                    ItemsSource = new[] { new NumberKeyedChoice(0) },
                    SelectedValuePath = "Id",
                };

                BindingOperations.SetBinding(
                    combo,
                    ComboBox.SelectedValueProperty,
                    new Binding(nameof(EditorField.Value))
                    {
                        Source = field,
                        Mode = BindingMode.TwoWay,
                    });

                combo.Measure(new Size(300, 60));
                combo.UpdateLayout();
            });
        });
    }

    /// <summary>Builds the list the way the editor's template builds it.</summary>
    private static ComboBox BuildChoice(EditorField field)
    {
        var combo = new ComboBox
        {
            ItemsSource = field.Options,
            SelectedValuePath = "Id",
            DisplayMemberPath = "DisplayName",
        };

        BindingOperations.SetBinding(
            combo,
            ComboBox.SelectedValueProperty,
            new Binding(nameof(EditorField.Value)) { Source = field, Mode = BindingMode.TwoWay });

        combo.Measure(new Size(300, 60));
        combo.UpdateLayout();

        return combo;
    }

    /// <summary>Runs an action on a single-threaded-apartment thread, as WPF requires.</summary>
    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // The exception is captured and rethrown on the calling thread.
            catch (Exception exception)
            {
                failure = exception;
            }
#pragma warning restore CA1031
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    /// <summary>A choice keyed by a number, which is the shape that broke the editor.</summary>
    private sealed record NumberKeyedChoice(int Id)
    {
        /// <inheritdoc />
        public override string ToString() => Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
