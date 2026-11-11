using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Common;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The recovery wizard's own behaviour. This screen exists for the worst day in the shop's life,
/// so a failure inside it has to become a message on the screen rather than an exception that
/// takes the programme down and leaves the owner with neither the shop nor the tool that could
/// bring it back.
/// </summary>
public sealed class RecoveryWizardTests
{
    [Fact]
    public async Task A_restore_that_throws_is_shown_to_the_owner_instead_of_ending_the_programme()
    {
        var wizard = new RecoveryWizardViewModel(
            new ThrowingRecoverHandler(),
            new NoDialogs(),
            new TestStrings(),
            new LanguageService());

        wizard.ChosenPath = "some-backup.sfb";

        await wizard.RestoreCommand.ExecuteAsync(null);

        wizard.StatusMessage.ShouldNotBeNullOrWhiteSpace();
        wizard.IsRestored.ShouldBeFalse();
        wizard.RequiresRestart.ShouldBeFalse();
        wizard.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task A_restore_that_fails_is_shown_to_the_owner()
    {
        var wizard = new RecoveryWizardViewModel(
            new RefusingRecoverHandler(),
            new NoDialogs(),
            new TestStrings(),
            new LanguageService());

        wizard.ChosenPath = "some-backup.sfb";

        await wizard.RestoreCommand.ExecuteAsync(null);

        wizard.StatusMessage.ShouldNotBeNullOrWhiteSpace();
        wizard.IsRestored.ShouldBeFalse();
        wizard.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task A_restore_with_no_file_chosen_says_so()
    {
        var wizard = new RecoveryWizardViewModel(
            new RefusingRecoverHandler(),
            new NoDialogs(),
            new TestStrings(),
            new LanguageService());

        await wizard.RestoreCommand.ExecuteAsync(null);

        wizard.StatusMessage.ShouldNotBeNullOrWhiteSpace();
        wizard.IsBusy.ShouldBeFalse();
    }

    /// <summary>A recover handler that fails the way a damaged database does.</summary>
    private sealed class ThrowingRecoverHandler : ICommandHandler<RecoverStoreCommand, Result<RestoreOutcome>>
    {
        public Task<Result<RestoreOutcome>> HandleAsync(RecoverStoreCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SQLite Error 26: 'file is not a database'.");
    }

    /// <summary>A recover handler that answers with a refusal.</summary>
    private sealed class RefusingRecoverHandler : ICommandHandler<RecoverStoreCommand, Result<RestoreOutcome>>
    {
        public Task<Result<RestoreOutcome>> HandleAsync(RecoverStoreCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result<RestoreOutcome>.Failure(BackupErrorCodes.RestoreFailed, "The backup could not be read."));
    }

    /// <summary>Dialogs that never open a window.</summary>
    private sealed class NoDialogs : IBackupDialogs
    {
        public Task<bool> ShowRestoreWizardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ShowRecoveryWizardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<string?> PickBackupFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>
    /// A stand-in for the application's strings, so the test does not need a resource manager.
    /// </summary>
    private sealed class TestStrings : IStrings
    {
        private static readonly Dictionary<string, string> Messages = new()
        {
            [StringKeys.RestoreNothingChosen] = "Choose a backup file first.",
            [StringKeys.BackupErrorRestoreFailed] = "The backup could not be restored.",
            [StringKeys.ErrorUnexpected] = "Something went wrong. Try again.",
        };

        public string this[string key] => Messages.TryGetValue(key, out string? message) ? message : key;

        public string Format(string key, params object?[] arguments) => string.Format(this[key], arguments);
    }
}
