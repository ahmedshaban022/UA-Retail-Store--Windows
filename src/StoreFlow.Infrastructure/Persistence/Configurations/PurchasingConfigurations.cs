using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoreFlow.Infrastructure.Persistence.Configurations;

/// <summary>Schema for purchase documents.</summary>
internal sealed class PurchaseDocumentConfiguration : IEntityTypeConfiguration<PurchaseDocument>
{
    public void Configure(EntityTypeBuilder<PurchaseDocument> builder)
    {
        builder.ToTable("PurchaseDocuments");
        builder.HasKey(document => document.Id);

        builder.Property(document => document.Number).HasMaxLength(32).IsRequired();
        builder.Property(document => document.Notes).HasMaxLength(1000);

        builder.Property(document => document.Subtotal).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(document => document.Discount).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(document => document.Tax).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(document => document.Total).HasConversion(Converters.MoneyToMinorUnits);

        builder.Property(document => document.BusinessDate).HasColumnType("TEXT");
        builder.Property(document => document.PostedUtc).HasColumnType("TEXT");

        builder.HasIndex(document => document.Number).IsUnique();
        builder.HasIndex(document => document.SupplierId);
        builder.HasIndex(document => document.BusinessDate);
        builder.HasIndex(document => document.Status);

        builder.HasOne(document => document.Supplier)
            .WithMany(supplier => supplier.PurchaseDocuments)
            .HasForeignKey(document => document.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(document => document.User)
            .WithMany()
            .HasForeignKey(document => document.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(document => document.ReversesDocument)
            .WithMany()
            .HasForeignKey(document => document.ReversesDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for purchase lines.</summary>
internal sealed class PurchaseLineConfiguration : IEntityTypeConfiguration<PurchaseLine>
{
    public void Configure(EntityTypeBuilder<PurchaseLine> builder)
    {
        builder.ToTable("PurchaseLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.UnitCost).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.Tax).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.Total).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.Notes).HasMaxLength(500);

        builder.HasIndex(line => line.PurchaseDocumentId);
        builder.HasIndex(line => line.ProductId);

        builder.HasOne(line => line.PurchaseDocument)
            .WithMany(document => document.Lines)
            .HasForeignKey(line => line.PurchaseDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(line => line.Product)
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
