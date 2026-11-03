using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StoreFlow.Desktop.Views;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Shows the backup wizards and the file picker. The view models ask for a window; this decides
/// what that looks like, which keeps the windows out of the screen logic.
/// </summary>
public interface IBackupDialogs
{
    /// <summary>
    /// Opens the restore wizard.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the shop was restored.</returns>
    Task<bool> ShowRestoreWizardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the recovery wizard.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the shop was restored.</returns>
    Task<bool> ShowRecoveryWizardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the owner for a backup file from anywhere on this computer.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The full path of the chosen file, or null when nothing was chosen.</returns>
    Task<string?> PickBackupFileAsync(CancellationToken cancellationToken = default);
}

/// <summary>Shows the backup wizards.</summary>
public sealed class BackupDialogs(IServiceProvider services) : IBackupDialogs
{
    /// <inheritdoc />
    public async Task<bool> ShowRestoreWizardAsync(CancellationToken cancellationToken = default)
    {
        RestoreWizardViewModel viewModel = services.GetRequiredService<RestoreWizardViewModel>();

        await viewModel.RefreshAsync(cancellationToken);

        var window = new RestoreWizardWindow { DataContext = viewModel };
        Prepare(window);

        return window.ShowDialog() == true;
    }

    /// <inheritdoc />
    public async Task<bool> ShowRecoveryWizardAsync(CancellationToken cancellationToken = default)
    {
        RecoveryWizardViewModel viewModel = services.GetRequiredService<RecoveryWizardViewModel>();

        var window = new RecoveryWizardWindow { DataContext = viewModel };
        Prepare(window);

        return window.ShowDialog() == true;
    }

    /// <inheritdoc />
    public Task<string?> PickBackupFileAsync(CancellationToken cancellationToken = default)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a StoreFlow backup",
            Filter = "StoreFlow backup (*.sfb)|*.sfb|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    private static void Prepare(Window window)
    {
        if (System.Windows.Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }
    }
}
