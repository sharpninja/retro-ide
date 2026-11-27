namespace RetroIde.Emulation;

/// <summary>Describes a frame's pixel dimensions, row stride in bytes and packed pixel encoding.</summary>
/// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
public sealed record VideoFormat(int Width, int Height, int Stride, VideoPixelFormat PixelFormat);
