using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Common;
using StoreFlow.Application.Inventory;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enumerations;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Inventory;

/// <summary>
/// Exports the filtered purchase register to a CSV file. The file holds exactly what the list shows,
/// and the application can read its own file back (PRD PUR-05, REP-10).
/// </summary>
public sealed class ExportPurchasesCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IExportService exportService,
    IUserSession session) : ICommandHandler<ExportPurchasesCommand, Result<int>>
{
    /// <summary>The stable keys of the exported columns, in order.</summary>
    public static readonly IReadOnlyList<string> CanonicalFields =
    [
        "number",
        "date",
        "supplier",
        "status",
        "lines",
        "quantity",
        "subtotal",
        "discount",
        "tax",
        "total",
        "reverses",
    ];

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(ExportPurchasesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The purchase register is guarded by the same permission as the purchase document itself:
        // whoever may receive goods may also take a copy of the register.
        Result permitted = await authorization.RequireAsync(Permission.PurchasePost, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<PurchaseDocument> documents = context.PurchaseDocuments.AsNoTracking();

        PurchaseFilter filter = command.Filter;

        if (filter.SupplierId is int supplierId)
        {
            documents = documents.Where(document => document.SupplierId == supplierId);
        }

        if (filter.From is DateOnly from)
        {
            documents = documents.Where(document => document.BusinessDate >= from);
        }

        if (filter.To is DateOnly to)
        {
            documents = documents.Where(document => document.BusinessDate <= to);
        }

        if (filter.Status is DocumentStatus status)
        {
            documents = documents.Where(document => document.Status == status);
        }

        string? search = filter.Search?.Trim();

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = InventorySearch.LikePattern(search);

            documents = documents.Where(document =>
                EF.Functions.Like(document.Number, pattern, InventorySearch.LikeEscapeCharacter)
                || (document.Supplier != null
                    && EF.Functions.Like(document.Supplier.Name, pattern, InventorySearch.LikeEscapeCharacter)));
        }

        List<PurchaseDocument> rows = await documents
            .Include(document => document.Supplier)
            .Include(document => document.Lines)
            .OrderByDescending(document => document.BusinessDate)
            .ThenByDescending(document => document.Id)
            .ToListAsync(cancellationToken);

        var table = new ExportTable(
            CanonicalFields
                .Select(field => new ExportColumn(field, HeaderFor(field, command.Headers)))
                .ToList(),
            rows.Select(row => (IReadOnlyList<object?>)[
                row.Number,
                row.BusinessDate,
                row.Supplier?.Name ?? string.Empty,
                row.Status.ToString(),
                row.Lines.Count,
                QuantityOf(row),
                row.Subtotal.Amount,
                row.Discount.Amount,
                row.Tax.Amount,
                row.Total.Amount,
                row.ReversesDocumentId?.ToString() ?? string.Empty,
            ]).ToList());

        Result written = await exportService.WriteCsvAsync(command.FilePath, table, cancellationToken);

        if (written.IsFailure)
        {
            return Result<int>.Failure(written.Error);
        }

        await auditWriter.WriteAsync(
            new AuditEntry(
                InventoryAudit.PurchaseExport,
                nameof(PurchaseDocument),
                null,
                null,
                $"Exported {rows.Count} purchase documents",
                session.Current?.UserId),
            cancellationToken);

        return Result<int>.Success(rows.Count);
    }

    /// <summary>
    /// The total quantity on the document, in the units the lines were entered in.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The total quantity.</returns>
    private static decimal QuantityOf(PurchaseDocument document)
    {
        decimal total = 0m;

        foreach (PurchaseLine line in document.Lines)
        {
            total += line.QuantityScaled / (decimal)Math.Pow(10, line.QuantityScale);
        }

        return total;
    }

    /// <summary>
    /// The heading written for a column, falling back to the stable key.
    /// </summary>
    /// <param name="field">The column key.</param>
    /// <param name="headers">The localised headings.</param>
    /// <returns>The heading.</returns>
    private static string HeaderFor(string field, IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue(field, out string? header) && !string.IsNullOrWhiteSpace(header)
            ? header
            : field;
}
