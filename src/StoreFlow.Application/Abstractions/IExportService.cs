using StoreFlow.Application.Common;

namespace StoreFlow.Application.Abstractions;

/// <summary>A column in an exported table.</summary>
/// <param name="Key">The stable key of the column.</param>
/// <param name="Header">The heading written to the file.</param>
public sealed record ExportColumn(string Key, string Header);

/// <summary>A table ready to be written to a file.</summary>
/// <param name="Columns">The column definitions.</param>
/// <param name="Rows">The rows, each holding one value per column in order.</param>
public sealed record ExportTable(IReadOnlyList<ExportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>
/// Writes data out of the application. CSV is written as UTF-8 with a byte-order mark so Arabic
/// text opens correctly in Excel (PRD §5.8 REP-10, UAT-15).
/// </summary>
public interface IExportService
{
    /// <summary>Writes a table to a CSV file.</summary>
    Task<Result> WriteCsvAsync(string filePath, ExportTable table, CancellationToken cancellationToken = default);

    /// <summary>Writes a table to a JSON file.</summary>
    Task<Result> WriteJsonAsync(string filePath, ExportTable table, CancellationToken cancellationToken = default);
}
