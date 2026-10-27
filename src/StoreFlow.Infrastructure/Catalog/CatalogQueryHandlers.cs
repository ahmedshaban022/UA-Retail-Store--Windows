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
/// Searches the product list by name, Arabic name, stock-keeping unit code and barcode
/// (PRD PRD-02). The search runs in the database, so a catalogue of tens of thousands of products
/// is filtered by the index rather than in memory.
/// </summary>
public sealed class SearchProductsQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<SearchProductsQuery, Result<ProductPage>>
{
    /// <summary>The error code when the caller may not see the catalogue.</summary>
    public const string NotAuthorizedCode = AuthorizationService.NotAuthorizedCode;

    /// <inheritdoc />
    public async Task<Result<ProductPage>> HandleAsync(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.CatalogView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<ProductPage>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Product> products = context.Products.AsNoTracking();

        if (!query.Filter.IncludeInactive)
        {
            products = products.Where(product => product.IsActive);
        }

        if (query.Filter.CategoryId is int categoryId)
        {
            products = products.Where(product => product.CategoryId == categoryId);
        }

        string? search = query.Filter.Search?.Trim();

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = LikePattern(search);

            products = products.Where(product =>
                EF.Functions.Like(product.Name, pattern, LikeEscapeCharacter)
                || (product.NameAr != null && EF.Functions.Like(product.NameAr, pattern, LikeEscapeCharacter))
                || EF.Functions.Like(product.Sku, pattern, LikeEscapeCharacter)
                || (product.Barcode != null && EF.Functions.Like(product.Barcode, pattern, LikeEscapeCharacter))
                || product.Barcodes.Any(barcode => EF.Functions.Like(barcode.Barcode, pattern, LikeEscapeCharacter)));
        }

        int totalCount = await products.CountAsync(cancellationToken);

        List<ProductListItem> items = await products
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 500))
            .Select(product => new ProductListItem(
                product.Id,
                product.Sku,
                product.Name,
                product.NameAr,
                product.Category == null ? null : product.Category.Name,
                product.Unit == null ? null : product.Unit.Name,
                product.SalePrice,
                product.CostPrice,
                product.Barcode,
                product.Barcodes.Count,
                product.ImagePath,
                product.IsActive))
            .ToListAsync(cancellationToken);

        return Result<ProductPage>.Success(new ProductPage(items, totalCount));
    }

    /// <summary>The character that escapes the wildcards inside a searched term.</summary>
    internal const string LikeEscapeCharacter = "\\";

    /// <summary>
    /// Builds a SQL pattern that matches the whole term, with the wildcards inside the term escaped
    /// so that searching for <c>50%</c> finds the text <c>50%</c> and not everything starting with 50.
    /// </summary>
    /// <param name="term">The term the owner typed.</param>
    /// <returns>The pattern to match against.</returns>
    internal static string LikePattern(string term)
    {
        var builder = new System.Text.StringBuilder(term.Length + 2);

        builder.Append('%');

        foreach (char character in term)
        {
            if (character is '%' or '_' || character == LikeEscapeCharacter[0])
            {
                builder.Append(LikeEscapeCharacter);
            }

            builder.Append(character);
        }

        builder.Append('%');

        return builder.ToString();
    }
}

/// <summary>Reads one product for the editor.</summary>
public sealed class GetProductQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<GetProductQuery, Result<ProductDetails>>
{
    /// <summary>The error code when the product does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <inheritdoc />
    public async Task<Result<ProductDetails>> HandleAsync(GetProductQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.CatalogView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<ProductDetails>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Product? product = await context.Products
            .AsNoTracking()
            .Include(candidate => candidate.Barcodes)
            .Include(candidate => candidate.Unit)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.ProductId, cancellationToken);

        if (product is null)
        {
            return Result<ProductDetails>.Failure(NotFoundCode, "This product no longer exists.");
        }

        int precision = product.Unit?.Precision ?? 0;

        IReadOnlyList<string> additionalBarcodes = product.Barcodes
            .Select(barcode => barcode.Barcode)
            .OrderBy(barcode => barcode, StringComparer.Ordinal)
            .ToList();

        return Result<ProductDetails>.Success(new ProductDetails(
            product.Id,
            product.Sku,
            product.Name,
            product.NameAr,
            product.Description,
            product.CategoryId,
            product.UnitId,
            product.TaxCategoryId,
            product.SalePrice,
            product.CostPrice,
            UnitQuantity.FromScaledLong(product.ReorderLevelScaled, precision).Value,
            product.Barcode,
            additionalBarcodes,
            product.ImagePath,
            product.IsActive,
            product.CreatedUtc,
            product.UpdatedUtc));
    }
}

/// <summary>Lists the categories, with the number of products in each.</summary>
public sealed class ListCategoriesQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListCategoriesQuery, Result<IReadOnlyList<CategorySummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CategorySummary>>> HandleAsync(
        ListCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.CatalogView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<CategorySummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Category> categories = context.Categories.AsNoTracking();

        if (!query.IncludeInactive)
        {
            categories = categories.Where(category => category.IsActive);
        }

        List<CategorySummary> summaries = await categories
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new CategorySummary(
                category.Id,
                category.Name,
                category.NameAr,
                category.IsActive,
                category.Products.Count))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CategorySummary>>.Success(summaries);
    }
}

/// <summary>Lists the selling units, with the number of products sold in each.</summary>
public sealed class ListUnitsQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListUnitsQuery, Result<IReadOnlyList<UnitSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<UnitSummary>>> HandleAsync(
        ListUnitsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.CatalogView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<UnitSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Unit> units = context.Units.AsNoTracking();

        if (!query.IncludeInactive)
        {
            units = units.Where(unit => unit.IsActive);
        }

        List<UnitSummary> summaries = await units
            .OrderBy(unit => unit.Name)
            .ThenBy(unit => unit.Id)
            .Select(unit => new UnitSummary(
                unit.Id,
                unit.Name,
                unit.NameAr,
                unit.Precision,
                unit.IsActive,
                unit.Products.Count))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<UnitSummary>>.Success(summaries);
    }
}

/// <summary>Lists the suppliers.</summary>
public sealed class ListSuppliersQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<ListSuppliersQuery, Result<IReadOnlyList<SupplierSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SupplierSummary>>> HandleAsync(
        ListSuppliersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.CatalogView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<IReadOnlyList<SupplierSummary>>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Supplier> suppliers = context.Suppliers.AsNoTracking();

        if (!query.IncludeInactive)
        {
            suppliers = suppliers.Where(supplier => supplier.IsActive);
        }

        string? search = query.Search?.Trim();

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = SearchProductsQueryHandler.LikePattern(search);

            suppliers = suppliers.Where(supplier =>
                EF.Functions.Like(supplier.Name, pattern, SearchProductsQueryHandler.LikeEscapeCharacter)
                || (supplier.NameAr != null
                    && EF.Functions.Like(supplier.NameAr, pattern, SearchProductsQueryHandler.LikeEscapeCharacter)));
        }

        List<SupplierSummary> summaries = await suppliers
            .OrderBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id)
            .Select(supplier => new SupplierSummary(
                supplier.Id,
                supplier.Name,
                supplier.NameAr,
                supplier.Phone,
                supplier.Notes,
                supplier.IsActive))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SupplierSummary>>.Success(summaries);
    }
}

/// <summary>Reads the reference data the pickers on the product editor need.</summary>
public sealed class GetCatalogReferenceDataQueryHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization)
    : IQueryHandler<GetCatalogReferenceDataQuery, Result<CatalogReferenceData>>
{
    /// <inheritdoc />
    public async Task<Result<CatalogReferenceData>> HandleAsync(
        GetCatalogReferenceDataQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = await authorization.RequireAsync(Permission.CatalogView, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<CatalogReferenceData>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<CatalogOption> categories = await context.Categories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.Name)
            .Select(category => new CatalogOption(category.Id, category.Name, category.NameAr))
            .ToListAsync(cancellationToken);

        List<CatalogOption> units = await context.Units
            .AsNoTracking()
            .Where(unit => unit.IsActive)
            .OrderBy(unit => unit.Name)
            .Select(unit => new CatalogOption(unit.Id, unit.Name, unit.NameAr, unit.Precision))
            .ToListAsync(cancellationToken);

        List<CatalogOption> taxCategories = await context.TaxCategories
            .AsNoTracking()
            .OrderBy(tax => tax.Name)
            .Select(tax => new CatalogOption(tax.Id, tax.Name, null))
            .ToListAsync(cancellationToken);

        int defaultUnitId = units.Count > 0 ? units[0].Id : 0;
        int defaultTaxCategoryId = taxCategories.Count > 0 ? taxCategories[0].Id : 0;

        return Result<CatalogReferenceData>.Success(new CatalogReferenceData(
            categories,
            units,
            taxCategories,
            defaultUnitId,
            defaultTaxCategoryId));
    }
}
