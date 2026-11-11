using Microsoft.EntityFrameworkCore;
using StoreFlow.Desktop.Composition;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop;

/// <summary>
/// What the programme does about an exception that escaped to the dispatcher.
/// </summary>
/// <remarks>
/// The shop's programme must not end because a screen threw. The fault is recorded, the owner is
/// told, and the shop carries on trading. Without this, any exception that escaped a command took
/// the programme down with it and the owner was left with no till at all — which is how a single
/// fault in the recovery wizard cost a shop its programme on the day it needed it most.
/// </remarks>
public static class DispatcherFailure
{
    /// <summary>
    /// Decides what the programme does about an exception that escaped to the dispatcher.
    /// </summary>
    /// <param name="exception">The exception that escaped.</param>
    /// <param name="strings">The application's strings, when the host is up.</param>
    /// <returns>The decision: the message for the owner, and whether the programme carries on.</returns>
    public static FailureDecision Decide(Exception exception, IStrings? strings)
    {
        ArgumentNullException.ThrowIfNull(exception);

        StartupDiagnostics.Write("Unhandled dispatcher exception.", exception);

        return new FailureDecision(MessageFor(exception, strings), KeepRunning: true);
    }

    /// <summary>
    /// The message the owner reads.
    /// </summary>
    /// <param name="exception">The exception that escaped.</param>
    /// <param name="strings">The application's strings, when the host is up.</param>
    /// <returns>The text to show.</returns>
    public static string MessageFor(Exception exception, IStrings? strings)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (strings is null)
        {
            return "Something went wrong. Close this message and try again.";
        }

        // Somebody else moved the stock while this screen was working. The write was refused, so
        // nothing was changed, and the owner can simply try again.
        return exception is DbUpdateConcurrencyException
            ? strings[StringKeys.ErrorStockChanged]
            : strings[StringKeys.ErrorUnexpected];
    }
}

/// <summary>What the programme does about a fault, and what the owner is told.</summary>
/// <param name="Message">The text to show the owner.</param>
/// <param name="KeepRunning">True when the programme carries on trading.</param>
public readonly record struct FailureDecision(string Message, bool KeepRunning);
