using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RetroIde.Application.Emulators;
using RetroIde.Application.Workspaces;
using RetroIde.Emulation;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class EmulatorBundleIntegrationTests
{
    /// <summary>Verifies that an opened session holds the exact package selected by its project.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
    [Theory]
    [InlineData(SessionMode.Launch, SessionClosePolicy.StopOwnedHost)]
    [InlineData(SessionMode.Attach, SessionClosePolicy.Detach)]
    public async Task Selected_project_binding_opens_with_its_close_policy_and_owns_the_bundle_lease(
        SessionMode mode, SessionClosePolicy policy)
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        var (workspace, project, installation) = await PrepareSelectedBindingAsync(root).ConfigureAwait(true);
        var factory = new BindingFixture(installation.Manifest.AdapterId, policy);
        EmulatorConnection target = mode == SessionMode.Launch
            ? new EmulatorConnection.Launch("emulator", ["--profile", "fixture"], policy)
            : new EmulatorConnection.Attach("fixture.endpoint");
        var result = await workspace.OpenSelectedAsync(project, new EmulatorSessionPlan("fixture-profile", target), path =>
        {
            Assert.Equal(installation.EntryPoint("binding"), path, StringComparer.Ordinal);
            return factory;
        }, token).ConfigureAwait(true);
        Assert.True(result.Succeeded, result.Error?.Message);
        var running = result.Value!;
        await using (running.ConfigureAwait(true))
        {
            Assert.Equal(policy, running.InitialState.ClosePolicy);
            Assert.Equal(FirstPin.Version, running.Connection.Identity.BundleVersion, StringComparer.Ordinal);
            Assert.Equal(running.InitialState, (await running.Control.ReadAsync(token)).Value);
            Assert.Equal(AdapterApi, factory.Request?.ContractVersion);
            if (mode == SessionMode.Launch)
            {
                var launch = Assert.IsType<SessionTarget.Launch>(factory.Request?.Target);
                Assert.Equal(installation.EntryPoint("emulator"), launch.ExecutablePath, StringComparer.Ordinal);
                Assert.Equal(["--profile", "fixture"], launch.Arguments);
            }
            else
            {
                var attach = Assert.IsType<SessionTarget.Attach>(factory.Request?.Target);
                Assert.Equal("fixture.endpoint", attach.EndpointReference, StringComparer.Ordinal);
            }

            Assert.Equal(EmulatorBundleStore.Removal.InUse,
                root.Store.Remove(FirstPin, RuntimeInformation.RuntimeIdentifier, token));
            var operation = Guid.NewGuid();
            var closed = await running.Connection.CloseAsync(new SessionCloseRequest(running.Connection.Identity, operation, "desktop"),
                token).ConfigureAwait(true);
            Assert.Equal(new SessionCloseResult(operation, policy), closed.Value);
        }

        Assert.True(factory.Session?.Disposed);
        Assert.Equal(EmulatorBundleStore.Removal.Removed, root.Store.Remove(FirstPin, RuntimeInformation.RuntimeIdentifier, token));
    }

    /// <summary>Verifies that a reconnect never returns a different backend as the original instance.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
    [Fact]
    public async Task Reconnect_to_a_replacement_instance_disposes_the_new_connection_and_releases_its_lease()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        var (workspace, project, installation) = await PrepareSelectedBindingAsync(root).ConfigureAwait(true);
        var expected = new SessionIdentity(installation.Manifest.AdapterId, FirstPin.Bundle, FirstPin.Version, Guid.NewGuid());
        var factory = new BindingFixture(expected.AdapterId, SessionClosePolicy.Detach);
        var target = new EmulatorConnection.Attach("fixture.endpoint", expected);

        var result = await workspace.OpenSelectedAsync(project, new EmulatorSessionPlan("fixture-profile", target), _ => factory, token)
            .ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal(AdapterErrorCodes.Incompatible, result.Error!.Code, StringComparer.Ordinal);
        Assert.True(factory.Session?.Disposed);
        Assert.Equal(expected, Assert.IsType<SessionTarget.Attach>(factory.Request?.Target).ExpectedInstance);
        Assert.Equal(EmulatorBundleStore.Removal.Removed, root.Store.Remove(FirstPin, RuntimeInformation.RuntimeIdentifier, token));
    }

    /// <summary>Verifies incompatible package bindings are rejected before starting a host.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-session-contract.</remarks>
    [Fact]
    public async Task A_mismatched_installed_binding_cannot_open_an_emulator_session()
    {
        using var root = new BundleStoreFixture();
        var token = root.CancellationToken;
        var (workspace, project, _) = await PrepareSelectedBindingAsync(root).ConfigureAwait(true);
        var factory = new BindingFixture("unregistered-adapter", SessionClosePolicy.StopOwnedHost);
        var target = new EmulatorConnection.Launch("emulator", [], SessionClosePolicy.StopOwnedHost);

        var result = await workspace.OpenSelectedAsync(project, new EmulatorSessionPlan("fixture-profile", target), _ => factory, token)
            .ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal(AdapterErrorCodes.Incompatible, result.Error!.Code, StringComparer.Ordinal);
        Assert.Null(factory.Request);
        Assert.Equal(EmulatorBundleStore.Removal.Removed, root.Store.Remove(FirstPin, RuntimeInformation.RuntimeIdentifier, token));
    }

    private static async Task<(EmulatorWorkspace Workspace, WorkspaceExplorer.Root Project, EmulatorBundleStore.InstalledBundle Installation)>
        PrepareSelectedBindingAsync(BundleStoreFixture root)
    {
        var runtime = RuntimeInformation.RuntimeIdentifier;
        var executable = OperatingSystem.IsWindows() ? "bin/uae.exe" : "bin/uae";
        using var zip = Archive(("adapter/Uae.dll", 0), (executable, 0x81ed), ("profiles/amiga-aga.json", 0));
        var bytes = ReleaseManifest(FirstPin, Digest(zip));
        if (OperatingSystem.IsWindows())
            bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(WindowsHash, Digest(zip), StringComparison.Ordinal));
        var installed = await root.Store.InstallAsync(FirstPin, runtime, AdapterApi, bytes, zip, root.CancellationToken).ConfigureAwait(false);
        var workspace = new EmulatorWorkspace(root.Directory, NullLogger.Instance);
        var folder = Directory.CreateDirectory(Path.Combine(root.Directory, "project")).FullName;
        var project = new WorkspaceExplorer.Root(Guid.NewGuid(), WorkspaceExplorer.RootKind.Project, folder);
        await workspace.SelectAsync(project, FirstPin, false, root.CancellationToken).ConfigureAwait(false);
        return (workspace, project, installed);
    }

    private sealed class BindingFixture(string adapterId, SessionClosePolicy policy) : IEmulatorAdapterFactory
    {
        internal SessionRequest? Request { get; private set; }
        internal ConnectionFixture? Session { get; private set; }

        public ValueTask<AdapterResult<AdapterDescriptor>> DescribeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<AdapterDescriptor>.Success(new(adapterId, [AdapterApi], [SessionMode.Attach, SessionMode.Launch],
                ["68000"])));

        public ValueTask<AdapterResult<IEmulatorSession>> OpenAsync(SessionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            Session = new ConnectionFixture(new SessionIdentity(adapterId, request.BundleId, request.BundleVersion, Guid.NewGuid()), policy);
            return ValueTask.FromResult(AdapterResult<IEmulatorSession>.Success(Session));
        }
    }

    private sealed class ConnectionFixture(SessionIdentity identity, SessionClosePolicy policy) : IEmulatorSession
    {
        public SessionIdentity Identity { get; } = identity;
        internal bool Disposed { get; private set; }

        public ValueTask<AdapterResult<SessionState>> GetStateAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<SessionState>.Success(new(Identity, SessionLifecycle.Connected, ExecutionStatus.Paused,
                1, policy, new CapabilitySnapshot(1, []), new EventCursor(Identity, 1))));

        public IAsyncEnumerable<SessionEvent> ReadEventsAsync(EventCursor? after, CancellationToken cancellationToken) =>
            AsyncEnumerable.Empty<SessionEvent>();

        public bool TryGetPort<TPort>(CapabilityId capability, [NotNullWhen(true)] out TPort? port) where TPort : class, IEmulatorPort
        {
            port = null;
            return false;
        }

        public ValueTask<AdapterResult<SessionCloseResult>> CloseAsync(SessionCloseRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdapterResult<SessionCloseResult>.Success(new(request.OperationId, policy)));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
