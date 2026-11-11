using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Guards the state of the release the repository is working towards.
///
/// The failure this guards against is a release that has been written, tested and committed but
/// never published. The notes read like a shipped release, the version sits in the project file,
/// the commit message calls it a release, and nothing anywhere says that no shop can download it.
/// Every file involved is read here the way a reader reads it, so the disagreement is caught in
/// the suite rather than by a shop that waits for an update which never comes.
/// </summary>
public sealed class ReleaseStatusTests
{
    private const string Unpublished = "unpublished";
    private const string Published = "published";

    private static string RepositoryRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreFlow.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        return root.FullName;
    }

    private static string RepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

    /// <summary>The version the programme is built as, read out of the project file.</summary>
    private static Version ProjectVersion()
    {
        string project = RepositoryFile("src", "StoreFlow.Desktop", "StoreFlow.Desktop.csproj");
        Match match = Regex.Match(project, @"<Version>(?<version>[^<]+)</Version>");
        match.Success.ShouldBeTrue("The project file does not say which version it is.");
        return Version.Parse(match.Groups["version"].Value.Trim());
    }

    /// <summary>The version the release notes are about.</summary>
    private static Version NotesVersion()
    {
        string notes = RepositoryFile("docs", "RELEASE-NOTES.md");
        Match match = Regex.Match(
            notes,
            @"^#\s+StoreFlow Desktop\s+v(?<version>[0-9]+(?:\.[0-9]+)*)",
            RegexOptions.Multiline);
        match.Success.ShouldBeTrue("The release notes do not say which version they are about.");
        return Version.Parse(match.Groups["version"].Value);
    }

    /// <summary>The word the notes use to say whether the release has been published.</summary>
    private static string Status()
    {
        string notes = RepositoryFile("docs", "RELEASE-NOTES.md");
        Match match = Regex.Match(
            notes,
            @"^\*\*Release status:\s*(?<status>[^*]+?)\*\*",
            RegexOptions.Multiline);

        match.Success.ShouldBeTrue(
            "The release notes do not carry a line saying whether the release has been published. " +
            "A release that has not been made must say so, or it reads exactly like one that has.");

        // The word may be written in backticks and end in a full stop; neither is part of it.
        return Regex.Replace(match.Groups["status"].Value, @"[`\s.]", string.Empty);
    }

    /// <summary>The versions that have a row in the record of what a shop actually received.</summary>
    private static List<Version> PublishedVersions()
    {
        string record = RepositoryFile("docs", "RELEASES.md");
        List<Version> versions = [];

        foreach (Match match in Regex.Matches(
                     record,
                     @"^\|\s*v(?<version>[0-9]+(?:\.[0-9]+)*)\s*\|",
                     RegexOptions.Multiline))
        {
            versions.Add(Version.Parse(match.Groups["version"].Value));
        }

        return versions;
    }

    [Fact]
    public void The_notes_describe_the_version_the_programme_is_built_as()
    {
        // The notes are published as the body of the release, so notes about one version beside
        // a project built as another would ship the wrong description of the wrong programme.
        NotesVersion().ShouldBe(ProjectVersion());
    }

    [Fact]
    public void The_notes_say_whether_the_release_has_been_published()
    {
        string status = Status();
        (status == Unpublished || status == Published).ShouldBeTrue(
            $"The status line says '{status}'. It has to say '{Unpublished}' or '{Published}'.");
    }

    [Fact]
    public void The_status_line_agrees_with_the_record_of_published_releases()
    {
        string status = Status();
        bool recorded = PublishedVersions().Contains(ProjectVersion());

        if (status == Published)
        {
            recorded.ShouldBeTrue(
                "The notes say this release is published, so it needs a row in docs/RELEASES.md " +
                "recording what a shop received.");
        }
        else
        {
            recorded.ShouldBeFalse(
                "The notes say this release has not been published, so it must not have a row in " +
                "docs/RELEASES.md — that table records what a shop actually received.");
        }
    }

    [Fact]
    public void No_published_release_is_newer_than_the_programme()
    {
        Version project = ProjectVersion();
        List<Version> newer = PublishedVersions().Where(version => version > project).ToList();

        newer.ShouldBeEmpty(
            "A release that has been published is newer than the programme in the tree, which " +
            "means the version was moved backwards.");
    }

    [Fact]
    public void The_published_body_does_not_carry_the_status_line()
    {
        // The status line is bookkeeping about the repository, not something a shop needs to
        // read on the release page. The workflow takes it out, and this test runs the
        // workflow's own pattern against the notes to prove that it does.
        string workflow = RepositoryFile(".github", "workflows", "release.yml");

        Match pattern = Regex.Match(workflow, @"\$status\s*=\s*'(?<pattern>[^']+)'");
        pattern.Success.ShouldBeTrue(
            "The workflow does not work out how to take the status line out of the release body.");

        string body = Regex.Replace(
            RepositoryFile("docs", "RELEASE-NOTES.md"),
            pattern.Groups["pattern"].Value,
            string.Empty);

        body.ShouldNotContain("Release status");
        body.ShouldContain("StoreFlow Desktop v", Case.Sensitive);
    }

    [Fact]
    public void The_workflow_refuses_to_publish_a_body_without_the_status_line()
    {
        // Without the check the workflow would publish a body that says nothing about its own
        // state, and a wording change in the notes would slip the line back onto the page.
        string workflow = RepositoryFile(".github", "workflows", "release.yml");

        bool refuses = workflow
            .Split('\n')
            .Any(line => line.Contains("throw", StringComparison.Ordinal)
                         && line.Contains("status", StringComparison.OrdinalIgnoreCase));

        refuses.ShouldBeTrue(
            "The workflow should refuse to publish a release body whose status line is missing.");
    }
}
