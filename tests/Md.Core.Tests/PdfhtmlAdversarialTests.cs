using System.Text;
using Md.Core.Document;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.Core.Tests;

// Adversarial pins for the pdfhtml module, written by the refuter.
//
// EVERY expectation below was measured on a sibling port, never read off the C#:
//
//   * SWIFT  — `swiftc -O probe.swift` against the real `NSRegularExpression` /
//     `replacingOccurrences` / `range(of:)` / `String(contentsOf:encoding:)` that
//     md.macOS/md/DocumentExport.swift uses. Each expectation carries its printed line.
//   * TS     — `vitest run` inside /Users/nettrash/Develop/nettrash.me/md.vscode against
//     `src/export/html.ts` and `src/export/pdf.ts`.
//
// Two of these were found by mutation testing: the whole suite stayed green with the
// `src` run's terminator class narrowed from `[^;}]` to `[^}]`, and with the face-name
// class widened from `[A-Za-z0-9_-]` to .NET's Unicode `[\w-]`. Both mutants are byte
// divergences from all three shipping ports; both are killed here.
public class PdfhtmlAdversarialTests
{
    private const char TurkishI = (char)0x0130;    // LATIN CAPITAL LETTER I WITH DOT ABOVE
    private const char FullwidthA = (char)0xFF21;  // FULLWIDTH LATIN CAPITAL LETTER A

    private static int Count(string haystack, string needle)
    {
        var total = 0;
        var from = 0;
        while (true)
        {
            var at = haystack.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0) return total;
            total++;
            from = at + needle.Length;
        }
    }

    private static Func<string, byte[]?> Assets(params (string Path, string Content)[] files) =>
        name =>
        {
            foreach (var (path, content) in files)
            {
                if (string.Equals(path, name, StringComparison.Ordinal)) return Encoding.UTF8.GetBytes(content);
            }
            return null;
        };

    /// <summary>
    /// The <c>src</c> run ends at a <c>;</c> as well as at the rule's <c>}</c>. The vendored
    /// <c>katex.min.css</c> happens to put <c>src</c> last in every rule, so its twenty faces
    /// never exercise the semicolon — and the whole suite stayed green with the terminator
    /// narrowed to <c>[^}]</c>, which would have swallowed the declarations after it.
    /// </summary>
    /// <remarks>
    /// SWIFT probe (`NSRegularExpression`, pattern verbatim from `embeddedKatexCSS`), printed:
    /// <c>@font-face{font-family:KaTeX_Main;src:url(data:font/woff2;base64,TVo=) format("woff2");font-weight:700;font-style:normal}.katex{font-size:1.21em}</c>
    /// A `font-weight` eaten here is not a cosmetic loss: the bold face would be selected for
    /// every weight, so a document's bold math would come out of the export in the wrong stroke.
    /// </remarks>
    [Fact]
    public void TheSrcRunStopsAtASemicolonAndNotOnlyAtTheRulesClosingBrace()
    {
        const string sheet = "@font-face{font-family:KaTeX_Main;"
            + "src:url(fonts/KaTeX_Main-Regular.woff2) format(\"woff2\"),"
            + "url(fonts/KaTeX_Main-Regular.ttf) format(\"truetype\");"
            + "font-weight:700;font-style:normal}.katex{font-size:1.21em}";

        var css = HtmlExport.EmbeddedKatexCss(Assets(
            (HtmlExport.KatexCssAsset, sheet),
            ("rich/fonts/KaTeX_Main-Regular.woff2", "MZ")));

        Assert.Equal(
            "@font-face{font-family:KaTeX_Main;"
            + "src:url(data:font/woff2;base64,TVo=) format(\"woff2\");"
            + "font-weight:700;font-style:normal}.katex{font-size:1.21em}",
            css);
    }

    /// <summary>
    /// A face whose name leaves <c>[A-Za-z0-9_-]</c> is not a KaTeX face, and its rule is left
    /// exactly as written — even when a file of that name is sitting in the package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The class is spelled out in all three ports. .NET's <c>\w</c> is Unicode-aware, so
    /// <c>[\w-]</c> would match <c>KaTeX_İ</c> and <c>Ａwide</c> and inline them — a byte
    /// divergence, and a lookup key that a Turkish or a full-width-folding culture disagrees
    /// about. The suite stayed green under exactly that mutation, because the previous test of
    /// this rule supplied no file for the non-ASCII names: "not matched" and "matched, file
    /// missing" produce the same output. Here the files ARE supplied, so only the class decides.
    /// </para>
    /// <para>
    /// SWIFT probe printed, with <c>KaTeX_İ</c> present in the font map:
    /// <c>@font-face{src:url(fonts/KaTeX_İ.woff2) format("woff2")}</c> — untouched.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFaceNameOutsideTheAsciiClassIsLeftAloneEvenWhenItsFileIsRightThere()
    {
        var sheet = "@font-face{src:url(fonts/KaTeX_" + TurkishI + ".woff2) format(\"woff2\")}"
            + "@font-face{src:url(fonts/" + FullwidthA + "wide.woff2) format(\"woff2\")}"
            + "@font-face{src:url(fonts/Ok-1_2.woff2) format(\"woff2\")}";

        // Every face is readable. The ASCII one proves the reader works, so a green run cannot
        // mean "nothing was inlined at all".
        var css = HtmlExport.EmbeddedKatexCss(Assets(
            (HtmlExport.KatexCssAsset, sheet),
            ("rich/fonts/KaTeX_" + TurkishI + ".woff2", "z"),
            ("rich/fonts/" + FullwidthA + "wide.woff2", "z"),
            ("rich/fonts/Ok-1_2.woff2", "y")));

        Assert.Equal(
            "@font-face{src:url(fonts/KaTeX_" + TurkishI + ".woff2) format(\"woff2\")}"
            + "@font-face{src:url(fonts/" + FullwidthA + "wide.woff2) format(\"woff2\")}"
            + "@font-face{src:url(data:font/woff2;base64,eQ==) format(\"woff2\")}",
            css);
    }

    /// <summary>
    /// Both head insertions take <b>every</b> <c>&lt;/head&gt;</c>, which is Swift's
    /// <c>replacingOccurrences</c> — not Kotlin's and TypeScript's <c>replaceFirst</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rendered document has exactly one <c>&lt;/head&gt;</c> (every scrap of author text is
    /// escaped), so the ports agree on every real page and the choice is invisible — until
    /// somebody "simplifies" it in one direction or the other. This records which one md.win
    /// took and why: the Swift is the source of truth for this module's bytes.
    /// </para>
    /// <para>
    /// SWIFT probe on <c>&lt;html&gt;&lt;head&gt;&lt;/head&gt;&lt;head&gt;&lt;/head&gt;…</c>:
    /// <c>two-head insert count: 2</c>.
    /// TS probe, same input through <c>selfContainedHTML</c>: <c>mermaid notice count: 1</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothHeadInsertionsTakeEveryOccurrenceLikeTheSwiftAndNotLikeTheTypeScript()
    {
        const string captured = "<html><head></head><head></head>"
            + "<body><pre class=\"mermaid\"><svg></svg></pre></body></html>";
        var page = HtmlExport.PreparePage(
            captured,
            Assets((HtmlExport.KatexCssAsset, ".katex{font-size:1.21em}")),
            hasMath: true);

        Assert.Equal(2, Count(page, "</head>"));
        Assert.Equal(2, Count(page, HtmlExport.KatexNotice));      // Swift: 2. Kotlin/TS: 1.
        Assert.Equal(2, Count(page, HtmlExport.MermaidNotice));    // Swift: 2. Kotlin/TS: 1.
        Assert.Equal(2, Count(page, "<style>.katex{font-size:1.21em}</style>"));
        // …and the order inside each head is still stylesheet, KaTeX notice, Mermaid notice.
        Assert.Equal(2, Count(page,
            "<style>.katex{font-size:1.21em}</style>\n" + HtmlExport.KatexNotice + "\n"
            + HtmlExport.MermaidNotice + "\n</head>"));
    }

    /// <summary>
    /// A document whose <i>title</i> quotes the body rule has its title rewritten and its
    /// stylesheet left alone. Ugly, shared by every port, and deliberately not "fixed" here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rewrite takes the first <c>padding: 48px 56px;</c> in the file, and the skeleton puts
    /// <c>&lt;title&gt;</c> ahead of <c>&lt;style&gt;</c>. The rule contains no <c>&lt;</c>,
    /// <c>&gt;</c> or <c>&amp;</c>, so a title made of it survives escaping verbatim and gets
    /// there first. A document named after a CSS declaration is absurd; a port that quietly
    /// disagreed with the other three about which occurrence wins is not.
    /// </para>
    /// <para>
    /// SWIFT probe (`range(of:)` + `replacingCharacters`) on
    /// <c>&lt;title&gt;padding: 48px 56px;&lt;/title&gt;&lt;style&gt;body { padding: 48px 56px; }&lt;/style&gt;</c>
    /// printed <c>&lt;title&gt;padding: 34px 39px;&lt;/title&gt;&lt;style&gt;body { padding: 48px 56px; }&lt;/style&gt;</c>.
    /// TS probe on the real A5 document: styled title <c>padding: 34px 39px;</c>, remaining
    /// body-rule count 1, a5-padding count 1.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADocumentTitledLikeTheBodyRuleHasItsTitleRewrittenAndItsSheetSpared()
    {
        var html = MarkdownHtml.Document("hello", PdfExport.BodyPaddingRule, dark: false, export: true);
        Assert.True(html.IndexOf("<title>", StringComparison.Ordinal)
            < html.IndexOf("<style>", StringComparison.Ordinal), "the skeleton puts the title first");

        var styled = PdfExport.StyledForExport(html, PageSize.A5);

        Assert.Contains("<title>padding: 34px 39px;</title>", styled, StringComparison.Ordinal);
        Assert.Equal(1, Count(styled, "padding: 34px 39px;"));
        // The sheet keeps A4's margin: the one occurrence the rewrite was aiming at was spent.
        Assert.Equal(1, Count(styled, PdfExport.BodyPaddingRule));
        Assert.Contains("padding: 48px 56px;", styled[styled.IndexOf("<style>", StringComparison.Ordinal)..],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// "Self-contained" is engine- and font-self-contained, <b>not</b> asset-self-contained: the
    /// author's relative <c>&lt;img&gt;</c> is still relative in the exported file.
    /// </summary>
    /// <remarks>
    /// A known, accepted gap on all four ports (html.ts spells it out: "closing it here alone
    /// would be a parity break, not a bug fix"). It is pinned because it looks exactly like a bug
    /// and is the obvious thing for a future contributor to "fix" on one platform.
    /// TS probe of <c>![cat](photo.png)</c>: <c>&lt;img src="photo.png" alt="cat"&gt;</c>.
    /// </remarks>
    [Fact]
    public void TheAuthorsRelativeImageStaysRelativeInTheExportedFile()
    {
        var document = HtmlExport.ExportDocument("![cat](photo.png)", "T");
        Assert.Contains("src=\"photo.png\"", document, StringComparison.Ordinal);

        // The capture is the live DOM with the scripts and stylesheet links gone; an <img> is
        // neither, so it survives untouched into the file.
        var page = HtmlExport.PreparePage(document["<!DOCTYPE html>\n".Length..], document, Assets());
        Assert.Contains("src=\"photo.png\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", page, StringComparison.Ordinal);
        Assert.DoesNotContain("base64", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// KNOWN DIVERGENCE, unreachable with the shipped asset: a UTF-8 BOM on
    /// <c>katex.min.css</c> reaches the exported page here, where Foundation would have eaten it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SWIFT probe: <c>String(contentsOf:encoding:.utf8)</c> of <c>EF BB BF ".katex{a:b}"</c>
    /// returns a non-nil 11-character string whose first scalar is U+002E — the BOM is stripped.
    /// <c>Encoding.GetString</c> does not strip a preamble, and neither do Kotlin's
    /// <c>ByteArray.toString(Charsets.UTF_8)</c> nor Node's <c>readFileSync(…, 'utf8')</c>, so
    /// md.win sides with two of the three ports and against the one it is porting from.
    /// </para>
    /// <para>
    /// It cannot fire today: the vendored <c>src/Md.App/rich/katex.min.css</c> starts with
    /// <c>@font-fa</c>, and the same file is vendored byte-identically into all four apps. This
    /// test exists so that a re-vendored, BOM-carrying sheet is a red test rather than a silently
    /// dropped first rule in every exported page with math. If it ever fires, strip a leading
    /// U+FEFF here and in md.Android and md.vscode together — not in one port alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABomOnTheStylesheetIsCarriedIntoThePageWhereFoundationWouldHaveEatenIt()
    {
        var vendored = File.ReadAllBytes(VendoredKatexCss());
        Assert.False(vendored.AsSpan(0, 3).SequenceEqual([(byte)0xEF, (byte)0xBB, (byte)0xBF]),
            "the vendored sheet must stay BOM-free; the divergence below is only latent");

        var bommed = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(".katex{font-size:1.21em}")).ToArray();
        var css = HtmlExport.EmbeddedKatexCss(name =>
            string.Equals(name, HtmlExport.KatexCssAsset, StringComparison.Ordinal) ? bommed : null);

        // The leading escape is U+FEFF, spelled out: an invisible literal in a .cs file is a
        // riddle, and this is the whole point of the assertion.
        Assert.Equal("\uFEFF.katex{font-size:1.21em}", css);   // Swift returns ".katex{font-size:1.21em}"
    }

    /// <summary>The vendored KaTeX sheet in the repo, found the way HtmlExportTests finds it.</summary>
    private static string VendoredKatexCss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "md.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Md.App", "rich", "katex.min.css");
    }
}
