namespace RetroIde.Emulation;

/// <summary>Identifies a byte-packed video frame's pixel encoding.</summary>
/// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
public enum VideoPixelFormat
{
    /// <summary>Blue, green, red, and alpha, one byte per component.</summary>
    Bgra32,
    /// <summary>Red, green, blue, and alpha, one byte per component.</summary>
    Rgba32,
    /// <summary>Red, green, and blue, one byte per component.</summary>
    Rgb24,
}
