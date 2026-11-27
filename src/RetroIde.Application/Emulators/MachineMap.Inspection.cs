using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal static partial class MachineMap
{
    internal static AdapterError? CheckMappedAccess(MachineDescriptor machine, SessionOperation scope,
        MemoryAddress address, int bytes, MemoryAccess access)
    {
        var id = scope.OperationId;
        if (machine.MapRevision != address.MapRevision)
            return new(AdapterErrorCodes.RevisionConflict, "The active emulator memory map has changed.", id)
                { CurrentMapRevision = machine.MapRevision };
        var cpu = machine.Cpus.FirstOrDefault(item => string.Equals(item.Id, address.CpuId, StringComparison.Ordinal));
        var space = machine.MemorySpaces.FirstOrDefault(item => string.Equals(item.Id, address.SpaceId, StringComparison.Ordinal));
        if (cpu is null || space is null || !cpu.MemorySpaceIds.Contains(space.Id, StringComparer.Ordinal))
            return new(AdapterErrorCodes.InvalidArgument, "The CPU or memory space is unavailable for the selected target.", id);
        if (!TryEndAddress(address.Offset, bytes, space.AddressBits, space.BytesPerAddress, out var end))
            return new(AdapterErrorCodes.InvalidArgument, "The access exceeds the address space or is not aligned to its units.", id);

        var active = space.Regions.Where(region => IsActive(space, region));
        var selected = active.Where(region => region.Start <= address.Offset && region.End >= address.Offset)
            .OrderByDescending(Priority).FirstOrDefault();
        if (selected is null || end > selected.End ||
            (address.MappingId is not null && !string.Equals(address.MappingId, selected.Mapping?.Id, StringComparison.Ordinal)) ||
            active.Any(region => Priority(region) > Priority(selected) && region.Start <= end && region.End >= address.Offset))
            return new(AdapterErrorCodes.InvalidArgument, "The access crosses or conflicts with the active memory mapping.", id);
        if (!selected.Access.HasFlag(access))
            return new(AdapterErrorCodes.PermissionDenied, access == MemoryAccess.Inspect
                ? "The mapped region cannot be read without side effects." : "The mapped region does not permit this access.", id);
        return null;
    }

    private static bool IsActive(MemorySpaceDescriptor space, MemoryRegionDescriptor region) =>
        region.Mapping is not { SelectionGroup: { } group, Id: { } id } ||
        space.ActiveMappings.Any(selection => string.Equals(group, selection.Key, StringComparison.Ordinal) &&
            string.Equals(id, selection.Value, StringComparison.Ordinal));

    internal static bool TryEndAddress(ulong start, int bytes, int width, int bytesPerAddress, out ulong end)
    {
        end = default;
        if (width is < 1 or > 64 || bytesPerAddress is < 1 or > 64 || bytes <= 0 || bytes % bytesPerAddress != 0)
            return false;
        var max = MaxAddress(width);
        var unitsPastStart = (ulong)(bytes / bytesPerAddress - 1);
        if (start > max || unitsPastStart > max - start)
            return false;
        end = start + unitsPastStart;
        return true;
    }
}
