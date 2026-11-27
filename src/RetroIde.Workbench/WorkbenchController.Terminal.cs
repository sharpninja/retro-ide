using RetroIde.Application;
using RetroIde.Application.Git;
using RetroIde.Application.Terminals;

namespace RetroIde.Workbench;

public sealed partial class WorkbenchController
{
    private sealed partial record Registration
    {
        public async Task<FeatureOutcome> OpenTerminalAsync(GitSelection selection, CancellationToken token)
        {
            if (Controller._documents is not { } documents || !documents.TryReserveTerminal())
                return new("terminal_limit", "Close a terminal before opening another.");
            RepositoryTerminal? pending = null;
            try
            {
                pending = await RepositoryTerminal.StartAsync(selection.Repository, 80, 24, Controller._logger, token)
                    .ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(Controller.Resolve(View.ViewKey), this) || !ReferenceEquals(SelectedGit, selection))
                    return GitOperationRecovery.ChangedSelection();
                if (documents.ShowTerminal(pending) is not { } tab)
                    return new("terminal_limit", "Close a terminal before opening another.");
                pending = null;
                return FeatureOutcome.Success("Repository terminal opened.", tab);
            }
            catch (Exception error) when (RuntimeFailures.CanRecover(error) && error is not OperationCanceledException)
            {
                RuntimeFailures.Report(Controller._logger, error, "Open repository terminal");
                return new("terminal_start_failed", "The native shell could not be started.");
            }
            finally
            {
                documents.ReleaseTerminalReservation();
                if (pending is not null)
                    await pending.DisposeAsync().ConfigureAwait(false);
            }
        }

        public TerminalFeatures.Session[] ListTerminals() => Controller._documents?.ListTerminals() ?? [];

        public RepositoryTerminal? FindTerminal(Guid viewKey) => Controller._documents?.FindTerminal(viewKey);

        public bool TryCloseTerminal(Guid viewKey, out Task shutdown)
        {
            shutdown = Task.CompletedTask;
            return Controller._documents?.TryCloseTerminal(viewKey, out shutdown) == true;
        }
    }
}
