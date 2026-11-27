namespace RetroIde.Emulation;

/// <summary>Identifies the native byte order of a machine, CPU, or address space.</summary>
/// <remarks>TEST-RETRO-017#ac-test-machine-descriptor-contract.</remarks>
public enum ByteOrder
{
    /// <summary>The least significant byte appears first in memory.</summary>
    LittleEndian,
    /// <summary>The most significant byte appears first in memory.</summary>
    BigEndian,
}
