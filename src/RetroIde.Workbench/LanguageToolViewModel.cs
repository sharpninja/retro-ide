using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroIde.Application;

namespace RetroIde.Workbench;

internal abstract partial class LanguageToolViewModel(WorkbenchViewModel workbench) : ObservableObject, IView
{
    public Guid ViewKey { get; } = Guid.NewGuid();

    internal Guid WorkspaceId => workbench.WorkspaceId;
    internal Guid? ParentViewKey { get; set; }
    internal WorkbenchLocalization Localization => workbench.Localization ?? WorkbenchLocalization.English;

    public event EventHandler<ViewCommandRequested>? CommandRequested;

    [ObservableProperty]
    public partial OperationResult? LastResult { get; private set; }

    public void Receive(OperationResult result)
    {
        Dispatcher.UIThread.VerifyAccess();
        LastResult = result;
    }

    protected async Task<OperationResult?> SendAsync<T>(Feature<T> feature, T input, CancellationToken token) where T : class
    {
        Dispatcher.UIThread.VerifyAccess();
        var request = new ViewCommandRequested(Guid.NewGuid(), feature.Id, workbench.Address, ViewKey, input, token);
        CommandRequested?.Invoke(this, request);
        if (request.Completion is { } completion)
            await completion.ConfigureAwait(true);
        return request.Result;
    }

    protected void ReportInputError(string slug) =>
        Receive(OperationResult.Failure(Guid.NewGuid(), "invalid_language_configuration", Localization.Text(slug)));
}
