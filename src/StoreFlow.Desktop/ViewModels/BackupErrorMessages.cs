using StoreFlow.Application.Backup;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Turns the stable error codes the backup flow returns into the text the owner reads.
/// </summary>
/// <remarks>
/// The handlers answer with a code and an English fallback message. The interface shows the
/// localised message built from the same code, so a shop running in Arabic is never shown an
/// English sentence the rest of the screen does not speak.
/// </remarks>
public static class BackupErrorMessages
{
    /// <summary>
    /// Builds the localised message for an error code.
    /// </summary>
    /// <param name="strings">The application's strings.</param>
    /// <param name="code">The stable error code.</param>
    /// <param name="arguments">The values the message needs, such as the file that was missing.</param>
    /// <returns>The message to show.</returns>
    public static string Resolve(IStrings strings, string code, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(arguments);

        string key = KeyFor(code);

        return arguments.Length == 0
            ? strings[key]
            : strings.Format(key, arguments);
    }

    /// <summary>
    /// The resource key that carries the message for an error code.
    /// </summary>
    /// <param name="code">The stable error code.</param>
    /// <returns>The resource key.</returns>
    public static string KeyFor(string code) => code switch
    {
        BackupErrorCodes.NotFound => StringKeys.BackupErrorNotFound,
        BackupErrorCodes.IntegrityFailed => StringKeys.BackupErrorIntegrityFailed,
        BackupErrorCodes.SchemaMismatch => StringKeys.BackupErrorSchemaMismatch,
        BackupErrorCodes.TargetUnreachable => StringKeys.BackupErrorTargetUnreachable,
        BackupErrorCodes.CaptureFailed => StringKeys.BackupErrorCaptureFailed,
        BackupErrorCodes.UploadFailed => StringKeys.BackupErrorUploadFailed,
        BackupErrorCodes.RestoreBusy => StringKeys.BackupErrorRestoreBusy,
        BackupErrorCodes.RestoreFailed => StringKeys.BackupErrorRestoreFailed,
        BackupErrorCodes.DirectoryUnusable => StringKeys.BackupErrorDirectoryUnusable,
        BackupErrorCodes.TargetNotConfigured => StringKeys.BackupErrorTargetNotConfigured,
        BackupErrorCodes.NotABackup => StringKeys.BackupErrorNotABackup,
        _ => StringKeys.ErrorUnexpected,
    };
}
