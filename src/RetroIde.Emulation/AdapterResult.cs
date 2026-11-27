using System.Diagnostics.CodeAnalysis;

namespace RetroIde.Emulation;

/// <summary>A typed adapter outcome containing exactly one value or structured error.</summary>
public sealed class AdapterResult<T> where T : class
{
    private AdapterResult(T? value, AdapterError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>Gets the returned data when the operation succeeded.</summary>
    public T? Value { get; }

    /// <summary>Gets the error when the operation failed or its outcome is unknown.</summary>
    public AdapterError? Error { get; }

    /// <summary>Reports whether <see cref="Value"/> contains a result.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => Error is null;

    /// <summary>Creates a successful result carrying data.</summary>
    public static AdapterResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new AdapterResult<T>(value, null);
    }

    /// <summary>Creates a failed or uncertain result carrying an error.</summary>
    public static AdapterResult<T> Failure(AdapterError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new AdapterResult<T>(default, error);
    }
}
