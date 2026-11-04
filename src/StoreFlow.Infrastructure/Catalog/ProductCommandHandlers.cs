using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Quantity;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Catalog;

/// <summary>
/// Creates or updates a product. The handler authorises first, validates second, and commits once
/// (PRD §3, §8.1).
/// </summary>
public sealed class SaveProductCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock,
    IProductImageStore imageStore) : ICommandHandler<SaveProductCommand, Result<int>>
{
    /// <summary>The error code when the stock-keeping unit code is already taken.</summary>
    public const string DuplicateSkuCode = "catalog.duplicate_sku";

    /// <summary>The error code when the barcode is already taken.</summary>
    public const string DuplicateBarcodeCode = "catalog.duplicate_barcode";

    /// <summary>The error code when the product does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <summary>The error code when a referenced record does not exist.</summary>
    public const string InvalidReferenceCode = "catalog.invalid_reference";

    /// <summary>The error code when the stored picture cannot be used.</summary>
    public const string InvalidImageCode = "catalog.invalid_image";

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(SaveProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogEdit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Product? product = null;

        if (command.ProductId is int productId)
        {
            product = await context.Products
                .Include(existing => existing.Barcodes)
                .FirstOrDefaultAsync(existing => existing.Id == productId, cancellationToken);

            if (product is null)
            {
                return Result<int>.Failure(NotFoundCode, "This product no longer exists.");
            }
        }

        Unit? unit = await context.Units
            .FirstOrDefaultAsync(candidate => candidate.Id == command.UnitId, cancellationToken);

        if (unit is null)
        {
            return Result<int>.Failure(InvalidReferenceCode, "Choose a selling unit for this product.");
        }

        if (command.CategoryId is int categoryId
            && !await context.Categories.AnyAsync(category => category.Id == categoryId, cancellationToken))
        {
            return Result<int>.Failure(InvalidReferenceCode, "The chosen category no longer exists.");
        }

        if (!await context.TaxCategories.AnyAsync(tax => tax.Id == command.TaxCategoryId, cancellationToken))
        {
            return Result<int>.Failure(InvalidReferenceCode, "The chosen tax category no longer exists.");
        }

        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateProduct(
            command.Sku,
            command.Name,
            command.NameAr,
            command.Description,
            command.SalePrice,
            command.CostPrice,
            command.ReorderLevel,
            command.Barcode,
            command.AdditionalBarcodes,
            unit.Precision);

        if (issues.Count > 0)
        {
            return Result<int>.Failure(issues[0].Code, issues[0].Message);
        }

        string sku = command.Sku.Trim();
        string name = command.Name.Trim();

        int? currentProductId = product?.Id;

        bool skuTaken = await context.Products.AnyAsync(
            existing => existing.Sku == sku && (currentProductId == null || existing.Id != currentProductId),
            cancellationToken);

        if (skuTaken)
        {
            return Result<int>.Failure(
                DuplicateSkuCode,
                $"The stock-keeping unit code {sku} is already used by another product.");
        }

        string? primaryBarcode = Normalise(command.Barcode);
        var additionalBarcodes = command.AdditionalBarcodes
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(Normalise)
            .Where(candidate => candidate is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string barcode in additionalBarcodes.Prepend(primaryBarcode).Where(candidate => candidate is not null).Cast<string>())
        {
            if (await BarcodeBelongsToAnotherProductAsync(context, barcode, product?.Id, cancellationToken))
            {
                return Result<int>.Failure(
                    DuplicateBarcodeCode,
                    $"The barcode {barcode} is already used by another product.");
            }
        }

        string? imagePath = NormaliseImagePath(command.ImagePath);

        if (imagePath is not null && !imageStore.Exists(imagePath))
        {
            return Result<int>.Failure(InvalidImageCode, "The chosen picture is no longer available. Choose it again.");
        }

        DateTimeOffset now = clock.UtcNow;
        bool isNew = product is null;

        product ??= new Product
        {
            CreatedUtc = now,
        };

        string? beforeHash = isNew
            ? null
            : CatalogAudit.Hash(product.Sku, product.Name, product.NameAr, product.Description,
                product.CategoryId?.ToString(), product.UnitId.ToString(), product.TaxCategoryId.ToString(),
                product.SalePrice.ToString(), product.CostPrice.ToString(),
                product.ReorderLevelScaled.ToString(), product.Barcode, product.ImagePath,
                product.IsActive.ToString());

        product.Sku = sku;
        product.Name = name;
        product.NameAr = NullIfEmpty(command.NameAr);
        product.Description = NullIfEmpty(command.Description);
        product.CategoryId = command.CategoryId;
        product.UnitId = command.UnitId;
        product.TaxCategoryId = command.TaxCategoryId;
        product.SalePrice = command.SalePrice;
        product.CostPrice = command.CostPrice;
        product.ReorderLevelScaled = new UnitQuantity(command.ReorderLevel, unit.Precision).ToScaledLong();
        product.Barcode = primaryBarcode;
        product.ImagePath = imagePath;
        product.IsActive = command.IsActive;
        product.UpdatedUtc = now;

        if (isNew)
        {
            context.Products.Add(product);
        }

        SynchroniseBarcodes(product, additionalBarcodes);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.BeforeWriteAsync(WritePoint.SaveProduct, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // A new product has no id until its row has been saved, so the audit entry joins the same
        // transaction and is made permanent together with the product it describes.
        auditWriter.AddTo(
            context,
            new AuditEntry(
                isNew ? CatalogAudit.ProductCreate : CatalogAudit.ProductUpdate,
                nameof(Product),
                product.Id.ToString(),
                beforeHash,
                $"{product.Name} ({product.Sku})",
                null));

        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<int>.Success(product.Id);
    }

    /// <summary>
    /// Replaces the product's extra barcodes with the given set, so a barcode the owner removed stops
    /// answering to this product.
    /// </summary>
    /// <param name="product">The product whose barcodes are being replaced.</param>
    /// <param name="additionalBarcodes">The barcodes the product should answer to.</param>
    internal static void SynchroniseBarcodes(Product product, IReadOnlyList<string> additionalBarcodes)
    {
        var wanted = new HashSet<string>(additionalBarcodes, StringComparer.OrdinalIgnoreCase);

        foreach (ProductBarcode existing in product.Barcodes.ToList())
        {
            if (!wanted.Contains(existing.Barcode))
            {
                product.Barcodes.Remove(existing);
            }
        }

        foreach (string barcode in additionalBarcodes)
        {
            bool present = product.Barcodes.Any(existing =>
                string.Equals(existing.Barcode, barcode, StringComparison.OrdinalIgnoreCase));

            if (!present)
            {
                product.Barcodes.Add(new ProductBarcode
                {
                    Product = product,
                    Barcode = barcode,
                });
            }
        }
    }

    /// <summary>
    /// True when the barcode already answers to a product other than the one being saved.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="barcode">The barcode to check.</param>
    /// <param name="productId">The product being saved, or null when a new product is being created.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the barcode is taken.</returns>
    internal static async Task<bool> BarcodeBelongsToAnotherProductAsync(
        StoreFlowDbContext context,
        string barcode,
        int? productId,
        CancellationToken cancellationToken)
    {
        bool takenByPrimary = await context.Products.AnyAsync(
            product => product.Barcode == barcode && (productId == null || product.Id != productId),
            cancellationToken);

        if (takenByPrimary)
        {
            return true;
        }

        return await context.ProductBarcodes.AnyAsync(
            existing => existing.Barcode == barcode && (productId == null || existing.ProductId != productId),
            cancellationToken);
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormaliseImagePath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Activates or deactivates a product (PRD PRD-01).</summary>
public sealed class SetProductActiveCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<SetProductActiveCommand, Result>
{
    /// <summary>The error code when the product does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <inheritdoc />
    public async Task<Result> HandleAsync(SetProductActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogEdit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Product? product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(NotFoundCode, "This product no longer exists.");
        }

        if (product.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        string? beforeHash = CatalogAudit.Hash(product.IsActive.ToString());

        product.IsActive = command.IsActive;
        product.UpdatedUtc = clock.UtcNow;

        auditWriter.AddTo(
            context,
            new AuditEntry(
                CatalogAudit.ProductActiveChanged,
                nameof(Product),
                product.Id.ToString(),
                beforeHash,
                command.IsActive ? "Active" : "Deactivated",
                null));

        await context.BeforeWriteAsync(WritePoint.SetProductActive, cancellationToken);
        await context.BeforeWriteAsync(WritePoint.WriteAuditEntryInTransaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
