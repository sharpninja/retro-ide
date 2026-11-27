using Porta.Pty;
using RetroIde.Application;
using RetroIde.Application.Terminals;

namespace RetroIde.Workbench;

// The renderer can be torn down or moved without stopping the repository session.
internal sealed class TerminalAttachment(RepositoryTerminal terminal) : IPtyConnection
{
    private readonly TerminalStream _stream = new(terminal);
    public Stream ReaderStream => _stream;
    public Stream WriterStream => _stream;
    public bool SupportsCancellableRead => true;
    public int Pid => terminal.ProcessId;
    public bool HasExited => terminal.ExitResult is not null;
    public int ExitCode => terminal.ExitResult?.Code ?? 0;
    public int ExitSignal => terminal.ExitResult?.Signal ?? 0;
    public event EventHandler<PtyExitedEventArgs>? ProcessExited
    {
        add => terminal.ProcessExited += value;
        remove => terminal.ProcessExited -= value;
    }

    public bool WaitForExit(int milliseconds) => terminal.WaitForExitAsync(CancellationToken.None).Wait(milliseconds);

    public void Kill() => throw new NotSupportedException("The workbench owns terminal process lifetime.");

    public void Resize(int columns, int rows) => _ = ResizeAsync(columns, rows, _stream.Detached);

    public void Dispose() => _stream.Dispose();

    private async Task ResizeAsync(int columns, int rows, CancellationToken detached)
    {
        if (!terminal.IsRunning || columns is < 1 or > 1000 || rows is < 1 or > 1000)
            return;
        try { await terminal.ResizeAsync(columns, rows, detached).ConfigureAwait(false); }
        catch (OperationCanceledException) when (detached.IsCancellationRequested || !terminal.IsRunning) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            terminal.ReportFailure(error, "Resize repository terminal presentation");
        }
    }

    private sealed class TerminalStream : Stream
    {
        private readonly RepositoryTerminal _terminal;
        private readonly CancellationTokenSource _detached = new();
        private long _offset;
        private bool _disposed;

        internal TerminalStream(RepositoryTerminal terminal)
        {
            _terminal = terminal;
            Detached = _detached.Token;
        }

        internal CancellationToken Detached { get; }
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
                return 0;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Detached);
            var read = await _terminal.WaitForOutputAsync(_offset, buffer.Length, linked.Token).ConfigureAwait(false);
            read.Bytes.AsSpan().CopyTo(buffer.Span);
            _offset = read.NextOffset;
            return read.Bytes.Length;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Detached);
            await _terminal.WriteAsync(buffer, linked.Token).ConfigureAwait(false);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _detached.Cancel();
                _detached.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
