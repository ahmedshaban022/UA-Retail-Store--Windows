using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Abstractions;
using StoreFlow.Application.Authorization;
using StoreFlow.Application.Catalog;
using StoreFlow.Application.Common;
using StoreFlow.Domain.Authorization;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Money;
using StoreFlow.Infrastructure.Catalog;
using StoreFlow.Infrastructure.Export;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Services;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// The catalogue's behaviour against a real SQLite database, with the real migrations, the real
/// unique indexes and the real triggers.
/// </summary>
public sealed class CatalogTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture = new();

    private AllowAllAuthorization _authorization = null!;
    private EfAuditWriter _auditWriter = null!;
    private TestImageStore _imageStore = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        _authorization = new AllowAllAuthorization();
        _auditWriter = new EfAuditWriter(_fixture.Factory, new TestClock(), new NoUserSession());
        _imageStore = new TestImageStore();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_product_is_created_and_read_back()
    {
        Result<int> created = await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        created.IsSuccess.ShouldBeTrue();

        var handler = new GetProductQueryHandler(_fixture.Factory, _authorization);

        Result<ProductDetails> read = await handler.HandleAsync(
            new GetProductQuery(created.Value),
            CancellationToken.None);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Sku.ShouldBe("RICE-001");
        read.Value.Name.ShouldBe("Rice");
        read.Value.Barcode.ShouldBe("6221000000017");
        read.Value.SalePrice.Amount.ShouldBe(450m);
        read.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task A_code_that_is_already_used_is_refused()
    {
        await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        Result<int> again = await SaveProductAsync("RICE-001", "Rice again", "6221000000018");

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe(SaveProductCommandHandler.DuplicateSkuCode);
    }

    [Fact]
    public async Task A_code_that_differs_only_in_case_is_the_same_code()
    {
        await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        Result<int> again = await SaveProductAsync("rice-001", "Rice again", "6221000000018");

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe(SaveProductCommandHandler.DuplicateSkuCode);
    }

    [Fact]
    public async Task A_barcode_that_is_already_used_is_refused()
    {
        await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        Result<int> again = await SaveProductAsync("RICE-002", "Rice two", "6221000000017");

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe(SaveProductCommandHandler.DuplicateBarcodeCode);
    }

    [Fact]
    public async Task A_barcode_cannot_be_the_extra_code_of_one_product_and_the_main_code_of_another()
    {
        Result<int> first = await SaveProductAsync("RICE-001", "Rice", "6221000000017", ["6221000000018"]);

        first.IsSuccess.ShouldBeTrue();

        Result<int> second = await SaveProductAsync("RICE-002", "Rice two", "6221000000018");

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(SaveProductCommandHandler.DuplicateBarcodeCode);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_primary_barcode_that_is_another_product_s_extra_code()
    {
        Result<int> first = await SaveProductAsync("RICE-001", "Rice", "6221000000017", ["6221000000018"]);

        first.IsSuccess.ShouldBeTrue();

        Result<int> second = await SaveProductAsync("RICE-002", "Rice two", "6221000000019");

        second.IsSuccess.ShouldBeTrue();

        // The application layer catches this first, so the trigger is proved by going around it.
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Product product = await context.Products.FirstAsync(candidate => candidate.Id == second.Value);

        product.Barcode = "6221000000018";

        await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_database_itself_refuses_an_extra_barcode_that_is_another_product_s_primary_code()
    {
        Result<int> first = await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        first.IsSuccess.ShouldBeTrue();

        Result<int> second = await SaveProductAsync("RICE-002", "Rice two", "6221000000019");

        second.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        context.ProductBarcodes.Add(new ProductBarcode
        {
            ProductId = second.Value,
            Barcode = "6221000000017",
        });

        await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_product_is_deactivated_and_kept()
    {
        Result<int> created = await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        var handler = new SetProductActiveCommandHandler(_fixture.Factory, _authorization, _auditWriter, new TestClock());

        Result deactivated = await handler.HandleAsync(
            new SetProductActiveCommand(created.Value, false),
            CancellationToken.None);

        deactivated.IsSuccess.ShouldBeTrue();

        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        Product? product = await context.Products.FirstOrDefaultAsync(candidate => candidate.Id == created.Value);

        product.ShouldNotBeNull();
        product.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task The_audit_trail_records_what_happened_to_a_product()
    {
        Result<int> created = await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        var handler = new GetProductAuditQueryHandler(_fixture.Factory, _authorization);

        Result<IReadOnlyList<AuditEntryView>> history = await handler.HandleAsync(
            new GetProductAuditQuery(created.Value, 20),
            CancellationToken.None);

        history.IsSuccess.ShouldBeTrue();
        history.Value.ShouldNotBeEmpty();
        history.Value[0].Action.ShouldBe("product.create");
        history.Value[0].EntityType.ShouldBe("Product");
    }

    [Fact]
    public async Task A_product_is_found_by_its_name_code_or_barcode()
    {
        await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        var handler = new SearchProductsQueryHandler(_fixture.Factory, _authorization);

        foreach (string term in new[] { "Rice", "RICE", "6221000000017" })
        {
            Result<ProductPage> found = await handler.HandleAsync(
                new SearchProductsQuery(new ProductFilter(term), 0, 50),
                CancellationToken.None);

            found.IsSuccess.ShouldBeTrue();
            found.Value.TotalCount.ShouldBe(1, $"'{term}' should have found the product");
        }
    }

    [Fact]
    public async Task A_product_is_found_by_its_extra_barcode()
    {
        await SaveProductAsync("RICE-001", "Rice", "6221000000017", ["6221000000099"]);

        var handler = new SearchProductsQueryHandler(_fixture.Factory, _authorization);

        Result<ProductPage> found = await handler.HandleAsync(
            new SearchProductsQuery(new ProductFilter("6221000000099"), 0, 50),
            CancellationToken.None);

        found.Value.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_wildcard_in_the_search_is_taken_literally()
    {
        await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        var handler = new SearchProductsQueryHandler(_fixture.Factory, _authorization);

        Result<ProductPage> found = await handler.HandleAsync(
            new SearchProductsQuery(new ProductFilter("%"), 0, 50),
            CancellationToken.None);

        found.Value.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_category_a_unit_and_a_supplier_are_created()
    {
        var categoryHandler = new SaveCategoryCommandHandler(_fixture.Factory, _authorization, _auditWriter, new TestClock());
        var unitHandler = new SaveUnitCommandHandler(_fixture.Factory, _authorization, _auditWriter);
        var supplierHandler = new SaveSupplierCommandHandler(_fixture.Factory, _authorization, _auditWriter, new TestClock());

        Result<int> category = await categoryHandler.HandleAsync(
            new SaveCategoryCommand(null, "Grocery", "بقالة", true),
            CancellationToken.None);

        Result<int> unit = await unitHandler.HandleAsync(
            new SaveUnitCommand(null, "Box", "صندوق", 0, true),
            CancellationToken.None);

        Result<int> supplier = await supplierHandler.HandleAsync(
            new SaveSupplierCommand(null, "Test Supplier", "مورد", "01001234567", "Delivers on Sunday", true),
            CancellationToken.None);

        category.IsSuccess.ShouldBeTrue();
        unit.IsSuccess.ShouldBeTrue();
        supplier.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_name_that_is_already_used_is_refused()
    {
        var handler = new SaveCategoryCommandHandler(_fixture.Factory, _authorization, _auditWriter, new TestClock());

        await handler.HandleAsync(new SaveCategoryCommand(null, "Grocery", null, true), CancellationToken.None);

        Result<int> again = await handler.HandleAsync(
            new SaveCategoryCommand(null, "grocery", null, true),
            CancellationToken.None);

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe(SaveCategoryCommandHandler.DuplicateNameCode);
    }

    [Fact]
    public async Task The_precision_of_a_unit_that_products_use_cannot_be_changed()
    {
        var unitHandler = new SaveUnitCommandHandler(_fixture.Factory, _authorization, _auditWriter);

        Result<int> unit = await unitHandler.HandleAsync(
            new SaveUnitCommand(null, "Box", null, 0, true),
            CancellationToken.None);

        unit.IsSuccess.ShouldBeTrue();

        await SaveProductAsync("RICE-001", "Rice", "6221000000017", unitId: unit.Value);

        Result<int> changed = await unitHandler.HandleAsync(
            new SaveUnitCommand(unit.Value, "Box", null, 3, true),
            CancellationToken.None);

        changed.IsFailure.ShouldBeTrue();
        changed.Error.Code.ShouldBe(SaveUnitCommandHandler.PrecisionInUseCode);
    }

    [Fact]
    public async Task A_product_list_is_exported_and_read_back()
    {
        await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        string path = Path.Combine(Path.GetTempPath(), $"storeflow-export-{Guid.NewGuid():N}.csv");

        try
        {
            var exportHandler = new ExportProductsCommandHandler(
                _fixture.Factory,
                _authorization,
                _auditWriter,
                new CsvExportService());

            var headers = new Dictionary<string, string>
            {
                [ProductCsvMapper.FieldSku] = "SKU",
                [ProductCsvMapper.FieldName] = "Name",
                [ProductCsvMapper.FieldCategory] = "Category",
                [ProductCsvMapper.FieldUnit] = "Unit",
                [ProductCsvMapper.FieldTaxCategory] = "TaxCategory",
                [ProductCsvMapper.FieldSalePrice] = "SalePrice",
                [ProductCsvMapper.FieldCostPrice] = "CostPrice",
                [ProductCsvMapper.FieldReorderLevel] = "ReorderLevel",
                [ProductCsvMapper.FieldBarcode] = "Barcode",
                [ProductCsvMapper.FieldAdditionalBarcodes] = "AdditionalBarcodes",
                [ProductCsvMapper.FieldDescription] = "Description",
                [ProductCsvMapper.FieldIsActive] = "IsActive",
                [ProductCsvMapper.FieldNameAr] = "NameAr",
            };

            Result<int> exported = await exportHandler.HandleAsync(
                new ExportProductsCommand(path, new ProductFilter(), headers),
                CancellationToken.None);

            exported.IsSuccess.ShouldBeTrue();
            exported.Value.ShouldBe(1);

            File.Exists(path).ShouldBeTrue();

            var previewHandler = new PreviewProductImportQueryHandler(_fixture.Factory, _authorization);

            Result<ProductImportPreview> preview = await previewHandler.HandleAsync(
                new PreviewProductImportQuery(path, ProductImportMode.UpsertBySku, headers),
                CancellationToken.None);

            preview.IsSuccess.ShouldBeTrue();
            preview.Value.Rows.ShouldHaveSingleItem();

            // The exported file names a product that already exists, so it is read back as an update
            // of that product rather than as a new one.
            preview.Value.Rows[0].IsValid.ShouldBeTrue();
            preview.Value.Rows[0].IsUpdate.ShouldBeTrue();
            preview.Value.Rows[0].Resolved!.Sku.ShouldBe("RICE-001");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_file_of_products_is_imported()
    {
        string path = Path.Combine(Path.GetTempPath(), $"storeflow-import-{Guid.NewGuid():N}.csv");

        try
        {
            await File.WriteAllTextAsync(
                path,
                "SKU,Name,SalePrice,CostPrice,Barcode\n" +
                "RICE-001,Rice,450.00,400.00,6221000000017\n" +
                "RICE-002,Rice two,460.00,410.00,6221000000018\n",
                new System.Text.UTF8Encoding(false));

            var handler = new ImportProductsCommandHandler(_fixture.Factory, _authorization, _auditWriter, new TestClock());

            Result<ImportSummary> imported = await handler.HandleAsync(
                new ImportProductsCommand(
                    path,
                    ProductImportMode.CreateOnly,
                    new Dictionary<string, string> { [ProductCsvMapper.FieldSku] = "SKU" }),
                CancellationToken.None);

            imported.IsSuccess.ShouldBeTrue();
            imported.Value.Created.ShouldBe(2);
            imported.Value.Failed.ShouldBe(0);

            await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

            (await context.Products.CountAsync()).ShouldBe(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_row_that_is_already_there_is_left_alone_when_the_import_only_creates()
    {
        await SaveProductAsync("RICE-001", "Rice", "6221000000017");

        string path = Path.Combine(Path.GetTempPath(), $"storeflow-import-{Guid.NewGuid():N}.csv");

        try
        {
            await File.WriteAllTextAsync(
                path,
                "SKU,Name,SalePrice\nRICE-001,Rice changed,500.00\n",
                new System.Text.UTF8Encoding(false));

            var handler = new ImportProductsCommandHandler(_fixture.Factory, _authorization, _auditWriter, new TestClock());

            Result<ImportSummary> imported = await handler.HandleAsync(
                new ImportProductsCommand(
                    path,
                    ProductImportMode.CreateOnly,
                    new Dictionary<string, string> { [ProductCsvMapper.FieldSku] = "SKU" }),
                CancellationToken.None);

            imported.IsSuccess.ShouldBeTrue();
            imported.Value.Created.ShouldBe(0);
            imported.Value.Failed.ShouldBe(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task<Result<int>> SaveProductAsync(
        string sku,
        string name,
        string? barcode,
        IReadOnlyList<string>? additionalBarcodes = null,
        int? unitId = null)
    {
        int resolvedUnitId = unitId ?? await DefaultUnitIdAsync();

        var handler = new SaveProductCommandHandler(
            _fixture.Factory,
            _authorization,
            _auditWriter,
            new TestClock(),
            _imageStore);

        return await handler.HandleAsync(
            new SaveProductCommand(
                null,
                sku,
                name,
                null,
                null,
                null,
                resolvedUnitId,
                await DefaultTaxCategoryIdAsync(),
                new Money(450m),
                new Money(400m),
                0m,
                barcode,
                additionalBarcodes ?? [],
                null,
                true),
            CancellationToken.None);
    }

    private async Task<int> DefaultUnitIdAsync()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        return await context.Units.Where(unit => unit.Name == "Piece").Select(unit => unit.Id).FirstAsync();
    }

    private async Task<int> DefaultTaxCategoryIdAsync()
    {
        await using StoreFlowDbContext context = await _fixture.Factory.CreateDbContextAsync();

        return await context.TaxCategories.Select(tax => tax.Id).FirstAsync();
    }

    /// <summary>Authorises everything, so these tests exercise the catalogue and not the roles.</summary>
    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public bool IsAllowed(Permission permission) => true;

        public Task<Result> RequireAsync(Permission permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>A session with nobody signed in, so the audit trail records system actions.</summary>
    private sealed class NoUserSession : IUserSession
    {
        public UserSession? Current => null;

        public bool IsAuthenticated => false;

        public bool IsLocked => false;

        public void SignIn(UserSession session)
        {
        }

        public void SignOut()
        {
        }

        public void Lock()
        {
        }

        public void Unlock()
        {
        }
    }

    /// <summary>A picture store that keeps the picture in memory, so no file is written by a test.</summary>
    private sealed class TestImageStore : IProductImageStore
    {
        public Task<Result<string>> SaveAsync(string sourceFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string>.Success("products/test.png"));

        public Task<Result> DeleteAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public string ResolvePath(string relativePath) => relativePath;

        public bool Exists(string relativePath) => true;
    }
}
