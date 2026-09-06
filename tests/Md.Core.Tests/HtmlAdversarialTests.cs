using System.Globalization;
using Md.Core.Markdown;

namespace Md.Core.Tests;

/// <summary>
/// Adversarial pins for the HTML writer, written during review from the ports themselves — never
/// from this implementation's output. Each expectation names where it came from: the Swift oracle
/// (`md.macOS/md/*.swift` compiled with `swiftc` and driven over the case), the TypeScript oracle
/// (`md.vscode/src/render/{html,inline}.ts` bundled with esbuild and driven from node), or the
/// Kotlin source (`md.Android/.../markdown/MarkdownHtml.kt`), all measured on 2026-09-06.
/// </summary>
/// <remarks>
/// <para>
/// Two of these record a place where the four ports do <b>not</b> agree, and say which side this
/// one takes. That is the honest state of the corpus: the goldens were captured from md.vscode,
/// which agrees with the Swift on everything the fixtures contain, and the disagreements below sit
/// outside the fixtures in both directions. Neither is a bug to be quietly "fixed" on one platform:
/// changing either would move md.win off whichever port it currently matches.
/// </para>
/// </remarks>
public class HtmlAdversarialTests
{
    private static string Inline(string text, bool softBreaks = false) => MarkdownHtml.Inline(text, softBreaks);

    private static string Body(string source) => MarkdownHtml.Body(source, "t", false).Html;

    private static string Cp(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static readonly string Acute = Cp(0x0301);        // COMBINING ACUTE ACCENT (Mn, GCB=Extend)
    private static readonly string Zwnj = Cp(0x200C);         // ZERO WIDTH NON-JOINER (Cf, GCB=Extend)
    private static readonly string DottedI = Cp(0x0130);      // LATIN CAPITAL LETTER I WITH DOT ABOVE
    private static readonly string CombiningDot = Cp(0x0307);  // COMBINING DOT ABOVE
    private static readonly string Sigma = Cp(0x03A3);        // GREEK CAPITAL LETTER SIGMA
    private static readonly string SmallSigma = Cp(0x03C3);   // GREEK SMALL LETTER SIGMA
    private static readonly string FinalSigma = Cp(0x03C2);   // GREEK SMALL LETTER FINAL SIGMA
    private static readonly string TokenOpen = Cp(0xE000);
    private static readonly string TokenClose = Cp(0xE001);

    // MARK: - Escaping is ordinal, never grapheme-aware

    /// <summary>
    /// <c>Escape</c> and the soft-break conversion replace CODE UNITS. Swift does not: Foundation's
    /// <c>String.replacingOccurrences(of:with:)</c> matches whole grapheme clusters under canonical
    /// equivalence, so an <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> or <c>\n</c> immediately
    /// followed by a combining mark or a joiner is one cluster, is not equal to the bare character,
    /// and is left ALONE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on the Swift oracle, 2026-09-06: <c>a&lt;{acute}b</c> renders as <c>a&lt;{acute}b</c>
    /// — a raw <c>&lt;</c> in the page — and <c>[a](x"{acute}y)</c> as
    /// <c>&lt;a href="x"{acute}y"&gt;a&lt;/a&gt;</c>, a raw quote that closes the <c>href</c> early
    /// and breaks the attribute. (Not an injection vector: ICU's <c>\s</c>, which ends the URL,
    /// contains every character HTML accepts between attributes, so no second attribute can be
    /// started. It is broken markup, not an escape hatch.)
    /// </para>
    /// <para>
    /// The TypeScript (<c>String.prototype.replace</c>) and the Kotlin
    /// (<c>String.replace(String, String)</c>) both work on code units and escape all of these, and
    /// so does this port. It is deliberately NOT bug-compatible with the Swift here: reproducing
    /// Foundation's cluster matching would mean shipping a raw <c>"</c> into an attribute value on
    /// purpose. No fixture in the corpus contains any of these sequences, so the goldens are silent
    /// — which is exactly why this test exists.
    /// </para>
    /// </remarks>
    [Fact]
    public void EscapeIsOrdinalNeverGraphemeAware()
    {
        // TS oracle: "a&lt;́b" / "a&gt;́b" / "a&amp;́b" / "a&quot;́b".
        Assert.Equal("a&lt;" + Acute + "b", Inline("a<" + Acute + "b"));
        Assert.Equal("a&gt;" + Acute + "b", Inline("a>" + Acute + "b"));
        Assert.Equal("a&amp;" + Acute + "b", Inline("a&" + Acute + "b"));
        Assert.Equal("a&quot;" + Acute + "b", Inline("a\"" + Acute + "b"));
        // A joiner is GCB=Extend too, so it forms a cluster the same way.
        Assert.Equal("a&lt;" + Zwnj + "b", Inline("a<" + Zwnj + "b"));
        // Inside a code span, whose content is escaped by the same routine.
        Assert.Equal("<code>a&lt;" + Acute + "b</code>", Inline("`a<" + Acute + "b`"));
        // The attribute case, where it actually matters.
        Assert.Equal("<a href=\"x&quot;" + Acute + "y\">a</a>", Inline("[a](x\"" + Acute + "y)"));
        // And the soft-break pass, which is the same kind of literal replace.
        Assert.Equal("a<br>\n" + Acute + "b", Inline("a\n" + Acute + "b", softBreaks: true));
        // A block-level render carries it too.
        Assert.Equal("<p>para a&lt;" + Acute + "b end</p>", Body("para a<" + Acute + "b end"));
    }

    /// <summary>
    /// Restore replaces the protection token by ordinal comparison, so a format character following
    /// the span cannot swallow it.
    /// </summary>
    /// <remarks>
    /// The known token leak (a code span inside display math) puts a bare U+E000/U+E001 pair into
    /// the working string; if the literal replace were cluster-based, a ZWNJ after the closing
    /// U+E001 would make the token unmatchable. Swift oracle, 2026-09-06: <c>$$x`y`z$$</c> followed
    /// by a ZWNJ collapses to <c>{U+E000}1{U+E001}{ZWNJ}</c> — the whole formula is gone, not merely
    /// leaked. TS oracle and this port both give the span with the documented inner leak. Removing
    /// the ZWNJ makes all three agree, which is what the second assertion pins.
    /// </remarks>
    [Fact]
    public void TokenRestoreIsOrdinalSoAFormatCharacterCannotSwallowIt()
    {
        var leaked = "<span class=\"md-mathd\">x" + TokenOpen + "0" + TokenClose + "z</span>";
        Assert.Equal(leaked + Zwnj, Inline("$$x`y`z$$" + Zwnj));
        // Swift, TypeScript and this port all agree once the joiner is gone.
        Assert.Equal(leaked + " w", Inline("$$x`y`z$$ w"));
    }

    // MARK: - Folding the fence's info word

    /// <summary>
    /// The info word is folded with the FULL Unicode lowercase mapping, not the simple 1:1 one.
    /// </summary>
    /// <remarks>
    /// U+0130 LATIN CAPITAL LETTER I WITH DOT ABOVE lowercases to two code points, <c>i</c> +
    /// U+0307, in <c>SpecialCasing.txt</c>. All three other ports do that — Swift
    /// <c>lowercased()</c>, Kotlin <c>lowercase(Locale.ROOT)</c>, TypeScript <c>toLowerCase()</c>;
    /// all three oracles agree on <c>class="language-i{U+0307}stanbul"</c> (measured 2026-09-06).
    /// .NET's <c>ToLowerInvariant()</c> is the SIMPLE mapping and leaves U+0130 alone, which is why
    /// <c>ScalarText.FullLowercase</c> — the routine the slug already uses — has to be asked here.
    /// Culture is never consulted either way: a Turkish <c>CurrentCulture</c> must not fold
    /// <c>LATEX</c> to something else.
    /// </remarks>
    [Fact]
    public void FenceLanguageIsFoldedWithSwiftsFullUnicodeMapping()
    {
        Assert.Equal("<pre><code class=\"language-i" + CombiningDot + "stanbul\">body</code></pre>",
            Body("```" + DottedI + "stanbul\nbody\n```"));
        Assert.Equal("<pre><code class=\"language-t" + Cp(0x00FC) + "rki" + CombiningDot + "ye\">code</code></pre>",
            Body("```T" + Cp(0x00DC) + "RK" + DottedI + "YE\ncode\n```"));
        // A dotted I cannot change which branch is taken, only the class it lands in.
        Assert.Equal(new EngineNeeds(false, false, false, false, true),
            MarkdownHtml.Body("```" + DottedI + "stanbul\nbody\n```", "t", false).Needs);
        // U+212A KELVIN SIGN folds to a plain "k" in every port.
        Assert.Equal("<pre><code class=\"language-k\">body</code></pre>", Body("```" + Cp(0x212A) + "\nbody\n```"));
    }

    /// <summary>
    /// The fold is scalar by scalar: the Greek Final_Sigma context rule is NOT applied.
    /// </summary>
    /// <remarks>
    /// Swift maps each scalar on its own, so a trailing U+03A3 lowercases to U+03C3 and never to
    /// U+03C2 (Swift oracle, 2026-09-06: <c>class="language-{sigma}{sigma}"</c>). JavaScript's
    /// <c>toLowerCase</c> and Java's <c>toLowerCase(Locale.ROOT)</c> both DO apply the rule, so
    /// md.vscode emits <c>{sigma}{final sigma}</c> here — a genuine three-way split settled in
    /// favour of md.macOS, the source of truth, exactly as <c>ScalarText.FullLowercase</c> records
    /// for the slug. No fixture reaches it.
    /// </remarks>
    [Fact]
    public void FenceLanguageFoldingNeverAppliesTheFinalSigmaRule()
    {
        Assert.Equal("<pre><code class=\"language-" + SmallSigma + SmallSigma + "\">body</code></pre>",
            Body("```" + Sigma + Sigma + "\nbody\n```"));
        Assert.Equal("<pre><code class=\"language-" + SmallSigma + "a" + SmallSigma + "\">body</code></pre>",
            Body("```" + Sigma + "a" + Sigma + "\nbody\n```"));
        Assert.DoesNotContain(FinalSigma, Body("```" + Sigma + Sigma + "\nbody\n```"), StringComparison.Ordinal);
    }

    // MARK: - The engine probes, asked directly

    /// <summary>
    /// The five probe strings, spelled exactly as <c>MarkdownHTML.document</c> spells them, asked of
    /// the public <see cref="MarkdownHtml.Needs(string)"/> rather than through a rendered body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other engine test drives <c>Body</c>, so the probe LITERALS are never pinned: dropping
    /// the closing <c>&gt;</c> from the Mermaid probe leaves the whole suite green, because nothing
    /// this writer emits can tell the two apart. <see cref="MarkdownHtml.Needs(string)"/> is public
    /// and Wave C's HTML export hands it markup that came back out of a WebView, where a processed
    /// container reads <c>&lt;pre class="mermaid" data-processed="true"&gt;</c> — which the Swift
    /// probe rejects and a truncated one would accept.
    /// </para>
    /// <para>
    /// From the Swift, <c>MarkdownHTML.swift</c>: <c>body.contains("&lt;pre class=\"mermaid\"&gt;")</c>,
    /// <c>"&lt;div class=\"plantuml\"&gt;"</c>, <c>"&lt;div class=\"graphviz\""</c> (no closing angle
    /// — <c>data-engine</c> varies), <c>"md-mathi"</c> / <c>"md-mathd"</c>,
    /// <c>"&lt;pre&gt;&lt;code class=\"language-"</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void EngineProbesAreTheExactContainerStringsSwiftSpells()
    {
        var off = EngineNeeds.None;

        // Mermaid and PlantUML carry their closing angle bracket; an attribute defeats them.
        Assert.Equal(off with { Mermaid = true }, MarkdownHtml.Needs("<pre class=\"mermaid\">x</pre>"));
        Assert.Equal(off, MarkdownHtml.Needs("<pre class=\"mermaid\" data-processed=\"true\">x</pre>"));
        Assert.Equal(off with { Plantuml = true }, MarkdownHtml.Needs("<div class=\"plantuml\">x</div>"));
        Assert.Equal(off, MarkdownHtml.Needs("<div class=\"plantuml\" data-md-src=\"eA==\">x</div>"));

        // Graphviz is the one probe that stops before the angle, because `data-engine` varies.
        Assert.Equal(off with { Graphviz = true }, MarkdownHtml.Needs("<div class=\"graphviz\" data-engine=\"circo\">x</div>"));
        Assert.Equal(off with { Graphviz = true }, MarkdownHtml.Needs("<div class=\"graphviz\">x</div>"));

        // Highlight keys off the tag pair as well as the class prefix.
        Assert.Equal(off with { Highlight = true }, MarkdownHtml.Needs("<pre><code class=\"language-swift\">x</code></pre>"));
        Assert.Equal(off, MarkdownHtml.Needs("<pre> <code class=\"language-swift\">x</code></pre>"));
        Assert.Equal(off, MarkdownHtml.Needs("<code class=\"language-swift\">x</code>"));

        // Math is the bare class name, which is why prose naming it pulls KaTeX in.
        Assert.Equal(off with { Math = true }, MarkdownHtml.Needs("md-mathi"));
        Assert.Equal(off with { Math = true }, MarkdownHtml.Needs("md-mathd"));

        // The probes are ordinal and case-sensitive.
        Assert.Equal(off, MarkdownHtml.Needs("<PRE CLASS=\"MERMAID\">x</PRE>"));
        Assert.Equal(off, MarkdownHtml.Needs("MD-MATHI"));
        Assert.Equal(off, MarkdownHtml.Needs(""));
    }

    // MARK: - Culture

    /// <summary>
    /// Nothing in the writer consults <see cref="CultureInfo.CurrentCulture"/>: every fixture
    /// renders to the recorded golden bytes under a comma-decimal, a right-to-left and a
    /// dotted-I culture alike.
    /// </summary>
    /// <remarks>
    /// The list indent (<c>padding-left:1.60em</c>) is a <c>double</c> formatted with <c>"F2"</c>,
    /// which a German culture would write <c>1,60</c> — and the CSS rule would then be dropped by
    /// the browser, silently, on a German Windows. The fence's info word is folded, which a Turkish
    /// culture would spell differently. The expectation is the golden corpus itself, so this is
    /// anchored to md.vscode's recorded bytes rather than to a second call of the same code.
    /// </remarks>
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void EveryFixtureRendersToItsGoldenUnderAnyCurrentCulture(string cultureName)
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

            foreach (var (directory, name) in new[]
                     {
                         ("testdata", "lists"), ("testdata", "test"), ("testdata", "tables"),
                         ("testdata", "code"), ("testdata", "math"), ("examples", "09-Writer Tools"),
                     })
            {
                var source = Fixtures.Read(Path.Combine(directory, name + ".md"));
                var expected = Fixtures.Read(Path.Combine("golden", directory, name + ".html"));
                Assert.Equal(expected, MarkdownHtml.Body(source, name, dark: false).Html + "\n");
            }

            // The indent is the case a culture would break first, and a deep one is not in any fixture.
            Assert.Contains("padding-left:1.60em", Body("- a\n  - b"), StringComparison.Ordinal);
            Assert.Contains("padding-left:4.80em", Body("- a\n      - d"), StringComparison.Ordinal);

            // The three stylesheets are byte-identical under any culture too.
            Assert.Equal(Fixtures.Read(Path.Combine("golden", "stylesheet-light.css")), MarkdownHtml.Css(false, false) + "\n");
            Assert.Equal(Fixtures.Read(Path.Combine("golden", "stylesheet-export.css")), MarkdownHtml.Css(false, true) + "\n");

            // A Turkish culture must not fold the info word with its own I rules: `LATEX` still
            // reaches the math branch (Swift oracle: `<div class="md-mathd">x</div>`) and `DOT`
            // still reaches the dot layout engine.
            Assert.Equal("<div class=\"md-mathd\">x</div>", Body("```LATEX\nx\n```"));
            Assert.Equal("<div class=\"graphviz\" data-engine=\"dot\">digraph {}</div>", Body("```DOT\ndigraph {}\n```"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }
}
