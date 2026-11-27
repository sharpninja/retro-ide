using System.Collections.Immutable;

namespace RetroIde.Emulation;

/// <summary>Exposes scoped stop rules and native instruction inspection where advertised.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public interface IDebuggerPort : IEmulatorPort
{
    /// <summary>Gets the versioned debugger capability ID.</summary>
    public static CapabilityId Capability { get; } = new("retroide.debugger", new Version(1, 0));
    /// <summary>Identifies enumeration of breakpoints and watchpoints.</summary>
    public const string ListRules = "list-rules";
    /// <summary>Identifies a change to a breakpoint or watchpoint.</summary>
    public const string ChangeRule = "change-rule";
    /// <summary>Identifies a native disassembly query.</summary>
    public const string Disassemble = "disassemble";
    /// <summary>Identifies a bounded instruction trace query.</summary>
    public const string ReadTrace = "read-trace";

    /// <summary>Lists the selected CPU's installed stop rules.</summary>
    ValueTask<AdapterResult<RuleSet>> ListRulesAsync(ScopedQuery<Cpu> request, CancellationToken cancellationToken);
    /// <summary>Creates, updates or removes a rule using its stable ID.</summary>
    ValueTask<AdapterResult<MutationReceipt>> ChangeRuleAsync(MutationRequest<RuleChange> request, CancellationToken cancellationToken);
    /// <summary>Returns native decoding for the bounded, revision-scoped address range.</summary>
    ValueTask<AdapterResult<Decoded>> DisassembleAsync(ScopedQuery<DisassemblyRange> request, CancellationToken cancellationToken);
    /// <summary>Reads the requested slice of a CPU's instruction trace.</summary>
    ValueTask<AdapterResult<Trace>> ReadTraceAsync(ScopedQuery<TraceRange> request, CancellationToken cancellationToken);

    /// <summary>Identifies one CPU.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Cpu(string Id);
    /// <summary>Identifies the accesses that cause a rule to stop the guest.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    [Flags]
    public enum Trigger
    {
        /// <summary>No stop condition.</summary>
        None = 0,
        /// <summary>Stop before execution at the address.</summary>
        Execute = 1,
        /// <summary>Stop on a guest read.</summary>
        Read = 2,
        /// <summary>Stop on a guest write.</summary>
        Write = 4,
    }
    /// <summary>Describes an address-scoped execution breakpoint or memory watchpoint.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Rule(string Id, MemoryAddress Address, int ByteCount, Trigger When, bool Enabled);
    /// <summary>Returns rules at the observed state revision.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record RuleSet(string CpuId, ulong StateRevision, ImmutableArray<Rule> Items);
    /// <summary>Names the two ways a backend can change a rule.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public enum RuleAction
    {
        /// <summary>Create or replace the rule by ID, including its enabled state.</summary>
        Put,
        /// <summary>Remove the rule by ID.</summary>
        Remove,
    }
    /// <summary>Requests a change to one rule. Removal uses the rule's stable ID.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record RuleChange(RuleAction Action, Rule Rule);
    /// <summary>Bounds the number of instructions decoded beginning at an exact address.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record DisassemblyRange(MemoryAddress Start, int InstructionCount);
    /// <summary>Identifies an instruction and its source bytes.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Instruction(MemoryAddress Address, ImmutableArray<byte> Bytes, string Mnemonic, string Operands);
    /// <summary>Returns a bounded disassembly at an observed state revision.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Decoded(MemoryAddress Start, ulong StateRevision, ImmutableArray<Instruction> Items);
    /// <summary>Requests recorded instructions after an optional per-CPU cursor.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record TraceRange(string CpuId, ulong? After, int Limit);
    /// <summary>Reports an observed instruction and timestamp at a sequence.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record TraceEntry(ulong Sequence, MediaTimestamp Timestamp, Instruction Instruction);
    /// <summary>Returns trace entries and reports whether the requested cursor expired.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public sealed record Trace(string CpuId, bool Gap, ImmutableArray<TraceEntry> Entries);
}
