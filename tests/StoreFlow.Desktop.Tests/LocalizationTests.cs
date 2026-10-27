using System.Globalization;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

public sealed class LocalizationTests
{
    private const string ResourceBaseName = "StoreFlow.Desktop.Resources.Strings";

    [Fact]
    public void The_english_and_arabic_resource_sets_hold_the_same_keys()
    {
        var manager = new ResourceManager(ResourceBaseName, typeof(Strings).Assembly);

        HashSet<string> englishKeys = Keys(manager, new CultureInfo("en"));
        HashSet<string> arabicKeys = Keys(manager, new CultureInfo("ar"), useParents: false);

        englishKeys.ShouldNotBeEmpty();
        arabicKeys.ShouldNotBeEmpty();

        arabicKeys.ShouldBe(englishKeys, ignoreOrder: true);
    }

    [Fact]
    public void The_arabic_resource_set_is_really_arabic()
    {
        var manager = new ResourceManager(ResourceBaseName, typeof(Strings).Assembly);

        manager.GetString("Nav.Dashboard", new CultureInfo("ar")).ShouldBe("لوحة التحكم");
        manager.GetString("Nav.Dashboard", new CultureInfo("en")).ShouldBe("Dashboard");
    }

    [Fact]
    public void The_localization_service_resolves_the_resources_through_dependency_injection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        using ServiceProvider provider = services.BuildServiceProvider();

        IStringLocalizer<Strings> localizer = provider.GetRequiredService<IStringLocalizer<Strings>>();

        localizer["Login.SignIn"].Value.ShouldBe("Sign in");
        localizer["Login.Username"].Value.ShouldBe("User name");
    }

    [Fact]
    public void Every_string_key_exists_in_both_languages()
    {
        var manager = new ResourceManager(ResourceBaseName, typeof(Strings).Assembly);

        HashSet<string> englishKeys = Keys(manager, new CultureInfo("en"));

        foreach (string key in englishKeys)
        {
            manager.GetString(key, new CultureInfo("en")).ShouldNotBeNullOrWhiteSpace(key);
            manager.GetString(key, new CultureInfo("ar")).ShouldNotBeNullOrWhiteSpace(key);
        }
    }

    private static HashSet<string> Keys(ResourceManager manager, CultureInfo culture, bool useParents = true)
    {
        ResourceSet? set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: useParents);

        if (set is null)
        {
            return [];
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in set)
        {
            keys.Add((string)entry.Key);
        }

        return keys;
    }
}
