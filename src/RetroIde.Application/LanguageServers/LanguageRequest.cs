namespace RetroIde.Application.LanguageServers;

internal sealed record LanguageRequest(string Method, string Provider, int? Offset = null,
    Func<LanguageRequest.Context, object>? Parameters = null)
{
    internal object Build(string uri, string text, string encoding)
    {
        var context = new Context(uri, text, encoding);
        if (Parameters is not null)
            return Parameters(context);
        return Offset is { } at
            ? new { textDocument = context.Document, position = context.Position(at) }
            : new { textDocument = context.Document };
    }

    internal readonly record struct Context(string Uri, string Text, string Encoding)
    {
        internal object Document => new { uri = Uri };

        internal LspPosition Position(int offset) => LspPosition.AtOffset(Text, offset, Encoding);
    }
}

internal sealed record LanguageRange(LspPosition Start, LspPosition End)
{
    internal bool IsOrdered => Start.Line >= 0 && Start.Character >= 0 && End.Character >= 0 &&
        (End.Line > Start.Line || End.Line == Start.Line && End.Character >= Start.Character);
}
