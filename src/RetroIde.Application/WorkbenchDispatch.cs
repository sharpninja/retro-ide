using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using RetroIde.Application.Git;
using RetroIde.Application.Workspaces;
using RetroIde.Application.Localization;

namespace RetroIde.Application;

/// <summary>Scope and revocable capabilities for one authenticated attachment or workbench view.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-scope</remarks>
public sealed class ActorGrant(string actor, Guid workspaceId, Guid viewKey, bool canMutate)
{
    private readonly Lock _gate = new();
    private bool _revoked;

    /// <summary>Identifies the actor without exposing credentials.</summary>
    public string Actor { get; } = actor;

    /// <summary>Identifies the granted workspace.</summary>
    public Guid WorkspaceId { get; } = workspaceId;

    /// <summary>Identifies the selected live registration.</summary>
    public Guid ViewKey { get; } = viewKey;

    /// <summary>Indicates whether the grant remains usable.</summary>
    public bool IsActive
    {
        get { lock (_gate) return !_revoked; }
    }

    /// <summary>Invalidates the grant, including requests already waiting for the UI dispatcher.</summary>
    public void Revoke()
    {
        lock (_gate)
            _revoked = true;
    }

    internal OperationResult Authorize(Guid operationId, bool mutation, Func<OperationResult> execute)
    {
        lock (_gate)
        {
            if (_revoked)
                return OperationResult.Failure(operationId, "unauthorized", "The attachment has been revoked.");
            if (mutation && !canMutate)
                return OperationResult.Failure(operationId, "forbidden", "This attachment grants read access only.");
            return execute();
        }
    }
}

/// <summary>Common response envelope for UI and MCP workbench operations.</summary>
/// <remarks>TEST-RETRO-007#ac-test-cqrs-unit-of-work</remarks>
public sealed record OperationResult(Guid OperationId, string Code, string Summary, JsonElement? Context = null)
{
    /// <summary>Gets the detached data returned by a feature, when applicable.</summary>
    public JsonElement? Data { get; init; }

    /// <summary>Creates a structured rejection without view context.</summary>
    public static OperationResult Failure(Guid operationId, string code, string summary) => new(operationId, code, summary);
}

/// <summary>Arguments for the current context query.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-viewmodel-context</remarks>
public sealed record GetContextInput;

/// <summary>Arguments for focusing a tool.</summary>
/// <remarks>TEST-RETRO-007#ac-test-cqrs-unit-of-work</remarks>
public sealed record FocusToolInput(string ToolId);

/// <summary>A live registration resolved on the workbench dispatcher.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-viewmodel-context</remarks>
public interface IWorkbenchRegistration
{
    /// <summary>Gets the current registration identity and revision.</summary>
    WorkbenchAddress Address { get; }

    /// <summary>Gets the source of the explicitly marked context properties.</summary>
    object ContextSource { get; }

    /// <summary>Focuses an eligible tool in the live layout.</summary>
    bool TryFocus(string toolId);
}

/// <summary>Typed execution entry point shared by the view controller and MCP.</summary>
/// <remarks>TEST-RETRO-007#ac-test-cqrs-unit-of-work; TEST-RETRO-015#ac-test-mcp-same-instance</remarks>
public interface IWorkbenchDispatcher
{
    /// <summary>Runs one registered operation in its captured scope.</summary>
    Task<OperationResult> DispatchAsync<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? context,
        ActorGrant actor, Guid operationId, CancellationToken cancellationToken) where TInput : class;
}

/// <summary>Tool description derived from a typed feature registration.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-inventory</remarks>
public sealed record FeatureDescription(JsonElement InputSchema, JsonElement OutputSchema);

/// <summary>Identifies a supported IDE operation and its MCP representation.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-inventory</remarks>
public abstract class FeatureRegistration
{
    private protected FeatureRegistration(string id, string toolName, string description, bool mutation)
    {
        Id = id;
        ToolName = toolName;
        Description = description;
        IsMutation = mutation;
    }

    /// <summary>Gets the stable application feature ID.</summary>
    public string Id { get; }

    /// <summary>Gets the published MCP tool name.</summary>
    public string ToolName { get; }

    /// <summary>Gets the discovery description.</summary>
    public string Description { get; }

    /// <summary>Indicates that the registration needs mutation permission and captured context.</summary>
    public bool IsMutation { get; }

    internal abstract JsonObject InputSchema { get; }
    internal abstract Type? DataType { get; }

    /// <summary>Adapts wire arguments to the registered typed operation.</summary>
    public abstract Task<OperationResult> InvokeAsync(IWorkbenchDispatcher dispatcher, ViewContextContract contract,
        IDictionary<string, JsonElement>? arguments, ActorGrant actor, Guid operationId, CancellationToken cancellationToken);
}

/// <summary>Pairs one typed input with its scoped handler.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-inventory</remarks>
public sealed class Feature<TInput> : FeatureRegistration where TInput : class
{
    private readonly Func<TInput, IWorkbenchRegistration, FeatureOutcome>? _handler;
    internal Func<TInput, IWorkbenchRegistration, CancellationToken, Task<FeatureOutcome>>? PrepareAsync { get; }

    internal Feature(string id, string toolName, string description, bool mutation,
        Func<TInput, IWorkbenchRegistration, FeatureOutcome> handler, Type? dataType = null)
        : base(id, toolName, description, mutation)
    {
        _handler = handler;
        DataType = dataType;
    }

    internal Feature(string id, string toolName, string description,
        Func<TInput, IWorkbenchRegistration, CancellationToken, Task<FeatureOutcome>> prepareAsync, Type dataType, bool mutation = false)
        : base(id, toolName, description, mutation)
    {
        PrepareAsync = prepareAsync;
        DataType = dataType;
    }

    internal override JsonObject InputSchema => WireJson.Options.GetJsonSchemaAsNode(typeof(TInput), FeatureRegistry.RequiredRoot).AsObject();
    internal override Type? DataType { get; }

    internal FeatureOutcome Execute(TInput input, IWorkbenchRegistration view) =>
        (_handler ?? throw new InvalidOperationException("Use async dispatch for this feature."))(input, view);

    /// <inheritdoc />
    public override Task<OperationResult> InvokeAsync(IWorkbenchDispatcher dispatcher, ViewContextContract contract,
        IDictionary<string, JsonElement>? arguments, ActorGrant actor, Guid operationId, CancellationToken cancellationToken)
    {
        WorkbenchAddress? address = null;
        var inputJson = new JsonObject();
        if (arguments is not null)
        {
            foreach (var (name, value) in arguments)
            {
                if (IsMutation && string.Equals(name, "context", StringComparison.Ordinal))
                {
                    if (!contract.TryRead(value, out address))
                        return Invalid();
                }
                else
                {
                    inputJson[name] = JsonNode.Parse(value.GetRawText());
                }
            }
        }

        if (IsMutation && address is null)
            return Invalid();

        TInput? input;
        try { input = inputJson.Deserialize<TInput>(WireJson.Options); }
        catch (JsonException) { return Invalid(); }

        return input is null ? Invalid() : dispatcher.DispatchAsync(this, input, address, actor, operationId, cancellationToken);

        Task<OperationResult> Invalid() =>
            Task.FromResult(OperationResult.Failure(operationId, "invalid_arguments", "The arguments do not match the tool schema."));
    }
}

/// <summary>Single inventory of the supported workbench queries and commands.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-inventory</remarks>
public sealed class FeatureRegistry
{
    internal const string FocusToolId = "workbench.view.focus";

    internal static JsonSchemaExporterOptions RequiredRoot { get; } = new() { TreatNullObliviousAsNonNullable = true };

    /// <summary>Registers the workbench handlers against the live ViewModel contract.</summary>
    public FeatureRegistry(ViewContextContract context)
        : this(context, documentContext: null)
    {
    }

    /// <summary>Registers the common workbench, workspace and scoped Git operations.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-015#ac-test-mcp-inventory. Use one discovery list for Git and the other workbench features.</remarks>
    internal FeatureRegistry(ViewContextContract context, ViewContextContract? documentContext,
        Func<DocumentSaveSnapshot, CancellationToken, Task<PreparedDocumentSave>>? stageSave = null)
    {
        DocumentContextContract = documentContext;
        Context = context;
        GetContext = new("workbench.context.read", "workbench_get_context", "Read the selected workbench's live public context.", false,
            static (_, _) => new("ok", "Workbench context read."));
        FocusTool = new(FocusToolId, "workbench_focus_tool", "Focus a tool pane using the current workbench context.", true,
            static (input, view) => view.TryFocus(input.ToolId)
                ? new("ok", "Tool focused.")
                : new("tool_unavailable", "The selected tool cannot be focused."));
        Explorer = new ExplorerFeatures(stageSave);
        Documents = new DocumentFeatures();
        Localization = new LocalizationFeatures();
        Git = new GitFeatures();
        Features = ImmutableArray.Create<FeatureRegistration>(GetContext, FocusTool, Explorer.OpenRoot, Explorer.ReadRoots,
            Explorer.ReadChildren, Explorer.SetExpansion, Explorer.SelectEntry, Explorer.OpenEntry, Explorer.RevealEntry,
            Explorer.SaveDocument, Documents.GetContext, Documents.Find, Documents.Replace, Documents.Close,
            Documents.Edit, Documents.Undo, Documents.Redo, Localization.GetLanguage, Localization.SetLanguage,
            Git.Discover, Git.Select, Git.Status, Git.Initialize, Git.Clone);
    }

    internal ExplorerFeatures Explorer { get; }
    internal DocumentFeatures Documents { get; }
    internal LocalizationFeatures Localization { get; }
    /// <summary>Gets the Git operations used by the shared controller and MCP registry.</summary>
    internal GitFeatures Git { get; }
    internal ViewContextContract? DocumentContextContract { get; }

    /// <summary>Gets the public workbench context contract shared by its tools.</summary>
    public ViewContextContract Context { get; }

    /// <summary>Gets the context query.</summary>
    public Feature<GetContextInput> GetContext { get; }

    /// <summary>Gets the focus command.</summary>
    public Feature<FocusToolInput> FocusTool { get; }

    /// <summary>Gets all registered operations.</summary>
    public IReadOnlyList<FeatureRegistration> Features { get; }

    /// <summary>Generates the operation's discoverable argument and result schemas.</summary>
    public FeatureDescription Describe(FeatureRegistration feature)
    {
        var input = feature.InputSchema;
        ProjectDocumentMember(input);
        if (feature.IsMutation)
        {
            input["properties"]!.AsObject().Add("context", JsonNode.Parse(Context.Schema.GetRawText()));
            input["required"]!.AsArray().Add("context");
        }

        var output = WireJson.Options.GetJsonSchemaAsNode(typeof(OperationResult), RequiredRoot).AsObject();
        var properties = output["properties"]!.AsObject();
        properties["context"] = JsonNode.Parse(Context.Schema.GetRawText());
        if (feature.DataType is { } type)
            properties["data"] = DescribeData(type);
        else
            properties.Remove("data");
        return new FeatureDescription(JsonSerializer.SerializeToElement(input), JsonSerializer.SerializeToElement(output));
    }

    private JsonNode DescribeData(Type type)
    {
        if (type == typeof(DocumentContext) && DocumentContextContract is { } contract)
            return JsonNode.Parse(contract.Schema.GetRawText())!;

        var schema = WireJson.Options.GetJsonSchemaAsNode(type, RequiredRoot);
        if (schema is JsonObject envelope)
            ProjectDocumentMember(envelope);
        return schema;
    }

    private void ProjectDocumentMember(JsonObject schema)
    {
        if (DocumentContextContract is { } contract && schema["properties"] is JsonObject properties && properties.ContainsKey("document"))
            properties["document"] = JsonNode.Parse(contract.Schema.GetRawText());
    }
}

/// <summary>Serializes authorization, live context validation, handler execution, and result capture on the UI thread.</summary>
/// <remarks>TEST-RETRO-007#ac-test-cqrs-unit-of-work; TEST-RETRO-015#ac-test-mcp-scope</remarks>
public sealed class WorkbenchUnitOfWork(ViewContextContract contract, Func<Guid, IWorkbenchRegistration?> resolve)
{
    /// <summary>Executes one synchronous feature in the authorized registration.</summary>
    public OperationResult Execute<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? captured,
        ActorGrant actor, Guid operationId, CancellationToken cancellationToken) where TInput : class
    {
        return actor.Authorize(operationId, feature.IsMutation, () =>
        {
            var failure = Validate(captured, actor, feature.IsMutation, operationId, cancellationToken, out var view);
            return failure ?? Complete(operationId, view!, feature.Execute(input, view!));
        });
    }

    internal async Task<OperationResult> ExecuteAsync<TInput>(Feature<TInput> feature, TInput input, WorkbenchAddress? captured,
        ActorGrant actor, Guid operationId, CancellationToken cancellationToken) where TInput : class
    {
        if (feature.PrepareAsync is not { } prepare)
            return Execute(feature, input, captured, actor, operationId, cancellationToken);

        // Preparation can do I/O. Apply any modeled change only after revalidating
        // the grant and registration on the caller's dispatcher.
        Task<FeatureOutcome>? pending = null;
        IWorkbenchRegistration? origin = null;
        var prepared = actor.Authorize(operationId, feature.IsMutation, () =>
        {
            var failure = Validate(captured, actor, feature.IsMutation, operationId, cancellationToken, out origin);
            if (failure is not null)
                return failure;
            pending = prepare(input, origin!, cancellationToken);
            return new OperationResult(operationId, "prepared", "");
        });
        if (pending is null)
            return prepared;

        FeatureOutcome outcome;
        try { outcome = await pending.ConfigureAwait(true); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Failure(operationId, "cancelled", "The operation was cancelled.");
        }
        using var preparedResource = outcome.PreparedResource;
        return actor.Authorize(operationId, feature.IsMutation, () =>
        {
            var failure = Validate(captured, actor, feature.IsMutation, operationId, cancellationToken, out var current,
                compareRevision: !outcome.KeepCapturedTarget);
            if (failure is not null)
                return failure;
            if (!ReferenceEquals(origin, current))
                return OperationResult.Failure(operationId, "view_closed", "The originating registration is no longer live.");

            var committed = outcome.Commit?.Invoke(current!) ?? outcome;
            return Complete(operationId, current!, committed);
        });
    }

    private OperationResult? Validate(WorkbenchAddress? captured, ActorGrant actor, bool mutation, Guid operationId,
        CancellationToken cancellationToken, out IWorkbenchRegistration? view, bool compareRevision = true)
    {
        view = null;
        if (cancellationToken.IsCancellationRequested)
            return Reject("cancelled", "The operation was cancelled.");
        if (captured is not null && captured.WorkspaceId != actor.WorkspaceId)
            return Reject("workspace_mismatch", "The context belongs to a different workspace.");

        view = resolve(captured?.ViewKey ?? actor.ViewKey);
        if (view is null)
            return Reject("view_closed", "The workbench registration is no longer live.");

        var current = view.Address;
        if (current.WorkspaceId != actor.WorkspaceId || current.ViewKey != actor.ViewKey)
            return Reject("view_mismatch", "The registration is outside the attachment scope.");
        if (mutation && captured is null)
            return Reject("invalid_arguments", "A captured workbench context is required.");
        if (compareRevision && captured is not null && captured.WorkbenchRevision != current.WorkbenchRevision)
            return Reject("stale_revision", "The workbench changed. Read its current context and retry.");
        if (compareRevision && captured is not null && !string.Equals(captured.ActiveToolId, current.ActiveToolId, StringComparison.Ordinal))
            return Reject("context_mismatch", "The captured tool selection differs from the workbench.");
        return null;

        OperationResult Reject(string code, string summary) => OperationResult.Failure(operationId, code, summary);
    }

    private OperationResult Complete(Guid operationId, IWorkbenchRegistration view, FeatureOutcome outcome) =>
        string.Equals(outcome.Code, "ok", StringComparison.Ordinal)
            ? new OperationResult(operationId, outcome.Code, outcome.Summary, contract.Capture(view.ContextSource)) { Data = outcome.Data }
            : OperationResult.Failure(operationId, outcome.Code, outcome.Summary);
}
