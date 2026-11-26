using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Proves the rendered editor opens the exact source and position reported by a diagnostic fixture.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-011#ac-test-save-source. Retain valid and rejected one-based locations.</remarks>
public sealed partial class Phase02DiagnosticNavigationContractTests
{
    /// <summary>Checks that each source and location fault changes the observable result.</summary>
    [Theory]
    [InlineData("wrong-source")]
    [InlineData("wrong-line")]
    [InlineData("wrong-column")]
    [InlineData("accepts-zero")]
    public async Task Diagnostic_reference_rejects_wrong_source_or_position(string fault)
    {
        using var fixture = new DiagnosticSource();
        await VerifyNavigationAsync(new ReferenceNavigation(fixture), fixture);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyNavigationAsync(new ReferenceNavigation(fixture, fault), fixture));
    }

    private static async Task VerifyNavigationAsync(INavigationPort editor, DiagnosticSource source)
    {
        var target = new DiagnosticLocation(source.RelativePath, 2, 5);
        var opened = await editor.NavigateAsync(target);
        Assert.Equal("ok", opened.Code);
        Assert.Equal(source.RelativePath, opened.RelativePath);
        Assert.Equal(DiagnosticSource.ExpectedText, opened.Text);
        Assert.Equal((2, 5), (opened.Line, opened.Column));
        Assert.NotEqual(Guid.Empty, opened.View);

        var invalid = await editor.NavigateAsync(target with { Line = 0, Column = 0 });
        Assert.Equal(opened with { Code = "invalid_location" }, invalid);
    }

    private interface INavigationPort
    {
        Task<NavigationResult> NavigateAsync(DiagnosticLocation request);
    }

    private sealed record DiagnosticLocation(string Path, int Line, int Column);
    private sealed record NavigationResult(string Code, Guid View, string RelativePath, string Text, int Line, int Column);

    private sealed class DiagnosticSource : IDisposable
    {
        public const string ExpectedText = "; header\nlda #$01\nsta $d020\n";
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("retro-ide-diagnostic-contract-");

        public DiagnosticSource()
        {
            var directory = _root.CreateSubdirectory("src");
            File.WriteAllText(Path.Combine(directory.FullName, "main.asm"), ExpectedText);
            File.WriteAllText(Path.Combine(directory.FullName, "other.asm"), "; unexpected file\n");
        }

        public string Root => _root.FullName;
        public string RelativePath { get; } = Path.Combine("src", "main.asm");
        public string OtherPath { get; } = Path.Combine("src", "other.asm");

        public void Dispose() => _root.Delete(recursive: true);
    }

    private sealed class ReferenceNavigation(DiagnosticSource source, string? fault = null) : INavigationPort
    {
        private NavigationResult? _current;

        public async Task<NavigationResult> NavigateAsync(DiagnosticLocation request)
        {
            if (request.Line < 1 || request.Column < 1)
                return Assert.IsType<NavigationResult>(_current) with { Code = IsFault("accepts-zero") ? "ok" : "invalid_location" };

            var path = IsFault("wrong-source") ? source.OtherPath : request.Path;
            var text = await File.ReadAllTextAsync(Path.Combine(source.Root, path), TestContext.Current.CancellationToken);
            _current = new NavigationResult("ok", Guid.NewGuid(), path, text,
                IsFault("wrong-line") ? 1 : request.Line, IsFault("wrong-column") ? 1 : request.Column);
            return _current;
        }

        private bool IsFault(string candidate) => string.Equals(fault, candidate, StringComparison.Ordinal);
    }
}
