using System.Text;
using System.Text.RegularExpressions;
using Md.Core.Markdown;
using Xunit.Sdk;

namespace Md.Core.Tests;

// The golden corpus: md.vscode's whole-document snapshots of the HTML writer, adopted verbatim as
// this port's parity gate. The three shipping apps assert `contains(...)` on single constructs;
// sixteen Swift-vs-Kotlin divergences went unnoticed until a 3,555-document differential run found
// them, so md.vscode records the rendered BODY of every fixture, pins the `<head>` skeleton once,
// the three stylesheets once and the "which engines does each document need" table once. These
// files are not documentation and not a specification: a tripwire. Every comparison is ordinal
// and byte-for-byte — no newline normalisation, no trimming.
//
// PROVENANCE OF THE TWO WINDOWS GOLDENS. Fixtures/testdata/test.md is the per-repo exception
// (it names the platform and its build command), so golden/testdata/test.html and
// golden/document.html could not be copied from md.vscode: they were regenerated from this
// renderer once every other golden (24 bodies, engine-needs.json, the three stylesheets) was
// byte-exact and this renderer had been fed md.vscode's OWN test.md and had reproduced
// md.vscode's own two goldens byte-for-byte; then diffed against those originals. The complete
// diff, both files:
//
//   --- md.vscode/test/fixtures/golden/testdata/test.html
//   +++ Fixtures/golden/testdata/test.html
//   @@ -71 +71 @@
//   -  &quot;platforms&quot;: [&quot;Visual Studio Code&quot;],
//   +  &quot;platforms&quot;: [&quot;Windows&quot;],
//   @@ -97 +97 @@
//   -<p><img src="https://nettrash.me/favicon.ico" alt="Alt text for an image" title="Optional title"></p>
//   +<p><img src="https://nettrash.me/favicon-192x192.png" alt="Alt text for an image" title="Optional title"></p>
//   @@ -99 +99 @@
//   -<p><a href="https://nettrash.me"><img src="https://nettrash.me/favicon.ico" alt="Alt text"></a></p>
//   +<p><a href="https://nettrash.me"><img src="https://nettrash.me/md-icon.png" alt="Alt text"></a></p>
//   @@ -177 +177,178 @@
//   -<pre><code class="language-bash">npm ci &amp;&amp; npm run compile</code></pre>
//   +<pre><code class="language-powershell">dotnet build src\Md.App\Md.App.csproj `
//   +  -p:Platform=x64</code></pre>
//
//   --- md.vscode/test/fixtures/golden/document.html
//   +++ Fixtures/golden/document.html
//   (the same four hunks, fourteen lines further down: 85, 111, 113 and 191,192)
//
// Nothing else differs; the four hunks are exactly the four lines test.md localises (the
// `platforms` JSON, the two image URLs — the macOS spellings — and the build-command fence).
// engine-needs.json is untouched by the edit: the fence keeps a language class, so
// `testdata/test` stays "math mermaid plantuml highlight".
public class GoldenTests
{
    private static readonly string[] Testdata =
    [
        "blockquotes", "code", "edge-cases", "headings", "images", "inline", "lists",
        "math", "mermaid", "notes", "outline", "page-breaks", "plantuml", "tables",
        "test", "thematic-breaks",
    ];

    private static readonly string[] Examples =
    [
        "01-Welcome", "02-Formatting", "03-Tables", "04-Code",
        "05-Images", "06-Math", "07-Diagrams", "08-Plots", "09-Writer Tools",
    ];

    public static IEnumerable<object[]> All()
    {
        foreach (var name in Testdata) yield return ["testdata", name];
        foreach (var name in Examples) yield return ["examples", name];
    }

    private static string Source(string directory, string name) => Fixtures.Read(Path.Combine(directory, name + ".md"));

    private static string Golden(string relativePath) => Fixtures.Read(Path.Combine("golden", relativePath));

    /// <summary>The placeholder the document golden carries in place of the stylesheet; two U+2026.</summary>
    private static readonly string StylesheetPlaceholder =
        "/* " + (char)0x2026 + " see stylesheet-light.css " + (char)0x2026 + " */";

    // MARK: - The snapshots

    [Theory]
    [MemberData(nameof(All))]
    public void RendersEveryFixtureBodyAsRecorded(string directory, string name)
    {
        // Bodies, not documents, so a stylesheet change shows up once rather than twenty-five
        // times. Neither `title` nor `dark` reaches the body.
        var actual = MarkdownHtml.Body(Source(directory, name), name, dark: false).Html + "\n";
        GoldenDiff.AssertEqual(Golden(Path.Combine(directory, name + ".html")), actual, directory + "/" + name + ".html");
    }

    [Fact]
    public void NeedsTheRecordedEngines()
    {
        // JSON.stringify(table, null, 2) + "\n", written by hand rather than through a serializer
        // whose newline and escaping are version-dependent. Word order is the probe order.
        var builder = new StringBuilder("{\n");
        var first = true;
        foreach (var row in All())
        {
            var directory = (string)row[0];
            var name = (string)row[1];
            if (!first) builder.Append(",\n");
            first = false;
            var needs = MarkdownHtml.Body(Source(directory, name), name, dark: false).Needs;
            builder.Append("  \"").Append(directory).Append('/').Append(name).Append("\": \"").Append(needs.ToString()).Append('"');
        }
        builder.Append("\n}\n");
        GoldenDiff.AssertEqual(Golden("engine-needs.json"), builder.ToString(), "engine-needs.json");
    }

    [Fact]
    public void WrapsADocumentAsRecorded()
    {
        // The only place the skeleton bytes are pinned on a real document: head order, the
        // `</style>` glue, `data-md-dark`, the module script last, no trailing newline. The
        // stylesheet is replaced by a placeholder so that it is pinned once, in its own files.
        var html = MarkdownHtml.Document(Source("testdata", "test"), "test", dark: false);
        var actual = html.Replace(MarkdownHtml.Css(false, false), StylesheetPlaceholder, StringComparison.Ordinal);
        GoldenDiff.AssertEqual(Golden("document.html"), actual, "document.html");
        Assert.EndsWith("</html>", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void StylesAsRecordedInAllThreeVariants()
    {
        // Three reachable sheets, not four: `dark && !export` runs before the palette is computed.
        GoldenDiff.AssertEqual(Golden("stylesheet-light.css"), MarkdownHtml.Css(false, false) + "\n", "stylesheet-light.css");
        GoldenDiff.AssertEqual(Golden("stylesheet-dark.css"), MarkdownHtml.Css(true, false) + "\n", "stylesheet-dark.css");
        GoldenDiff.AssertEqual(Golden("stylesheet-export.css"), MarkdownHtml.Css(false, true) + "\n", "stylesheet-export.css");
        Assert.Equal(MarkdownHtml.Css(false, true), MarkdownHtml.Css(true, true));
    }

    // MARK: - Corpus invariants (the checks the bodies alone cannot make)

    [Theory]
    [MemberData(nameof(All))]
    public void WrapsEveryFixtureInAWholeDocument(string directory, string name)
    {
        // The HTML half of TestDataTests' "every fixture parses and renders": a fixture that
        // renders an empty body would still pass its golden, so the skeleton is asserted too.
        var html = MarkdownHtml.Document(Source(directory, name), name, dark: false);
        Assert.StartsWith("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n", html, StringComparison.Ordinal);
        Assert.Contains("\n<body data-md-dark=\"0\">\n", html, StringComparison.Ordinal);
        Assert.EndsWith("<script type=\"module\" src=\"rich/md-init.js\"></script>\n</body>\n</html>", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void KeepsTheOutlineAndTheEmittedAnchorIdsInStep(string directory, string name)
    {
        // One slug counter serves both, walked over top-level headings in document order, so a
        // contents entry always finds its anchor. A heading nested in a quote appears in neither.
        var source = Source(directory, name);
        var ids = Regex.Matches(MarkdownHtml.Body(source, name, dark: false).Html, "<h[1-6] id=\"([^\"]*)\"")
            .Select(match => match.Groups[1].Value);
        Assert.Equal(MarkdownParser.Outline(source).Select(entry => entry.Slug), ids);
    }

    // MARK: - The fixtures' own promises, HTML side

    [Fact]
    public void NotesFixtureKeepsPrivateNotesOutOfTheHtml()
    {
        // Both spellings vanish — the `note:` ones the panel shows and the plain comments — and
        // the prose around them is untouched.
        var html = MarkdownHtml.Document(Source("testdata", "notes"), "notes", dark: false);
        Assert.Contains("Visible prose before", html, StringComparison.Ordinal);
        Assert.Contains("Visible prose after", html, StringComparison.Ordinal);
        Assert.DoesNotContain("private author note", html, StringComparison.Ordinal);
        Assert.DoesNotContain("plain comment", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ImagesFixtureEmitsImgTags()
    {
        var html = MarkdownHtml.Body(Source("testdata", "images"), "images", dark: false).Html;
        Assert.Contains("<img src=\"https://nettrash.me/favicon.ico\" alt=\"nettrash.me favicon\" title=\"The favicon\">", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://nettrash.me\"><img src=\"https://nettrash.me/favicon.ico\" alt=\"badge\"></a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RichFixturesEmitTheirContainers()
    {
        var math = MarkdownHtml.Document(Source("testdata", "math"), "math", dark: false);
        Assert.Contains("class=\"md-mathi\"", math, StringComparison.Ordinal);
        Assert.Contains("class=\"md-mathd\"", math, StringComparison.Ordinal);
        Assert.Contains("rich/katex.min.js", math, StringComparison.Ordinal);
        // The currency guard survives a real document: the price is prose, not a formula.
        Assert.Contains("$99.99", math, StringComparison.Ordinal);
        Assert.Contains("<pre class=\"mermaid\">", MarkdownHtml.Body(Source("testdata", "mermaid"), "mermaid", dark: false).Html, StringComparison.Ordinal);
        // plantuml.md is the only place the ```puml alias is pinned on a real file.
        var plantuml = Source("testdata", "plantuml");
        Assert.Contains("```puml\n", plantuml, StringComparison.Ordinal);
        Assert.Equal(3, MarkdownHtml.Body(plantuml, "plantuml", dark: false).Html.Split("<div class=\"plantuml\">").Length - 1);
    }

    // MARK: - The corpus's own honesty

    [Fact]
    public void GoldenFilesCarryNoCarriageReturnAndNoBom()
    {
        // A mis-configured clone (core.autocrlf) would rewrite every expected output at once; the
        // failure should say so rather than show 25 diffs on the first character of line two.
        var files = Directory.GetFiles(Fixtures.Path("golden"), "*", SearchOption.AllDirectories);
        Assert.Equal(25 + 1 + 1 + 3 + 1, files.Length);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Path.GetFileName(file) + " has a BOM");
            Assert.DoesNotContain((byte)'\r', bytes);
        }
        // Every body golden and every stylesheet ends with exactly one LF; the document golden
        // ends at `</html>` with none.
        foreach (var row in All())
        {
            var text = Golden(Path.Combine((string)row[0], (string)row[1] + ".html"));
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
            Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal), row[1] + ".html ends with two newlines");
        }
        Assert.EndsWith("</html>", Golden("document.html"), StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentGoldenEmbedsTheTestBodyGolden()
    {
        // The text between `<body data-md-dark="0">\n` and the module script in document.html is
        // byte-identical to test.html minus its trailing newline — the two files were made from
        // the same render and must stay in step when either is regenerated.
        var document = Golden("document.html");
        var open = "<body data-md-dark=\"0\">\n";
        var close = "\n<script type=\"module\" src=\"rich/md-init.js\"></script>";
        var start = document.IndexOf(open, StringComparison.Ordinal) + open.Length;
        var end = document.IndexOf(close, start, StringComparison.Ordinal);
        Assert.True(start > open.Length && end > start);
        var body = Golden(Path.Combine("testdata", "test.html"));
        Assert.Equal(body.Substring(0, body.Length - 1), document.Substring(start, end - start));
    }

    [Fact]
    public void WindowsTestFixtureLocalisesExactlyTheFourPlatformLines()
    {
        // The per-repo exception, pinned so nobody "fixes" the corpus into agreement: the JSON
        // platforms line, the two image URLs and the build-command fence — and the body golden
        // carries their rendered forms.
        var source = Source("testdata", "test");
        Assert.Contains("  \"platforms\": [\"Windows\"],\n", source, StringComparison.Ordinal);
        Assert.Contains("   ```powershell\n   dotnet build src\\Md.App\\Md.App.csproj `\n     -p:Platform=x64\n   ```\n", source, StringComparison.Ordinal);
        Assert.Contains("(https://nettrash.me/favicon-192x192.png \"Optional title\")", source, StringComparison.Ordinal);
        Assert.Contains("[![Alt text](https://nettrash.me/md-icon.png)](https://nettrash.me)", source, StringComparison.Ordinal);

        var golden = Golden(Path.Combine("testdata", "test.html"));
        Assert.Contains("  &quot;platforms&quot;: [&quot;Windows&quot;],", golden, StringComparison.Ordinal);
        Assert.Contains("<pre><code class=\"language-powershell\">dotnet build src\\Md.App\\Md.App.csproj `\n  -p:Platform=x64</code></pre>", golden, StringComparison.Ordinal);
        Assert.DoesNotContain("Visual Studio Code", golden, StringComparison.Ordinal);
        Assert.DoesNotContain("npm ci", golden, StringComparison.Ordinal);
        // The engine table is unaffected by the localisation: the fence still names a language.
        Assert.Equal("math mermaid plantuml highlight", MarkdownHtml.Body(source, "test", false).Needs.ToString());
    }
}

/// <summary>
/// Ordinal golden comparison with a readable failure: a 114 KB body that differs in one byte
/// should say which line, and show the neighbourhood as a unified diff, not two walls of text.
/// </summary>
internal static class GoldenDiff
{
    public static void AssertEqual(string expected, string actual, string name)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal)) return;
        throw new XunitException(name + " differs from the golden (ours " + actual.Length + " chars, golden " + expected.Length + ")\n" + Unified(expected, actual));
    }

    /// <summary>
    /// The first run of differing lines, in unified-diff shape with three lines of context on
    /// each side. Lines are split on LF only, so a stray CR shows up as a visible `\r` in the hunk.
    /// </summary>
    private static string Unified(string expected, string actual)
    {
        var golden = expected.Split('\n');
        var ours = actual.Split('\n');
        var first = 0;
        while (first < golden.Length && first < ours.Length && string.Equals(golden[first], ours[first], StringComparison.Ordinal)) first++;
        // Trim the common tail so the hunk covers only what changed (an insertion shows as such).
        var tailGolden = golden.Length;
        var tailOurs = ours.Length;
        while (tailGolden > first && tailOurs > first && string.Equals(golden[tailGolden - 1], ours[tailOurs - 1], StringComparison.Ordinal))
        {
            tailGolden--;
            tailOurs--;
        }
        var contextStart = Math.Max(0, first - 3);
        var contextEnd = Math.Min(golden.Length, tailGolden + 3);
        var builder = new StringBuilder();
        builder.Append("--- golden\n+++ ours\n");
        builder.Append("@@ -").Append(contextStart + 1).Append(',').Append(contextEnd - contextStart)
            .Append(" +").Append(contextStart + 1).Append(',').Append(contextEnd - contextStart + (tailOurs - tailGolden)).Append(" @@\n");
        for (var i = contextStart; i < first; i++) builder.Append(' ').Append(Show(golden[i])).Append('\n');
        for (var i = first; i < tailGolden; i++) builder.Append('-').Append(Show(golden[i])).Append('\n');
        for (var i = first; i < tailOurs; i++) builder.Append('+').Append(Show(ours[i])).Append('\n');
        for (var i = tailGolden; i < contextEnd; i++) builder.Append(' ').Append(Show(golden[i])).Append('\n');
        if (first < golden.Length && first < ours.Length && tailGolden - first == 1 && tailOurs - first == 1)
        {
            var g = golden[first];
            var o = ours[first];
            var column = 0;
            while (column < g.Length && column < o.Length && g[column] == o[column]) column++;
            builder.Append("first difference on line ").Append(first + 1).Append(" at column ").Append(column + 1).Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>A line with its control and non-ASCII characters made visible, cut to 200 chars.</summary>
    private static string Show(string line)
    {
        var builder = new StringBuilder(Math.Min(line.Length, 220));
        foreach (var c in line)
        {
            if (c == '\r') builder.Append("\\r");
            else if (c == '\t') builder.Append("\\t");
            else if (c < 0x20 || (c >= 0x7F && c <= 0xA0) || (c >= 0xE000 && c <= 0xF8FF)) builder.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            else builder.Append(c);
            if (builder.Length > 200)
            {
                builder.Append(" [...]");
                break;
            }
        }
        return builder.ToString();
    }
}
