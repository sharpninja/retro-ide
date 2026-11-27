using System.Text.Json;

namespace RetroIde.Application.Emulators;

internal sealed record EmulatorSessionPlan(string ProfileId, EmulatorConnection Connection, JsonElement? ProviderSettings = null);
