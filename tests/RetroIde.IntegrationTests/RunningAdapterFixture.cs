using System.Diagnostics;
using System.Text.Json;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

internal sealed class RunningAdapterFixture : IWorkbenchWorkspaceContext, IAsyncDisposable
{
    private const string ExecutableName = "RetroIde.AdapterFixture";
    private readonly Process _process;

    public RunningAdapterFixture(string testDirectory)
    {
        var folder = Directory.CreateDirectory(Path.Combine(testDirectory, "adapter")).FullName;
        foreach (var extension in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
        {
            var file = ExecutableName + extension;
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(folder, file));
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ExecutableName + ".deps.json")));
        Assert.DoesNotContain(manifest.RootElement.GetProperty("libraries").EnumerateObject(),
            library => IsDesktopAssembly(library.Name.Split('/')[0]));

        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(folder, ExecutableName + ".dll"));
        start.ArgumentList.Add(WorkspaceId.ToString());
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch the adapter fixture.");
    }

    public Guid WorkspaceId { get; } = Guid.NewGuid();

    public Task<AdapterFixtureSnapshot> InspectAsync() => RequestAsync("inspect");

    public async Task<AdapterFixtureSession> AdvanceAsync() =>
        (await RequestAsync("advance").ConfigureAwait(false)).Session;

    public async Task<WorkbenchSession> OpenWorkbenchAsync(WorkbenchLayoutState? layout = null)
    {
        await RequestAsync("attach").ConfigureAwait(true);
        return new WorkbenchSession(this, layout);
    }

    private async Task<AdapterFixtureSnapshot> RequestAsync(string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await _process.StandardInput.WriteLineAsync(command.AsMemory(), timeout.Token).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
        var response = await _process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        Assert.NotNull(response);
        var snapshot = JsonSerializer.Deserialize<AdapterFixtureSnapshot>(response);
        Assert.NotNull(snapshot);
        Assert.Equal(_process.Id, snapshot.ProcessId);
        Assert.NotEqual(Environment.ProcessId, snapshot.ProcessId);
        Assert.Equal(WorkspaceId, snapshot.WorkspaceId);
        Assert.Contains(ExecutableName, snapshot.LoadedAssemblies);
        Assert.DoesNotContain(snapshot.LoadedAssemblies, IsDesktopAssembly);
        return snapshot;
    }

    private static bool IsDesktopAssembly(string name) =>
        name.StartsWith("Avalonia", StringComparison.Ordinal) ||
        name.StartsWith("Dock.", StringComparison.Ordinal) ||
        string.Equals("Dock", name, StringComparison.Ordinal) ||
        string.Equals("RetroIde.Workbench", name, StringComparison.Ordinal);

    public async ValueTask DisposeAsync()
    {
        using (_process)
        {
            _process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }
}
