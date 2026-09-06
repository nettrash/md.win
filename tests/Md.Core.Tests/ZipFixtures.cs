using System.IO.Compression;
using System.Text;
using Md.Core.Export;

namespace Md.Core.Tests;

/// <summary>
/// The test-private archive builders the macOS suite keeps beside its TextBundle tests:
/// a STORED archive through the app's own writer, and a hand-built one-entry DEFLATE
/// archive so the reader's inflate path meets a genuine compressed stream (the writer
/// only stores). Shared by ZipTests and TextBundleTests.
/// </summary>
internal static class ZipFixtures
{
    // Windows-1251 is not in .NET's default set; the codec registers the provider too,
    // but a fixture may be built before the codec's type has loaded.
    static ZipFixtures() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    public static byte[] Cp1251(string text) => Encoding.GetEncoding(1251).GetBytes(text);

    public static string Utf8(byte[] bytes) => new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

    /// <summary>A stored (method 0) archive through the very writer the EPUB export uses.</summary>
    public static byte[] Stored(params (string Name, byte[] Data)[] entries) =>
        ZipWriter.Archive(entries.Select(e => new ZipEntry(e.Name, e.Data)));

    /// <summary>
    /// Raw DEFLATE (RFC 1951, no zlib header) — the symmetric encode to the reader's decode,
    /// so a test archive matches a real one. DeflateStream, never ZLibStream.
    /// </summary>
    public static byte[] DeflateRaw(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflater = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflater.Write(data);
        }
        return output.ToArray();
    }

    /// <summary>
    /// A one-entry zip using DEFLATE (method 8), laid out exactly like the Swift test's
    /// <c>deflatedArchive</c>: local header, payload, central record, end record, all
    /// little-endian, DOS date 0x21. <paramref name="claimedSize"/> overrides the
    /// uncompressed size both headers declare (a lying archive); <paramref name="method"/>
    /// overrides the method code (an unsupported one).
    /// </summary>
    public static byte[] Deflated(string name, byte[] payload, uint? claimedSize = null, int method = 8) =>
        DeflatedPrecompressed(name, DeflateRaw(payload), claimedSize ?? (uint)payload.Length, ZipWriter.Crc32(payload), method);

    /// <summary>
    /// The same one-entry method-8 archive around an already-compressed stream, for a
    /// payload the test must never hold in memory (the 128 MiB cap boundary). The reader
    /// does not verify CRCs, so <paramref name="crc"/> may be left at 0.
    /// </summary>
    public static byte[] DeflatedPrecompressed(string name, byte[] compressed, uint claimed, uint crc = 0, int method = 8)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var output = new MemoryStream();

        void U16(int value)
        {
            output.WriteByte((byte)(value & 0xFF));
            output.WriteByte((byte)((value >> 8) & 0xFF));
        }

        void U32(uint value)
        {
            output.WriteByte((byte)(value & 0xFF));
            output.WriteByte((byte)((value >> 8) & 0xFF));
            output.WriteByte((byte)((value >> 16) & 0xFF));
            output.WriteByte((byte)((value >> 24) & 0xFF));
        }

        // Local file header + compressed payload.
        U32(0x04034B50);
        U16(20); U16(0); U16(method);            // version / flags / method
        U16(0); U16(0x21);                       // dos time / date
        U32(crc);
        U32((uint)compressed.Length); U32(claimed);
        U16(nameBytes.Length); U16(0);           // name / extra length
        output.Write(nameBytes);
        output.Write(compressed);

        // Central-directory file header.
        var centralStart = (uint)output.Length;
        U32(0x02014B50);
        U16(20); U16(20); U16(0); U16(method);   // made-by / needed / flags / method
        U16(0); U16(0x21);
        U32(crc);
        U32((uint)compressed.Length); U32(claimed);
        U16(nameBytes.Length); U16(0); U16(0);   // name / extra / comment
        U16(0); U16(0); U32(0);                  // disk / internal / external attrs
        U32(0);                                  // local header offset
        output.Write(nameBytes);
        var centralSize = (uint)output.Length - centralStart;

        // End of central directory.
        U32(0x06054B50);
        U16(0); U16(0); U16(1); U16(1);          // disks / entries
        U32(centralSize); U32(centralStart);
        U16(0);                                  // comment length
        return output.ToArray();
    }

    public static ZipEntry? Named(IReadOnlyList<ZipEntry>? entries, string name) =>
        entries?.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
}
