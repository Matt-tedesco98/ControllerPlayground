using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace ControllerPlayground.Input.Keyboard;

// One reader and one serialized writer, including concurrent callers. No pipe is
// created here. A partial/cancelled IO poisons the framing lifetime: close/reopen,
// never resume parsing an unknown byte boundary. No text is included in errors.
public sealed class KeyboardFramedStream : IAsyncDisposable {
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _reader = new(1), _writer = new(1);
    private int _unusable;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public bool IsUsable => Volatile.Read(ref _unusable) == 0;
    public KeyboardFramedStream(Stream stream, bool leaveOpen = false) {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream)); _leaveOpen = leaveOpen;
    }
    private void CheckUsable() { if (!IsUsable) throw new KeyboardProtocolException("Framing lifetime has ended; reconnect required."); }
    public static byte[] Encode(KeyboardEnvelope message) {
        KeyboardProtocolValidation.Validate(message);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, KeyboardJsonContext.Default.KeyboardEnvelope);
        if (payload.Length == 0 || payload.Length > KeyboardProtocol.MaxFrameBytes) throw new KeyboardProtocolException("Frame exceeds protocol bound.");
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4); return frame;
    }
    public async ValueTask WriteAsync(KeyboardEnvelope message, CancellationToken cancellationToken = default) {
        // Validate before touching the stream, and before acquiring its write lock.
        byte[] frame = Encode(message);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            CheckUsable(); cancellationToken.ThrowIfCancellationRequested();
            try {
                await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            } catch { Interlocked.Exchange(ref _unusable, 1); throw; }
        } finally { _writer.Release(); }
    }
    public async ValueTask<KeyboardEnvelope?> ReadAsync(CancellationToken cancellationToken = default) {
        await _reader.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            CheckUsable(); cancellationToken.ThrowIfCancellationRequested();
            try {
                byte[] prefix = new byte[4];
                int first = await _stream.ReadAsync(prefix.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (first == 0) { Interlocked.Exchange(ref _unusable, 1); return null; }
                await ReadRemainder(prefix.AsMemory(first), cancellationToken).ConfigureAwait(false);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
                if (size == 0 || size > KeyboardProtocol.MaxFrameBytes) throw new KeyboardProtocolException("Invalid frame length.");
                byte[] payload = new byte[(int)size]; // Only allocate after the prefix has been bounded.
                await ReadRemainder(payload, cancellationToken).ConfigureAwait(false);
                try {
                    StrictUtf8.GetCharCount(payload);
                    RejectDuplicateMembers(payload);
                    var message = JsonSerializer.Deserialize(payload, KeyboardJsonContext.Default.KeyboardEnvelope);
                    KeyboardProtocolValidation.Validate(message); return message;
                } catch (Exception error) when (error is JsonException or NotSupportedException or DecoderFallbackException) {
                    throw new KeyboardProtocolException("Malformed protocol payload.");
                }
            } catch { Interlocked.Exchange(ref _unusable, 1); throw; }
        } finally { _reader.Release(); }
    }
    private static void RejectDuplicateMembers(ReadOnlySpan<byte> payload) {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = 24 });
        Stack<HashSet<string>> members = new();
        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.StartObject) members.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) members.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !members.Peek().Add(reader.GetString()!))
                throw new KeyboardProtocolException("Duplicate JSON member.");
        }
    }
    private async ValueTask ReadRemainder(Memory<byte> buffer, CancellationToken cancellationToken) {
        while (buffer.Length > 0) {
            int read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new KeyboardProtocolException("Truncated frame.");
            buffer = buffer[read..];
        }
    }
    public async ValueTask DisposeAsync() {
        Interlocked.Exchange(ref _unusable, 1);
        if (!_leaveOpen) await _stream.DisposeAsync().ConfigureAwait(false);
        // Do not dispose semaphores while cancelled IO is unwinding on another task.
    }
}
