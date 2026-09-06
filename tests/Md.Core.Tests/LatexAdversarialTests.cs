using Md.Core.Export;

namespace Md.Core.Tests;

/// <summary>
/// Adversarial parity tests added by review of the <c>.tex</c> writer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every expected value below came from the real Swift.</b> <c>md.macOS/md/LaTeXExport.swift</c>
/// (with <c>MarkdownParser</c>, <c>MarkdownHTML</c>, <c>ScalarText</c> and <c>Plot</c>) was compiled
/// with <c>swiftc</c> into a stdin/stdout oracle and run on these exact inputs; the strings here are
/// its output, never the C# writer's read back. The oracle decodes its input with
/// <c>String(decoding:as:UTF8.self)</c> — <c>String(data:encoding:)</c> silently eats a leading
/// U+FEFF and would have hidden every BOM case.
/// </para>
/// <para>
/// Each test names the surviving mutant that motivated it. Mutation testing of the 121-case suite
/// killed 23 of 26 mutants; these are the three that lived, each proved non-equivalent by running
/// the mutant over a 32,023-document corpus and diffing against that same Swift oracle.
/// </para>
/// </remarks>
public class LatexAdversarialTests
{
    private static string Chr(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static string Join(params string[] lines) => string.Join("\n", lines);

    /// <summary>The body between <c>\begin{document}</c> and <c>\end{document}</c>, trimmed.</summary>
    private static string Body(string source)
    {
        var tex = LaTeXExport.Document(source);
        const string opening = "\\begin{document}\n";
        const string closing = "\n\\end{document}";
        var start = tex.IndexOf(opening, StringComparison.Ordinal);
        var end = tex.IndexOf(closing, StringComparison.Ordinal);
        Assert.True(start >= 0 && end >= 0, "the document is not wrapped in a document environment");
        var from = start + opening.Length;
        return tex.Substring(from, end - from).Trim('\n');
    }

    /// <summary>
    /// Surviving mutant M15: <c>Guarded</c> resuming at <c>match.Index + match.Length</c> instead of
    /// <c>match.Index + 1</c> after a word guard rejects a match. Nothing in the 121 cases moved.
    /// <para>
    /// It is not equivalent, and the reason is that the closing delimiter of a rejected match can be
    /// the <i>opening</i> delimiter of a good one: in <c>a_b _c_</c> the leftmost match is
    /// <c>_b _</c>, rejected because <c>a</c> precedes it, and the <c>_</c> that ended it opens the
    /// real emphasis. ICU restarts its scan one code unit on from the failed attempt, so the Swift
    /// finds <c>_c_</c>; skipping past the whole rejected match loses it and prints <c>\_c\_</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void ARejectedWordGuardResumesOneCodeUnitOnNotPastTheWholeMatch()
    {
        // Swift oracle, verbatim.
        Assert.Equal(@"a\_b \emph{c}", Body("a_b _c_"));
        Assert.Equal(@"9\_b \emph{c}", Body("9_b _c_"));
        Assert.Equal(@"a\$b $c$", Body("a$b $c$"));
        // The same with an astral letter doing the rejecting — the hand-rolled guard decodes the
        // surrogate pair, so U+1D504 MATHEMATICAL FRAKTUR CAPITAL A is a letter here as it is to ICU.
        Assert.Equal(Chr(0x1D504) + @"\_b \emph{c}", Body(Chr(0x1D504) + "_b _c_"));
    }

    /// <summary>
    /// Surviving mutant M26: <c>DollarGuard</c> built with <c>dollarToo: false</c>, dropping the
    /// <c>$</c> from the formula pattern's <c>[\p{L}\p{N}_$]</c> guard class. All 121 cases passed —
    /// the currency case (<c>"$5 and $10"</c>) is decided by the digit in the class, not the dollar.
    /// <para>
    /// The guard earns its <c>$</c> on a run of three or more: <c>$x$$</c> is not a formula in any
    /// port, because the character after the closing delimiter is another dollar. Without it the
    /// writer opens math mode on half a display delimiter, and <c>$a&amp;b$$</c> additionally invents
    /// an <c>aligned</c> around text the author never meant as mathematics.
    /// </para>
    /// </summary>
    [Fact]
    public void ADollarIsAWordCharacterToTheFormulaGuardOnBothSides()
    {
        // Swift oracle, verbatim: none of these is a formula.
        Assert.Equal(@"\$x\$\$", Body("$x$$"));
        Assert.Equal(@"\$a\&b\$\$", Body("$a&b$$"));
        Assert.Equal(@"\$\$x\$", Body("$$x$"));
        Assert.Equal(@"\$a\$\$b\$", Body("$a$$b$"));
        // …while the same spans with an ordinary neighbour still are, so the guard is not simply off.
        Assert.Equal("$x$", Body("$x$"));
        Assert.Equal("$x$ .", Body("$x$ ."));
    }

    /// <summary>
    /// Surviving mutant M19: hoisting the footnote-reference pass above the image and link passes.
    /// All 121 cases passed, because a footnote reference cannot appear in a link <i>label</i> or an
    /// image's <i>alt</i> — <c>[^\]]</c> stops at the <c>]</c> that <c>[^a]</c> must contain.
    /// <para>
    /// It reaches the title form, whose <c>(.*?)</c> crosses a <c>]</c> happily. Run in the Swift's
    /// order the title is consumed with the raw <c>[^a]</c> inside it, the note is never cited and
    /// the orphan trailer prints it; run early, the reference becomes a whole <c>\footnote</c>, is
    /// swallowed by the title, and the author's note is on no page while the counter still moved.
    /// The bracketed label is the second half: <c>[x [^a]](u)</c> is not a link at all in the Swift.
    /// </para>
    /// </summary>
    [Fact]
    public void FootnoteReferencesConvertAfterImagesAndLinksNotBefore()
    {
        // Swift oracle, verbatim: the title is consumed whole, so the note stays uncited and the
        // orphan trailer is what prints it.
        Assert.Equal(
            Join(
                @"\begin{figure}[ht]",
                @"\centering",
                @"\includegraphics[width=\linewidth]{p.png}",
                @"\caption{x}",
                @"\end{figure}",
                @"",
                @"% Footnotes defined but never referenced " + Chr(0x2014) + @" kept so nothing is lost.",
                @"\footnote{the note}"),
            Body("![x](p.png \"see [^a]\")\n\n[^a]: the note\n"));

        // And a label carrying a reference is not a link: the label pattern cannot cross the `]`.
        Assert.Equal(@"[x \footnote{the note}](u)", Body("[x [^a]](u)\n\n[^a]: the note\n"));
        Assert.Equal(@"[x \footnote{the note}](u ""t"")", Body("[x [^a]](u \"t\")\n\n[^a]: the note\n"));
    }

    /// <summary>
    /// The two spelled-out ICU classes in the link and image patterns, measured against the real
    /// engine rather than argued: an <c>NSRegularExpression</c> probe on this Mac reports
    /// <c>\s</c> = <c>[\t\n\v\f\r\u0085\p{Z}]</c> (U+180E, U+200B and U+FEFF are <b>out</b>) and
    /// <c>.</c> as excluding exactly <c>\n \v \f \r U+0085 U+2028 U+2029</c>. Every value below is
    /// the Swift oracle's output for that input.
    /// <para>
    /// This is where .NET's own metacharacters would part company: its <c>.</c> excludes only
    /// <c>\n</c>, so a title carrying U+2028 or NEL would keep matching and the link would silently
    /// survive a line terminator the Swift refuses.
    /// </para>
    /// </summary>
    [Fact]
    public void LinkUrlAndTitleUseIcuWhitespaceAndIcuDotNotDotNetsOwn()
    {
        // URL class `[^)\s]+`: a separator ends the URL (no link, since no title follows), a
        // format character does not (it is part of the URL).
        Assert.Equal("[a](x" + Chr(0x1680) + "y)", Body("[a](x" + Chr(0x1680) + "y)"));   // Zs
        Assert.Equal("[a](x" + Chr(0x00A0) + "y)", Body("[a](x" + Chr(0x00A0) + "y)"));   // Zs
        Assert.Equal("[a](x" + Chr(0x0085) + "y)", Body("[a](x" + Chr(0x0085) + "y)"));   // NEL
        Assert.Equal("[a](x" + Chr(0x000B) + "y)", Body("[a](x" + Chr(0x000B) + "y)"));   // VT
        Assert.Equal(@"\href{x" + Chr(0x200B) + "y}{a}", Body("[a](x" + Chr(0x200B) + "y)"));
        Assert.Equal(@"\href{x" + Chr(0xFEFF) + "y}{a}", Body("[a](x" + Chr(0xFEFF) + "y)"));
        Assert.Equal(@"\href{x" + Chr(0x180E) + "y}{a}", Body("[a](x" + Chr(0x180E) + "y)"));

        // Title class `.`: a line terminator kills the match, a zero-width character does not.
        Assert.Equal(@"[a](u ""one" + Chr(0x0085) + @"two"")", Body("[a](u \"one" + Chr(0x0085) + "two\")"));
        Assert.Equal(@"[a](u ""one" + Chr(0x2028) + @"two"")", Body("[a](u \"one" + Chr(0x2028) + "two\")"));
        Assert.Equal(@"[a](u ""one" + Chr(0x000B) + @"two"")", Body("[a](u \"one" + Chr(0x000B) + "two\")"));
        Assert.Equal(@"\href{u}{a}", Body("[a](u \"one" + Chr(0x200B) + "two\")"));
        Assert.Equal(@"\href{u}{a}", Body("[a](u \"one" + Chr(0xFEFF) + "two\")"));
    }

    /// <summary>
    /// <c>PercentDecoded</c> on the bytes Swift cannot express and .NET can be talked into
    /// mangling: a four-byte sequence must decode as one scalar (not two U+FFFD), a surrogate
    /// encoded as UTF-8 and an overlong NUL must both be refused whole, and a decoded U+0000 is
    /// a file name character like any other rather than a terminator. Swift oracle, verbatim.
    /// </summary>
    [Fact]
    public void PercentDecodingIsWholeScalarsAndNeverRepairsWhatItCannotRead()
    {
        Assert.Equal(
            Join(@"\begin{figure}[ht]", @"\centering",
                 @"\includegraphics[width=\linewidth]{" + Chr(0x1F600) + ".png}",
                 @"\caption{a}", @"\end{figure}"),
            Body("![a](%F0%9F%98%80.png)"));

        // A UTF-8-encoded lone surrogate and an overlong NUL are not valid UTF-8, so the path is
        // handed back with its `%` intact — and a `%` is a file name `\includegraphics` refuses.
        Assert.Equal(
            Join(@"\emph{a}", @"% md: image skipped " + Chr(0x2014) + @" %ED%A0%80.png is not a file name LaTeX can read."),
            Body("![a](%ED%A0%80.png)"));
        Assert.Equal(
            Join(@"\emph{a}", @"% md: image skipped " + Chr(0x2014) + @" a%C0%80b.png is not a file name LaTeX can read."),
            Body("![a](a%C0%80b.png)"));
        Assert.Equal(
            Join(@"\emph{a}", @"% md: image skipped " + Chr(0x2014) + @" trailing%2 is not a file name LaTeX can read."),
            Body("![a](trailing%2)"));

        // `%00` is valid UTF-8 for U+0000 and decodes; the scalar goes into the argument as itself.
        Assert.Equal(
            Join(@"\begin{figure}[ht]", @"\centering",
                 @"\includegraphics[width=\linewidth]{x" + Chr(0x0000) + "y.png}",
                 @"\caption{a}", @"\end{figure}"),
            Body("![a](x%00y.png)"));

        Assert.Equal(Chr(0x1F600) + ".png", LaTeXExport.PercentDecoded("%F0%9F%98%80.png"));
        Assert.Equal("%ED%A0%80.png", LaTeXExport.PercentDecoded("%ED%A0%80.png"));
    }

    /// <summary>
    /// One document exercising most of the writer at once, pinned to the byte against the Swift:
    /// front matter (escaped title, a case-folded <c>AUTHOR</c> key, an invented <c>\date{}</c>),
    /// the Cyrillic <c>fontenc</c> trigger hidden in a Latin-looking author name, package order,
    /// a footnote first cited from a moving argument and then from a table head and a block quote,
    /// an <c>aligned</c> supplied in both a heading and a header cell, the bracket guards on a row
    /// and on a soft-broken line, a percent-decoded figure, a diagram fence, a code block that
    /// closes its own <c>verbatim</c>, nested quotes, and the orphan trailer.
    /// <para>
    /// A whole-file assertion is the point: the per-feature tests all pass under a preamble whose
    /// packages are reordered or a body whose blank-line spacing has drifted.
    /// </para>
    /// </summary>
    [Fact]
    public void AWholeDocumentIsByteForByteWhatTheSwiftWrites()
    {
        var source = Join(
            "---",
            "title: 100% & _more_",
            // A Cyrillic small letter IE (U+0435) inside an otherwise Latin word: the T2A trigger
            // has to see the title block, not just the body.
            "AUTHOR: n" + Chr(0x0435) + "ttrash",
            "---",
            "",
            @"# H[^1] with $a &= b$",
            "",
            @"| h[^1] | $p \\ q$ |",
            "|:--|--:|",
            @"| [x] | c & d |",
            "| solo |",
            "",
            "text",
            "![cap **b**](my%20dir/a.png)",
            "more [l](u \"t\")",
            "[^2] tail",
            "",
            "```dot",
            "digraph{a->b}",
            "```",
            "",
            "```",
            @"\end{verbatim}",
            "```",
            "",
            "> quote [^1]",
            "> > deep",
            "",
            "[^1]: note **with** `code`",
            "[^3]: orphan",
            "");

        var expected = Join(
            @"\documentclass{article}",
            @"\usepackage[utf8]{inputenc}",
            @"\usepackage[T1,T2A]{fontenc}",
            @"\usepackage{amsmath}",
            @"\usepackage{graphicx}",
            @"\usepackage{longtable}",
            @"\usepackage{hyperref}",
            @"\title{100\% \& \_more\_}",
            @"\author{n" + Chr(0x0435) + @"ttrash}",
            @"\date{}",
            @"\begin{document}",
            @"\maketitle",
            @"",
            @"\section{H\protect\footnote{note \textbf{with} \texttt{code}} with $\begin{aligned}a &= b\end{aligned}$}",
            @"",
            @"\begin{longtable}{lr}",
            @"\hline",
            @"\textbf{h\footnotemark[1]} & \textbf{$\begin{aligned}p \\ q\end{aligned}$} \\",
            @"\hline",
            @"\endhead",
            @"{}[x] & c \& d \\",
            @"solo &  \\",
            @"\hline",
            @"\end{longtable}",
            @"",
            @"text",
            @"\begin{figure}[ht]",
            @"\centering",
            @"\includegraphics[width=\linewidth]{my dir/a.png}",
            @"\caption{cap \textbf{b}}",
            @"\end{figure}\\",
            @"more \href{u}{l}\\",
            @"{}[\textasciicircum{}2] tail",
            @"",
            @"% dot diagram source " + Chr(0x2014) + @" LaTeX has no renderer for it, so it is kept as written.",
            @"\begin{verbatim}",
            @"digraph{a->b}",
            @"\end{verbatim}",
            @"",
            @"\noindent\verb|\end{verbatim}|\par",
            @"",
            @"\begin{quote}",
            @"quote \footnotemark[1]",
            @"",
            @"\begin{quote}",
            @"deep",
            @"\end{quote}",
            @"\end{quote}",
            @"",
            @"% Footnotes defined but never referenced " + Chr(0x2014) + @" kept so nothing is lost.",
            @"\footnote{orphan}",
            @"",
            @"\end{document}",
            @"");

        Assert.Equal(expected, LaTeXExport.Document(source));
    }
}
