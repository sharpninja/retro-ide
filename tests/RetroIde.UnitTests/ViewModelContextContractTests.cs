using System.Text.Json;
using System.Text.Json.Nodes;
using RetroIde.Application;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.UnitTests;

/// <summary>Checks the published workbench context and typed feature inventory.</summary>
public sealed class ViewModelContextContractTests
{
    /// <summary>TEST-RETRO-015#ac-test-mcp-viewmodel-context</summary>
    [Fact]
    public void Public_schema_and_snapshot_share_an_exact_allowlist()
    {
        var contract = new ViewContextContract(typeof(WorkbenchViewModel));
        var view = new WorkbenchViewModel(Guid.NewGuid());
        var schema = contract.Schema;
        var snapshot = contract.Capture(view);
        string[] expected = ["activeToolId", "viewKey", "workbenchRevision", "workspaceId"];

        Assert.Equal(expected, Fields(schema.GetProperty("properties")));
        Assert.Equal(expected, Fields(snapshot));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(view.ViewKey, snapshot.GetProperty("viewKey").GetGuid());
        Assert.Equal(view.WorkspaceId, snapshot.GetProperty("workspaceId").GetGuid());
        Assert.Equal(0, snapshot.GetProperty("workbenchRevision").GetInt64());
        var typedSnapshot = JsonSerializer.SerializeToElement(view.Address, WireJson.Options);
        Assert.Equal(JsonValueKind.Null, typedSnapshot.GetProperty("activeToolId").ValueKind);
        Assert.True(contract.TryRead(typedSnapshot, out var restored));
        Assert.Equal(view.Address, restored);
        var properties = schema.GetProperty("properties");
        Assert.Equal(0, properties.GetProperty("workbenchRevision").GetProperty("minimum").GetInt32());
        Assert.Equal(Guid.Empty, properties.GetProperty("viewKey").GetProperty("not").GetProperty("const").GetGuid());
        Assert.Equal(Guid.Empty, properties.GetProperty("workspaceId").GetProperty("not").GetProperty("const").GetGuid());

        var submitted = JsonNode.Parse(snapshot.GetRawText())!.AsObject();
        submitted["workbenchRevision"] = -1;
        Assert.False(contract.TryRead(JsonSerializer.SerializeToElement(submitted), out var negative));
        Assert.Null(negative);
        submitted["workbenchRevision"] = 0;
        submitted["viewKey"] = Guid.Empty;
        Assert.False(contract.TryRead(JsonSerializer.SerializeToElement(submitted), out var empty));
        Assert.Null(empty);
        Assert.False(contract.TryRead(JsonSerializer.SerializeToElement(new { view.ViewKey, command = "hidden" }, WireJson.Options), out _));
    }

    /// <summary>TEST-RETRO-015#ac-test-mcp-inventory</summary>
    [Fact]
    public void Every_feature_has_a_unique_tool_and_context_contract()
    {
        var contract = new ViewContextContract(typeof(WorkbenchViewModel));
        var registry = new FeatureRegistry(contract);
        string[] toolNames =
        [
            "editor_close_document", "editor_edit_text", "editor_find_text", "editor_get_document",
            "editor_redo", "editor_replace_text", "editor_save_document", "editor_undo",
            "git_abort_operation", "git_add_remote", "git_amend", "git_apply_patch", "git_apply_stash", "git_clone_repository", "git_close_diff", "git_close_history", "git_commit",
            "git_continue_operation",
            "git_create_branch", "git_create_stash", "git_delete_branch", "git_discover_repository", "git_drop_stash", "git_export_patch", "git_fetch",
            "git_get_commit_details", "git_get_commit_state", "git_get_diff", "git_get_history", "git_get_operation", "git_get_status",
            "git_initialize_repository", "git_inspect_stash", "git_list_refs", "git_list_remotes", "git_list_stashes", "git_open_diff", "git_open_history",
            "git_pop_stash", "git_pull", "git_push", "git_remove_remote", "git_rename_branch", "git_rename_remote", "git_reset", "git_review_patch", "git_review_reset",
            "git_select_repository", "git_set_remote_url", "git_set_upstream", "git_skip_operation", "git_stage_file", "git_stage_hunks",
            "git_start_operation", "git_switch_branch",
            "git_unstage_file", "git_unstage_hunks",
            "workbench_focus_tool", "workbench_get_context", "workbench_get_display_language", "workbench_set_display_language",
            "workspace_list_children", "workspace_list_roots", "workspace_open_file", "workspace_open_root",
            "workspace_reveal_entry", "workspace_select_entry", "workspace_set_expanded",
        ];
        Assert.Equal(toolNames, registry.Features.Select(x => x.ToolName).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(registry.Features.Count, registry.Features.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        var focus = registry.Describe(registry.FocusTool);
        var read = registry.Describe(registry.GetContext);
        Assert.Equal(contract.Schema.GetRawText(), focus.InputSchema.GetProperty("properties").GetProperty("context").GetRawText());
        Assert.Equal(contract.Schema.GetRawText(), read.OutputSchema.GetProperty("properties").GetProperty("context").GetRawText());
    }

    private static string[] Fields(JsonElement element) => element.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
}
