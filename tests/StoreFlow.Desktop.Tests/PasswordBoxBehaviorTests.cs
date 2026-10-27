using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using StoreFlow.Desktop.Controls;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Covers the bindable password field.
/// </summary>
/// <remarks>
/// WPF's <see cref="PasswordBox"/> cannot be bound directly, so the product uses an attached
/// behaviour instead. The behaviour has to carry a keystroke to the view model without swallowing
/// the binding, which is exactly the kind of thing that breaks silently — so it is pinned here.
/// </remarks>
public sealed class PasswordBoxBehaviorTests
{
    [Fact]
    public void Typing_in_a_password_box_updates_the_bound_view_model()
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

            viewModel.Password.ShouldBe("StoreFlow!2026");
        });
    }

    [Fact]
    public void A_value_set_on_the_view_model_reaches_the_password_box()
    {
        RunOnStaThread(() =>
        {
            var viewModel = new TestViewModel();
            var box = new PasswordBox { DataContext = viewModel };

            BindingOperations.SetBinding(
                box,
                PasswordBoxBehavior.PasswordProperty,
                new Binding(nameof(TestViewModel.Password)) { Mode = BindingMode.TwoWay });

            viewModel.Password = "from-the-model";

            box.Password.ShouldBe("from-the-model");
        });
    }

    [Fact]
    public void Clearing_the_password_box_clears_the_bound_value()
    {
        RunOnStaThread(() =>
        {
            var viewModel = new TestViewModel { Password = "StoreFlow!2026" };
            var box = new PasswordBox { DataContext = viewModel };

            BindingOperations.SetBinding(
                box,
                PasswordBoxBehavior.PasswordProperty,
                new Binding(nameof(TestViewModel.Password)) { Mode = BindingMode.TwoWay });

            box.Password = string.Empty;

            viewModel.Password.ShouldBeEmpty();
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
