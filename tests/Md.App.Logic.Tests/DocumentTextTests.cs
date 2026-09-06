using System.Text;
using Md.App.Logic.Documents;
using Md.Core.Document;

namespace Md.App.Logic.Tests;

/// <summary>
/// §3.2 and §6.3: the model is LF, the file keeps its own dressing. These pin the round trip a
/// document makes between disk and the TextBox — the one place where "it looks the same" is not
/// enough, because a CRLF file silently rewritten as LF is a diff in every line of somebody's repo.
/// </summary>
public class DocumentTextTests
{
    static readonly Encoding Cp1251 = DocumentFixtures.Cp1251;

    // ---- LineEndings ----

    [Theory]
    [InlineData("a\nb", NewLine.Lf)]
    [InlineData("a\r\nb", NewLine.CrLf)]
    [InlineData("a\rb", NewLine.Lf)]              // a lone CR is a break, but never written back
    [InlineData("no break at all", NewLine.Lf)]
    [InlineData("", NewLine.Lf)]
    [InlineData("a\r", NewLine.Lf)]               // CR at the very end: no LF follows it
    public void TheFirstLineBreakDecidesTheFilesConvention(string text, NewLine expected) =>
        Assert.Equal(expected, LineEndings.Detect(text));

    [Fact]
    public void AMixedFileTakesItsConventionFromTheFirstBreakOnly()
    {
        Assert.Equal(NewLine.CrLf, LineEndings.Detect("a\r\nb\nc"));
        Assert.Equal(NewLine.Lf, LineEndings.Detect("a\nb\r\nc"));
    }

    [Theory]
    [InlineData("a\r\nb\rc\nd", "a\nb\nc\nd")]
    [InlineData("\r\n\r\n", "\n\n")]
    [InlineData("plain", "plain")]
    public void NormalizeFoldsEveryFormToLf(string input, string expected) =>
        Assert.Equal(expected, LineEndings.Normalize(input));

    [Fact]
    public void ApplyIsTheInverseOfNormalizeForBothConventions()
    {
        const string model = "# Title\n\nBody\n";
        Assert.Equal(model, LineEndings.Apply(model, NewLine.Lf));
        Assert.Equal("# Title\r\n\r\nBody\r\n", LineEndings.Apply(model, NewLine.CrLf));
        Assert.Equal(model, LineEndings.Normalize(LineEndings.Apply(model, NewLine.CrLf)));
    }

    // ---- EditorText ----

    [Fact]
    public void TheTextBoxsCarriageReturnsNeverReachTheModel()
    {
        // What a WinUI TextBox reports for text assigned with any of the three forms.
        Assert.Equal("a\nb\nc", EditorText.FromTextBox("a\rb\rc"));
        Assert.Equal("a\nb\nc", EditorText.FromTextBox("a\r\nb\r\nc"));
        Assert.Equal("a\nb\nc", EditorText.FromTextBox("a\nb\nc"));
    }

    [Fact]
    public void AssigningTheModelBackIsTheIdentityAndRoundTrips()
    {
        const string model = "one\ntwo\n";
        Assert.Same(model, EditorText.ToTextBox(model));
        // The control normalises on assignment, so whatever it then reports folds back to the model.
        Assert.Equal(model, EditorText.FromTextBox(model.Replace("\n", "\r")));
    }

    [Theory]
    [InlineData(3, 4, 10, 3, 4)]
    [InlineData(20, 5, 10, 10, 0)]      // caret past the end of a shorter document
    [InlineData(8, 5, 10, 8, 2)]        // selection runs off the end: keep what is left
    [InlineData(-1, -1, 10, 0, 0)]
    public void AnExternalReplaceKeepsTheSelectionClamped(int start, int length, int textLength, int expectedStart, int expectedLength)
    {
        var (s, l) = EditorText.ClampSelection(start, length, textLength);
        Assert.Equal((expectedStart, expectedLength), (s, l));
    }

    // ---- LineOffsets ----

    [Fact]
    public void OffsetOfLineCountsEveryBreakFormOnce()
    {
        const string text = "zero\rone\r\ntwo\nthree";
        Assert.Equal(0, LineOffsets.OffsetOfLine(0, text));
        Assert.Equal(5, LineOffsets.OffsetOfLine(1, text));       // after "zero\r"
        Assert.Equal(10, LineOffsets.OffsetOfLine(2, text));      // after "one\r\n"
        Assert.Equal(14, LineOffsets.OffsetOfLine(3, text));      // after "two\n"
        Assert.Equal("three", text[LineOffsets.OffsetOfLine(3, text)..]);
    }

    [Fact]
    public void OffsetOfLinePastTheEndIsTheStartOfTheLastLine()
    {
        const string text = "a\nb\nc";
        Assert.Equal(4, LineOffsets.OffsetOfLine(2, text));
        Assert.Equal(4, LineOffsets.OffsetOfLine(99, text));
        Assert.Equal(0, LineOffsets.OffsetOfLine(0, ""));
        Assert.Equal(0, LineOffsets.OffsetOfLine(5, ""));
    }

    [Fact]
    public void OffsetOfLineIgnoresTheUnicodeLineSeparatorsBecauseTheParserDoes()
    {
        const string text = "a\u2028b\u2029c\nd";
        Assert.Equal(0, LineOffsets.OffsetOfLine(0, text));
        Assert.Equal(6, LineOffsets.OffsetOfLine(1, text));       // only the \n counted
    }

    [Fact]
    public void OffsetsAreUtf16UnitsSoAnAstralCharacterCountsAsTwo()
    {
        const string text = "\U0001F600\nnext";     // one emoji = two UTF-16 units
        Assert.Equal(3, LineOffsets.OffsetOfLine(1, text));
        Assert.Equal("next", text[LineOffsets.OffsetOfLine(1, text)..]);
    }

    // ---- TextFileDressing ----

    [Fact]
    public void APlainUtf8LfFileRoundTripsByteForByte()
    {
        var bytes = Encoding.UTF8.GetBytes("# Scene\nBody\n");
        var (text, dressing) = TextFileDressing.Undress(bytes)!.Value;
        Assert.Equal("# Scene\nBody\n", text);
        Assert.Equal(new TextFileDressing(TextEncoding.Utf8, false, NewLine.Lf), dressing);
        Assert.Equal(bytes, dressing.Dress(text, out var used));
        Assert.Equal(TextEncoding.Utf8, used);
    }

    [Fact]
    public void ACrLfFileIsEditedAsLfAndSavedAsCrLf()
    {
        var bytes = Encoding.UTF8.GetBytes("one\r\ntwo\r\n");
        var (text, dressing) = TextFileDressing.Undress(bytes)!.Value;
        Assert.Equal("one\ntwo\n", text);
        Assert.Equal(NewLine.CrLf, dressing.NewLine);
        Assert.Equal(bytes, dressing.Dress(text, out _));
        // And a line typed into it comes back out with the file's own break.
        Assert.Equal("one\r\ntwo\r\nthree\r\n", Encoding.UTF8.GetString(dressing.Dress(text + "three\n", out _)));
    }

    [Fact]
    public void ALoneCarriageReturnFileBecomesLf()
    {
        var (text, dressing) = TextFileDressing.Undress(Encoding.UTF8.GetBytes("one\rtwo\r"))!.Value;
        Assert.Equal("one\ntwo\n", text);
        Assert.Equal(NewLine.Lf, dressing.NewLine);
        Assert.Equal("one\ntwo\n", Encoding.UTF8.GetString(dressing.Dress(text, out _)));
    }

    [Fact]
    public void AUtf8BomIsStrippedOnReadAndWrittenBackOnSave()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("hello")).ToArray();
        var (text, dressing) = TextFileDressing.Undress(bytes)!.Value;
        Assert.Equal("hello", text);                       // no stray U+FEFF in the editor
        Assert.True(dressing.HadUtf8Bom);
        Assert.Equal(bytes, dressing.Dress(text, out _));  // the decision this design records
    }

    [Fact]
    public void AFileWithoutABomNeverGainsOne()
    {
        var (text, dressing) = TextFileDressing.Undress(Encoding.UTF8.GetBytes("hello"))!.Value;
        Assert.False(dressing.HadUtf8Bom);
        Assert.Equal(Encoding.UTF8.GetBytes("hello"), dressing.Dress(text, out _));
    }

    [Fact]
    public void ABomedUtf16FileRoundTripsThroughItsOwnEncoding()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Привет\r\n")).ToArray();
        var (text, dressing) = TextFileDressing.Undress(bytes)!.Value;
        Assert.Equal("Привет\n", text);
        Assert.Equal(TextEncoding.Utf16, dressing.Encoding);
        Assert.Equal(NewLine.CrLf, dressing.NewLine);
        Assert.False(dressing.HadUtf8Bom);                  // the UTF-16 BOM is the codec's job
        Assert.Equal(bytes, dressing.Dress(text, out _));
    }

    [Fact]
    public void ALegacyCp1251FileIsSavedBackInCp1251()
    {
        var bytes = Cp1251.GetBytes("Привет, мир!");
        var (text, dressing) = TextFileDressing.Undress(bytes)!.Value;
        Assert.Equal("Привет, мир!", text);
        Assert.Equal(TextEncoding.WindowsCP1251, dressing.Encoding);
        Assert.Equal(Cp1251.GetBytes("Привет, мир! Ещё."), dressing.Dress(text + " Ещё.", out var used));
        Assert.Equal(TextEncoding.WindowsCP1251, used);
    }

    [Fact]
    public void TextThatOutgrowsCp1251UpgradesToUtf8AndSaysSo()
    {
        var (text, dressing) = TextFileDressing.Undress(Cp1251.GetBytes("Привет"))!.Value;
        var grown = text + " 🙂";
        Assert.Equal(Encoding.UTF8.GetBytes(grown), dressing.Dress(grown, out var used));
        Assert.Equal(TextEncoding.Utf8, used);
        // The caller must remember the upgrade, or every later autosave retries the losing encoding.
        Assert.Equal(TextEncoding.Utf8, dressing.WithEncoding(used).Encoding);
    }

    [Fact]
    public void AnUpgradedFileDoesNotAcquireAUtf8Bom()
    {
        var dressing = new TextFileDressing(TextEncoding.WindowsCP1251, HadUtf8Bom: true, NewLine.Lf);
        var bytes = dressing.Dress("🙂", out var used);
        Assert.Equal(TextEncoding.Utf8, used);
        Assert.Equal(Encoding.UTF8.GetBytes("🙂"), bytes);
        Assert.False(dressing.WithEncoding(used).HadUtf8Bom);
    }

    [Fact]
    public void AFileThatIsNothingButABomReadsAsEmptyAndKeepsIt()
    {
        var (text, dressing) = TextFileDressing.Undress([0xEF, 0xBB, 0xBF])!.Value;
        Assert.Equal("", text);
        Assert.True(dressing.HadUtf8Bom);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, dressing.Dress("", out _));
    }

    [Fact]
    public void AnEmptyFileIsAnEmptyUtf8LfDocument()
    {
        var (text, dressing) = TextFileDressing.Undress([])!.Value;
        Assert.Equal("", text);
        Assert.Equal(TextFileDressing.Default, dressing);
        Assert.Empty(dressing.Dress("", out _));
    }
}
