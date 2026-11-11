using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoreFlow.Infrastructure.Persistence.Configurations;

/// <summary>Schema for the authoritative stock ledger.</summary>
internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(movement => movement.Id);

        builder.Property(movement => movement.UnitCost).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(movement => movement.Reason).HasMaxLength(500);

        builder.Property(movement => movement.PostedUtc).HasColumnType("TEXT");
        builder.Property(movement => movement.BusinessDate).HasColumnType("TEXT");

        builder.HasIndex(movement => new { movement.ProductId, movement.Id });
        builder.HasIndex(movement => movement.PostedUtc);
        builder.HasIndex(movement => new { movement.MovementType, movement.DocumentId });
        builder.HasIndex(movement => movement.BusinessDate);

        builder.HasOne(movement => movement.Product)
            .WithMany()
            .HasForeignKey(movement => movement.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(movement => movement.User)
            .WithMany()
            .HasForeignKey(movement => movement.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for the cached stock balance.</summary>
internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("StockBalances");
        builder.HasKey(balance => balance.ProductId);

        builder.Property(balance => balance.AverageUnitCost).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(balance => balance.UpdatedUtc).HasColumnType("TEXT");

        // The version is a real concurrency token: the update carries the version it read, so a
        // write that landed on a balance somebody else has already moved is refused instead of
        // quietly overwriting it. The ledger and the balance have to agree, and this is what keeps
        // them agreeing when two people move the same stock at once (PRD §5.4 invariant 1).
        builder.Property(balance => balance.Version).IsConcurrencyToken();

        builder.HasIndex(balance => balance.QuantityOnHandScaled);

        builder.HasOne(balance => balance.Product)
            .WithMany()
            .HasForeignKey(balance => balance.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Schema for stock count sessions.</summary>
internal sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.ToTable("StockCounts");
        builder.HasKey(count => count.Id);

        builder.Property(count => count.Number).HasMaxLength(32).IsRequired();
        builder.Property(count => count.Reason).HasMaxLength(500);

        builder.Property(count => count.BusinessDate).HasColumnType("TEXT");
        builder.Property(count => count.CreatedUtc).HasColumnType("TEXT");
        builder.Property(count => count.ApprovedUtc).HasColumnType("TEXT");

        builder.HasIndex(count => count.Number).IsUnique();
        builder.HasIndex(count => count.BusinessDate);

        builder.HasOne(count => count.User)
            .WithMany()
            .HasForeignKey(count => count.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(count => count.ApprovedByUser)
            .WithMany()
            .HasForeignKey(count => count.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for counted stock lines.</summary>
internal sealed class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> builder)
    {
        builder.ToTable("StockCountLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.UnitCost).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.DifferenceValue).HasConversion(Converters.MoneyToMinorUnits);
        builder.Property(line => line.Reason).HasMaxLength(500);

        builder.HasIndex(line => line.StockCountId);
        builder.HasIndex(line => line.ProductId);

        builder.HasOne(line => line.StockCount)
            .WithMany(count => count.Lines)
            .HasForeignKey(line => line.StockCountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(line => line.Product)
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
