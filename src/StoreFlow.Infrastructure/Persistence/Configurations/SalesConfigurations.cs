using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoreFlow.Infrastructure.Persistence.Configurations;

/// <summary>Schema for sales invoices.</summary>
internal sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        builder.ToTable("SalesInvoices");
        builder.HasKey(invoice => invoice.Id);

        builder.Property(invoice => invoice.Number).HasMaxLength(32).IsRequired();
        builder.Property(invoice => invoice.Notes).HasMaxLength(1000);

        builder.Property(invoice => invoice.Subtotal).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(invoice => invoice.Discount).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(invoice => invoice.Tax).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(invoice => invoice.Total).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(invoice => invoice.Paid).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(invoice => invoice.Change).HasConversion(Converters.MoneyToMinorUnits);

        builder.Property(invoice => invoice.BusinessDate).HasColumnType("TEXT");
        builder.Property(invoice => invoice.PostedUtc).HasColumnType("TEXT");

        builder.HasIndex(invoice => invoice.Number).IsUnique();
        builder.HasIndex(invoice => invoice.ClientOperationId).IsUnique();
        builder.HasIndex(invoice => invoice.BusinessDate);
        builder.HasIndex(invoice => invoice.PostedUtc);
        builder.HasIndex(invoice => invoice.UserId);

        builder.HasOne(invoice => invoice.User)
            .WithMany()
            .HasForeignKey(invoice => invoice.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for sales invoice lines, including their immutable snapshots.</summary>
internal sealed class SalesInvoiceLineConfiguration : IEntityTypeConfiguration<SalesInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceLine> builder)
    {
        builder.ToTable("SalesInvoiceLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.NameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(line => line.SkuSnapshot).HasMaxLength(64).IsRequired();
        builder.Property(line => line.BarcodeSnapshot).HasMaxLength(64);
        builder.Property(line => line.UnitSnapshot).HasMaxLength(60).IsRequired();

        builder.Property(line => line.UnitPrice).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.Discount).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.InvoiceDiscountShare).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.Tax).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.Total).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.UnitCostSnapshot).HasConversion(Converters.MoneyToMinorUnits);

        builder.HasIndex(line => line.SalesInvoiceId);
        builder.HasIndex(line => line.ProductId);

        builder.HasOne(line => line.SalesInvoice)
            .WithMany(invoice => invoice.Lines)
            .HasForeignKey(line => line.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(line => line.Product)
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for payments.</summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Amount).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(payment => payment.CashReceived).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(payment => payment.Change).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(payment => payment.Reference).HasMaxLength(120);

        builder.HasIndex(payment => payment.SalesInvoiceId);

        builder.HasOne(payment => payment.SalesInvoice)
            .WithMany(invoice => invoice.Payments)
            .HasForeignKey(payment => payment.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Schema for sales returns.</summary>
internal sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.ToTable("SalesReturns");
        builder.HasKey(salesReturn => salesReturn.Id);

        builder.Property(salesReturn => salesReturn.Number).HasMaxLength(32).IsRequired();
        builder.Property(salesReturn => salesReturn.Reason).HasMaxLength(500).IsRequired();
        builder.Property(salesReturn => salesReturn.Notes).HasMaxLength(1000);

        builder.Property(salesReturn => salesReturn.RefundTotal).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(salesReturn => salesReturn.BusinessDate).HasColumnType("TEXT");
        builder.Property(salesReturn => salesReturn.PostedUtc).HasColumnType("TEXT");

        builder.HasIndex(salesReturn => salesReturn.Number).IsUnique();
        builder.HasIndex(salesReturn => salesReturn.OriginalInvoiceId);
        builder.HasIndex(salesReturn => salesReturn.BusinessDate);

        builder.HasOne(salesReturn => salesReturn.OriginalInvoice)
            .WithMany()
            .HasForeignKey(salesReturn => salesReturn.OriginalInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(salesReturn => salesReturn.User)
            .WithMany()
            .HasForeignKey(salesReturn => salesReturn.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for sales return lines.</summary>
internal sealed class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine>
{
    public void Configure(EntityTypeBuilder<SalesReturnLine> builder)
    {
        builder.ToTable("SalesReturnLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.RefundAmount).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.OriginalUnitCost).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.Reason).HasMaxLength(500);

        builder.HasIndex(line => line.SalesReturnId);
        builder.HasIndex(line => line.SalesInvoiceLineId);

        builder.HasOne(line => line.SalesReturn)
            .WithMany(salesReturn => salesReturn.Lines)
            .HasForeignKey(line => line.SalesReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(line => line.SalesInvoiceLine)
            .WithMany(invoiceLine => invoiceLine.ReturnLines)
            .HasForeignKey(line => line.SalesInvoiceLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(line => line.Product)
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
