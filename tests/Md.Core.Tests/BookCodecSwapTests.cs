using System.Text;
using Md.Core.Book;

namespace Md.Core.Tests;

/// <summary>
/// The two places the shipped stand-in codec and the real
/// <c>Md.Core.Document.PlainTextCodec</c> disagree, now that the session reads and writes
/// through the real one. Both differences are the codec's Foundation measurements
/// reaching the book window: a UTF-8 BOM is not text, and 0x98 is not Windows-1251.
/// </summary>
/// <remarks>
/// <c>BookFlushGate.Requested</c> is a static event every live session answers, so this
/// class shares the session collection (see BookArticleSessionTests).
/// </remarks>
[Collection("BookArticleSession")]
public class BookCodecSwapTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    private sealed class Scratch : IDisposable
    {
        public string Root { get; }

        public Scratch()
        {
            Root = Path.Combine(Path.GetTempPath(), "md-codec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Write(string name, byte[] data)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllBytes(path, data);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void ABomedUtf8ArticleLosesItsBomOnReadAndOnSave()
    {
        // Foundation's .utf8 decode drops a leading EF BB BF and its encode writes none
        // back (measured). The stand-in kept the U+FEFF in the buffer, so it reached the
        // preview, the word count and the next save.
        using var scratch = new Scratch();
        var article = scratch.Write("01-Bom.md", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8.GetBytes("# Scene\n")).ToArray());
        using var session = new BookArticleSession();
        session.OpenBook(scratch.Root);

        Assert.True(session.Select(article));
        Assert.Equal("# Scene\n", session.Text);
        Assert.Equal(65001, session.Encoding.CodePage);

        session.Edit("# Scene\n\nMore.\n");
        Assert.True(session.FlushNow());
        Assert.Equal(Utf8.GetBytes("# Scene\n\nMore.\n"), File.ReadAllBytes(article));
        session.CloseBook();
    }

    [Fact]
    public void TheOneByteWindows1251LeavesUndefinedOpensAsLatin1()
    {
        // 0x98 is the single undefined byte in CP1251 and Foundation refuses it in both
        // directions; .NET's table maps it to U+0098 anyway, so the codec refuses it by
        // hand. The stand-in did not, and read such a file as CP1251 — which would have
        // saved it back as CP1251 on the Mac's Latin-1 file.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);
        using var scratch = new Scratch();
        var bytes = cp1251.GetBytes("Привет").Concat(new byte[] { 0x98 }).ToArray();
        var article = scratch.Write("01-Undefined.md", bytes);
        using var session = new BookArticleSession();
        session.OpenBook(scratch.Root);

        Assert.True(session.Select(article));
        Assert.Equal(28591, session.Encoding.CodePage);
        Assert.Equal(Encoding.Latin1.GetString(bytes), session.Text);

        // And it round-trips as Latin-1: every byte survives a save untouched.
        session.Edit(session.Text + "!");
        Assert.True(session.FlushNow());
        Assert.Equal(bytes.Concat(new byte[] { (byte)'!' }).ToArray(), File.ReadAllBytes(article));
        Assert.Equal(28591, session.Encoding.CodePage);
        session.CloseBook();
    }
}
