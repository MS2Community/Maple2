using System.Numerics;
using System.Runtime.InteropServices;

namespace Maple2.Model.Common;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly record struct Vector3B(sbyte X, sbyte Y, sbyte Z) {
    private const float BLOCK_SIZE = 150f;

    public Vector3B(int X, int Y, int Z) : this((sbyte) X, (sbyte) Y, (sbyte) Z) { }

    public static Vector3B ConvertFromInt(int value) {
        // Ensure the input is within the 24-bit range
        if (value is < 0 or > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0 and 0xFFFFFF");

        // Extract each byte and interpret as signed 8-bit values
        sbyte z = unchecked((sbyte) ((value >> 16) & 0xFF));
        sbyte y = unchecked((sbyte) ((value >> 8) & 0xFF));
        sbyte x = unchecked((sbyte) (value & 0xFF));
        return new Vector3B(x, y, z);
    }

    public static implicit operator Vector3B(Vector3 vector) {
        return new Vector3B(
            (sbyte) MathF.Round(vector.X / BLOCK_SIZE),
            (sbyte) MathF.Round(vector.Y / BLOCK_SIZE),
            (sbyte) MathF.Round(vector.Z / BLOCK_SIZE)
        );
    }

    public static implicit operator Vector3(Vector3B vector) {
        return new Vector3(
            vector.X * BLOCK_SIZE,
            vector.Y * BLOCK_SIZE,
            vector.Z * BLOCK_SIZE
        );
    }

    public static Vector3B operator +(in Vector3B a, in Vector3B b) =>
        new((sbyte) (a.X + b.X), (sbyte) (a.Y + b.Y), (sbyte) (a.Z + b.Z));

    public static Vector3 operator *(in Vector3B a, float multiplier) =>
        new(a.X * multiplier, a.Y * multiplier, a.Z * multiplier);

    public int ConvertToInt() {
        // Convert x, y, and z to a single 24-bit integer
        return ((Z & 0xFF) << 16) | ((Y & 0xFF) << 8) | (X & 0xFF);
    }

    // We override GetHashCode because only 3/4 bytes are relevant.
    public override int GetHashCode() {
        return X << 16 | Y << 8 | (byte) Z;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 6)]
public readonly record struct Vector3S(short X, short Y, short Z) {
    // This offset is used to correct rounding errors due to floating point arithmetic.
    private const float OFFSET = 0.001f;

    public Vector3 Vector3 => new Vector3(X, Y, Z);

    public static implicit operator Vector3S(Vector3 vector) {
        return new Vector3S(
            (short) Math.Clamp(MathF.Round(vector.X), short.MinValue, short.MaxValue),
            (short) Math.Clamp(MathF.Round(vector.Y), short.MinValue, short.MaxValue),
            (short) Math.Clamp(MathF.Round(vector.Z), short.MinValue, short.MaxValue)
        );
    }

    public static implicit operator Vector3(Vector3S vector) {
        return new Vector3(
            vector.X + (vector.X >= 0 ? OFFSET : -OFFSET),
            vector.Y + (vector.Y >= 0 ? OFFSET : -OFFSET),
            vector.Z + OFFSET
        );
    }

    public static Vector3S operator +(in Vector3S a, in Vector3S b) =>
        new Vector3S((short) (a.X + b.X), (short) (a.Y + b.Y), (short) (a.Z + b.Z));
    public static Vector3S operator -(in Vector3S a, in Vector3S b) =>
        new Vector3S((short) (a.X - b.X), (short) (a.Y - b.Y), (short) (a.Z - b.Z));
    public static Vector3S operator *(in Vector3S a, in Vector3S b) =>
        new Vector3S((short) (a.X * b.X), (short) (a.Y * b.Y), (short) (a.Z * b.Z));
    public static Vector3S operator /(in Vector3S a, in Vector3S b) =>
        new Vector3S((short) (a.X / b.X), (short) (a.Y / b.Y), (short) (a.Z / b.Z));

    public override string ToString() => $"<{X}, {Y}, {Z}>";
}
