using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIde.Application;
using RetroIde.Application.Emulators;
using RetroIde.Application.Workspaces;
using Bucket = RetroIde.Application.Emulators.EmulatorWorkspace.BucketState;
using Installed = RetroIde.Application.Emulators.EmulatorWorkspace.InstalledState;
using Pin = RetroIde.Application.Emulators.EmulatorBundleStore.Pin;
using Project = RetroIde.Application.Workspaces.WorkspaceExplorer.Root;
using Release = RetroIde.Application.Emulators.EmulatorWorkspace.ReleaseState;

namespace RetroIde.Workbench;

internal sealed partial class EmulatorsViewModel : DockedToolViewModel
{
    internal const string PaneId = "emulators";
    private readonly FeatureRegistry _registry;
    private readonly InteractionBlocks _interaction;
    private readonly ILogger _logger;
    private CancellationTokenSource? _activeCommand;
    private Task _pendingWork = Task.CompletedTask;
    private bool _closing;

    internal EmulatorsViewModel(WorkbenchViewModel workbench, FeatureRegistry registry, InteractionBlocks interaction, ILogger logger) : base(workbench)
    {
        _registry = registry;
        _interaction = interaction;
        _logger = logger;
        PropertyChanged += (_, _) => UpdateEligibility();
    }

    [ObservableProperty] public partial Bucket[] Buckets { get; private set; } = [];
    [ObservableProperty] public partial Bucket? SelectedBucket { get; set; }
    [ObservableProperty] public partial Release? SelectedRelease { get; set; }
    [ObservableProperty] public partial Installed[] Installations { get; private set; } = [];
    [ObservableProperty] public partial Installed? SelectedInstallation { get; set; }
    [ObservableProperty] public partial Project[] Projects { get; private set; } = [];
    [ObservableProperty] public partial Project? SelectedProject { get; set; }
    [ObservableProperty] public partial Pin? ActivePin { get; private set; }
    [ObservableProperty] public partial Pin[] PreviousPins { get; private set; } = [];
    [ObservableProperty] public partial Pin? SelectedPrevious { get; set; }
    [ObservableProperty] public partial string? BucketId { get; set; } = "official";
    [ObservableProperty] public partial string? Repository { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; private set; }

    partial void OnSelectedBucketChanged(Bucket? value) =>
        SelectedRelease = value?.Releases.FirstOrDefault(release => release.Pin == SelectedRelease?.Pin) ??
            value?.Releases.FirstOrDefault();

    partial void OnSelectedProjectChanged(Project? value)
    {
        if (!IsBusy && value is not null)
            ReadProjectCommand.Execute(null);
    }

    [RelayCommand]
    private async Task OpenPaneAsync(CancellationToken token)
    {
        if (await SendAsync(_registry.FocusTool, new FocusToolInput(PaneId), token).ConfigureAwait(true) is { Code: "ok" })
            await ReloadAsync(token).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task ReloadAsync(CancellationToken token) => BlockAsync(ReloadCoreAsync, token);

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private Task ConfigureAsync(CancellationToken token) => ChangeAsync(_registry.Emulators.Configure, new(BucketId!, Repository!), token);

    [RelayCommand(CanExecute = nameof(HasBucket))]
    private Task RefreshBucketAsync(CancellationToken token) => ChangeAsync(_registry.Emulators.Refresh, new(SelectedBucket!.Id), token);

    [RelayCommand(CanExecute = nameof(HasBucket))]
    private Task RemoveBucketAsync(CancellationToken token) => ChangeAsync(_registry.Emulators.Remove, new(SelectedBucket!.Id), token);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync(CancellationToken token) => ChangeAsync(_registry.Emulators.Install, new(SelectedRelease!.Pin), token);

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private Task UpdateAsync(CancellationToken token) =>
        ChangeAsync(_registry.Emulators.Update, new(SelectedInstallation!.Pin, SelectedRelease!.Pin), token);

    [RelayCommand(CanExecute = nameof(HasInstallation))]
    private Task RemoveAsync(CancellationToken token) => ChangeAsync(_registry.Emulators.RemoveBundle, new(SelectedInstallation!.Pin), token);

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private Task SelectAsync(CancellationToken token) =>
        ChangeAsync(_registry.Emulators.Select, new(SelectedProject!.Id, SelectedInstallation!.Pin), token);

    [RelayCommand(CanExecute = nameof(CanRollback))]
    private Task RollbackAsync(CancellationToken token) =>
        ChangeAsync(_registry.Emulators.Rollback, new(SelectedProject!.Id, SelectedPrevious!), token);

    [RelayCommand]
    private Task ReadProjectAsync(CancellationToken token) => BlockAsync(LoadProjectAsync, token);

    private bool IsAvailable() => !_closing && !IsBusy && ParentViewKey is not null;
    private bool CanConfigure() => IsAvailable() && !string.IsNullOrWhiteSpace(BucketId) && !string.IsNullOrWhiteSpace(Repository);
    private bool HasBucket() => IsAvailable() && SelectedBucket is not null;
    private bool HasInstallation() => IsAvailable() && SelectedInstallation is not null;
    private bool CanInstall() => IsAvailable() && SelectedRelease is { IsCompatible: true };
    private bool CanUpdate() => CanInstall() && SelectedInstallation is { } installed && SelectedRelease is { } release &&
        string.Equals(installed.Pin.Bucket, release.Pin.Bucket, StringComparison.Ordinal) &&
        string.Equals(installed.Pin.Bundle, release.Pin.Bundle, StringComparison.Ordinal) &&
        !string.Equals(installed.Pin.Version, release.Pin.Version, StringComparison.Ordinal);
    private bool CanSelect() => IsAvailable() && SelectedProject is not null && SelectedInstallation is { IsCompatible: true };
    private bool CanRollback() => IsAvailable() && SelectedProject is not null && SelectedPrevious is { } pin &&
        Installations.Any(installed => installed.Pin == pin && installed.IsCompatible);

    private void UpdateEligibility()
    {
        ReloadCommand.NotifyCanExecuteChanged();
        ConfigureCommand.NotifyCanExecuteChanged();
        RefreshBucketCommand.NotifyCanExecuteChanged();
        RemoveBucketCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        SelectCommand.NotifyCanExecuteChanged();
        RollbackCommand.NotifyCanExecuteChanged();
    }

    private Task ChangeAsync<T>(Feature<T> feature, T input, CancellationToken token) where T : class => BlockAsync(async commandToken =>
    {
        var result = await SendAsync(feature, input, commandToken).ConfigureAwait(true);
        if (result is { Code: "ok" })
        {
            await ReloadCoreAsync(commandToken).ConfigureAwait(true);
            Receive(result);
        }
    }, token);

    private async Task ReloadCoreAsync(CancellationToken token)
    {
        if (Data<EmulatorWorkspace.CatalogState>(await SendAsync(_registry.Emulators.List, new GetContextInput(), token).ConfigureAwait(true)) is { } catalog)
        {
            var previous = SelectedBucket?.Id;
            Buckets = catalog.Buckets;
            SelectedBucket = Buckets.FirstOrDefault(bucket => string.Equals(bucket.Id, previous, StringComparison.Ordinal)) ?? Buckets.FirstOrDefault();
        }
        if (Data<Installed[]>(await SendAsync(_registry.Emulators.Inventory, new GetContextInput(), token).ConfigureAwait(true)) is { } installed)
        {
            var previous = SelectedInstallation?.Pin;
            Installations = installed;
            SelectedInstallation = installed.FirstOrDefault(item => item.Pin == previous) ?? installed.FirstOrDefault();
        }
        if (Data<Project[]>(await SendAsync(_registry.Explorer.ReadRoots, new ExplorerFeatures.ReadRootsInput(), token).ConfigureAwait(true)) is { } roots)
        {
            var previous = SelectedProject?.Id;
            Projects = roots.Where(static root => root.Kind == WorkspaceExplorer.RootKind.Project).ToArray();
            SelectedProject = Projects.FirstOrDefault(root => root.Id == previous) ?? Projects.FirstOrDefault();
        }
        await LoadProjectAsync(token).ConfigureAwait(true);
    }

    private async Task LoadProjectAsync(CancellationToken token)
    {
        var selected = SelectedProject;
        var read = selected is null ? null :
            Data<EmulatorWorkspace.SelectionState>(await SendAsync(_registry.Emulators.Selection, new EmulatorFeatures.ProjectInput(selected.Id), token).ConfigureAwait(true));
        if (SelectedProject != selected)
            return;
        var previous = SelectedPrevious;
        ActivePin = read?.Selection?.Active;
        PreviousPins = read?.Selection?.Previous.ToArray() ?? [];
        SelectedPrevious = PreviousPins.FirstOrDefault(pin => pin == previous) ?? PreviousPins.FirstOrDefault();
    }

    internal void CancelPending()
    {
        _closing = true;
        _activeCommand?.Cancel();
    }

    internal Task DrainAsync()
    {
        CancelPending();
        return _pendingWork;
    }

    private Task BlockAsync(Func<CancellationToken, Task> operation, CancellationToken token)
    {
        if (!IsAvailable())
            return Task.CompletedTask;
        var command = CancellationTokenSource.CreateLinkedTokenSource(token);
        _activeCommand = command;
        IsBusy = true;
        _interaction.Begin(PaneId);
        _pendingWork = RunBlockedAsync(operation, command);
        return _pendingWork;
    }

    private async Task RunBlockedAsync(Func<CancellationToken, Task> operation, CancellationTokenSource command)
    {
        try { await operation(command.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) when (command.IsCancellationRequested) { }
        finally
        {
            _activeCommand = null;
            command.Dispose();
            _interaction.End(PaneId);
            IsBusy = false;
        }
    }

    private T? Data<T>(OperationResult? result) where T : class
    {
        if (result is not { Code: "ok", Data: { } payload })
            return null;
        try { return payload.Deserialize<T>(WireJson.Options); }
        catch (JsonException error)
        {
            RuntimeFailures.Report(_logger, error, "Display emulator management results");
            Receive(OperationResult.Failure(result.OperationId, "emulator_result_invalid", Localization.Text("emulator.pane.result-invalid")));
            return null;
        }
    }
}
