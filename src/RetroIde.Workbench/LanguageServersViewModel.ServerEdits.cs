using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;

namespace RetroIde.Workbench;

internal sealed partial class LanguageServersViewModel
{
    [ObservableProperty]
    public partial IReadOnlyList<LanguageServerEditPreview> ServerEdits { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyServerEditCommand), nameof(DeclineServerEditCommand))]
    public partial LanguageServerEditPreview? SelectedServerEdit { get; set; }

    [ObservableProperty]
    public partial string? ServerEditText { get; private set; }

    partial void OnSelectedServerEditChanged(LanguageServerEditPreview? value) => RefreshServerEditText();

    private void RefreshServerEditText() => ServerEditText = SelectedServerEdit is { } edit
        ? LanguageEditorResult.FormatPreview(edit.Targets, DescribeEditPath) : null;

    private string DescribeEditPath(LanguageEditTarget target) =>
        Roots.Count > 1 && Roots.FirstOrDefault(root => root.Id == target.RootId) is { } root
            ? Path.Combine(root.Path, target.RelativePath) : target.RelativePath;

    internal void UpdateServerEdits(LanguageServerEditPreview[] edits)
    {
        if (ServerEdits.SequenceEqual(edits))
            return;

        var selected = SelectedServerEdit?.ReviewId;
        ServerEdits = edits;
        SelectedServerEdit = edits.FirstOrDefault(edit => edit.ReviewId == selected) ?? edits.FirstOrDefault();
    }

    private bool HasServerEdit() => SelectedServerEdit is not null;

    [RelayCommand(CanExecute = nameof(HasServerEdit))]
    private Task ApplyServerEditAsync(CancellationToken token) => DecideServerEditAsync(LanguageFeatures.ApplyServerEdit, token);

    [RelayCommand(CanExecute = nameof(HasServerEdit))]
    private Task DeclineServerEditAsync(CancellationToken token) => DecideServerEditAsync(LanguageFeatures.DeclineServerEdit, token);

    private async Task DecideServerEditAsync(Feature<LanguageServerFeatures.DecideServerEditInput> feature, CancellationToken token)
    {
        if (Interaction.IsBlocked(PaneId) || SelectedServerEdit is not { } review)
            return;

        Interaction.Begin(PaneId);
        try { await SendAsync(feature, new LanguageServerFeatures.DecideServerEditInput(review.ReviewId), token).ConfigureAwait(true); }
        finally { Interaction.End(PaneId); }
    }
}
