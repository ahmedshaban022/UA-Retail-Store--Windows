using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using StoreFlow.Desktop.Controls;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// A map of how WPF actually behaves when a password is bound, written down as tests.
/// </summary>
/// <remarks>
/// The bindable password field is built on two facts that are easy to get wrong and that fail
/// silently when they do:
/// <list type="bullet">
/// <item>setting the attached property with <c>SetValue</c> updates a two-way bound source and
/// leaves the binding in place;</item>
/// <item>the attached property's default value must not equal the value a binding typically
/// transfers, because a transfer that changes nothing never runs the property-changed callback —
/// and that callback is what attaches the keystroke handler.</item>
/// </list>
/// The first implementation of this product got the second one wrong: the password box filled in
/// perfectly, the setup appeared to run, and the view model never received a single character.
/// </remarks>
public sealed class PasswordDiagnostics
{
    [Fact]
    public void Set_value_updates_a_two_way_bound_source()
    {
        RunOnStaThread(() =>
        {
            var viewModel = new TestViewModel();
            var box = new PasswordBox { DataContext = viewModel };

            BindingOperations.SetBinding(
                box,
                PasswordBoxBehavior.PasswordProperty,
                new Binding(nameof(TestViewModel.Password)) { Mode = BindingMode.TwoWay });

            box.Password = "StoreFlow!2026";
            box.SetValue(PasswordBoxBehavior.PasswordProperty, box.Password);

            viewModel.Password.ShouldBe("StoreFlow!2026");
        });
    }

    [Fact]
    public void Set_value_leaves_the_binding_in_place()
    {
        RunOnStaThread(() =>
        {
            var viewModel = new TestViewModel();
            var box = new PasswordBox { DataContext = viewModel };

            BindingOperations.SetBinding(
                box,
                PasswordBoxBehavior.PasswordProperty,
                new Binding(nameof(TestViewModel.Password)) { Mode = BindingMode.TwoWay });

            box.Password = "StoreFlow!2026";
            box.SetValue(PasswordBoxBehavior.PasswordProperty, box.Password);

            BindingOperations.GetBindingExpression(box, PasswordBoxBehavior.PasswordProperty)
                .ShouldNotBeNull();

            // The model must still be able to push a value back down afterwards.
            viewModel.Password = "from-the-model";
            box.Password.ShouldBe("from-the-model");
        });
    }

    [Fact]
    public void The_default_value_is_null_so_the_first_bind_is_a_real_change()
    {
        RunOnStaThread(() =>
        {
            // If the default matched the value a binding transfers, the property-changed callback
            // would never run, the keystroke handler would never be attached, and the password
            // would silently never reach the view model.
            var box = new PasswordBox();

            box.GetValue(PasswordBoxBehavior.PasswordProperty).ShouldBeNull();
        });
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

    private sealed class TestViewModel : INotifyPropertyChanged
    {
        private string _password = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Password
        {
            get => _password;
            set
            {
                if (_password == value)
                {
                    return;
                }

                _password = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Password)));
            }
        }
    }
}
