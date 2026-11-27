using CommunityToolkit.Mvvm.Input;
using RetroIde.Application.Terminals;

namespace RetroIde.Workbench;

internal sealed partial class SourceControlViewModel
{
    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private async Task OpenTerminalAsync(CancellationToken token)
    {
        if (Repository is not { } repository)
            return;
        Interaction.Begin(PaneId);
        try
        {
            await RequestAsync(Terminals.Open, new TerminalFeatures.OpenInput(repository.WorktreePath), token).ConfigureAwait(true);
        }
        finally { Interaction.End(PaneId); }
    }
}
