using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.Theming;

namespace StoreFlow.Desktop.Views;

/// <summary>The application shell window.</summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowDirection.Watch(this);
    }

    /// <summary>Applies the reading direction that matches the current language.</summary>
    public void ApplyDirection()
    {
        WindowDirection.Apply(this);
    }
}
