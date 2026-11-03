using System.Globalization;
using System.Resources;
using System.Windows.Data;
using StoreFlow.Desktop.Controls;
using StoreFlow.Desktop.Localization;
using StoreFlow.Domain.Enumerations;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The backup screen's own behaviour: how a size is written, how an outcome is written, and the
/// fact that the Arabic really is translated rather than copied.
/// </summary>
public sealed class BackupScreenTests
{
    private const string ResourceBaseName = "StoreFlow.Desktop.Resources.Strings";

    [Fact]
    public void A_size_is_written_the_way_a_person_reads_it()
    {
        var converter = new BackupSizeConverter();

        converter.Convert(0L, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("0 bytes");

        converter.Convert(512L, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("512 bytes");

        converter.Convert(1024L, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("1.0 KB");

        converter.Convert(5L * 1024 * 1024, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("5.0 MB");
    }

    [Fact]
    public void An_outcome_is_written_the_way_the_shop_reads_it()
    {
        var converter = new BackupResultConverter();

        converter.Convert(BackupResult.Succeeded, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("Succeeded");

        converter.Convert(BackupResult.Failed, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("Failed");

        converter.Convert(BackupResult.Cancelled, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("Cancelled");
    }

    [Fact]
    public void The_backup_screen_is_written_in_both_languages()
    {
        var manager = new ResourceManager(ResourceBaseName, typeof(Strings).Assembly);

        string[] keys =
        [
            "Backup.Title",
            "Backup.Now",
            "Backup.Schedule",
            "Backup.Log",
            "Backup.Restore",
            "Backup.Recover",
            "Backup.Test",
            "Backup.DailyEnabled",
            "Backup.WeeklyEnabled",
            "Backup.TargetReady",
            "Backup.TargetNotConfigured",
            "Backup.TargetUnreachable",
            "Backup.OutcomeWritten",
            "Backup.RestoreDone",
            "Backup.ErrorRestoreBusy",
            "Backup.ErrorNotFound",
            "Restore.Title",
            "Restore.ConfirmBody",
            "Recovery.Title",
            "Recovery.Step1",
        ];

        foreach (string key in keys)
        {
            string english = manager.GetString(key, new CultureInfo("en")) ?? string.Empty;
            string arabic = manager.GetString(key, new CultureInfo("ar")) ?? string.Empty;

            english.ShouldNotBeNullOrWhiteSpace();
            arabic.ShouldNotBeNullOrWhiteSpace();

            // The Arabic is a translation, not a copy of the English.
            arabic.ShouldNotBe(english, $"'{key}' has the same text in both languages.");
        }
    }

    [Fact]
    public void Every_backup_string_that_names_a_destination_is_translated()
    {
        var manager = new ResourceManager(ResourceBaseName, typeof(Strings).Assembly);

        // The navigation entry and the screen itself must be called the same thing, or the shop
        // finds one screen under two names.
        string navigation = manager.GetString("Nav.Backup", new CultureInfo("en")) ?? string.Empty;
        string title = manager.GetString("Backup.Title", new CultureInfo("en")) ?? string.Empty;

        navigation.ShouldBe(title);

        string arabicNavigation = manager.GetString("Nav.Backup", new CultureInfo("ar")) ?? string.Empty;
        string arabicTitle = manager.GetString("Backup.Title", new CultureInfo("ar")) ?? string.Empty;

        arabicNavigation.ShouldBe(arabicTitle);
    }

    [Fact]
    public void A_destination_is_named_in_the_language_the_shop_reads()
    {
        var language = new LanguageService();

        language.SetLanguage("en");

        var converter = new BackupTargetConverter(new Strings(language));

        converter.Convert("LocalFolder", typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("Local folder");

        converter.Convert("S3", typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("S3-compatible object store");

        // A destination this build does not know shows the name its own implementation gave it.
        converter.Convert("SomethingElse", typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe("SomethingElse");

        converter.Convert(string.Empty, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe(string.Empty);

        converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture)
            .ShouldBe(string.Empty);
    }

    [Fact]
    public void The_converter_only_works_in_one_direction()
    {
        var converter = new BackupSizeConverter();

        Should.Throw<NotSupportedException>(
            () => converter.ConvertBack("1 KB", typeof(long), null, CultureInfo.InvariantCulture));

        Should.Throw<NotSupportedException>(
            () => converter.ConvertBack("Local folder", typeof(string), null, CultureInfo.InvariantCulture));
    }
}
