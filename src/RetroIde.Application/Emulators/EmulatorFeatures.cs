using System.ComponentModel;
using Project = RetroIde.Application.Workspaces.WorkspaceExplorer.Root;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorFeatures
{
    internal sealed record ConfigureInput(string BucketId, string Repository);
    internal sealed record BucketInput(string BucketId);

    internal Feature<GetContextInput> List { get; } = new("emulator.buckets.list", "emulator_list_buckets",
        "Read configured emulator buckets, their verified cached releases, and host compatibility.",
        static (_, view, token) => view is IEmulatorRegistration { Emulators: { } workspace }
            ? RunAsync(workspace, static (emulators, cancellation) => emulators.ReadAsync(cancellation), "Emulator buckets read.", token)
            : Task.FromResult(Unavailable()), typeof(EmulatorWorkspace.CatalogState));

    internal Feature<ConfigureInput> Configure { get; } = new("emulator.bucket.configure", "emulator_configure_bucket",
        "Add or update an emulator bucket using an absolute Git repository URL.",
        static (input, view, _) => Prepare(view, (workspace, token) => workspace.ConfigureAsync(input.BucketId, input.Repository, token),
            "Emulator bucket configured."), typeof(EmulatorWorkspace.BucketState), mutation: true);

    internal Feature<BucketInput> Refresh { get; } = new("emulator.bucket.refresh", "emulator_refresh_bucket",
        "Refresh a configured Git bucket and publish its catalog after verifying every manifest.",
        static (input, view, _) => Prepare(view, (workspace, token) => workspace.RefreshAsync(input.BucketId, token),
            "Emulator bucket refreshed."), typeof(EmulatorWorkspace.BucketState), mutation: true, background: true);

    internal Feature<BucketInput> Remove { get; } = new("emulator.bucket.remove", "emulator_remove_bucket",
        "Remove a configured emulator bucket and retain its installed bundles and project pins.",
        static (input, view, _) => Prepare(view, (workspace, token) => workspace.RemoveAsync(input.BucketId, token),
            "Emulator bucket removed."), typeof(EmulatorWorkspace.BucketChange), mutation: true);

    private static Task<FeatureOutcome> Prepare<T>(IWorkbenchRegistration view,
        Func<EmulatorWorkspace, CancellationToken, Task<T>> execute, string success, string area = "bucket", Project? project = null)
    {
        if (view is not IEmulatorRegistration { Emulators: { } workspace })
            return Task.FromResult(Unavailable());
        return Task.FromResult(new FeatureOutcome("ok", "Emulator operation prepared.")
        {
            Commit = current =>
            {
                if (current is not IEmulatorRegistration active || !ReferenceEquals(active.Emulators, workspace))
                    return Unavailable();
                if (project is not null && ResolveProject(current, project.Id) != project)
                    return MissingProject();
                return new FeatureOutcome("ok", "Emulator operation authorized.")
                {
                    ExecuteAuthorizedAsync = token => RunAsync(workspace, execute, success, token, area),
                };
            },
        });
    }

    private static async Task<FeatureOutcome> RunAsync<T>(EmulatorWorkspace workspace,
        Func<EmulatorWorkspace, CancellationToken, Task<T>> execute, string success, CancellationToken token, string area = "bucket")
    {
        try
        {
            var result = await execute(workspace, token).ConfigureAwait(false);
            return FeatureOutcome.Success(success, result);
        }
        catch (EmulatorWorkspace.Rejected error)
        {
            return new(error.Code, error.Message);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException
            or KeyNotFoundException or Win32Exception or HttpRequestException or InvalidOperationException)
        {
            workspace.ReportFailure(error);
            return error switch
            {
                KeyNotFoundException => new("emulator_bucket_missing", "The emulator bucket is not configured."),
                InvalidDataException or ArgumentException => new($"emulator_{area}_invalid", $"The emulator {area} input or stored data is invalid."),
                InvalidOperationException => new($"emulator_{area}_unavailable", error.Message),
                _ => new($"emulator_{area}_io", $"The emulator {area} could not be read or updated."),
            };
        }
    }

    private static FeatureOutcome Unavailable() => new("emulators_unavailable", "Emulator bundle management is unavailable.");
}
