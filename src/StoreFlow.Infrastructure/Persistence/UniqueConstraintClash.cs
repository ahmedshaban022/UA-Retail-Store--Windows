using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>
/// Recognises the moment the unique index refuses a value another clerk has just taken.
/// </summary>
/// <remarks>
/// <para>
/// A stock-keeping unit code, a barcode, a purchase number and a count number are all guarded by
/// a unique index. The shop checks them before it writes, but the check and the write are not one
/// act: two clerks can pass the check together, and the index then refuses the second write.
/// </para>
/// <para>
/// That refusal is an ordinary answer on a busy shop, and it must not escape as an exception. The
/// crash policy would take the programme down for it, and the clerk would lose the document they
/// were writing.
/// </para>
/// </remarks>
public static class UniqueConstraintClash
{
    /// <summary>The extended SQLite error codes that mean a unique value is already there.</summary>
    private static readonly int[] UniqueClashCodes =
    [
        2067, // SQLITE_CONSTRAINT_UNIQUE
        1555, // SQLITE_CONSTRAINT_PRIMARYKEY
        2579, // SQLITE_CONSTRAINT_ROWID
    ];

    /// <summary>
    /// True when the exception is the unique index refusing a value that is already there.
    /// </summary>
    /// <param name="exception">The exception the write raised.</param>
    /// <returns>True when a unique value was refused.</returns>
    public static bool IsClash(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is SqliteException sqlite && IsUnique(sqlite))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the SQLite error is the unique index refusing a value.
    /// </summary>
    /// <param name="exception">The SQLite error.</param>
    /// <returns>True when a unique value was refused.</returns>
    private static bool IsUnique(SqliteException exception)
    {
        foreach (int code in UniqueClashCodes)
        {
            if (exception.SqliteExtendedErrorCode == code)
            {
                return true;
            }
        }

        // A provider that does not name the constraint still gives the primary code, which is
        // SQLITE_CONSTRAINT, and the message carries the word.
        return exception.SqliteErrorCode == 19
            && exception.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }
}
