using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nuke.Common;

internal sealed partial class Build
{
    private static readonly string[] TestProjects = ["RetroIde.UnitTests", "RetroIde.IntegrationTests"];
    private static readonly Regex RunIdPattern = new(@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,79}\z", RegexOptions.CultureInvariant);
    private static readonly Regex ClassPattern = new(@"\A[A-Za-z_][A-Za-z0-9_.]*\z", RegexOptions.CultureInvariant);

    [Parameter("Unique directory for test receipts under TestResults.")]
    private readonly string? TestRunId = null;

    [Parameter("One or more test selections in project:Class[,Class] form.", Name = "test-groups")]
    private readonly string[] SelectedTestGroups = [];

    [Parameter("Path to the effective QBrain criteria export for trace verification.", Name = "qbrain-export")]
    private readonly string? QBrainExport = null;

    [Parameter("Latest phase whose compiled test links must be present.")]
    private readonly string ThroughPhase = "03";

    /// <summary>Builds the Release solution with the normal NuGet audit configuration.</summary>
    /// <remarks>2026-11-26. Runs the local Release build required by PLAN-RETROIDE-001 without adding the Nuke project to the solution.</remarks>
    public Target Compile => task => task
        .Description("Build the Release solution with NuGet audit enabled.")
        .Executes(() => Run("dotnet", "build", (RootDirectory / "RetroIde.sln").ToString(), "-c", "Release"));

    /// <summary>Runs all managed unit and integration tests, requiring TRX receipts with zero failures or skips.</summary>
    /// <remarks>2026-11-26. Implements the local managed gate in PLAN-RETROIDE-001.</remarks>
    public Target Test => task => task
        .Description("Run the full managed test suites with per-project TRX receipts.")
        .DependsOn(Compile)
        .Executes(() =>
        {
            var results = CreateFreshRunDirectory();
            foreach (var project in TestProjects)
                RunManagedTests(project, Path.Combine(results, project), [], alreadyBuilt: true);
        });

    /// <summary>Runs only the named classes and rejects selections that execute no matching tests.</summary>
    /// <remarks>2026-11-26. Adds focused test verification for PLAN-RETROIDE-001. Full phase results are recorded by the Test target.</remarks>
    public Target TestGroups => task => task
        .Description("Run selected test classes with one TRX per selected project.")
        .Executes(() =>
        {
            var selections = ParseSelections(SelectedTestGroups);
            var results = CreateFreshRunDirectory();
            foreach (var (project, classes) in selections)
                RunManagedTests(project, Path.Combine(results, "groups", project), classes, alreadyBuilt: false);
        });

    /// <summary>Runs the native Workbench RemoteControl suite on the calling host's test display.</summary>
    /// <remarks>2026-11-26. Runs the input-free native acceptance path for TEST-RETRO-001#verify-ac-behavior-desktop-1 and Phase 03 editor and Git regressions.</remarks>
    public Target TestNative => task => task
        .Description("Run native RemoteControl without desktop keyboard or mouse input.")
        .DependsOn(Compile)
        .Executes(() => Run("dotnet",
            (RootDirectory / "tests" / "RetroIde.NativeTests" / "bin" / "Release" / "net10.0" / "RetroIde.NativeTests.dll").ToString(),
            "--remote-control"));

    /// <summary>Checks compiled public contract and test links through the requested BDPv4 phase.</summary>
    /// <remarks>2026-11-26. Runs the existing criterion and contract verifier against the effective QBrain export.</remarks>
    public Target ValidateTraceability => task => task
        .Description("Compare compiled test links and public contracts with the QBrain export.")
        .DependsOn(Compile)
        .Executes(() =>
        {
            var export = QBrainExport ?? throw new InvalidOperationException("Supply --qbrain-export for the effective QBrain criteria.");
            Run("pwsh", "-NoProfile", "-NonInteractive", "-File", (RootDirectory / "eng" / "Verify-Phase01Trace.ps1").ToString(),
                "-ThroughPhase", ThroughPhase, "-QBrainExport", Path.GetFullPath(export));
        });

    private string CreateFreshRunDirectory()
    {
        if (TestRunId is null || !RunIdPattern.IsMatch(TestRunId))
            throw new InvalidOperationException("Supply a unique --test-run-id using letters, digits, hyphens or underscores.");

        var path = (RootDirectory / "TestResults" / TestRunId).ToString();
        if (Directory.Exists(path))
            throw new InvalidOperationException($"Test run {TestRunId} already has a receipt directory.");
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, List<string>> ParseSelections(string[] selections)
    {
        if (selections.Length == 0)
            throw new InvalidOperationException("Supply --test-groups values in project:Class[,Class] form.");

        var projects = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            var group = selection.Split(':', 2);
            if (group.Length != 2 || !TestProjects.Contains(group[0], StringComparer.Ordinal))
                throw new InvalidOperationException($"Unknown managed test project in group '{selection}'.");
            var requested = group[1].Split(',', StringSplitOptions.TrimEntries);
            if (requested.Any(name => !ClassPattern.IsMatch(name)))
                throw new InvalidOperationException($"Invalid test class in group '{selection}'.");

            if (!projects.TryGetValue(group[0], out var classes))
                projects.Add(group[0], classes = []);
            classes.AddRange(requested.Except(classes, StringComparer.Ordinal));
        }

        return projects;
    }

    private static void RunManagedTests(string project, string directory, IReadOnlyCollection<string> classes, bool alreadyBuilt)
    {
        var source = (RootDirectory / "tests" / project / (project + ".csproj")).ToString();
        var args = new List<string>
        {
            "test", source, "-c", "Release", "--results-directory", directory, "--logger", $"trx;LogFileName={project}.trx",
        };
        if (alreadyBuilt)
            args.Add("--no-build");
        if (classes.Count > 0)
        {
            var platform = OperatingSystem.IsWindows() ? "Linux" : "Windows";
            var names = string.Join('|', classes.Select(name => $"FullyQualifiedName~{name}."));
            args.AddRange(["--filter", $"({names})&Platform!={platform}"]);
        }

        Run("dotnet", [.. args]);
        VerifyTrx(Path.Combine(directory, project + ".trx"), classes);
    }

    private static void VerifyTrx(string path, IReadOnlyCollection<string> classes)
    {
        var run = XDocument.Load(path).Root ?? throw new InvalidDataException($"Empty test receipt {path}.");
        var ns = run.Name.Namespace;
        var results = run.Element(ns + "Results")?.Elements(ns + "UnitTestResult").ToArray() ?? [];
        if (results.Length == 0 || results.Any(result => (string?)result.Attribute("outcome") != "Passed"))
            throw new InvalidOperationException($"The managed suite ran no tests or had a failed or skipped case. See {path}.");

        var executed = results.Select(result => (string?)result.Attribute("testId")).ToHashSet(StringComparer.Ordinal);
        var testClasses = run.Element(ns + "TestDefinitions")?.Elements(ns + "UnitTest")
            .Where(test => executed.Contains((string?)test.Attribute("id")))
            .Select(test => (string?)test.Element(ns + "TestMethod")?.Attribute("className"))
            .OfType<string>().ToArray() ?? [];
        foreach (var requested in classes)
        {
            if (!testClasses.Any(name => name == requested || name.EndsWith("." + requested, StringComparison.Ordinal)))
                throw new InvalidOperationException($"No tests executed from class '{requested}'. See {path}.");
        }
    }
}
