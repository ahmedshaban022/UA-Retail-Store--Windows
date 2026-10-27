using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoreFlow.Infrastructure.Persistence.Configurations;

/// <summary>Schema for the catalogue: categories, units, tax categories and products.</summary>
internal sealed class CatalogConfigurations : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name).HasMaxLength(120).IsRequired();
        builder.Property(category => category.NameAr).HasMaxLength(120);
        builder.HasIndex(category => category.Name).IsUnique();

        builder.Property(category => category.CreatedUtc).HasColumnType("TEXT");
        builder.Property(category => category.UpdatedUtc).HasColumnType("TEXT");
    }
}

/// <summary>Schema for selling units.</summary>
internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units");
        builder.HasKey(unit => unit.Id);

        builder.Property(unit => unit.Name).HasMaxLength(60).IsRequired();
        builder.Property(unit => unit.NameAr).HasMaxLength(60);
        builder.HasIndex(unit => unit.Name).IsUnique();
    }
}

/// <summary>Schema for tax categories.</summary>
internal sealed class TaxCategoryConfiguration : IEntityTypeConfiguration<TaxCategory>
{
    public void Configure(EntityTypeBuilder<TaxCategory> builder)
    {
        builder.ToTable("TaxCategories");
        builder.HasKey(taxCategory => taxCategory.Id);

        builder.Property(taxCategory => taxCategory.Code).HasMaxLength(40).IsRequired();
        builder.Property(taxCategory => taxCategory.Name).HasMaxLength(80).IsRequired();
        builder.HasIndex(taxCategory => taxCategory.Code).IsUnique();

        builder.Property(taxCategory => taxCategory.EffectiveFromUtc).HasColumnType("TEXT");
    }
}

/// <summary>Schema for products.</summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(product => product.Id);

        builder.Property(product => product.Sku).HasMaxLength(64).IsRequired();
        builder.Property(product => product.Name).HasMaxLength(200).IsRequired();
        builder.Property(product => product.NameAr).HasMaxLength(200);
        builder.Property(product => product.Description).HasMaxLength(1000);
        builder.Property(product => product.Barcode).HasMaxLength(64);
        builder.Property(product => product.ImagePath).HasMaxLength(400);

        builder.Property(product => product.SalePrice).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(product => product.CostPrice).HasConversion(Converters.MoneyToMinorUnits);

        builder.HasIndex(product => product.Sku).IsUnique();
        builder.HasIndex(product => product.Barcode)
            .IsUnique()
            .HasFilter("\"Barcode\" IS NOT NULL AND \"Barcode\" <> ''");
        builder.HasIndex(product => new { product.IsActive, product.Name });
        builder.HasIndex(product => new { product.CategoryId, product.IsActive });

        builder.Property(product => product.CreatedUtc).HasColumnType("TEXT");
        builder.Property(product => product.UpdatedUtc).HasColumnType("TEXT");

        builder.HasOne(product => product.Category)
            .WithMany(category => category.Products)
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(product => product.Unit)
            .WithMany(unit => unit.Products)
            .HasForeignKey(product => product.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(product => product.TaxCategory)
            .WithMany(taxCategory => taxCategory.Products)
            .HasForeignKey(product => product.TaxCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for additional product barcodes.</summary>
internal sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.ToTable("ProductBarcodes");
        builder.HasKey(barcode => barcode.Id);

        builder.Property(barcode => barcode.Barcode).HasMaxLength(64).IsRequired();
        builder.HasIndex(barcode => barcode.Barcode).IsUnique();
        builder.HasIndex(barcode => barcode.ProductId);

        builder.HasOne(barcode => barcode.Product)
            .WithMany(product => product.Barcodes)
            .HasForeignKey(barcode => barcode.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Schema for suppliers.</summary>
internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(supplier => supplier.Id);

        builder.Property(supplier => supplier.Name).HasMaxLength(160).IsRequired();
        builder.Property(supplier => supplier.NameAr).HasMaxLength(160);
        builder.Property(supplier => supplier.Phone).HasMaxLength(40);
        builder.Property(supplier => supplier.Notes).HasMaxLength(1000);

        builder.HasIndex(supplier => supplier.Name).IsUnique();

        builder.Property(supplier => supplier.CreatedUtc).HasColumnType("TEXT");
        builder.Property(supplier => supplier.UpdatedUtc).HasColumnType("TEXT");
    }
}
