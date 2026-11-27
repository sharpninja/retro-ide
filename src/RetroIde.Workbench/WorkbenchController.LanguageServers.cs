using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

public sealed partial class WorkbenchController
{
    internal LanguageServerWorkspace LanguageServers { get; }

    internal Task DrainLanguageServersAsync() => LanguageServers.DisposeAsync().AsTask();

    private sealed partial record Registration
    {
        public LanguageServerWorkspace LanguageServers => Controller.LanguageServers;

        public LanguageDocument? ReadLanguageDocument(DocumentContext context) =>
            Controller._documents?.ReadLanguageDocument(context);

        public void ReportLanguageFailure(Exception error, string action) =>
            RuntimeFailures.Report(Controller._logger, error, action);
    }
}
