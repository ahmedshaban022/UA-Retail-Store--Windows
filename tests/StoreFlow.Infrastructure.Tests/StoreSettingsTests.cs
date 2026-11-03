using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Settings;
using StoreFlow.Domain.Entities;
using StoreFlow.Infrastructure.Backup;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Infrastructure.Settings;
using Shouldly;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// Covers the shop's own settings.
/// </summary>
/// <remarks>
/// The installation lays the settings down once, when the shop is new. From then on they belong to
/// the shop: a shop that moves its backup folder, or changes the discount it allows, must still
/// have those settings after the next restart. The installer has no say in them.
/// </remarks>
public sealed class StoreSettingsTests
{
    [Fact]
    public async Task A_new_shop_starts_with_the_default_settings()
    {
        await using Profile profile = await Profile.CreateAsync();

        var reader = new StoreSettingsReader(profile.Factory);

        StoreSettings settings = await reader.GetAsync();

        settings.DailyBackupRetention.ShouldBe(StoreSettings.Defaults.DailyBackupRetention);
        settings.WeeklyBackupRetention.ShouldBe(StoreSettings.Defaults.WeeklyBackupRetention);
        settings.BackupDirectory.ShouldBeNull();
        settings.MaximumDiscountPercent.ShouldBe(StoreSettings.Defaults.MaximumDiscountPercent);
    }

    [Fact]
    public async Task The_shop_s_settings_survive_a_restart()
    {
        await using Profile profile = await Profile.CreateAsync();

        var store = new BackupSettingsStore(profile.Factory, new MutableClock());
        var reader = new StoreSettingsReader(profile.Factory);

        await store.SaveAsync(StoreSettings.Defaults with
        {
            BackupDirectory = @"D:\shop-backups",
            DailyBackupRetention = 5,
            WeeklyBackupRetention = 2,
            MaximumDiscountPercent = 25m,
        });

        // The application is started again.
        var initializer = new DatabaseInitializer(profile.Factory, new MutableClock());
        await initializer.InitializeAsync();

        StoreSettings settings = await reader.GetAsync();

        settings.BackupDirectory.ShouldBe(@"D:\shop-backups");
        settings.DailyBackupRetention.ShouldBe(5);
        settings.WeeklyBackupRetention.ShouldBe(2);
        settings.MaximumDiscountPercent.ShouldBe(25m);
    }

    [Fact]
    public async Task The_shop_s_details_survive_a_restart()
    {
        await using Profile profile = await Profile.CreateAsync();

        var details = new StoreDetails("Al-Masria Store", null, "12 Tahrir Street", "+201000000000", null, null);

        await using (StoreFlowDbContext context = await profile.Factory.CreateDbContextAsync())
        {
            // The shop writes its own details over the ones the installation laid down, the way the
            // first-run wizard does.
            Setting existing = await context.Settings
                .SingleAsync(item => item.Key == DatabaseInitializer.StoreDetailsSettingKey);

            existing.Value = JsonSerializer.Serialize(details);

            await context.SaveChangesAsync();
        }

        // The application is started again.
        var initializer = new DatabaseInitializer(profile.Factory, new MutableClock());
        await initializer.InitializeAsync();

        await using StoreFlowDbContext readContext = await profile.Factory.CreateDbContextAsync();

        Setting setting = await readContext.Settings
            .SingleAsync(item => item.Key == DatabaseInitializer.StoreDetailsSettingKey);

        setting.Value.ShouldContain("Al-Masria Store");
        setting.Value.ShouldContain("12 Tahrir Street");
    }
}
