using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StoreFlow.Infrastructure.Catalog;

/// <summary>
/// Creates or updates a category (PRD PRD-04). A product always belongs to a category or to the
/// explicit uncategorised value, so a category can never be removed from under a product.
/// </summary>
public sealed class SaveCategoryCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<SaveCategoryCommand, Result<int>>
{
    /// <summary>The error code when the category name is already taken.</summary>
    public const string DuplicateNameCode = "catalog.duplicate_name";

    /// <summary>The error code when the category does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(SaveCategoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogEdit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Category? category = null;

        if (command.CategoryId is int categoryId)
        {
            category = await context.Categories
                .FirstOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

            if (category is null)
            {
                return Result<int>.Failure(NotFoundCode, "This category no longer exists.");
            }
        }

        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateCategory(command.Name, command.NameAr);

        if (issues.Count > 0)
        {
            return Result<int>.Failure(issues[0].Code, issues[0].Message);
        }

        string name = command.Name.Trim();

        int? currentCategoryId = category?.Id;

        bool nameTaken = await context.Categories.AnyAsync(
            existing => existing.Name == name && (currentCategoryId == null || existing.Id != currentCategoryId),
            cancellationToken);

        if (nameTaken)
        {
            return Result<int>.Failure(DuplicateNameCode, $"There is already a category called {name}.");
        }

        DateTimeOffset now = clock.UtcNow;
        bool isNew = category is null;

        category ??= new Category
        {
            CreatedUtc = now,
        };

        string? beforeHash = isNew
            ? null
            : CatalogAudit.Hash(category.Name, category.NameAr, category.IsActive.ToString());

        category.Name = name;
        category.NameAr = NullIfEmpty(command.NameAr);
        category.IsActive = command.IsActive;
        category.UpdatedUtc = now;

        if (isNew)
        {
            context.Categories.Add(category);
        }

        await context.BeforeWriteAsync(WritePoint.SaveCategory, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                isNew ? CatalogAudit.CategoryCreate : CatalogAudit.CategoryUpdate,
                nameof(Category),
                category.Id.ToString(),
                beforeHash,
                category.Name,
                null),
            cancellationToken);

        return Result<int>.Success(category.Id);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Activates or deactivates a category.</summary>
public sealed class SetCategoryActiveCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<SetCategoryActiveCommand, Result>
{
    /// <summary>The error code when the category does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <inheritdoc />
    public async Task<Result> HandleAsync(SetCategoryActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogEdit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Category? category = await context.Categories
            .FirstOrDefaultAsync(candidate => candidate.Id == command.CategoryId, cancellationToken);

        if (category is null)
        {
            return Result.Failure(NotFoundCode, "This category no longer exists.");
        }

        if (category.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        string? beforeHash = CatalogAudit.Hash(category.IsActive.ToString());

        category.IsActive = command.IsActive;
        category.UpdatedUtc = clock.UtcNow;

        await context.BeforeWriteAsync(WritePoint.SetCategoryActive, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                CatalogAudit.CategoryActiveChanged,
                nameof(Category),
                category.Id.ToString(),
                beforeHash,
                command.IsActive ? "Active" : "Deactivated",
                null),
            cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Creates or updates a selling unit. The precision of a unit decides how many decimal places a
/// quantity in it may carry, so it is part of the unit's identity and not a display choice.
/// </summary>
public sealed class SaveUnitCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter) : ICommandHandler<SaveUnitCommand, Result<int>>
{
    /// <summary>The error code when the unit name is already taken.</summary>
    public const string DuplicateNameCode = "catalog.duplicate_name";

    /// <summary>The error code when the unit does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <summary>The error code when the unit is used by products and its precision cannot change.</summary>
    public const string PrecisionInUseCode = "catalog.precision_in_use";

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(SaveUnitCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogEdit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Unit? unit = null;

        if (command.UnitId is int unitId)
        {
            unit = await context.Units
                .FirstOrDefaultAsync(candidate => candidate.Id == unitId, cancellationToken);

            if (unit is null)
            {
                return Result<int>.Failure(NotFoundCode, "This unit no longer exists.");
            }
        }

        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateUnit(command.Name, command.NameAr, command.Precision);

        if (issues.Count > 0)
        {
            return Result<int>.Failure(issues[0].Code, issues[0].Message);
        }

        string name = command.Name.Trim();

        int? currentUnitId = unit?.Id;

        bool nameTaken = await context.Units.AnyAsync(
            existing => existing.Name == name && (currentUnitId == null || existing.Id != currentUnitId),
            cancellationToken);

        if (nameTaken)
        {
            return Result<int>.Failure(DuplicateNameCode, $"There is already a unit called {name}.");
        }

        if (unit is not null && unit.Precision != command.Precision)
        {
            // Quantities already recorded in this unit were scaled to its old precision, so changing
            // it would silently reinterpret every one of them.
            bool inUse = await context.Products.AnyAsync(product => product.UnitId == unit.Id, cancellationToken);

            if (inUse)
            {
                return Result<int>.Failure(
                    PrecisionInUseCode,
                    "This unit is used by products, so its precision cannot be changed. Create a new unit instead.");
            }
        }

        bool isNew = unit is null;

        unit ??= new Unit();

        string? beforeHash = isNew
            ? null
            : CatalogAudit.Hash(unit.Name, unit.NameAr, unit.Precision.ToString(), unit.IsActive.ToString());

        unit.Name = name;
        unit.NameAr = NullIfEmpty(command.NameAr);
        unit.Precision = command.Precision;
        unit.IsActive = command.IsActive;

        if (isNew)
        {
            context.Units.Add(unit);
        }

        await context.BeforeWriteAsync(WritePoint.SaveUnit, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                isNew ? CatalogAudit.UnitCreate : CatalogAudit.UnitUpdate,
                nameof(Unit),
                unit.Id.ToString(),
                beforeHash,
                unit.Name,
                null),
            cancellationToken);

        return Result<int>.Success(unit.Id);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Activates or deactivates a selling unit.</summary>
public sealed class SetUnitActiveCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter) : ICommandHandler<SetUnitActiveCommand, Result>
{
    /// <summary>The error code when the unit does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <inheritdoc />
    public async Task<Result> HandleAsync(SetUnitActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogEdit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Unit? unit = await context.Units
            .FirstOrDefaultAsync(candidate => candidate.Id == command.UnitId, cancellationToken);

        if (unit is null)
        {
            return Result.Failure(NotFoundCode, "This unit no longer exists.");
        }

        if (unit.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        string? beforeHash = CatalogAudit.Hash(unit.IsActive.ToString());

        unit.IsActive = command.IsActive;

        await context.BeforeWriteAsync(WritePoint.SetUnitActive, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                CatalogAudit.UnitActiveChanged,
                nameof(Unit),
                unit.Id.ToString(),
                beforeHash,
                command.IsActive ? "Active" : "Deactivated",
                null),
            cancellationToken);

        return Result.Success();
    }
}

/// <summary>Creates or updates a supplier (PRD PUR-01).</summary>
public sealed class SaveSupplierCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<SaveSupplierCommand, Result<int>>
{
    /// <summary>The error code when the supplier name is already taken.</summary>
    public const string DuplicateNameCode = "catalog.duplicate_name";

    /// <summary>The error code when the supplier does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(SaveSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogEdit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result<int>.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Supplier? supplier = null;

        if (command.SupplierId is int supplierId)
        {
            supplier = await context.Suppliers
                .FirstOrDefaultAsync(candidate => candidate.Id == supplierId, cancellationToken);

            if (supplier is null)
            {
                return Result<int>.Failure(NotFoundCode, "This supplier no longer exists.");
            }
        }

        IReadOnlyList<ValidationIssue> issues = CatalogValidation.ValidateSupplier(
            command.Name,
            command.NameAr,
            command.Phone,
            command.Notes);

        if (issues.Count > 0)
        {
            return Result<int>.Failure(issues[0].Code, issues[0].Message);
        }

        string name = command.Name.Trim();

        int? currentSupplierId = supplier?.Id;

        bool nameTaken = await context.Suppliers.AnyAsync(
            existing => existing.Name == name && (currentSupplierId == null || existing.Id != currentSupplierId),
            cancellationToken);

        if (nameTaken)
        {
            return Result<int>.Failure(DuplicateNameCode, $"There is already a supplier called {name}.");
        }

        DateTimeOffset now = clock.UtcNow;
        bool isNew = supplier is null;

        supplier ??= new Supplier
        {
            CreatedUtc = now,
        };

        string? beforeHash = isNew
            ? null
            : CatalogAudit.Hash(supplier.Name, supplier.NameAr, supplier.Phone, supplier.Notes, supplier.IsActive.ToString());

        supplier.Name = name;
        supplier.NameAr = NullIfEmpty(command.NameAr);
        supplier.Phone = NullIfEmpty(command.Phone);
        supplier.Notes = NullIfEmpty(command.Notes);
        supplier.IsActive = command.IsActive;
        supplier.UpdatedUtc = now;

        if (isNew)
        {
            context.Suppliers.Add(supplier);
        }

        await context.BeforeWriteAsync(WritePoint.SaveSupplier, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                isNew ? CatalogAudit.SupplierCreate : CatalogAudit.SupplierUpdate,
                nameof(Supplier),
                supplier.Id.ToString(),
                beforeHash,
                supplier.Name,
                null),
            cancellationToken);

        return Result<int>.Success(supplier.Id);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Activates or deactivates a supplier.</summary>
public sealed class SetSupplierActiveCommandHandler(
    IDbContextFactory<StoreFlowDbContext> contextFactory,
    IAuthorizationService authorization,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<SetSupplierActiveCommand, Result>
{
    /// <summary>The error code when the supplier does not exist.</summary>
    public const string NotFoundCode = "catalog.not_found";

    /// <inheritdoc />
    public async Task<Result> HandleAsync(SetSupplierActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result permitted = await authorization.RequireAsync(Permission.CatalogEdit, cancellationToken);

        if (permitted.IsFailure)
        {
            return Result.Failure(permitted.Error);
        }

        await using StoreFlowDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Supplier? supplier = await context.Suppliers
            .FirstOrDefaultAsync(candidate => candidate.Id == command.SupplierId, cancellationToken);

        if (supplier is null)
        {
            return Result.Failure(NotFoundCode, "This supplier no longer exists.");
        }

        if (supplier.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        string? beforeHash = CatalogAudit.Hash(supplier.IsActive.ToString());

        supplier.IsActive = command.IsActive;
        supplier.UpdatedUtc = clock.UtcNow;

        await context.BeforeWriteAsync(WritePoint.SetSupplierActive, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEntry(
                CatalogAudit.SupplierActiveChanged,
                nameof(Supplier),
                supplier.Id.ToString(),
                beforeHash,
                command.IsActive ? "Active" : "Deactivated",
                null),
            cancellationToken);

        return Result.Success();
    }
}
