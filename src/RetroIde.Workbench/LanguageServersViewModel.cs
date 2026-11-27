using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed partial class LanguageServersViewModel(WorkbenchViewModel workbench, FeatureRegistry registry,
    InteractionBlocks interaction) : LanguageToolViewModel(workbench)
{
    internal const string PaneId = "language-servers";

    private LanguageServerFeatures LanguageFeatures => registry.Language;
    private InteractionBlocks Interaction => interaction;

    private WorkspaceExplorer.Root[] _workspaceRoots = [];
    private LanguageServerStatus[] _profiles = [];

    [ObservableProperty]
    public partial IReadOnlyList<WorkspaceExplorer.Root> Roots { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCommand))]
    public partial WorkspaceExplorer.Root? SelectedRoot { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ProfileRow> Profiles { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestartCommand), nameof(StopCommand), nameof(RemoveCommand), nameof(ReadOutputCommand))]
    public partial ProfileRow? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string? ProfileId { get; set; } = LanguageProfileKey.DefaultProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCommand))]
    public partial string? Executable { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCommand))]
    public partial string? Associations { get; set; }

    [ObservableProperty]
    public partial string? Arguments { get; set; } = "[]";

    [ObservableProperty]
    public partial string? InitializationOptions { get; set; }

    [ObservableProperty]
    public partial string? Settings { get; set; }

    [ObservableProperty]
    public partial string? StandardError { get; private set; }

    internal void Update(WorkspaceExplorer.Root[] roots, LanguageServerStatus[] profiles)
    {
        var rootsChanged = !_workspaceRoots.SequenceEqual(roots);
        if (rootsChanged)
        {
            _workspaceRoots = roots;
            var root = SelectedRoot?.Id;
            Roots = roots;
            SelectedRoot = roots.FirstOrDefault(candidate => candidate.Id == root) ?? roots.FirstOrDefault();
            RefreshServerEditText();
        }

        if (!rootsChanged && _profiles.SequenceEqual(profiles, ProfileStateEquality.Instance))
            return;

        _profiles = profiles;
        RefreshLabels();
    }

    internal void RefreshLabels()
    {
        var key = SelectedProfile?.Key;
        Profiles = _profiles.Select(ProfileRowFor).ToArray();
        SelectedProfile = Profiles.FirstOrDefault(row => row.Key == key);
    }

    private ProfileRow ProfileRowFor(LanguageServerStatus profile)
    {
        var state = Localization.Text(WorkbenchText.LanguageState(profile.State));
        var root = Roots.FirstOrDefault(item => item.Id == profile.RootId)?.Path;
        var executable = profile.Name ?? Path.GetFileName(profile.Executable);
        return new(profile, $"{profile.ProfileId} · {state}", string.Join(" · ", new[] { root, executable, profile.PositionEncoding }
            .Where(value => !string.IsNullOrEmpty(value))));
    }

    partial void OnSelectedProfileChanged(ProfileRow? oldValue, ProfileRow? newValue)
    {
        if (oldValue?.Key != newValue?.Key)
            StandardError = null;
    }

    [RelayCommand]
    private Task OpenPaneAsync(CancellationToken token) => SendAsync(registry.FocusTool, new FocusToolInput(PaneId), token);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken token)
    {
        var result = await SendAsync(registry.Language.List, new GetContextInput(), token).ConfigureAwait(true);
        if (result is { Code: "ok", Data: { } data } && data.Deserialize<LanguageServerStatus[]>(WireJson.Options) is { } profiles)
            Update(_workspaceRoots, profiles);
    }

    private bool CanConfigure() => SelectedRoot is not null && !string.IsNullOrWhiteSpace(Executable) &&
        !string.IsNullOrWhiteSpace(Associations);

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private Task ConfigureAsync(CancellationToken token)
    {
        if (SelectedRoot is not { } root)
            return Task.CompletedTask;

        if (!TryCreateConfiguration(root.Id, out var input))
        {
            ReportInputError(WorkbenchText.LanguageInvalidFields);
            return Task.CompletedTask;
        }
        return ChangeAsync(registry.Language.Configure, input, token);
    }

    private bool HasProfile() => SelectedProfile is not null;
    private bool CanStop() => SelectedProfile?.Status.State is "running" or "starting" or "faulted";

    [RelayCommand(CanExecute = nameof(HasProfile))]
    private Task RestartAsync(CancellationToken token) => ForSelectedAsync(registry.Language.Restart, token);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync(CancellationToken token) => ForSelectedAsync(registry.Language.Stop, token);

    [RelayCommand(CanExecute = nameof(HasProfile))]
    private Task RemoveAsync(CancellationToken token) => ForSelectedAsync(registry.Language.Remove, token);

    [RelayCommand(CanExecute = nameof(HasProfile))]
    private async Task ReadOutputAsync(CancellationToken token)
    {
        if (SelectedProfile is not { Key: var key })
            return;
        var result = await SendAsync(registry.Language.Output, new LanguageServerFeatures.RootInput(key.RootId, key.ProfileId), token)
            .ConfigureAwait(true);
        if (SelectedProfile?.Key == key && result is { Code: "ok", Data: { } data })
            StandardError = data.Deserialize<LanguageServerOutput>(WireJson.Options)?.StandardError;
    }

    private Task ForSelectedAsync(Feature<LanguageServerFeatures.RootInput> feature, CancellationToken token) =>
        SelectedProfile is { Key: var key } ? ChangeAsync(feature, new(key.RootId, key.ProfileId), token) : Task.CompletedTask;

    private async Task ChangeAsync<T>(Feature<T> feature, T input, CancellationToken token) where T : class
    {
        if (interaction.IsBlocked(PaneId))
            return;

        interaction.Begin(PaneId);
        try
        {
            var result = await SendAsync(feature, input, token).ConfigureAwait(true);
            if (result is { Code: "ok", Data: { } data } &&
                data.Deserialize<LanguageServerStatus>(WireJson.Options) is { } status)
                SelectedProfile = Profiles.FirstOrDefault(row => row.Key == new LanguageProfileKey(status.RootId, status.ProfileId));
        }
        finally { interaction.End(PaneId); }
    }

    private bool TryCreateConfiguration(Guid root, out LanguageServerFeatures.ConfigureInput input)
    {
        input = null!;
        var associations = new List<LanguageAssociation>();
        foreach (var line in (Associations ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0)
                return false;
            associations.Add(new(parts[0], parts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }
        try
        {
            var args = string.IsNullOrWhiteSpace(Arguments) ? [] : JsonSerializer.Deserialize<string[]>(Arguments, WireJson.Options);
            if (args is null)
                return false;
            input = new(root, Executable?.Trim() ?? "", associations.ToArray(), args,
                ReadJson(InitializationOptions), ReadJson(Settings),
                string.IsNullOrWhiteSpace(ProfileId) ? null : ProfileId.Trim());
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static JsonElement? ReadJson(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<JsonElement>(value, WireJson.Options);

    internal sealed record ProfileRow(LanguageServerStatus Status, string Title, string Detail)
    {
        internal LanguageProfileKey Key => new(Status.RootId, Status.ProfileId);

        public override string ToString() => Title;
    }

    private sealed class ProfileStateEquality : IEqualityComparer<LanguageServerStatus>
    {
        internal static ProfileStateEquality Instance { get; } = new();

        public bool Equals(LanguageServerStatus? first, LanguageServerStatus? second)
        {
            if (first is null || second is null)
                return ReferenceEquals(first, second);
            return first with { WorkspaceRootIds = null } == second with { WorkspaceRootIds = null } &&
                (first.WorkspaceRootIds ?? []).SequenceEqual(second.WorkspaceRootIds ?? []);
        }

        public int GetHashCode(LanguageServerStatus status) => HashCode.Combine(status.RootId, status.ProfileId);
    }
}
