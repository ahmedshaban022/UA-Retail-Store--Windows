using StoreFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoreFlow.Infrastructure.Persistence.Configurations;

/// <summary>Schema for configuration entries.</summary>
internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("Settings");
        builder.HasKey(setting => setting.Key);

        builder.Property(setting => setting.Key).HasMaxLength(120);
        builder.Property(setting => setting.Value).IsRequired();
        builder.Property(setting => setting.UpdatedUtc).HasColumnType("TEXT");
    }
}

/// <summary>Schema for the audit trail.</summary>
internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.Action).HasMaxLength(80).IsRequired();
        builder.Property(log => log.EntityType).HasMaxLength(80).IsRequired();
        builder.Property(log => log.EntityId).HasMaxLength(64);
        builder.Property(log => log.BeforeHash).HasMaxLength(128);
        builder.Property(log => log.AfterSummary).HasMaxLength(1000);

        builder.Property(log => log.TimestampUtc).HasColumnType("TEXT");

        builder.HasIndex(log => log.TimestampUtc);
        builder.HasIndex(log => new { log.EntityType, log.EntityId });

        builder.HasOne(log => log.ActorUser)
            .WithMany()
            .HasForeignKey(log => log.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Schema for backup history.</summary>
internal sealed class BackupLogConfiguration : IEntityTypeConfiguration<BackupLog>
{
    public void Configure(EntityTypeBuilder<BackupLog> builder)
    {
        builder.ToTable("BackupLogs");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.TargetKind).HasMaxLength(60).IsRequired();
        builder.Property(log => log.Path).HasMaxLength(1000);
        builder.Property(log => log.Sha256).HasMaxLength(128);
        builder.Property(log => log.Message).HasMaxLength(2000);

        builder.Property(log => log.CreatedUtc).HasColumnType("TEXT");

        builder.HasIndex(log => log.CreatedUtc);
    }
}

/// <summary>Schema for the idempotency ledger.</summary>
internal sealed class RequestOutcomeConfiguration : IEntityTypeConfiguration<RequestOutcome>
{
    public void Configure(EntityTypeBuilder<RequestOutcome> builder)
    {
        builder.ToTable("RequestOutcomes");
        builder.HasKey(outcome => outcome.ClientOperationId);

        builder.Property(outcome => outcome.ClientOperationId).HasMaxLength(64);
        builder.Property(outcome => outcome.Kind).HasMaxLength(60).IsRequired();
        builder.Property(outcome => outcome.PayloadHash).HasMaxLength(128).IsRequired();
        builder.Property(outcome => outcome.ResultJson);

        builder.Property(outcome => outcome.CreatedUtc).HasColumnType("TEXT");
        builder.Property(outcome => outcome.CompletedUtc).HasColumnType("TEXT");

        builder.HasIndex(outcome => outcome.Status);
    }
}
