using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Maple2.PacketLib.Tools;

namespace Maple2.Tools.Extensions;

public static class PacketExtensions {
    // Zlib adds file header, so avoid compression if data is small
    private const int MIN_DEFLATE_SIZE = 10;
    private const int INT_SIZE = 4;

    // Write data deflated using Zlib
    public static void WriteDeflated(this ByteWriter writer, byte[] data, int offset, int length) {
        if (length < MIN_DEFLATE_SIZE) {
            writer.WriteBool(false);
            writer.WriteInt(length);
            writer.WriteBytes(data, offset, length);
            return;
        }

        writer.WriteBool(true);
        // We will write the deflated buffer size here later.
        int startIndex = writer.Length;
        writer.WriteInt(); // Reserve 4 bytes for later

        // Length of inflated buffer for client to use.
        writer.WriteIntBigEndian(length);

        var deflater = new Deflater(Deflater.BEST_SPEED);
        deflater.SetInput(data, offset, length);
        deflater.Finish();

        while (true) {
            int count = deflater.Deflate(writer.Buffer, writer.Length, writer.Remaining);
            if (count <= 0) {
                break;
            }

            writer.Seek(writer.Length + count);
            writer.ResizeBuffer(writer.Length * 2);
        }

        // We need to seek backwards to write the deflated size since we can't know them beforehand.
        int endIndex = writer.Length;
        writer.Seek(startIndex);
        writer.WriteInt(endIndex - startIndex - INT_SIZE);
        writer.Seek(endIndex);
    }

    public static T WriteHexString<T>(this T writer, string value) where T : IByteWriter {
        byte[] bytes = value.ToByteArray();
        writer.WriteBytes(bytes);
        return writer;
    }

    public static void WriteArray<T>(this IByteWriter writer, in T[] values) where T : struct {
        foreach (T value in values) {
            writer.Write<T>(value);
        }
    }

    public static T[] ReadArray<T>(this IByteReader packet, int size) where T : struct {
        var result = new T[size];
        for (int i = 0; i > size; i++) {
            result[i] = packet.Read<T>();
        }

        return result;
    }

    // Allows writing packet generically from class
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteClass<T>(this IByteWriter writer, T type) where T : IByteSerializable {
        type.WriteTo(writer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadClass<T>(this IByteReader packet) where T : IByteDeserializable {
        var type = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        type.ReadFrom(packet);
        return type;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadClassWithNew<T>(this IByteReader packet) where T : IByteDeserializable, new() {
        var type = new T();
        type.ReadFrom(packet);
        return type;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteCollection<T>(this IByteWriter writer, ICollection<T>? collection)
        where T : struct {
        if (collection == null) {
            writer.WriteInt(); // 0 items
            return;
        }

        writer.WriteInt(collection.Count);
        foreach (T type in collection) {
            writer.Write<T>(type);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ICollection<T> ReadCollection<T>(this IByteReader packet) where T : struct {
        if (packet.Available < INT_SIZE) {
            return [];
        }

        int count = packet.ReadInt();
        List<T> result = new List<T>(count);
        for (int i = 0; i < count; i++) {
            result.Add(packet.Read<T>());
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TCollection ReadCollection<TCollection, T>(this IByteReader packet)
        where TCollection : ICollection<T>, new() where T : struct {
        var result = new TCollection();
        if (packet.Available < INT_SIZE) {
            return result;
        }

        int count = packet.ReadInt();
        for (int i = 0; i < count; i++) {
            result.Add(packet.Read<T>());
        }

        return result;
    }

    private static void WriteIntBigEndian(this IByteWriter writer, int value) {
        writer.WriteByte((byte) (value >> 24));
        writer.WriteByte((byte) (value >> 16));
        writer.WriteByte((byte) (value >> 8));
        writer.WriteByte((byte) (value));
    }

    public static T WriteUnicodeStringWithLength<T>(this T writer, string value = "") where T : IByteWriter {
        if (string.IsNullOrEmpty(value)) {
            writer.WriteInt(0);
            return writer;
        }

        byte[] stringBytes = Encoding.Unicode.GetBytes(value);
        writer.WriteInt(stringBytes.Length);
        writer.WriteBytes(stringBytes);
        return writer;
    }

    // Read a Unicode string with a 4-byte byte length prefix
    public static string ReadUnicodeStringWithLength(this IByteReader reader) {
        // Read the 4-byte length prefix (byte count)
        int byteLength = reader.ReadInt();

        if (byteLength <= 0) {
            return string.Empty;
        }

        // Read the specified number of bytes
        byte[] stringBytes = reader.ReadBytes(byteLength);

        // Convert bytes to string using UTF-16LE encoding
        return Encoding.Unicode.GetString(stringBytes);
    }
}
