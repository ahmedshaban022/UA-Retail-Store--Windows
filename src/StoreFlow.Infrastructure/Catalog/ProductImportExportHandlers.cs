using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Money;
using StoreFlow.Domain.Quantity;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Catalog;

/// <summary>
/// Reads the catalogue as it stands, so the importer can tell a creation from an update and can
/// catch a barcode that already belongs to another product.
/// </summary>
/// <param name="snapshot">The catalogue.</param>
/// <param name="UnitPrecisions">The precision of each selling unit.</param>
internal sealed record CatalogSnapshotResult(CatalogSnapshot Snapshot, IReadOnlyDictionary<int, int> UnitPrecisions);

/// <summary>
/// Reads a CSV file and reports what an import would do, without writing anything (PRD PRD-06).
/// </summary>
public sealed class PreviewProductImportQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<PreviewProductImportQuery, Result<ProductImportPreview>>
{
    /// <summary>The error code when the file cannot be read.</summary>
    public const string FileNotReadableCode = "catalog.import.file_not_readable";

    /// <inheritdoc />
    public async Task<Result<ProductImportPreview>> HandleAsync(
        PreviewProductImportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.CatalogImportExport, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<ProductImportPreview>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IReadOnlyList<string[]> rows;

        try
        {
            rows = CsvReader.ParseFile(query.FilePath);
        }
        catch (IOException)
        {
            return Result<ProductImportPreview>.Failure(FileNotReadableCode, "The file could not be opened. Close it in Excel and try again.");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<ProductImportPreview>.Failure(FileNotReadableCode, "The file could not be opened. Check that the application may read it.");
        }

        if (rows.Count == 0)
        {
            return Result<ProductImportPreview>.Failure(FileNotReadableCode, "The file is empty.");
        }

        CatalogSnapshotResult snapshot = await CatalogSnapshotReader.ReadAsync(context, cancellationToken);

        CsvColumnMap columnMap = ProductCsvMapper.ResolveHeaders(rows[0], query.HeaderAliases);

        IReadOnlyList<ProductImportRow> importRows = ProductCsvMapper.BuildRows(
            rows,
            columnMap,
            snapshot.Snapshot,
            query.Mode);

        int valid = importRows.Count(row => row.IsValid);
        int updates = importRows.Count(row => row.IsValid && row.IsUpdate);

        return Result<ProductImportPreview>.Success(new ProductImportPreview(
            importRows,
            columnMap.UnmappedHeaders,
            valid,
            importRows.Count - valid,
            updates,
            valid - updates));
    }
}

/// <summary>
/// Imports products from a CSV file. The rows that cannot be imported are reported and skipped; the
/// rest are written in one transaction, so a file either lands whole or not at all.
/// </summary>
public sealed class ImportProductsCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock)
    : ICommandHandler<ImportProductsCommand, Result<ImportSummary>>
{
    /// <summary>The error code when the file cannot be read.</summary>
    public const string FileNotReadableCode = "catalog.import.file_not_readable";

    /// <inheritdoc />
    public async Task<Result<ImportSummary>> HandleAsync(
        ImportProductsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogImportExport, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<ImportSummary>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IReadOnlyList<string[]> rows;

        try
        {
            rows = CsvReader.ParseFile(command.FilePath);
        }
        catch (IOException)
        {
            return Result<ImportSummary>.Failure(FileNotReadableCode, "The file could not be opened. Close it in Excel and try again.");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<ImportSummary>.Failure(FileNotReadableCode, "The file could not be opened. Check that the application may read it.");
        }

        if (rows.Count == 0)
        {
            return Result<ImportSummary>.Failure(FileNotReadableCode, "The file is empty.");
        }

        CatalogSnapshotResult snapshot = await CatalogSnapshotReader.ReadAsync(context, cancellationToken);

        CsvColumnMap columnMap = ProductCsvMapper.ResolveHeaders(rows[0], command.HeaderAliases);

        IReadOnlyList<ProductImportRow> importRows = ProductCsvMapper.BuildRows(
            rows,
            columnMap,
            snapshot.Snapshot,
            command.Mode);

        DateTimeOffset now = clock.UtcNow;
        int created = 0;
        int updated = 0;
        int failed = 0;

        List<Product> productsToUpdate = [];

        foreach (ProductImportRow row in importRows)
        {
            if (!row.IsValid || row.Resolved is null)
            {
                failed++;
                continue;
            }

            ResolvedProduct resolved = row.Resolved;

            Product? product = row.ExistingProductId is int existingId
                ? await context.Products
                    .Include(candidate => candidate.Barcodes)
                    .FirstOrDefaultAsync(candidate => candidate.Id == existingId, cancellationToken)
                : null;

            if (product is null)
            {
                product = new Product
                {
                    CreatedUtc = now,
                };

                context.Products.Add(product);
                created++;
            }
            else
            {
                updated++;
            }

            int precision = snapshot.UnitPrecisions.TryGetValue(resolved.UnitId, out int unitPrecision)
                ? unitPrecision
                : 0;

            product.Sku = resolved.Sku;
            product.Name = resolved.Name;
            product.NameAr = resolved.NameAr;
            product.Description = resolved.Description;
            product.CategoryId = resolved.CategoryId;
            product.UnitId = resolved.UnitId;
            product.TaxCategoryId = resolved.TaxCategoryId;
            product.SalePrice = new Money(resolved.SalePrice);
            product.CostPrice = new Money(resolved.CostPrice);
            product.ReorderLevelScaled = new UnitQuantity(resolved.ReorderLevel, precision).ToScaledLong();
            product.Barcode = resolved.Barcode;
            product.IsActive = resolved.IsActive;
            product.UpdatedUtc = now;

            SaveProductCommandHandler.SynchroniseBarcodes(product, resolved.AdditionalBarcodes);

            productsToUpdate.Add(product);
        }

        await context.BeforeWriteAsync(WritePoint.ImportProducts, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                CatalogAudit.CatalogImport,
                nameof(Product),
                null,
                null,
                $"Created {created}, updated {updated}, failed {failed}",
                null),
            cancellationToken);

        return Result<ImportSummary>.Success(new ImportSummary(created, updated, failed));
    }
}

/// <summary>
/// Exports the filtered product list to a CSV file. The file holds exactly what the list shows, and
/// the application can read its own file back (PRD PRD-06, REP-10).
/// </summary>
public sealed class ExportProductsCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IExportService exportService)
    : ICommandHandler<ExportProductsCommand, Result<int>>
{
    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(ExportProductsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogImportExport, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Product> products = context.Products.AsNoTracking();

        if (!command.Filter.IncludeInactive)
        {
            products = products.Where(product => product.IsActive);
        }

        if (command.Filter.CategoryId is int categoryId)
        {
            products = products.Where(product => product.CategoryId == categoryId);
        }

        string? search = command.Filter.Search?.Trim();

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = SearchProductsQueryHandler.LikePattern(search);

            products = products.Where(product =>
                EF.Functions.Like(product.Name, pattern, SearchProductsQueryHandler.LikeEscapeCharacter)
                || (product.NameAr != null && EF.Functions.Like(product.NameAr, pattern, SearchProductsQueryHandler.LikeEscapeCharacter))
                || EF.Functions.Like(product.Sku, pattern, SearchProductsQueryHandler.LikeEscapeCharacter)
                || (product.Barcode != null && EF.Functions.Like(product.Barcode, pattern, SearchProductsQueryHandler.LikeEscapeCharacter))
                || product.Barcodes.Any(barcode => EF.Functions.Like(barcode.Barcode, pattern, SearchProductsQueryHandler.LikeEscapeCharacter)));
        }

        List<ProductExportRow> rows = await products
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Select(product => new ProductExportRow(
                product.Sku,
                product.Name,
                product.NameAr,
                product.Category == null ? null : product.Category.Name,
                product.Unit == null ? null : product.Unit.Name,
                product.TaxCategory == null ? null : product.TaxCategory.Name,
                product.SalePrice.Amount,
                product.CostPrice.Amount,
                product.ReorderLevelScaled,
                product.Unit == null ? 0 : product.Unit.Precision,
                product.Barcode,
                product.Barcodes.Count == 0
                    ? null
                    : string.Join(";", product.Barcodes.Select(barcode => barcode.Barcode).OrderBy(barcode => barcode)),
                product.Description,
                product.IsActive))
            .ToListAsync(cancellationToken);

        var columns = new List<ExportColumn>();

        foreach (string field in ProductCsvMapper.CanonicalFields)
        {
            columns.Add(new ExportColumn(field, HeaderFor(field, command.Headers)));
        }

        var table = new ExportTable(
            columns,
            rows.Select(row => (IReadOnlyList<object?>)
            [
                row.Sku,
                row.Name,
                row.NameAr ?? string.Empty,
                // An uncategorised product is written as an empty cell, so the file reads back as
                // uncategorised rather than as a category that does not exist.
                row.CategoryName ?? string.Empty,
                row.UnitName ?? string.Empty,
                row.TaxCategoryName ?? string.Empty,
                row.SalePrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                row.CostPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                UnitQuantity.FromScaledLong(row.ReorderLevelScaled, row.UnitPrecision).Value
                    .ToString("0.################", System.Globalization.CultureInfo.InvariantCulture),
                row.Barcode ?? string.Empty,
                row.AdditionalBarcodes ?? string.Empty,
                row.Description ?? string.Empty,
                row.IsActive ? "true" : "false",
            ]).ToList());

        Result written = await exportService.WriteCsvAsync(command.FilePath, table, cancellationToken);

        if (written.IsFailure)
        {
            return Result<int>.Failure(written.Error);
        }

        await auditWriter.WriteAsync(
            new AuditEntry(
                CatalogAudit.CatalogExport,
                nameof(Product),
                null,
                null,
                $"Exported {rows.Count} products",
                null),
            cancellationToken);

        return Result<int>.Success(rows.Count);
    }

    private static string HeaderFor(string field, IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue(field, out string? header) && !string.IsNullOrWhiteSpace(header)
            ? header
            : field;
}

/// <summary>One product, flattened for the export.</summary>
/// <param name="Sku">The stock-keeping unit code.</param>
/// <param name="Name">The product name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="CategoryName">The category name, or null when the product is uncategorised.</param>
/// <param name="UnitName">The selling unit name.</param>
/// <param name="TaxCategoryName">The tax category name.</param>
/// <param name="SalePrice">The selling price.</param>
/// <param name="CostPrice">The cost price.</param>
/// <param name="ReorderLevelScaled">The reorder level, scaled by the unit's precision.</param>
/// <param name="UnitPrecision">The precision of the selling unit.</param>
/// <param name="Barcode">The primary barcode.</param>
/// <param name="AdditionalBarcodes">The other barcodes, joined for the file.</param>
/// <param name="Description">The description.</param>
/// <param name="IsActive">False for a deactivated product.</param>
internal sealed record ProductExportRow(
    string Sku,
    string Name,
    string? NameAr,
    string? CategoryName,
    string? UnitName,
    string? TaxCategoryName,
    decimal SalePrice,
    decimal CostPrice,
    long ReorderLevelScaled,
    int UnitPrecision,
    string? Barcode,
    string? AdditionalBarcodes,
    string? Description,
    bool IsActive);

/// <summary>The identity of a product, as the importer needs it.</summary>
/// <param name="Id">The product identifier.</param>
/// <param name="Sku">The stock-keeping unit code.</param>
/// <param name="Barcode">The primary barcode.</param>
internal sealed record ProductIdentityRow(int Id, string Sku, string? Barcode);

/// <summary>Reads the catalogue snapshot the importer works against.</summary>
internal static class CatalogSnapshotReader
{
    /// <summary>Reads the catalogue as it stands.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The catalogue, and the precision of each selling unit.</returns>
    public static async Task<CatalogSnapshotResult> ReadAsync(
        StoreFlowDbContext context,
        CancellationToken cancellationToken)
    {
        var productIdsBySku = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var barcodeOwners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        List<ProductIdentityRow> products = await context.Products
            .AsNoTracking()
            .Select(product => new ProductIdentityRow(product.Id, product.Sku, product.Barcode))
            .ToListAsync(cancellationToken);

        List<ProductBarcode> barcodes = await context.ProductBarcodes
            .AsNoTracking()
            .Select(barcode => new ProductBarcode
            {
                ProductId = barcode.ProductId,
                Barcode = barcode.Barcode,
            })
            .ToListAsync(cancellationToken);

        foreach (ProductIdentityRow product in products)
        {
            productIdsBySku[product.Sku] = product.Id;

            if (!string.IsNullOrWhiteSpace(product.Barcode))
            {
                barcodeOwners[product.Barcode] = product.Id;
            }
        }

        foreach (ProductBarcode barcode in barcodes)
        {
            barcodeOwners[barcode.Barcode] = barcode.ProductId;
        }

        var categoriesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Category category in await context.Categories.AsNoTracking().ToListAsync(cancellationToken))
        {
            categoriesByName[category.Name] = category.Id;
        }

        var unitsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unitPrecisions = new Dictionary<int, int>();

        foreach (Unit unit in await context.Units.AsNoTracking().ToListAsync(cancellationToken))
        {
            unitsByName[unit.Name] = unit.Id;
            unitPrecisions[unit.Id] = unit.Precision;
        }

        var taxCategoriesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (TaxCategory taxCategory in await context.TaxCategories.AsNoTracking().ToListAsync(cancellationToken))
        {
            taxCategoriesByName[taxCategory.Name] = taxCategory.Id;
        }

        int defaultUnitId = unitPrecisions.Count > 0
            ? await context.Units.AsNoTracking().OrderBy(unit => unit.Id).Select(unit => unit.Id).FirstAsync(cancellationToken)
            : 0;

        int defaultTaxCategoryId = taxCategoriesByName.Count > 0
            ? await context.TaxCategories.AsNoTracking().OrderBy(tax => tax.Id).Select(tax => tax.Id).FirstAsync(cancellationToken)
            : 0;

        return new CatalogSnapshotResult(
            new CatalogSnapshot(
                productIdsBySku,
                barcodeOwners,
                categoriesByName,
                unitsByName,
                taxCategoriesByName,
                defaultUnitId,
                defaultTaxCategoryId),
            unitPrecisions);
    }
}
