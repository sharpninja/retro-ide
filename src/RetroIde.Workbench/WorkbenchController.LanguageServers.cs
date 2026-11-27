using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

public sealed partial class WorkbenchController
{
    internal LanguageServerWorkspace LanguageServers { get; }

    internal Task DrainLanguageServersAsync() => LanguageServers.DisposeAsync().AsTask();

    internal void ReportLanguageFailure(Exception error, string action) => RuntimeFailures.Report(_logger, error, action);

    private sealed partial record Registration
    {
        public LanguageServerWorkspace LanguageServers => Controller.LanguageServers;

        public LanguageDocument? ReadLanguageDocument(DocumentContext context) =>
            Controller._documents?.ReadLanguageDocument(context);

        public FeatureOutcome ApplyLanguageEdits(LanguageBufferEdit[] documents) =>
            Controller._documents?.ApplyLanguageEdits(documents) ?? new("view_closed", "The source editor is unavailable.");

        public void ReportLanguageFailure(Exception error, string action) =>
            Controller.ReportLanguageFailure(error, action);
    }
}
