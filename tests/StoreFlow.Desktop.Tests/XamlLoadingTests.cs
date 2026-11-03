using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Shouldly;
using StoreFlow.Desktop.Views;

namespace StoreFlow.Desktop.Tests;

/// <summary>Protects XAML from attributes accidentally left outside a control.</summary>
public sealed class XamlLoadingTests
{
    [Fact]
    public void Automation_properties_are_not_stranded_after_self_closed_elements()
    {
        DirectoryInfo root = FindRoot();
        string[] files = Directory.GetFiles(Path.Combine(root.FullName, "src"), "*.xaml", SearchOption.AllDirectories);
        files.ShouldNotBeEmpty();
        var violations = new List<string>();
        foreach (string file in files)
        {
            string? previous = null;
            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (previous is not null && previous.TrimEnd().EndsWith("/>", StringComparison.Ordinal)
                    && Regex.IsMatch(line, @"^\s*AutomationProperties\.\w+\s*="))
                {
                    violations.Add($"{Path.GetRelativePath(root.FullName, file)}:{lineNumber}");
                }

                previous = line;
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task First_run_window_loads()
    {
        const string childFlag = "STOREFLOW_XAML_TEST_CHILD";
        if (Environment.GetEnvironmentVariable(childFlag) == "1")
        {
            TestHost.RunOnStaThread(() =>
            {
                var app = new App();
                app.InitializeComponent();
                var window = new FirstRunWindow();
                window.Close();
                app.Shutdown();
            });
            return;
        }

        string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? throw new InvalidOperationException("DOTNET_ROOT must identify the .NET installation.");
        var start = new ProcessStartInfo(Path.Combine(dotnetRoot, "dotnet.exe"))
        {
            WorkingDirectory = FindRoot().FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("test");
        start.ArgumentList.Add(Path.Combine("tests", "StoreFlow.Desktop.Tests", "StoreFlow.Desktop.Tests.csproj"));
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--nologo");
        start.ArgumentList.Add("--filter");
        start.ArgumentList.Add("FullyQualifiedName~XamlLoadingTests.First_run_window_loads");
        start.Environment[childFlag] = "1";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the XAML test process.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await output + await error);
    }

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreFlow.sln")))
        {
            root = root.Parent;
        }

        return root ?? throw new DirectoryNotFoundException("Could not find StoreFlow.sln.");
    }
}