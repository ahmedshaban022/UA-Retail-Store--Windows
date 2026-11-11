using System;
using Shouldly;
using StoreFlow.Infrastructure.Updates;

namespace StoreFlow.Infrastructure.Tests;

/// <summary>
/// Checks the two things the updater does before it runs anything it has downloaded: it looks at
/// who signed the installer, and it makes the installer wait until the shop has closed.
/// </summary>
public sealed class InstallerTrustTests
{
    [Fact]
    public void An_installer_that_carries_no_signature_is_trusted()
    {
        // The release notes say in as many words that the installer is not signed. Refusing every
        // unsigned installer would leave the shop with no way to be updated at all.
        var trust = new AuthenticodeInstallerTrust(path => null);

        trust.IsTrusted(@"C:\shop\StoreFlow-1.0.1-x64.msi", out string? problem).ShouldBeTrue();
        problem.ShouldBeNull();
    }

    [Fact]
    public void An_installer_signed_by_a_publisher_this_build_does_not_know_is_refused()
    {
        // The checksum published beside the installer proves that the download arrived intact. It
        // cannot prove who published it, because the same publisher published both. A signature
        // this build does not expect is a publisher saying something the build cannot check, and
        // that is refused.
        var trust = new AuthenticodeInstallerTrust(path => "A1B2C3D4E5");

        trust.IsTrusted(@"C:\shop\StoreFlow-1.0.1-x64.msi", out string? problem).ShouldBeFalse();
        problem.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_installer_waits_for_the_programme_to_close_before_it_starts()
    {
        // The installer cannot replace the programme's files while the programme is holding them
        // open. An installer that starts too early is quietly deferred to the next reboot by
        // Windows Installer, and the shop is told the update was applied when it was not.
        var launcher = new MsiexecInstallerLauncher("StoreFlow");

        string script = launcher.BuildScript(
            @"C:\Windows\System32\msiexec.exe",
            @"C:\shop\StoreFlow-1.0.1-x64.msi");

        int wait = script.IndexOf("waitfor", StringComparison.Ordinal);
        int install = script.IndexOf("msiexec", StringComparison.Ordinal);

        wait.ShouldBeGreaterThanOrEqualTo(0, "the helper has to wait for the programme to close");
        install.ShouldBeGreaterThan(wait, "the installer has to start after the wait, not before it");

        script.ShouldContain("StoreFlow");
        script.ShouldContain("StoreFlow-1.0.1-x64.msi");
    }
}
