using System.Text.Json;
using RetroIde.Testing;

var workspaceId = Guid.Parse(args.Single());
var session = new AdapterFixtureSession(Guid.NewGuid(), "running", 42);
var effects = new List<string> { "launch" };

// This process represents only the independent session used by the shell test.
while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } command)
{
    switch (command)
    {
        case "attach":
            effects.Add(command);
            break;
        case "advance":
            session = session with { Revision = session.Revision + 1 };
            effects.Add(command);
            break;
        case "inspect":
            break;
        default:
            throw new InvalidDataException("Unknown adapter fixture command.");
    }

    var assemblies = AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetName().Name ?? string.Empty)
        .Order(StringComparer.Ordinal)
        .ToArray();
    var snapshot = new AdapterFixtureSnapshot(workspaceId, Environment.ProcessId, session, effects.ToArray(), assemblies);
    Console.WriteLine(JsonSerializer.Serialize(snapshot));
}
