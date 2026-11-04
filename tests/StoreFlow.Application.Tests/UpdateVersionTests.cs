using Shouldly;
using StoreFlow.Application.Updates;
using Xunit;

namespace StoreFlow.Application.Tests;

/// <summary>
/// The rules that decide whether a published release is newer than the running build.
/// </summary>
public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v1.1.0", "1.0.9", true)]
    [InlineData("v2.0.0", "1.99.99", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.9", "1.0.0", false)]
    [InlineData("v1.0.0", "1.0.1", false)]
    [InlineData("not-a-version", "1.0.0", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("v1.0.0", "not-a-version", false)]
    public void Only_a_strictly_newer_release_is_an_update(string? tag, string running, bool expected)
    {
        UpdateVersion.IsNewer(tag, running).ShouldBe(expected);
    }

    [Theory]
    [InlineData("v1.0.1", "1.0.1")]
    [InlineData("V1.0.1", "1.0.1")]
    [InlineData("1.0.1", "1.0.1")]
    [InlineData("v1.0.1+abc1234", "1.0.1")]
    [InlineData("  v1.0.1  ", "1.0.1")]
    public void A_tag_is_read_as_the_version_it_names(string tag, string expected)
    {
        UpdateVersion.TryParse(tag, out Version? version).ShouldBeTrue();
        version!.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("v1.0.1-rc1")]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData(null)]
    public void A_tag_that_is_not_a_released_version_is_never_an_update(string? tag)
    {
        UpdateVersion.TryParse(tag, out Version? _).ShouldBeFalse();
        UpdateVersion.IsNewer(tag, "1.0.0").ShouldBeFalse();
    }

    [Fact]
    public void The_check_result_knows_whether_there_is_something_to_install()
    {
        var installer = new Uri("https://example.test/a.msi");
        var checksum = new Uri("https://example.test/a.msi.sha256");

        var available = new UpdateCheckResult("1.0.0", "1.0.1", UpdateStatus.UpdateAvailable, installer, checksum);
        var upToDate = new UpdateCheckResult("1.0.0", "1.0.0", UpdateStatus.UpToDate);
        var unavailable = new UpdateCheckResult("1.0.0", null, UpdateStatus.Unavailable);

        available.HasUpdate.ShouldBeTrue();
        upToDate.HasUpdate.ShouldBeFalse();
        unavailable.HasUpdate.ShouldBeFalse();

        available.InstallerUrl.ShouldBe(installer);
        available.ChecksumUrl.ShouldBe(checksum);
        available.RunningVersion.ShouldBe("1.0.0");
        available.LatestVersion.ShouldBe("1.0.1");
    }
}
