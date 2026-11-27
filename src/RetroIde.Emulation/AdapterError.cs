namespace RetroIde.Emulation;

/// <summary>A stable error code, optional operation correlation and relevant current revision.</summary>
public sealed record AdapterError(string Code, string Message, Guid? OperationId = null, ulong? CurrentRevision = null);
