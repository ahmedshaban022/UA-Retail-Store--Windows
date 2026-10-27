using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoreFlow.Infrastructure.Persistence.Configurations;

/// <summary>Schema for roles.</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(role => role.Id);

        builder.Property(role => role.Code).HasMaxLength(40).IsRequired();
        builder.Property(role => role.Name).HasMaxLength(80).IsRequired();
        builder.Property(role => role.NameAr).HasMaxLength(80);

        builder.HasIndex(role => role.Code).IsUnique();
    }
}

/// <summary>Schema for the role-permission matrix.</summary>
internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(rolePermission => new { rolePermission.RoleId, rolePermission.Permission });

        builder.HasOne(rolePermission => rolePermission.Role)
            .WithMany(role => role.RolePermissions)
            .HasForeignKey(rolePermission => rolePermission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Schema for user accounts.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Username).HasMaxLength(80).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(400).IsRequired();

        builder.HasIndex(user => user.Username).IsUnique();
        builder.HasIndex(user => user.RoleId);

        builder.Property(user => user.LastLoginUtc).HasColumnType("TEXT");
        builder.Property(user => user.CreatedUtc).HasColumnType("TEXT");
        builder.Property(user => user.UpdatedUtc).HasColumnType("TEXT");

        builder.HasOne(user => user.Role)
            .WithMany(role => role.Users)
            .HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for approval requests.</summary>
internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.PayloadJson).IsRequired();
        builder.Property(request => request.DecisionReason).HasMaxLength(500);

        builder.Property(request => request.CreatedUtc).HasColumnType("TEXT");
        builder.Property(request => request.DecidedUtc).HasColumnType("TEXT");

        builder.HasIndex(request => request.Status);
        builder.HasIndex(request => request.CreatedUtc);

        builder.HasOne(request => request.RequestedByUser)
            .WithMany()
            .HasForeignKey(request => request.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(request => request.ApprovedByUser)
            .WithMany()
            .HasForeignKey(request => request.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
