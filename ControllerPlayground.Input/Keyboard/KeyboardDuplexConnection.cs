using System.Threading.Channels;

namespace ControllerPlayground.Input.Keyboard;

// Transport only: one continuous reader, one serialized writer, bounded queues.
// Lifecycle coordinators serialize model changes; no input actions execute here.
public sealed class KeyboardDuplexConnection : IAsyncDisposable {
    private sealed record Write(KeyboardEnvelope Message, TaskCompletionSource Completion);
    private readonly KeyboardFramedStream _wire;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<KeyboardEnvelope> _incoming = Channel.CreateBounded<KeyboardEnvelope>(16);
    private readonly Channel<Write> _outgoing = Channel.CreateBounded<Write>(16);
    private readonly Task _reader, _writer;
    public ChannelReader<KeyboardEnvelope> Incoming => _incoming.Reader;
    public CancellationToken Disconnected => _stop.Token;
    public KeyboardDuplexConnection(Stream stream) {
        _wire = new(stream); _reader = Task.Run(ReadLoop); _writer = Task.Run(WriteLoop);
    }
    private async Task ReadLoop() {
        Exception? failure = null;
        try {
            while (await _wire.ReadAsync(_stop.Token).ConfigureAwait(false) is { } frame)
                await _incoming.Writer.WriteAsync(frame, _stop.Token).ConfigureAwait(false);
        } catch (Exception error) { failure = error; }
        finally { _incoming.Writer.TryComplete(failure); _stop.Cancel(); }
    }
    private async Task WriteLoop() {
        try {
            await foreach (var item in _outgoing.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false)) {
                try {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(3));
                    await _wire.WriteAsync(item.Message, deadline.Token).ConfigureAwait(false); item.Completion.TrySetResult();
                } catch (Exception error) { item.Completion.TrySetException(error); throw; }
            }
        } catch (Exception error) {
            _incoming.Writer.TryComplete(error); _stop.Cancel();
            while (_outgoing.Reader.TryRead(out var pending)) pending.Completion.TrySetException(error);
        }
    }
    public async Task SendAsync(KeyboardEnvelope message, CancellationToken token = default) {
        KeyboardProtocolValidation.Validate(message);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        await _outgoing.Writer.WriteAsync(new(message, completion), linked.Token).ConfigureAwait(false);
        await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync() {
        _stop.Cancel(); _outgoing.Writer.TryComplete();
        await _wire.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(_reader, _writer).ConfigureAwait(false);
    }
}
