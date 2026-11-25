using System.Text.Json;
using RetroIde.Application;

namespace RetroIde.Workbench;

/// <summary>Versioned workbench preferences with no adapter, workspace, host, or credential state.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-2</remarks>
public sealed record WorkbenchLayoutState(int Version, DockBranch Main, DockBranch[] Floating, string? ActiveTool = null);

/// <summary>Topology of one dock host, including its auto-hidden panes and optional size.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-2</remarks>
public sealed record DockBranch(DockNode Content, PinnedTool[] Pinned, double? Width = null, double? Height = null);

/// <summary>Allowlisted structural data for a Dock split, tab group, splitter, or shell pane.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-2</remarks>
public sealed record DockNode(string Kind, string? Id = null, double? Proportion = null, string? Active = null,
    DockNode[]? Children = null, string? Alignment = null);

/// <summary>One auto-hidden pane together with its preferred redock group and edge.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-2</remarks>
public sealed record PinnedTool(string Tool, string Alignment, string? Group = null);

/// <summary>Reads and atomically replaces a local workbench layout.</summary>
/// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-2</remarks>
public sealed class LayoutStore(string path)
{
    /// <summary>Gets the path selected for layout preferences.</summary>
    public string Path { get; } = path;

    /// <summary>Reads a compatible layout, falling back to the default for missing or invalid data.</summary>
    public WorkbenchLayoutState? Load()
    {
        try
        {
            using var stream = File.OpenRead(Path);
            if (stream.Length > 128 * 1024)
                return null;
            return JsonSerializer.Deserialize<WorkbenchLayoutState>(stream, WireJson.Options) is { Version: 1 } state ? state : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Replaces the preferences only after a complete write and a final cancellation check.</summary>
    public void Save(WorkbenchLayoutState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = System.IO.Path.GetFullPath(Path);
        var folder = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!).FullName;
        var temporary = System.IO.Path.Combine(folder, $".layout-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, state, WireJson.Options);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
