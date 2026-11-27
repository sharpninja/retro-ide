namespace RetroIde.Emulation;

/// <summary>Identifies an artifact registered with a host transfer mechanism or issued by the backend.</summary>
/// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
public sealed record ArtifactReference(string Id, ArtifactReference.Origin Source)
{
    /// <summary>Identifies who owns or resolves the artifact reference. IDs carry no filesystem paths.</summary>
    /// <remarks>TEST-RETRO-010#ac-test-port-conformance.</remarks>
    public enum Origin
    {
        /// <summary>The IDE registered a local artifact for a supported transfer.</summary>
        LocalAttachment,
        /// <summary>The backend already received and verified the content.</summary>
        Uploaded,
        /// <summary>The backend owns the artifact and issued its identifier.</summary>
        Backend,
    }
}
