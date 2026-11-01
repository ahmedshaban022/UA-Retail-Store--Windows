using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoreFlow.Infrastructure.Persistence.Configurations;

/// <summary>Schema for suspended carts.</summary>
internal sealed class HeldCartConfiguration : IEntityTypeConfiguration<HeldCart>
{
    public void Configure(EntityTypeBuilder<HeldCart> builder)
    {
        builder.ToTable("HeldCarts");
        builder.HasKey(cart => cart.Id);

        builder.Property(cart => cart.Number).HasMaxLength(32).IsRequired();
        builder.Property(cart => cart.Label).HasMaxLength(60).IsRequired();
        builder.Property(cart => cart.ContentJson).IsRequired();

        builder.Property(cart => cart.HeldUtc).HasColumnType("TEXT");
        builder.Property(cart => cart.UpdatedUtc).HasColumnType("TEXT");

        builder.HasIndex(cart => cart.Number).IsUnique();
        builder.HasIndex(cart => cart.HeldUtc);

        builder.HasOne(cart => cart.User)
            .WithMany()
            .HasForeignKey(cart => cart.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
