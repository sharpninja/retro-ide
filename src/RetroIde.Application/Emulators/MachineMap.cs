using System.Collections.Immutable;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal static partial class MachineMap
{
    private const int MaximumRegionsPerSpace = 1024;
    private const int MaximumSpaces = 256;
    private const int MaximumTargetComponents = 256;
    private const MemoryAccess KnownAccess = MemoryAccess.Inspect | MemoryAccess.DeviceRead | MemoryAccess.Write;

    internal static bool IsValid(MachineDescriptor machine)
    {
        if (!Identifier(machine.MachineId) || !KnownOrder(machine.DefaultByteOrder) ||
            (machine.Profile is { } profile && (!EmulatorBundleManifest.IsIdentifier(profile.Id) || profile.SchemaVersion < 1)) ||
            !UniqueIds(machine.Cpus, static cpu => cpu.Id, MaximumTargetComponents) || machine.Cpus.IsEmpty ||
            !UniqueIds(machine.Clocks, static clock => clock.Id, MaximumTargetComponents) ||
            !UniqueIds(machine.MemorySpaces, static space => space.Id, MaximumSpaces))
            return false;

        var clocks = machine.Clocks.Select(static clock => clock.Id).ToHashSet(StringComparer.Ordinal);
        var spaces = machine.MemorySpaces.Select(static space => space.Id).ToHashSet(StringComparer.Ordinal);
        return machine.Clocks.All(static clock => clock.NominalHertz is not 0 &&
                   UniqueIds(clock.Phases, static phase => phase, MaximumTargetComponents)) &&
               machine.Cpus.All(cpu => Identifier(cpu.Family) && Identifier(cpu.Model) && Identifier(cpu.Variant) &&
                   KnownOrder(cpu.DefaultByteOrder) &&
                   UniqueIds(cpu.ClockDomainIds, static id => id, MaximumTargetComponents) && cpu.ClockDomainIds.All(clocks.Contains) &&
                   UniqueIds(cpu.MemorySpaceIds, static id => id, MaximumSpaces) && cpu.MemorySpaceIds.All(spaces.Contains)) &&
               machine.MemorySpaces.All(IsValidSpace);
    }

    private static bool IsValidSpace(MemorySpaceDescriptor space)
    {
        if (space.AddressBits is < 1 or > 64 || space.BytesPerAddress is < 1 or > 64 || !KnownOrder(space.DefaultByteOrder) ||
            !UniqueIds(space.Regions, static region => region.Id, MaximumRegionsPerSpace) || space.ActiveMappings is null ||
            space.ActiveMappings.Count > MaximumRegionsPerSpace)
            return false;

        var maximum = MaxAddress(space.AddressBits);
        var mappings = new HashSet<(string Group, string Mapping)>();
        var definitions = new Dictionary<string, MemoryMapping>(StringComparer.Ordinal);
        var mandatoryGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in space.Regions)
        {
            if (!IsValidRegion(region, maximum))
                return false;
            if (region.Mapping is { } definition && !definitions.TryAdd(definition.Id, definition) && definitions[definition.Id] != definition)
                return false;
            if (region.Mapping is { SelectionGroup: { } group } mapping)
            {
                mappings.Add((group, mapping.Id));
                if (mapping.Kind == MemoryMappingKind.Bank)
                    mandatoryGroups.Add(group);
            }
        }
        return space.ActiveMappings.All(selection => mappings.Contains((selection.Key, selection.Value))) &&
               mandatoryGroups.All(group => space.ActiveMappings.Keys.Contains(group, StringComparer.Ordinal)) &&
               space.Regions.All(region => MirrorHasTarget(region, space.Regions)) && NoAmbiguousOverlap(space.Regions);
    }

    private static bool IsValidRegion(MemoryRegionDescriptor region, ulong maximum)
    {
        if (region.Start > region.End || region.End > maximum || !Enum.IsDefined(region.Kind) ||
            (region.Access & ~KnownAccess) != MemoryAccess.None ||
            (region.Kind == MemoryRegionKind.Rom && region.Access.HasFlag(MemoryAccess.Write)))
            return false;
        if (region.Mapping is not { } mapping)
            return true;
        if (!Identifier(mapping.Id) || (mapping.SelectionGroup is not null && !Identifier(mapping.SelectionGroup)))
            return false;

        return mapping.Kind switch
        {
            MemoryMappingKind.Bank => mapping.SelectionGroup is not null && mapping.TargetStart is null && mapping.Priority == 0,
            MemoryMappingKind.Overlay => mapping.TargetStart is null && mapping.Priority > 0,
            MemoryMappingKind.Mirror => mapping.SelectionGroup is null && mapping.TargetStart is { } target && target <= maximum &&
                region.End - region.Start <= maximum - target && mapping.Priority == 0,
            MemoryMappingKind.Segment => mapping.TargetStart is { } segment &&
                region.End - region.Start <= ulong.MaxValue - segment && mapping.Priority == 0,
            _ => false,
        };
    }

    private static bool MirrorHasTarget(MemoryRegionDescriptor alias, ImmutableArray<MemoryRegionDescriptor> regions) =>
        alias.Mapping is not { Kind: MemoryMappingKind.Mirror, TargetStart: { } start } ||
        regions.Any(region => region.Mapping is null && region.Kind == alias.Kind && region.Start <= start &&
            region.End >= start + (alias.End - alias.Start) && (region.Access & alias.Access) == alias.Access);

    private static bool NoAmbiguousOverlap(ImmutableArray<MemoryRegionDescriptor> regions)
    {
        var ordered = regions.OrderBy(static region => region.Start).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var left = ordered[index];
            for (var other = index + 1; other < ordered.Length && ordered[other].Start <= left.End; other++)
            {
                var right = ordered[other];
                if (!MutuallyExclusive(left.Mapping, right.Mapping) && Priority(left) == Priority(right))
                    return false;
            }
        }
        return true;
    }

    private static bool MutuallyExclusive(MemoryMapping? left, MemoryMapping? right) =>
        left is { SelectionGroup: { } group } && right is not null &&
        string.Equals(group, right.SelectionGroup, StringComparison.Ordinal) && !string.Equals(left.Id, right.Id, StringComparison.Ordinal);

    private static int Priority(MemoryRegionDescriptor region) => region.Mapping?.Priority ?? 0;

    private static bool KnownOrder(ByteOrder? value) => value is null || Enum.IsDefined(value.Value);

    private static ulong MaxAddress(int bits) => bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;

    private static bool UniqueIds<T>(ImmutableArray<T> items, Func<T, string> key, int maximum)
    {
        if (items.IsDefault || items.Length > maximum)
            return false;
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null)
                return false;
            var id = key(item);
            if (!Identifier(id) || !unique.Add(id))
                return false;
        }
        return true;
    }

    internal static bool Identifier(string? value) =>
        value is { Length: > 0 and <= 96 } && char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
