namespace RetroIde.Emulation;

/// <summary>Describes the sample rate, channel count and an adapter-declared audio encoding.</summary>
/// <param name="SampleRate">Samples per second for each channel.</param>
/// <param name="Channels">Number of channels in the audio stream.</param>
/// <param name="Encoding">Codec name, such as pcm-s16le, pcm-f32le or opus.</param>
/// <param name="BytesPerSample">PCM bytes per sample per channel, or null for an encoded packet stream.</param>
/// <remarks>TEST-RETRO-010#ac-test-media-ports.</remarks>
public sealed record AudioFormat(int SampleRate, int Channels, string Encoding, int? BytesPerSample);
