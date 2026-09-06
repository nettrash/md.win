using System.Buffers.Binary;
using System.Text;

namespace Md.Core.Export;

/// <summary>
/// One archive member, shared by <see cref="ZipWriter"/> and <see cref="ZipReader"/>.
/// Equality is structural over the bytes (a plain record would compare the array by
/// reference, and the tests assert on payloads).
/// </summary>
public sealed record ZipEntry(string Name, byte[] Data)
{
    public bool Equals(ZipEntry? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        hash.AddBytes(Data);
        return hash.ToHashCode();
    }

    public override string ToString() => $"ZipEntry({Name}, {Data.Length} bytes)";
}

/// <summary>
/// The hand-rolled, STORED-only zip container the EPUB and TextPack exports pack into
/// (macOS <c>EPUBZipWriter</c>). Not <c>System.IO.Compression.ZipArchive</c>: that writes
/// the real modification time and DEFLATE, both byte differences from what the shipping
/// apps produce for no gain a reader can see. Every entry carries the fixed DOS date
/// 1980-01-01 (0x0021) and time 0, so an archive is a pure function of its payloads.
/// No zip64, no data descriptors, no comment, flag bit 11 (UTF-8 names) deliberately clear.
/// </summary>
public sealed class ZipWriter
{
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint EndRecordSignature = 0x06054B50;
    private const ushort DosDate = 0x0021;   // (1980-1980)<<9 | 1<<5 | 1
    private const ushort DosTime = 0;

    private readonly MemoryStream body = new();
    private readonly MemoryStream directory = new();
    private ushort count;

    /// <summary>Append one file. Order is preserved — an EPUB adds "mimetype" first.</summary>
    public void Add(string name, ReadOnlySpan<byte> contents)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var nameLength = checked((ushort)nameBytes.Length);
        var crc = Crc32(contents);
        var size = checked((uint)contents.Length);
        var offset = checked((uint)body.Length);

        // Local file header (30 bytes), name, payload.
        WriteU32(body, LocalHeaderSignature);
        WriteU16(body, 20);            // version needed
        WriteU16(body, 0);             // flags
        WriteU16(body, 0);             // method: stored
        WriteU16(body, DosTime);
        WriteU16(body, DosDate);
        WriteU32(body, crc);
        WriteU32(body, size);          // compressed == raw
        WriteU32(body, size);
        WriteU16(body, nameLength);
        WriteU16(body, 0);             // extra length
        body.Write(nameBytes);
        body.Write(contents);

        // Matching central-directory record (46 bytes), name.
        WriteU32(directory, CentralHeaderSignature);
        WriteU16(directory, 20);       // version made by
        WriteU16(directory, 20);       // version needed
        WriteU16(directory, 0);        // flags
        WriteU16(directory, 0);        // method
        WriteU16(directory, DosTime);
        WriteU16(directory, DosDate);
        WriteU32(directory, crc);
        WriteU32(directory, size);
        WriteU32(directory, size);
        WriteU16(directory, nameLength);
        WriteU16(directory, 0);        // extra
        WriteU16(directory, 0);        // comment
        WriteU16(directory, 0);        // disk number
        WriteU16(directory, 0);        // internal attributes
        WriteU32(directory, 0);        // external attributes
        WriteU32(directory, offset);
        directory.Write(nameBytes);
        count = checked((ushort)(count + 1));
    }

    /// <summary>The finished archive: entries, central directory, end record.</summary>
    public byte[] Finish()
    {
        var directoryLength = checked((uint)directory.Length);
        var directoryOffset = checked((uint)body.Length);
        var output = new MemoryStream((int)(body.Length + directory.Length + 22));
        body.WriteTo(output);
        directory.WriteTo(output);
        WriteU32(output, EndRecordSignature);
        WriteU16(output, 0);           // this disk
        WriteU16(output, 0);           // directory's disk
        WriteU16(output, count);
        WriteU16(output, count);
        WriteU32(output, directoryLength);
        WriteU32(output, directoryOffset);
        WriteU16(output, 0);           // comment length
        return output.ToArray();
    }

    /// <summary>Pack ordered entries in one call; the order is the archive order.</summary>
    public static byte[] Archive(IEnumerable<ZipEntry> entries)
    {
        var writer = new ZipWriter();
        foreach (var entry in entries) writer.Add(entry.Name, entry.Data);
        return writer.Finish();
    }

    /// <summary>
    /// Table-driven CRC-32 (the ZIP / PNG polynomial, reflected): init and final xor
    /// 0xFFFFFFFF. Pinned: "123456789" → 0xCBF43926, empty → 0.
    /// </summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = Table[(int)((crc ^ b) & 0xFF)] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) == 1 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
