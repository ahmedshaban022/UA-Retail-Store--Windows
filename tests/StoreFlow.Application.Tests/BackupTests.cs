using System.IO.Compression;
using StoreFlow.Application.Backup;
using StoreFlow.Application.Settings;
using Shouldly;

namespace StoreFlow.Application.Tests;

/// <summary>
/// The backup engine's own rules: what an archive says about itself, what a tampered archive
/// looks like, when a copy has grown too old to keep, and when the shop is due a backup.
/// </summary>
public sealed class BackupTests
{
    [Fact]
    public void An_archive_carries_a_manifest_that_describes_every_part()
    {
        string path = WriteArchive(out _);

        BackupArchiveInspection inspection = BackupArchive.Inspect(path);

        inspection.IsIntegrityOk.ShouldBeTrue(inspection.IntegrityMessage);
        inspection.Manifest.ShouldNotBeNull();
        inspection.Manifest!.FormatVersion.ShouldBe(BackupArchive.FormatVersion);
        inspection.Manifest.Parts.Count.ShouldBe(2);
        inspection.Manifest.Parts.ShouldContain(part => part.Path == BackupArchive.DatabaseEntryPath);
        inspection.Manifest.Parts.ShouldContain(part => part.Path == BackupArchive.SettingsEntryPath);
        inspection.ArchiveSha256.ShouldNotBeNullOrWhiteSpace();
        inspection.ArchiveSizeBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_manifest_records_the_checksum_of_every_part()
    {
        string path = WriteArchive(out _);

        using ZipArchive archive = ZipFile.OpenRead(path);

        ZipArchiveEntry database = archive.GetEntry(BackupArchive.DatabaseEntryPath)!;

        using Stream stream = database.Open();

        string actual = BackupArchive.ComputeSha256(stream);

        BackupManifest? manifest = BackupArchive.ReadManifest(path);

        manifest!.Parts
            .Single(part => part.Path == BackupArchive.DatabaseEntryPath)
            .Sha256.ShouldBe(actual);
    }

    [Fact]
    public void A_part_that_was_changed_after_the_archive_was_written_is_refused()
    {
        string path = WriteArchive(out _);
        string tampered = Path.Combine(Path.GetTempPath(), $"storeflow-tampered-{Guid.NewGuid():N}.sfb");

        Rewrite(path, tampered, BackupArchive.DatabaseEntryPath, "these are not the bytes that were written");

        BackupArchiveInspection inspection = BackupArchive.Inspect(tampered);

        inspection.IsIntegrityOk.ShouldBeFalse();
        inspection.MismatchedParts.ShouldContain(BackupArchive.DatabaseEntryPath);
        inspection.IntegrityMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_part_that_was_added_after_the_archive_was_written_is_refused()
    {
        string path = WriteArchive(out _);
        string tampered = Path.Combine(Path.GetTempPath(), $"storeflow-extra-{Guid.NewGuid():N}.sfb");

        Rewrite(path, tampered, "media/smuggled.png", "a part nobody wrote");

        BackupArchiveInspection inspection = BackupArchive.Inspect(tampered);

        inspection.IsIntegrityOk.ShouldBeFalse();
        inspection.MismatchedParts.ShouldContain("media/smuggled.png");
    }

    [Fact]
    public void An_archive_without_a_manifest_is_refused()
    {
        string path = Path.Combine(Path.GetTempPath(), $"storeflow-nomanifest-{Guid.NewGuid():N}.sfb");

        using (FileStream file = new(path, FileMode.Create, FileAccess.Write))
        using (ZipArchive archive = new(file, ZipArchiveMode.Create))
        {
            using Stream content = archive.CreateEntry("something.txt").Open();

            content.Write("hello"u8);
        }

        BackupArchiveInspection inspection = BackupArchive.Inspect(path);

        inspection.IsIntegrityOk.ShouldBeFalse();
        inspection.Manifest.ShouldBeNull();
    }

    [Fact]
    public void The_name_of_a_backup_says_which_backup_it_is()
    {
        DateTimeOffset taken = new(2026, 11, 3, 22, 30, 15, TimeSpan.Zero);

        BackupFileNaming.BuildName(BackupScheduleKind.Daily, taken)
            .ShouldBe("storeflow-daily-2026-11-03-223015Z.sfb");

        BackupFileNaming.BuildName(BackupScheduleKind.Weekly, taken)
            .ShouldBe("storeflow-weekly-2026-11-03-223015Z.sfb");

        BackupFileNaming.BuildName(BackupScheduleKind.Manual, taken)
            .ShouldBe("storeflow-manual-2026-11-03-223015Z.sfb");

        BackupFileNaming.Classify("storeflow-daily-2026-11-03-223015Z.sfb")
            .ShouldBe(BackupScheduleKind.Daily);

        BackupFileNaming.Classify("storeflow-weekly-2026-11-03-223015Z.sfb")
            .ShouldBe(BackupScheduleKind.Weekly);

        BackupFileNaming.Classify("storeflow-manual-2026-11-03-223015Z.sfb")
            .ShouldBe(BackupScheduleKind.Manual);

        BackupFileNaming.Classify("holiday-photo.png").ShouldBeNull();
    }

    [Fact]
    public void The_copy_past_the_number_the_shop_keeps_is_the_one_that_goes()
    {
        var artifacts = new List<BackupArtifactInfo>();

        for (int day = 1; day <= 4; day++)
        {
            artifacts.Add(Daily(day));
        }

        IReadOnlyList<BackupArtifactInfo> expired = BackupRetentionPolicy.SelectExpired(artifacts, 3, 4);

        expired.Count.ShouldBe(1);
        expired[0].FileName.ShouldBe(Daily(1).FileName);
    }

    [Fact]
    public void The_newest_copy_is_never_removed()
    {
        var artifacts = new List<BackupArtifactInfo> { Daily(1), Daily(2) };

        IReadOnlyList<BackupArtifactInfo> expired = BackupRetentionPolicy.SelectExpired(artifacts, 1, 1);

        expired.Count.ShouldBe(1);
        expired[0].FileName.ShouldBe(Daily(1).FileName);
        expired.ShouldNotContain(artifact => artifact.FileName == Daily(2).FileName);
    }

    [Fact]
    public void A_retention_of_zero_still_leaves_the_shop_one_copy()
    {
        var artifacts = new List<BackupArtifactInfo> { Daily(1), Daily(2) };

        IReadOnlyList<BackupArtifactInfo> expired = BackupRetentionPolicy.SelectExpired(artifacts, 0, 0);

        expired.Count.ShouldBe(1);
        expired[0].FileName.ShouldBe(Daily(1).FileName);
    }

    [Fact]
    public void A_backup_the_owner_took_by_hand_is_never_removed_by_the_scheduler()
    {
        var artifacts = new List<BackupArtifactInfo>
        {
            Daily(1),
            Daily(2),
            Manual(3),
        };

        IReadOnlyList<BackupArtifactInfo> expired = BackupRetentionPolicy.SelectExpired(artifacts, 1, 1);

        expired.Count.ShouldBe(1);
        expired[0].FileName.ShouldBe(Daily(1).FileName);
        expired.ShouldNotContain(artifact => artifact.Kind == BackupScheduleKind.Manual);
    }

    [Fact]
    public void The_daily_backup_is_due_once_its_time_has_passed()
    {
        BackupScheduleSettings schedule = BackupScheduleSettings.Defaults;

        var atTenOClock = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        var atEleven = new DateTimeOffset(2026, 11, 3, 23, 0, 0, TimeSpan.Zero);

        BackupSchedulePolicy.Decide(schedule, true, false, atTenOClock, BackupScheduleState.None)
            .IsDailyDue.ShouldBeFalse();

        BackupSchedulePolicy.Decide(schedule, true, false, atEleven, BackupScheduleState.None)
            .IsDailyDue.ShouldBeTrue();
    }

    [Fact]
    public void The_daily_backup_is_not_due_twice_on_the_same_day()
    {
        BackupScheduleSettings schedule = BackupScheduleSettings.Defaults;

        var now = new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.Zero);

        BackupSchedulePolicy.Decide(schedule, true, false, now, new BackupScheduleState(new DateOnly(2026, 11, 3), null))
            .IsDailyDue.ShouldBeFalse();
    }

    [Fact]
    public void A_day_that_was_missed_is_caught_up_once_and_not_once_a_day()
    {
        BackupScheduleSettings schedule = BackupScheduleSettings.Defaults;

        var now = new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.Zero);

        BackupScheduleDecision decision = BackupSchedulePolicy.Decide(
            schedule,
            true,
            false,
            now,
            new BackupScheduleState(new DateOnly(2026, 10, 30), null));

        decision.IsDailyDue.ShouldBeTrue();
    }

    [Fact]
    public void The_weekly_backup_is_due_on_its_day()
    {
        var schedule = new BackupScheduleSettings("LocalFolder", 23 * 60, 6, 23 * 60);

        // 2026-11-07 is a Saturday.
        var saturdayAtTen = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
        var saturdayAtEleven = new DateTimeOffset(2026, 11, 7, 23, 0, 0, TimeSpan.Zero);

        BackupSchedulePolicy.Decide(schedule, false, true, saturdayAtTen, BackupScheduleState.None)
            .IsWeeklyDue.ShouldBeFalse();

        BackupSchedulePolicy.Decide(schedule, false, true, saturdayAtEleven, BackupScheduleState.None)
            .IsWeeklyDue.ShouldBeTrue();
    }

    [Fact]
    public void A_weekly_backup_that_was_missed_is_caught_up_the_next_day()
    {
        var schedule = new BackupScheduleSettings("LocalFolder", 23 * 60, 6, 23 * 60);

        // 2026-11-08 is the Sunday after the Saturday the weekly backup was due.
        var sundayMorning = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

        BackupSchedulePolicy.Decide(schedule, false, true, sundayMorning, BackupScheduleState.None)
            .IsWeeklyDue.ShouldBeTrue();
    }

    [Fact]
    public void A_weekly_backup_that_already_ran_is_not_due_again()
    {
        var schedule = new BackupScheduleSettings("LocalFolder", 23 * 60, 6, 23 * 60);

        var sundayMorning = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

        BackupSchedulePolicy.Decide(schedule, false, true, sundayMorning, new BackupScheduleState(null, new DateOnly(2026, 11, 7)))
            .IsWeeklyDue.ShouldBeFalse();
    }

    [Fact]
    public void A_backup_that_is_switched_off_is_never_due()
    {
        BackupScheduleSettings schedule = BackupScheduleSettings.Defaults;

        var now = new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.Zero);

        BackupScheduleDecision decision = BackupSchedulePolicy.Decide(schedule, false, false, now, BackupScheduleState.None);

        decision.IsDailyDue.ShouldBeFalse();
        decision.IsWeeklyDue.ShouldBeFalse();
    }

    /// <summary>
    /// Writes a small archive with a database part and a settings part.
    /// </summary>
    private static string WriteArchive(out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), "storeflow-archive-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "storeflow-manual-2026-11-03-223015Z.sfb");

        using (FileStream file = new(path, FileMode.Create, FileAccess.Write))
        using (ZipArchive archive = new(file, ZipArchiveMode.Create))
        {
            BackupPartEntry database = BackupArchive.AddPart(archive, BackupArchive.DatabaseEntryPath, "the database"u8.ToArray());
            BackupPartEntry settings = BackupArchive.AddPart(archive, BackupArchive.SettingsEntryPath, "the settings"u8.ToArray());

            var manifest = new BackupManifest
            {
                FormatVersion = BackupArchive.FormatVersion,
                AppVersion = "1.0.0",
                SchemaVersion = "20261101193637_AddSalesReturnsAndReversals",
                CreatedUtc = new DateTimeOffset(2026, 11, 3, 22, 30, 15, TimeSpan.Zero),
                MachineName = "SHOP-PC",
                IsEncrypted = false,
                Parts = [database, settings],
            };

            BackupArchive.WriteManifest(archive, manifest);
        }

        return path;
    }

    /// <summary>
    /// Copies an archive, replacing one part's content on the way.
    /// </summary>
    private static void Rewrite(string source, string destination, string partPath, string content)
    {
        bool replaced = false;

        using (FileStream file = new(destination, FileMode.Create, FileAccess.Write))
        using (ZipArchive archive = new(file, ZipArchiveMode.Create))
        using (ZipArchive original = ZipFile.OpenRead(source))
        {
            foreach (ZipArchiveEntry entry in original.Entries)
            {
                ZipArchiveEntry copy = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);

                using Stream target = copy.Open();

                if (string.Equals(entry.FullName, partPath, StringComparison.Ordinal))
                {
                    target.Write(System.Text.Encoding.UTF8.GetBytes(content));
                    replaced = true;

                    continue;
                }

                using Stream sourceStream = entry.Open();

                sourceStream.CopyTo(target);
            }

            if (!replaced)
            {
                ZipArchiveEntry added = archive.CreateEntry(partPath, CompressionLevel.Optimal);

                using Stream target = added.Open();

                target.Write(System.Text.Encoding.UTF8.GetBytes(content));
            }
        }
    }

    private static BackupArtifactInfo Daily(int day) => new(
        $"C:\\backups\\storeflow-daily-2026-11-{day:00}-223000Z.sfb",
        $"storeflow-daily-2026-11-{day:00}-223000Z.sfb",
        1024,
        new DateTimeOffset(2026, 11, day, 22, 30, 0, TimeSpan.Zero),
        BackupScheduleKind.Daily);

    private static BackupArtifactInfo Manual(int day) => new(
        $"C:\\backups\\storeflow-manual-2026-11-{day:00}-223000Z.sfb",
        $"storeflow-manual-2026-11-{day:00}-223000Z.sfb",
        1024,
        new DateTimeOffset(2026, 11, day, 22, 30, 0, TimeSpan.Zero),
        BackupScheduleKind.Manual);
}
