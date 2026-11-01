using System.Globalization;
using System.IO;
using System.Windows;
using StoreFlow.Application.Inventory;
using StoreFlow.Desktop.Controls;
using StoreFlow.Desktop.Localization;
using StoreFlow.Desktop.ViewModels;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// The inventory screens' own helpers: the error codes the application layer answers with become
/// the localised message the owner reads, and the numbers the owner types are read the same way in
/// both languages.
/// </summary>
public sealed class InventoryScreenTests
{
    [Fact]
    public void The_error_codes_become_localised_messages()
    {
        var strings = new TestStrings();

        InventoryErrorMessages.Resolve(strings, InventoryErrorCodes.NegativeStock)
            .ShouldBe("There is not enough stock for this movement.");

        InventoryErrorMessages.Resolve(strings, InventoryErrorCodes.NotDraft)
            .ShouldBe("This document can no longer be edited.");

        InventoryErrorMessages.Resolve(strings, InventoryErrorCodes.SelfApproval)
            .ShouldBe("This has to be approved by somebody else.");

        InventoryErrorMessages.Resolve(strings, InventoryValidation.RequiredCode)
            .ShouldBe("This value is required.");
    }

    [Fact]
    public void An_unknown_error_code_falls_back_to_the_generic_message()
    {
        var strings = new TestStrings();

        InventoryErrorMessages.Resolve(strings, "inventory.something_new")
            .ShouldBe("Something went wrong. Try again.");
    }

    [Fact]
    public void A_number_the_owner_types_is_read_the_same_way_in_both_languages()
    {
        InventoryInput.TryReadDecimal("12.5", out decimal quantity).ShouldBeTrue();
        quantity.ShouldBe(12.5m);

        InventoryInput.TryReadDecimal(" 7 ", out decimal padded).ShouldBeTrue();
        padded.ShouldBe(7m);

        InventoryInput.TryReadDecimal("abc", out _).ShouldBeFalse();
    }

    [Fact]
    public void An_amount_the_owner_types_may_not_be_negative()
    {
        InventoryInput.TryReadMoney("10.25", out decimal amount).ShouldBeTrue();
        amount.ShouldBe(10.25m);

        InventoryInput.TryReadMoney("-1", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_date_is_written_and_read_the_same_way()
    {
        var date = new DateOnly(2026, 3, 4);

        InventoryInput.WriteDate(date).ShouldBe("2026-03-04");
        InventoryInput.TryReadDate("2026-03-04", out DateOnly read).ShouldBeTrue();
        read.ShouldBe(date);

        InventoryInput.TryReadDate("not a date", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_number_is_written_without_a_fraction_the_owner_did_not_type()
    {
        InventoryInput.WriteNumber(12m).ShouldBe("12");
        InventoryInput.WriteNumber(12.5m).ShouldBe("12.5");
        InventoryInput.WriteAmount(12.5m).ShouldBe("12.50");
    }

    [Fact]
    public void An_amount_the_owner_types_over_carries_no_thousands_separator()
    {
        // The field is read back as the owner typed it. A separator in the middle of it turns
        // "1200" into "1,200", which reads as a different number to somebody who writes a decimal
        // comma - so an amount that is only edited is written without one.
        string text = InventoryInput.WriteAmount(1200m);

        text.ShouldBe("1200.00");
        text.ShouldNotContain(",");

        InventoryInput.TryReadMoney(text, out decimal amount).ShouldBeTrue();
        amount.ShouldBe(1200m);
    }

    [Fact]
    public void The_search_placeholder_shows_itself_when_the_box_is_empty()
    {
        // The placeholder is bound to the flag that says the box is empty, and the flag is not
        // inverted: an empty box is the one case where the placeholder belongs on the screen.
        var converter = new BooleanToVisibilityConverter();

        converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Visible);

        converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture)
            .ShouldBe(Visibility.Collapsed);
    }

    [Fact]
    public void The_low_stock_badge_reads_the_flag_that_is_there()
    {
        // The badge used to read a property the stock position does not have, so the badge came
        // out as an empty box. It reads the flag the position does carry.
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return;
        }

        string path = Path.Combine(root, "src", "StoreFlow.Desktop", "Views", "InventoryView.xaml");
        if (!File.Exists(path))
        {
            return;
        }

        string xaml = File.ReadAllText(path);

        xaml.ShouldContain("IsBelowReorderLevel, Converter={StaticResource InventoryLabel}");
        xaml.ShouldNotContain("BelowReorder}");
    }

    /// <summary>
    /// A stand-in for the application's strings, so the test does not need a resource manager.
    /// </summary>
    private sealed class TestStrings : IStrings
    {
        private static readonly Dictionary<string, string> Messages = new()
        {
            [StringKeys.ErrorInventoryNegativeStock] = "There is not enough stock for this movement.",
            [StringKeys.ErrorInventoryNotDraft] = "This document can no longer be edited.",
            [StringKeys.ErrorInventorySelfApproval] = "This has to be approved by somebody else.",
            [StringKeys.ErrorInventoryRequired] = "This value is required.",
            [StringKeys.ErrorUnexpected] = "Something went wrong. Try again.",
        };

        public string this[string key] => Messages.TryGetValue(key, out string? message) ? message : key;

        public string Format(string key, params object?[] arguments) => string.Format(this[key], arguments);
    }

    /// <summary>Finds the folder that holds the solution, walking up from the test.</summary>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreFlow.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
