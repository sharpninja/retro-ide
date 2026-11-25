using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using RetroIde.Workbench;

[assembly: AvaloniaTestApplication(typeof(RetroIde.IntegrationTests.TestApplication))]

namespace RetroIde.IntegrationTests;

/// <summary>Runs the production workbench controls on the Avalonia headless platform.</summary>
public static class TestApplication
{
    /// <summary>Creates a headless application with the production styles.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<WorkbenchApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
