using StoreFlow.Application.Theming;
using Shouldly;

namespace StoreFlow.Application.Tests;

public sealed class ThemeContrastValidatorTests
{
    [Fact]
    public void Black_on_white_is_the_maximum_contrast()
    {
        double ratio = ThemeContrastValidator.ContrastRatio("#000000", "#FFFFFF");

        ratio.ShouldBe(21d, 0.01d);
    }

    [Fact]
    public void The_same_colour_has_no_contrast()
    {
        ThemeContrastValidator.ContrastRatio("#123456", "#123456").ShouldBe(1d, 0.001d);
    }

    [Fact]
    public void A_short_hex_colour_is_expanded()
    {
        ThemeContrastValidator.ContrastRatio("#000", "#FFF").ShouldBe(21d, 0.01d);
    }

    [Fact]
    public void The_default_theme_has_no_contrast_errors()
    {
        var theme = new ThemeDefinition();

        IReadOnlyList<ContrastIssue> issues = ThemeContrastValidator.Validate(theme);

        issues.ShouldNotContain(issue => issue.Severity == ContrastSeverity.Error);
    }

    [Fact]
    public void An_unreadable_colour_pair_is_reported_as_an_error()
    {
        var theme = new ThemeDefinition();
        theme.Colors.TextPrimary = "#F7F5F1";
        theme.Colors.SurfaceCanvas = "#F7F5F1";

        IReadOnlyList<ContrastIssue> issues = ThemeContrastValidator.Validate(theme);

        issues.ShouldContain(issue => issue.Severity == ContrastSeverity.Error);
    }

    [Fact]
    public void A_localised_text_falls_back_to_english()
    {
        var text = new LocalizedText { En = "StoreFlow", Ar = "ستور فلو" };

        text.Resolve("ar").ShouldBe("ستور فلو");
        text.Resolve("en").ShouldBe("StoreFlow");
        text.Resolve("fr").ShouldBe("StoreFlow");
    }
}
