using System.Text;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Common;

namespace StoreFlow.Infrastructure.Export;

/// <summary>
/// Writes exports to disk. CSV is UTF-8 with a byte-order mark, which is what makes Arabic product
/// names open correctly in Excel (PRD §5.8 REP-10, UAT-15).
/// </summary>
public sealed class CsvExportService : IExportService
{
    /// <inheritdoc />
    public async Task<Result> WriteCsvAsync(
        string filePath,
        ExportTable table,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result.Failure("export.invalid_path", "Choose a file to export to.");
        }

        ArgumentNullException.ThrowIfNull(table);

        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream stream = File.Create(filePath);
            await stream.WriteAsync(CsvWriter.ByteOrderMark, cancellationToken);

            byte[] content = Encoding.UTF8.GetBytes(CsvWriter.Write(table));
            await stream.WriteAsync(content, cancellationToken);

            return Result.Success();
        }
        catch (IOException exception)
        {
            return Result.Failure("export.io_error", $"The file could not be written. {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result.Failure("export.access_denied", $"The file could not be written. {exception.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result> WriteJsonAsync(
        string filePath,
        ExportTable table,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result.Failure("export.invalid_path", "Choose a file to export to.");
        }

        ArgumentNullException.ThrowIfNull(table);

        var rows = new List<Dictionary<string, object?>>(table.Rows.Count);

        foreach (IReadOnlyList<object?> row in table.Rows)
        {
            var record = new Dictionary<string, object?>(table.Columns.Count, StringComparer.Ordinal);

            for (int index = 0; index < table.Columns.Count; index++)
            {
                record[table.Columns[index].Key] = index < row.Count ? row[index] : null;
            }

            rows.Add(record);
        }

        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                filePath,
                System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8,
                cancellationToken);

            return Result.Success();
        }
        catch (IOException exception)
        {
            return Result.Failure("export.io_error", $"The file could not be written. {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result.Failure("export.access_denied", $"The file could not be written. {exception.Message}");
        }
    }
}
