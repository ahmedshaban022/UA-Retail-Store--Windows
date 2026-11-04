using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>
/// Guards the release workflow. A broken workflow is invisible to the compiler and to every
/// test in the suite: it only fails on the runner, after the build, when a release that the
/// programme is supposed to find does not appear. These tests read the workflow the way the
/// runner reads it, so the mistakes are caught here instead.
/// </summary>
public sealed class ReleaseWorkflowTests
{
    private static string WorkflowPath()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreFlow.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        return Path.Combine(root.FullName, ".github", "workflows", "release.yml");
    }

    private static string[] WorkflowLines() => File.ReadAllLines(WorkflowPath());

    /// <summary>A step of the workflow: its name, its id, and the outputs it writes.</summary>
    private sealed record Step(string Name, string? Id, IReadOnlyList<string> Outputs);

    private static List<Step> Steps()
    {
        List<Step> steps = [];
        string? name = null;
        string? id = null;
        List<string> outputs = [];

        foreach (string line in WorkflowLines())
        {
            Match start = Regex.Match(line, @"^\s*-\s+name:\s*(.+?)\s*$");
            if (start.Success)
            {
                if (name is not null)
                {
                    steps.Add(new Step(name, id, outputs));
                }

                name = start.Groups[1].Value;
                id = null;
                outputs = [];
                continue;
            }

            if (name is null)
            {
                continue;
            }

            Match idMatch = Regex.Match(line, @"^\s+id:\s*([A-Za-z0-9_-]+)\s*$");
            if (idMatch.Success)
            {
                id = idMatch.Groups[1].Value;
                continue;
            }

            // "name=value" >> $env:GITHUB_OUTPUT
            Match output = Regex.Match(line, @"^\s*""?(?<name>[A-Za-z0-9_]+)=.*GITHUB_OUTPUT");
            if (output.Success)
            {
                outputs.Add(output.Groups["name"].Value);
            }
        }

        if (name is not null)
        {
            steps.Add(new Step(name, id, outputs));
        }

        return steps;
    }

    [Fact]
    public void Every_step_output_the_workflow_reads_is_written_by_the_step_it_names()
    {
        List<Step> steps = Steps();
        steps.ShouldNotBeEmpty();

        Dictionary<string, Step> byId = steps
            .Where(step => step.Id is not null)
            .ToDictionary(step => step.Id!, StringComparer.Ordinal);

        List<string> problems = [];
        foreach (string line in WorkflowLines())
        {
            foreach (Match reference in Regex.Matches(line, @"steps\.(?<id>[A-Za-z0-9_-]+)\.outputs\.(?<output>[A-Za-z0-9_-]+)"))
            {
                string id = reference.Groups["id"].Value;
                string output = reference.Groups["output"].Value;

                if (!byId.TryGetValue(id, out Step? step))
                {
                    problems.Add($"'{id}' is read but no step in the workflow has that id.");
                    continue;
                }

                if (!step.Outputs.Contains(output, StringComparer.Ordinal))
                {
                    problems.Add(
                        $"step '{id}' is read for '{output}', but it writes " +
                        $"{(step.Outputs.Count == 0 ? "nothing" : string.Join(", ", step.Outputs))}.");
                }
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void The_tests_run_before_the_installer_is_built_and_the_release_is_published()
    {
        List<Step> steps = Steps();

        int test = steps.FindIndex(step => step.Name == "Test");
        int installer = steps.FindIndex(step => step.Name == "Build the installer");
        int publish = steps.FindIndex(step => step.Name == "Publish the release");

        test.ShouldBeGreaterThanOrEqualTo(0);
        installer.ShouldBeGreaterThan(test);
        publish.ShouldBeGreaterThan(installer);
    }

    [Fact]
    public void The_release_is_published_rather_than_left_as_a_draft()
    {
        // The programme looks for the newest published release. A draft is invisible to it.
        string workflow = File.ReadAllText(WorkflowPath());
        workflow.ShouldNotContain("--draft");
    }
}
