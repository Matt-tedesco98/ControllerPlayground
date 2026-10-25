using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ControllerPlayground.Input;

// Four words are storage only: this is an opaque 32-byte GameInput ID, not a GUID.
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 32)]
public readonly struct ControllerDeviceId : IEquatable<ControllerDeviceId> {
    private readonly ulong _a, _b, _c, _d;

    private ControllerDeviceId(ReadOnlySpan<byte> bytes) {
        _a = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        _b = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        _c = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]);
        _d = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]);
    }

    public static ControllerDeviceId FromBytes(ReadOnlySpan<byte> bytes) {
        if (bytes.Length != 32) throw new ArgumentException("A controller ID must contain exactly 32 bytes.", nameof(bytes));
        return new(bytes);
    }

    public byte[] ToByteArray() {
        byte[] bytes = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, _a);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), _b);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), _c);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), _d);
        return bytes;
    }

    public bool Equals(ControllerDeviceId other) => _a == other._a && _b == other._b && _c == other._c && _d == other._d;
    public override bool Equals(object? obj) => obj is ControllerDeviceId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);
    public override string ToString() => Convert.ToHexString(ToByteArray());
    public static bool operator ==(ControllerDeviceId left, ControllerDeviceId right) => left.Equals(right);
    public static bool operator !=(ControllerDeviceId left, ControllerDeviceId right) => !left.Equals(right);
}
