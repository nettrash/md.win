using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Md.Core.Export;

/// <summary>
/// A minimal zip reader: enough to pull the entries out of a <c>.textpack</c>
/// (macOS <c>ZipReader</c>). It walks the central directory, which is authoritative for
/// sizes and local-header offsets even when an entry was written with a streaming data
/// descriptor (sizes zeroed in the local header) — the reason a front-to-back local-header
/// walk is unreliable. STORED (0) and DEFLATE (8) only; anything else, or a truncated
/// archive, yields null rather than a guess. No I/O: bytes in, entries out.
/// </summary>
public static class ZipReader
{
    /// <summary>
    /// The most a single entry may claim to inflate to. The central directory's
    /// uncompressed size is an attacker-controlled u32, and the buffer is allocated
    /// before decoding — a hundred-byte crafted pack could otherwise force a
    /// multi-gigabyte allocation just by being opened.
    /// </summary>
    public const int MaxEntrySize = 128 * 1024 * 1024;

    /// <summary>
    /// Every file entry (directory entries — names ending in "/" — dropped), or null if
    /// the bytes are not a zip this reader can parse. An entry <paramref name="shouldInflate"/>
    /// rejects is still listed (by name, with empty data) but never allocated or inflated:
    /// import reads only <c>text.md</c>, so a hostile pack full of huge assets costs nothing.
    /// </summary>
    public static IReadOnlyList<ZipEntry>? Entries(byte[] archive, Func<string, bool>? shouldInflate = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var bytes = archive.AsSpan();
        if (!LocateEndRecord(bytes, out var count, out var centralOffset)) return null;

        // Offsets and sizes are u32 on the wire; keep them in long so a hostile value
        // cannot wrap before the bounds checks below compare it against the buffer.
        long offset = centralOffset;
        var results = new List<ZipEntry>(count);
        for (var i = 0; i < count; i++)
        {
            if (offset < 0 || offset + 46 > bytes.Length || U32(bytes, offset) != 0x02014B50) return null;
            int method = U16(bytes, offset + 10);
            long compressedSize = U32(bytes, offset + 20);
            long uncompressedSize = U32(bytes, offset + 24);
            int nameLength = U16(bytes, offset + 28);
            int extraLength = U16(bytes, offset + 30);
            int commentLength = U16(bytes, offset + 32);
            long localOffset = U32(bytes, offset + 42);
            var nameStart = offset + 46;
            if (nameStart + nameLength > bytes.Length) return null;
            // Lossy UTF-8 like Swift's String(decoding:as:UTF8.self); flag bit 11 is
            // ignored and CP437 legacy names are not supported on any port.
            var name = Encoding.UTF8.GetString(bytes.Slice((int)nameStart, nameLength));
            offset = nameStart + nameLength + extraLength + commentLength;

            // A directory carries no payload — and has no local header worth chasing.
            if (name.EndsWith("/", StringComparison.Ordinal)) continue;

            if (shouldInflate is not null && !shouldInflate(name))
            {
                results.Add(new ZipEntry(name, Array.Empty<byte>()));
                continue;
            }

            // The local header repeats its own name/extra lengths, which may differ from
            // the central copy, so the data offset is computed from the local ones.
            if (localOffset < 0 || localOffset + 30 > bytes.Length || U32(bytes, localOffset) != 0x04034B50) return null;
            int localNameLength = U16(bytes, localOffset + 26);
            int localExtraLength = U16(bytes, localOffset + 28);
            var dataStart = localOffset + 30 + localNameLength + localExtraLength;
            if (dataStart + compressedSize > bytes.Length) return null;
            var payload = bytes.Slice((int)dataStart, (int)compressedSize);

            byte[] content;
            switch (method)
            {
                case 0:
                    content = payload.ToArray();
                    break;
                case 8:
                    var inflated = Inflate(payload, uncompressedSize);
                    if (inflated is null) return null;
                    content = inflated;
                    break;
                default:
                    return null;
            }
            results.Add(new ZipEntry(name, content));
        }
        return results;
    }

    /// <summary>
    /// The End Of Central Directory record: 22 bytes near the tail, possibly trailed by a
    /// comment of up to 65535 bytes, so it is found by scanning backward for its signature.
    /// </summary>
    private static bool LocateEndRecord(ReadOnlySpan<byte> bytes, out int count, out uint centralOffset)
    {
        count = 0;
        centralOffset = 0;
        if (bytes.Length < 22) return false;
        var lowerBound = Math.Max(0, bytes.Length - 22 - 0xFFFF);
        for (var i = bytes.Length - 22; i >= lowerBound; i--)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x05 && bytes[i + 3] == 0x06)
            {
                count = U16(bytes, i + 10);
                centralOffset = U32(bytes, i + 16);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Inflate a raw DEFLATE stream (RFC 1951 — a zip entry holds no zlib header, so
    /// <see cref="DeflateStream"/>, never <c>ZLibStream</c>) to its known size. Refuses,
    /// without allocating, an entry that claims more than <see cref="MaxEntrySize"/>; fails
    /// if the stream yields anything other than exactly the declared byte count.
    /// </summary>
    private static byte[]? Inflate(ReadOnlySpan<byte> compressed, long expectedSize)
    {
        if (expectedSize == 0) return Array.Empty<byte>();
        if (expectedSize > MaxEntrySize) return null;
        if (compressed.IsEmpty) return null;
        var destination = new byte[(int)expectedSize];
        try
        {
            using var source = new MemoryStream(compressed.ToArray(), writable: false);
            using var inflater = new DeflateStream(source, CompressionMode.Decompress);
            var written = 0;
            while (written < destination.Length)
            {
                var n = inflater.Read(destination, written, destination.Length - written);
                if (n == 0) return null;                     // truncated or corrupt
                written += n;
            }
            // Apple's decoder reports `written != expected` for a short stream; a stream
            // that carries more than it declared is just as corrupt, so one more read
            // must find nothing.
            Span<byte> probe = stackalloc byte[1];
            if (inflater.Read(probe) != 0) return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        return destination;
    }

    private static int U16(ReadOnlySpan<byte> bytes, long index) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice((int)index, 2));

    private static uint U32(ReadOnlySpan<byte> bytes, long index) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice((int)index, 4));
}
