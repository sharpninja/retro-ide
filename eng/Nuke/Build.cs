using System.Diagnostics;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal sealed class Build : NukeBuild
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256, 512];
    private readonly string _releaseId = $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
    private AbsolutePath Bundle => RootDirectory / "artifacts" / "omarchy" / _releaseId;

    public static int Main() => Execute<Build>(build => build.PublishOmarchy);

    public Target PublishOmarchy => task => task
        .Description("Publish trimmed, self-contained ReadyToRun executables for Linux x64.")
        .Executes(() =>
        {
            Publish("RetroIde.Workbench", "workbench");
            Publish("RetroIde.Mcp.Stdio", "mcp");
        });

    public Target DeployOmarchy => task => task
        .DependsOn(PublishOmarchy)
        .Description("Install a per-user release and desktop launcher on Omarchy.")
        .Executes(Deploy);

    private void Publish(string project, string folder) => DotNetPublish(settings => settings
        .SetProject(RootDirectory / "src" / project / (project + ".csproj"))
        .SetConfiguration("Release")
        .SetRuntime("linux-x64")
        .SetSelfContained(true)
        .SetProperty("PublishReadyToRun", true)
        .SetProperty("PublishSingleFile", true)
        .SetProperty("PublishTrimmed", true)
        .SetProperty("TrimMode", "partial")
        .SetProperty("JsonSerializerIsReflectionEnabledByDefault", true)
        .SetProperty("IncludeNativeLibrariesForSelfExtract", true)
        .SetProperty("DebugType", "embedded")
        .SetProperty("PublishDocumentationFiles", false)
        .SetOutput(Bundle / folder));

    private void Deploy()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("DeployOmarchy requires Linux.");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configuredData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var data = !string.IsNullOrWhiteSpace(configuredData) && Path.IsPathFullyQualified(configuredData)
            ? configuredData : Path.Combine(home, ".local", "share");
        var install = Path.Combine(data, "retro-ide");
        var releases = Path.Combine(install, "releases");
        var release = Path.Combine(releases, _releaseId);
        var staging = release + ".staging";
        var bin = Path.Combine(home, ".local", "bin");
        var applications = Path.Combine(data, "applications");
        Directory.CreateDirectory(releases);
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(applications);

        CopyBundle(Bundle, staging);
        var app = Path.Combine(staging, "workbench", "RetroIde.Workbench");
        var mcp = Path.Combine(staging, "mcp", "RetroIde.Mcp.Stdio");
        Run(app, "--validate-bundled-resources");
        if (!File.Exists(mcp) || (File.GetUnixFileMode(mcp) & UnixFileMode.UserExecute) == 0)
            throw new InvalidOperationException("The published MCP executable is missing or lacks execute permission.");
        Directory.Move(staging, release);
        InstallIcons(Path.Combine(release, "workbench", "AppIcons"), data);

        var current = Path.Combine(install, "current");
        var workbench = Path.Combine(current, "workbench", "RetroIde.Workbench");
        var bridge = Path.Combine(current, "mcp", "RetroIde.Mcp.Stdio");
        ReplaceLink(Path.Combine(bin, "retro-ide"), workbench);
        ReplaceLink(Path.Combine(bin, "retro-ide-mcp"), bridge);
        var desktop = Path.Combine(applications, "retro-ide.desktop");
        var pendingDesktop = desktop + "." + _releaseId;
        File.WriteAllText(pendingDesktop,
            "[Desktop Entry]\nType=Application\nName=Retro IDE\n" +
            $"Exec={DesktopArgument(workbench)}\n" +
            "Icon=retro-ide\nTerminal=false\nCategories=Development;IDE;\n");
        File.Move(pendingDesktop, desktop, overwrite: true);
        ReplaceLink(current, release);
        Console.WriteLine($"Installed Retro IDE at {release}");
        Console.WriteLine($"Desktop launcher {desktop}");
    }

    private void InstallIcons(string assets, string data)
    {
        var sourceFiles = IconSizes.Select(size => Path.Combine(assets, $"retro-ide-{size}.png")).ToArray();
        if (sourceFiles.Any(file => !File.Exists(file)))
            throw new InvalidOperationException("The published Retro IDE icon set is incomplete.");

        foreach (var (size, source) in IconSizes.Zip(sourceFiles))
        {
            var dimension = $"{size}x{size}";
            var directory = Path.Combine(data, "icons", "hicolor", dimension, "apps");
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "retro-ide.png");
            var pending = target + "." + _releaseId;
            try
            {
                File.Copy(source, pending);
                File.Move(pending, target, overwrite: true);
            }
            finally { File.Delete(pending); }
        }
    }

    private static void CopyBundle(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(target, File.GetUnixFileMode(file));
        }
    }

    private static void ReplaceLink(string link, string destination)
    {
        var pending = link + "." + Guid.NewGuid().ToString("N");
        File.CreateSymbolicLink(pending, destination);
        try { Run("mv", "-Tf", "--", pending, link); }
        finally { File.Delete(pending); }
    }

    private static string DesktopArgument(string value) => "\"" + value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("$", "\\$", StringComparison.Ordinal)
        .Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("%", "%%", StringComparison.Ordinal) + "\"";

    private static void Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{executable} exited with code {process.ExitCode}.");
    }
}
