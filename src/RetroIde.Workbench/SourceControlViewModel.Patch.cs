using System.Text.Json;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed partial class SourceControlViewModel
{
    internal sealed record PatchFile(string Name, byte[] Bytes);

    internal Func<CancellationToken, Task>? PromptPatchesAsync { get; set; }
    internal Func<CancellationToken, Task<PatchFile?>>? PickPatchAsync { get; set; }
    internal Func<byte[], CancellationToken, Task<bool>>? SavePatchAsync { get; set; }

    [ObservableProperty]
    public partial string? PatchWorktree { get; private set; }
    [ObservableProperty]
    public partial string PatchLayer { get; set; } = "index";
    [ObservableProperty]
    public partial string? PatchFileName { get; private set; }
    [ObservableProperty]
    public partial string? PatchStatus { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPatchCommand))]
    public partial GitPatchOperations.Review? PatchReview { get; private set; }

    private string? _patchBase64;

    internal Control WrapPatches(Control content) => Interaction.Wrap(PaneId, content);
    partial void OnPatchLayerChanged(string value)
    {
        PatchReview = null;
        PatchStatus = null;
    }

    private bool CanApplyPatch() => PatchReview is { CanApply: true };

    [RelayCommand(CanExecute = nameof(CanRefreshStatus))]
    private async Task OpenPatchesAsync(CancellationToken token)
    {
        if (Repository is not { } repo || PromptPatchesAsync is not { } prompt)
            return;
        PatchWorktree = repo.WorktreePath;
        PatchLayer = "index";
        PatchFileName = null;
        PatchReview = null;
        PatchStatus = null;
        _patchBase64 = null;
        await prompt(token).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ExportPatchAsync(CancellationToken token) => RunPatchActionAsync(async (worktree, layer) =>
    {
        if (SavePatchAsync is not { } save)
            return;
        var result = await RequestAsync(Git.ExportPatch, new GitPatchOperations.ExportInput(worktree, layer), token).ConfigureAwait(true);
        if (result is { Code: "ok", Data: { } data } &&
            data.Deserialize<GitPatchOperations.Export>(WireJson.Options) is { } export)
        {
            if (export.Bytes == 0)
                PatchStatus = Localization.Text(WorkbenchText.GitPatchEmpty);
            else if (await save(Convert.FromBase64String(export.PatchBase64), token).ConfigureAwait(true))
                PatchStatus = result.Summary;
        }
        else
            PatchStatus = result?.Summary;
    });

    [RelayCommand]
    private Task ReviewPatchAsync(CancellationToken token) => RunPatchActionAsync(async (worktree, layer) =>
    {
        if (PickPatchAsync is not { } pick || await pick(token).ConfigureAwait(true) is not { } file)
            return;
        PatchFileName = file.Name;
        _patchBase64 = Convert.ToBase64String(file.Bytes);
        var result = await RequestAsync(Git.ReviewPatch, new GitPatchOperations.ReviewInput(worktree, _patchBase64, layer), token)
            .ConfigureAwait(true);
        PatchReview = string.Equals(PatchLayer, layer, StringComparison.Ordinal) && result is { Code: "ok", Data: { } data }
            ? data.Deserialize<GitPatchOperations.Review>(WireJson.Options) : null;
        PatchStatus = result?.Summary;
    }, invalidate: true);

    [RelayCommand(CanExecute = nameof(CanApplyPatch))]
    private Task ApplyPatchAsync(CancellationToken token) => RunPatchActionAsync(async (worktree, layer) =>
    {
        if (PatchReview is not { CanApply: true } review || _patchBase64 is not { } patch)
            return;
        PatchReview = null;
        var result = await RequestAsync(Git.ApplyPatch, new GitPatchOperations.ApplyInput(worktree, patch, layer, review.Revision), token)
            .ConfigureAwait(true);
        PatchStatus = result?.Summary;
    });

    private async Task RunPatchActionAsync(Func<string, string, Task> action, bool invalidate = false)
    {
        if (PatchWorktree is not { } worktree || Interaction.IsBlocked(PaneId))
            return;
        if (invalidate)
            PatchReview = null;
        PatchStatus = null;
        Interaction.Begin(PaneId);
        try { await action(worktree, PatchLayer).ConfigureAwait(true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PatchStatus = Localization.Text(WorkbenchText.GitPatchFileError);
        }
        finally { Interaction.End(PaneId); }
    }
}
