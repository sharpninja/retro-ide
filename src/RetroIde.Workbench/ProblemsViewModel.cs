using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed partial class ProblemsViewModel(WorkbenchViewModel workbench, FeatureRegistry registry) : LanguageToolViewModel(workbench)
{
    internal const string PaneId = "problems";

    [ObservableProperty]
    public partial IReadOnlyList<ProblemRow> Items { get; private set; } = [];

    [ObservableProperty]
    public partial bool Truncated { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenProblemCommand))]
    public partial ProblemRow? SelectedProblem { get; set; }

    private LanguageProblemSnapshot _snapshot = new([], false);
    private WorkspaceExplorer.Root[] _roots = [];

    internal void Update(LanguageProblemSnapshot snapshot, WorkspaceExplorer.Root[] roots)
    {
        if (snapshot.Truncated == _snapshot.Truncated && snapshot.Items.SequenceEqual(_snapshot.Items) && roots.SequenceEqual(_roots))
            return;

        _snapshot = snapshot;
        _roots = roots;
        RefreshLabels();
    }

    internal void RefreshLabels()
    {
        var selected = SelectedProblem?.Problem;
        Items = _snapshot.Items.Select(item =>
        {
            var file = item.Document.RelativePath;
            if (_roots.Length > 1 && _roots.FirstOrDefault(root => root.Id == item.Document.RootId) is { } root)
                file = Path.Combine(root.Path, file);
            var range = item.Location.Range.Start;
            var detail = string.Create(CultureInfo.InvariantCulture, $"{file} · {(long)range.Line + 1}:{(long)range.Character + 1}");
            var severity = Localization.Text(WorkbenchText.LanguageSeverity(item.Severity));
            var origin = Localization.Text(WorkbenchText.LanguageProblemSource);
            return new ProblemRow(item, $"{severity} · {item.Message}", $"{detail} · {origin}{DescribeSource(item)}");
        }).ToArray();
        Truncated = _snapshot.Truncated;
        SelectedProblem = Items.FirstOrDefault(row => row.Problem == selected);
    }

    private static string DescribeSource(LanguageProblem item) => string.IsNullOrWhiteSpace(item.Source)
        ? "" : string.IsNullOrWhiteSpace(item.Code) ? $" · {item.Source}" : $" · {item.Source} {item.Code}";

    [RelayCommand]
    private Task OpenPaneAsync(CancellationToken token) => SendAsync(registry.FocusTool, new FocusToolInput(PaneId), token);

    private bool CanOpenProblem() => SelectedProblem is not null;

    [RelayCommand(CanExecute = nameof(CanOpenProblem))]
    private Task OpenProblemAsync(CancellationToken token) => SelectedProblem is { Problem: var item }
        ? SendAsync(registry.Language.OpenLocation, new LanguageServerFeatures.OpenLocationInput(item.Document, item.SessionId, item.Location), token)
        : Task.CompletedTask;

    internal sealed record ProblemRow(LanguageProblem Problem, string Title, string Detail)
    {
        public override string ToString() => Title;
    }
}
