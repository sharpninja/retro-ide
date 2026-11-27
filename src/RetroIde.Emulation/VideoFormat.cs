namespace RetroIde.Emulation;

/// <summary>Describes a frame's pixel dimensions, row stride in bytes and packed pixel encoding.</summary>
public sealed record VideoFormat(int Width, int Height, int Stride, VideoPixelFormat PixelFormat);
