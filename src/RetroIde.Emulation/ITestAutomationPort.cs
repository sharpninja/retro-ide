using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Runs backend-registered emulator test fixtures and reads their structured results.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface ITestAutomationPort : IEmulatorPort
{
    /// <summary>Gets the versioned test automation capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.test-automation", new Version(1, 0));
    /// <summary>Identifies enumeration of advertised fixtures.</summary>
    public const string List = "list";
    /// <summary>Identifies starting a registered fixture.</summary>
    public const string Run = "run";
    /// <summary>Identifies reading a specific run's results.</summary>
    public const string ReadResult = "read-result";

    /// <summary>Lists the fixture identifiers exposed by this emulator.</summary>
    ValueTask<AdapterResult<Fixtures>> ListAsync(SessionOperation scope, CancellationToken cancellationToken);
    /// <summary>Runs a registered test fixture. The operation ID correlates subsequent results.</summary>
    ValueTask<AdapterResult<MutationReceipt>> RunAsync(MutationRequest<RunRequest> request, CancellationToken cancellationToken);
    /// <summary>Reads structured results and artifact references for one run.</summary>
    ValueTask<AdapterResult<Report>> ReadAsync(ScopedQuery<RunIdentity> request, CancellationToken cancellationToken);

    /// <summary>Identifies a registered fixture and its iteration limit.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Fixture(string Id, string Title, int MaxIterations);
    /// <summary>Returns the available fixtures.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Fixtures(ImmutableArray<Fixture> Items);
    /// <summary>Requests one registered fixture with a bounded iteration count.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record RunRequest(string FixtureId, int Iterations);
    /// <summary>Correlates a read to the operation that started the run.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record RunIdentity(Guid OperationId);
    /// <summary>Describes the outcome of a named fixture assertion.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Assertion(string Id, bool Passed, string Message);
    /// <summary>Describes progress and outcomes for the selected run.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Report(RunIdentity Run, bool Completed, ImmutableArray<Assertion> Assertions, ImmutableArray<ArtifactReference> Artifacts);
}
