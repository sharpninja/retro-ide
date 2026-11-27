using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies the document version and persisted-text contract independently of the LSP process.</summary>
/// <remarks>2026-11-26. TEST-RETRO-014#ac-test-language-documents. Exercises the observable text, version and save behavior for the three synchronization modes.</remarks>
public sealed partial class Phase03LanguageVersionContractTests
{
    internal const string OriginalText = "int original;\n";
    private const string EditedText = "int changed;\n";

    /// <summary>Accepts each supported mode and rejects wrong versions or stale text.</summary>
    /// <remarks>2026-11-26. TEST-RETRO-014#ac-test-language-documents. Validates the assertions against controlled observations before the native fixture binding.</remarks>
    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(0, "fallback-reuses-version")]
    [InlineData(1, "reopen-reuses-version")]
    [InlineData(2, "stale-server-buffer")]
    [InlineData(2, "dirty-write")]
    [InlineData(1, "stale-save-notification")]
    [InlineData(2, "missing-save-write")]
    public async Task Reference_detects_version_and_save_faults(int syncKind, string? fault)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reference = new ReferenceProbe(syncKind, fault);
        if (fault is null)
            await VerifyTimelineAsync(reference, timeout.Token).ConfigureAwait(false);
        else
            await Assert.ThrowsAnyAsync<XunitException>(() => VerifyTimelineAsync(reference, timeout.Token)).ConfigureAwait(false);
    }

    internal static async Task VerifyTimelineAsync(IVersionProbe probe, CancellationToken token)
    {
        Assert.Equal(new ServerDocument(1, OriginalText), await probe.ReadServerAsync(token).ConfigureAwait(false));
        await probe.EditAsync(EditedText, token).ConfigureAwait(false);
        Assert.Equal(new ServerDocument(2, EditedText), await probe.ReadServerAsync(token).ConfigureAwait(false));
        Assert.Equal(OriginalText, await probe.ReadDiskAsync(token).ConfigureAwait(false));

        await probe.DiscardAndReopenAsync(token).ConfigureAwait(false);
        Assert.Equal(new ServerDocument(3, OriginalText), await probe.ReadServerAsync(token).ConfigureAwait(false));
        Assert.Equal(OriginalText, await probe.ReadDiskAsync(token).ConfigureAwait(false));

        await probe.EditAsync(EditedText, token).ConfigureAwait(false);
        Assert.Equal(new ServerDocument(4, EditedText), await probe.ReadServerAsync(token).ConfigureAwait(false));
        Assert.Equal(OriginalText, await probe.ReadDiskAsync(token).ConfigureAwait(false));
        await probe.SaveAsync(token).ConfigureAwait(false);
        Assert.Equal(EditedText, await probe.ReadServerSaveAsync(token).ConfigureAwait(false));
        Assert.Equal(EditedText, await probe.ReadDiskAsync(token).ConfigureAwait(false));
    }

    internal sealed record ServerDocument(int Version, string Text);

    internal interface IVersionProbe
    {
        Task<ServerDocument> ReadServerAsync(CancellationToken token);
        Task<string> ReadDiskAsync(CancellationToken token);
        Task EditAsync(string text, CancellationToken token);
        Task DiscardAndReopenAsync(CancellationToken token);
        Task SaveAsync(CancellationToken token);
        Task<string> ReadServerSaveAsync(CancellationToken token);
    }

    private sealed class ReferenceProbe(int syncKind, string? fault) : IVersionProbe
    {
        private int _version = 1;
        private string _disk = OriginalText;
        private string _current = OriginalText;
        private string _saved = OriginalText;

        public Task<ServerDocument> ReadServerAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var text = _version == 2 && HasFault("stale-server-buffer") ? OriginalText : _current;
            return Task.FromResult(new ServerDocument(_version, text));
        }

        public Task<string> ReadDiskAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(_disk);
        }

        public Task EditAsync(string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _current = text;
            _version = syncKind == 0 && HasFault("fallback-reuses-version") ? 1 : _version + 1;
            if (HasFault("dirty-write"))
                _disk = text;
            return Task.CompletedTask;
        }

        public Task DiscardAndReopenAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _current = _disk;
            _version = HasFault("reopen-reuses-version") ? 1 : _version + 1;
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!HasFault("missing-save-write"))
                _disk = _current;
            _saved = HasFault("stale-save-notification") ? OriginalText : _current;
            return Task.CompletedTask;
        }

        public Task<string> ReadServerSaveAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(_saved);
        }

        private bool HasFault(string candidate) => string.Equals(fault, candidate, StringComparison.Ordinal);
    }
}
