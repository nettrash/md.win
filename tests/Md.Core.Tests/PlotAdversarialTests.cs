using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Md.Core.Markdown;

namespace Md.Core.Tests;

// Adversarial review of the ```plot port. Every expectation here was derived from
// the Swift source (`md.macOS/md/Plot.swift`: scalar-by-scalar scanning, the
// grammar's own whitespace, the 4096-node / 128-depth budgets, the 0.6 em legend
// estimate) or recomputed with an independent oracle (IEEE doubles in Python,
// formatted from the exact decimal expansion, ties to even) — never read off the
// C# output. Each test names the mutation of Plot.cs it exists to kill: the
// original suite left these four guards standing.
public class PlotAdversarialTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static void Has(string haystack, string needle, string? note = null) =>
        Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
            (note ?? "") + " expected to contain '" + needle + "' in: " + Clip(haystack));

    private static void Lacks(string haystack, string needle, string? note = null) =>
        Assert.False(haystack.Contains(needle, StringComparison.Ordinal),
            (note ?? "") + " expected NOT to contain '" + needle + "' in: " + Clip(haystack));

    private static string Clip(string s) => s.Length <= 200 ? s : s.Substring(0, 200) + "…";

    private static double Value(string expr, double x) => Plot.Evaluate(Plot.ParseExpression(expr, "x"), "x", x);

    private static readonly Regex RectPattern = new("<rect x=\"([\\d.]+)\" y=\"([\\d.]+)\" width=\"([\\d.]+)\" height=\"([\\d.]+)\"", RegexOptions.CultureInvariant);

    private static string FirstPair(string svg)
    {
        var at = svg.IndexOf("<polyline points=\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "no polyline in: " + Clip(svg));
        var start = at + "<polyline points=\"".Length;
        var end = svg.IndexOfAny(new[] { ' ', '"' }, start);
        return svg.Substring(start, end - start);
    }

    private static string LastPair(string svg)
    {
        var at = svg.LastIndexOf("<polyline points=\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "no polyline in: " + Clip(svg));
        var close = svg.IndexOf('"', at + "<polyline points=\"".Length);
        var space = svg.LastIndexOf(' ', close);
        return svg.Substring(space + 1, close - space - 1);
    }

    /// <summary>Run <paramref name="body"/> on a thread with exactly this much stack, so the result does not depend on the test host's thread.</summary>
    private static string OnThread(int stackKb, Func<string> body)
    {
        string? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception error) { failure = error; }
        }, stackKb * 1024);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException("the body threw on a " + stackKb + " KB thread: " + failure);
        return result!;
    }

    // Kills: ResolveY's 0.05 → 0.1. The suite's only `y: auto` coordinate check is a
    // flat series, whose padding is symmetric and so invisible.
    //
    // x over [-1, 1]: low −1, high 1, span 2, pad 0.1 → window [−1.1, 1.1] (both
    // exact doublings of 0.05 and 0.1, so 1 + 0.1 rounds to the 1.1 double and the
    // width is the 2.2 double). Default canvas, one unlabelled series → no legend:
    // left 40, top 40, plot 520 × 320. sy(−1) = 40 + (2.1/2.2)·320 = 345.4545… →
    // "345.45" at two places, "345.5" at one; sy(1) = 40 + (0.1/2.2)·320 =
    // 54.5454… → "54.55" / "54.5". niceStep(2.2): rough 0.275, decade 0.1, norm
    // 2.75 → step 0.2; ticks −1.0 … 1.0, eleven of them.
    [Fact]
    public void YAutoPadsFivePercentOfTheSpanOnEachSide()
    {
        var svg = Plot.PlotSvg("x: -1..1\nx");
        Assert.Equal("40.00,345.45", FirstPair(svg));
        Assert.Equal("560.00,54.55", LastPair(svg));
        Has(svg, "<line x1=\"40.0\" y1=\"345.5\" x2=\"560.0\" y2=\"345.5\"/>", "the grid line of the tick at -1");
        Has(svg, "<line x1=\"40.0\" y1=\"54.5\" x2=\"560.0\" y2=\"54.5\"/>", "the grid line of the tick at 1");
        Has(svg, "y=\"345.5\" text-anchor=\"end\" dominant-baseline=\"middle\">-1</text>");
        Has(svg, "y=\"54.5\" text-anchor=\"end\" dominant-baseline=\"middle\">1</text>");
        Assert.Equal(11, Regex.Matches(svg, "dominant-baseline=\"middle\">", RegexOptions.CultureInvariant).Count);
        // The parsed range is unpadded and the pad is applied only when drawing.
        Assert.Null(Plot.ParsePlot("x: -1..1\nx").YMin);
    }

    // Kills: the unary operand parsed at precedence 1 instead of the `^` level.
    // Every unary minus in the existing suite precedes a `^`, a `/`, a `%` or a
    // lone operand, where −(a op b) and (−a) op b coincide. `unary()` parses its
    // operand with `expression(powerPrecedence)`, so it binds tighter than every
    // additive and multiplicative operator and looser than `^` alone.
    [Fact]
    public void UnaryOperatorsBindTighterThanTheBinaryOnesExceptPower()
    {
        Assert.Equal(-2, Value("-x+1", 3));      // (−x) + 1, not −(x + 1) = −4
        Assert.Equal(-4, Value("-x-1", 3));      // (−x) − 1, not −(x − 1) = −2
        Assert.Equal(2, Value("!x+1", 0));       // (!0) + 1, not !(0 + 1) = 0
        Assert.Equal(4, Value("+x+1", 3));
        Assert.Equal(-6, Value("-x*2", 3));
        Assert.Equal(-6, Value("2*-x", 3));
        Assert.Equal(0.5, Value("2^-x", 1));
        Assert.Equal(-8, Value("-x^2+1", 3));    // (−(x²)) + 1
        Assert.Equal(1, Value("-x<0", 3));       // (−x) < 0
        Assert.Equal(0, Value("-(x<0)", 3));
    }

    // Kills: `xMin + i * dx` in SampleSeries. The recorded figure and the existing
    // test check only the first and last point of the run, and the Python oracle
    // (same IEEE doubles, exact decimal rounding) shows the two formulas agree
    // there and differ on 44 interior samples of this fence — the first at
    // sample 455: 348.47 against 348.48.
    [Fact]
    public void SamplesWithTheDivideFirstFormulaInTheMiddleOfTheRunToo()
    {
        var svg = Plot.PlotSvg("x: -6.3..0.7999999999999998\ny: -3.3..3.1\nx");
        Has(svg, " 276.60,348.47 ", "sample 455");
        Has(svg, " 282.84,344.21 ", "sample 467");
        Has(svg, " 293.24,337.12 ", "sample 487");
        Lacks(svg, "276.60,348.48", "what xMin + i * dx draws at sample 455");
        Lacks(svg, "282.84,344.22");
        Lacks(svg, "293.24,337.11");
    }

    // Kills: CountCharacters → string.Length. The existing emoji test compares a
    // one-emoji label with a one-letter label, and both fall under the 72 px legend
    // minimum, so the gutter is 72 either way. Twelve code points at 0.6 em of
    // 11 px are 79.2 → ceil 80 + 28 = 108 → a 412 px plot area for emoji and
    // letters alike; twenty-four UTF-16 units would be 158.4 → 159 + 28 = 187 →
    // 333 px, which is exactly what 24 letters get.
    [Fact]
    public void LegendGutterCountsCodePointsAboveTheMinimumWidth()
    {
        var twelveEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 12));
        var emoji = Plot.PlotSvg("x: -1..1\n" + twelveEmoji + " = x");
        var letters = Plot.PlotSvg("x: -1..1\nabcdefghijkl = x");
        var twentyFour = Plot.PlotSvg("x: -1..1\nabcdefghijklmnopqrstuvwx = x");
        Assert.Equal("<rect x=\"40.0\" y=\"40.0\" width=\"412.0\" height=\"320.0\"", RectPattern.Match(letters).Value);
        Assert.Equal("<rect x=\"40.0\" y=\"40.0\" width=\"412.0\" height=\"320.0\"", RectPattern.Match(emoji).Value);
        Assert.Equal("<rect x=\"40.0\" y=\"40.0\" width=\"333.0\" height=\"320.0\"", RectPattern.Match(twentyFour).Value);
        Has(emoji, ">" + twelveEmoji + "</text>");
        // A supplementary-plane character outside a label is named whole in the
        // message (Swift's bytes), and travels whole through a title.
        Has(Plot.RenderPlot("x + \U0001D465"), "plot: unexpected character '\U0001D465'\nx + \U0001D465");
        Has(Plot.PlotSvg("title: \U0001D465\nx"), "<title>\U0001D465</title>");
        Assert.Equal("\U0001D465", Plot.ParsePlot("title: \U0001D465\nx").Title);
    }

    // The scanner walks code points; a combining mark is a character of its own
    // and never part of the `=`, `..`, `#` or `:` next to it. In Swift the source
    // is `Array(source.unicodeScalars)`, so `=\u0301` is two scalars there too.
    [Fact]
    public void CombiningMarksNextToDelimitersAreCharactersNotDelimiters()
    {
        // `a =\u0301 x`: the `=` is still the label separator; the mark heads the body.
        Assert.Equal("<div class=\"plot\"><pre>plot: unexpected character '\u0301'\na =\u0301 x</pre></div>",
            Plot.RenderPlot("a =\u0301 x"));
        // A mark on the range's last digit is inside the constant expression.
        Has(Plot.RenderPlot("x: -1..1\u0301\nx"), "plot: unexpected character '\u0301'");
        // `#` followed by a mark is still a comment line: the first character decides.
        Assert.Equal("<div class=\"plot\"></div>", Plot.RenderPlot("#\u0301 sin(x)"));
        // A mark right after the directive colon is part of the value, and a title keeps it.
        Assert.Equal("\u0301T", Plot.ParsePlot("title:\u0301T\nx").Title);
        Has(Plot.PlotSvg("title: T\u0301\nx"), "<title>T\u0301</title>");
        // NBSP alone is a series line, not blank: space and tab are the only whitespace.
        Assert.Equal("<div class=\"plot\"><pre>plot: unexpected character '\u00A0'\n\u00A0</pre></div>",
            Plot.RenderPlot("\u00A0"));
        // A mark glued to a name splits the name: `sin\u0301(x)` is `sin` then a stray mark.
        Has(Plot.RenderPlot("sin\u0301(x)"), "plot: unexpected character '\u0301'");
    }

    // CR, LF and CRLF are the parser's three line breaks and nothing else is; the
    // figure a fence draws cannot depend on which one the editor saved.
    [Fact]
    public void CrCrlfAndLfFencesDrawTheSameFigureButAreDifferentMemoKeys()
    {
        var lf = "x: -1..1\ntitle: T\nsin(x)";
        var crlf = "x: -1..1\r\ntitle: T\r\nsin(x)";
        var cr = "x: -1..1\rtitle: T\rsin(x)";
        var drawn = Plot.PlotSvg(lf);
        Assert.Equal(drawn, Plot.PlotSvg(crlf));
        Assert.Equal(drawn, Plot.PlotSvg(cr));
        Assert.Equal(drawn, Plot.PlotSvg(crlf + "\r\n"));
        Has(drawn, "<title>T</title>");
        // Blank input in every spelling is the empty container, and never an error.
        foreach (var blank in new[] { "", "\n", "\r", "\r\n", "\r\n\r\n", "\t\r\n \r", " \t" })
        {
            Assert.Equal("<div class=\"plot\"></div>", Plot.RenderPlot(blank));
        }
        // Mixed breaks in one fence, and a trailing one, are three series.
        Assert.Equal(3, Plot.ParsePlot("sin(x)\rcos(x)\r\ntan(x)\n").Series.Count);
        // A tab inside a value survives; only the ends are trimmed. `\r` alone
        // ends the title, so "A\rB" is a title and a series `B`.
        Assert.Equal("A\tB", Plot.ParsePlot("title:\tA\tB\t\nx").Title);
        Assert.Equal("A", Plot.ParsePlot("title: A\rB = x").Title);
        Assert.Equal("B", Plot.ParsePlot("title: A\rB = x").Series[0].Label);
        // The memo is keyed on the exact text: three spellings are three entries.
        var memo = new PlotMemo(32);
        _ = Plot.RenderPlot(lf, memo);
        _ = Plot.RenderPlot(crlf, memo);
        _ = Plot.RenderPlot(cr, memo);
        Assert.Equal(new PlotMemo.Statistics(3, 0, 3), memo.Snapshot);
    }

    // The budgets, at their exact boundaries. `count()` charges one node per
    // binary and unary node, so `x` + 4096 × `+x` is exactly 4096 nodes and legal;
    // `expression()` charges one depth level per call, so 127 parentheses reach
    // depth 128 and 128 reach 129. Run on a 16 MB thread so the contract, not the
    // host thread, decides.
    [Fact]
    public void TheNodeAndDepthBudgetsAreExactAtTheirBoundaries()
    {
        var results = OnThread(16 * 1024, () =>
        {
            var out_ = new StringBuilder();
            out_.Append(Plot.RenderPlot("x: -1..1\nx" + Repeat("+x", 4096)).Contains("<polyline", StringComparison.Ordinal) ? "4096:draws;" : "4096:no;");
            out_.Append(Plot.RenderPlot("x: -1..1\nx" + Repeat("+x", 4097)).Contains("plot: expression too large", StringComparison.Ordinal) ? "4097:large;" : "4097:no;");
            out_.Append(Plot.RenderPlot("x: -1..1\n" + Repeat("(", 127) + "x" + Repeat(")", 127)).Contains("<polyline", StringComparison.Ordinal) ? "127:draws;" : "127:no;");
            out_.Append(Plot.RenderPlot("x: -1..1\n" + Repeat("(", 128) + "x" + Repeat(")", 128)).Contains("plot: expression nested too deeply", StringComparison.Ordinal) ? "128:deep;" : "128:no;");
            out_.Append(Plot.RenderPlot("x: -1..1\n" + Repeat("-", 127) + "x").Contains("<polyline", StringComparison.Ordinal) ? "-127:draws;" : "-127:no;");
            out_.Append(Plot.RenderPlot("x: -1..1\n" + Repeat("-", 128) + "x").Contains("plot: expression nested too deeply", StringComparison.Ordinal) ? "-128:deep" : "-128:no");
            return out_.ToString();
        });
        Assert.Equal("4096:draws;4097:large;127:draws;128:deep;-127:draws;-128:deep", results);
        // 127 minuses is an odd number of them, so the curve is -x: it climbs out of
        // the auto window's top-left corner and ends bottom-right.
        var odd = OnThread(16 * 1024, () => Plot.PlotSvg("x: -1..1\ny: -1..1\n" + Repeat("-", 127) + "x"));
        Assert.Equal("40.00,40.00", FirstPair(odd));
        Assert.Equal("560.00,360.00", LastPair(odd));
    }

    // A legal fence must never be a dead process. The 4096-node chain recurses
    // 4096 frames deep in the evaluator, which a 1 MB Windows thread does not
    // reliably hold (measured on this machine: > 768 KB in Release, > 1 MB in
    // Debug under tier-0 JIT). On a starved thread the answer is the family's
    // `plot:` line; on a roomy one it is the drawing; on neither is it a crash.
    [Fact]
    public void TheLegalChainOnAStarvedThreadIsAContainerNotACrash()
    {
        var source = "x: -1..1\nx" + Repeat("+x", 4096);
        var starved = OnThread(256, () => Plot.RenderPlot(source, new PlotMemo(32)));
        Assert.StartsWith("<div class=\"plot\">", starved, StringComparison.Ordinal);
        Assert.EndsWith("</div>", starved, StringComparison.Ordinal);
        Assert.True(starved.Contains("<polyline", StringComparison.Ordinal)
                    || starved.Contains("plot: expression too large\n" + source, StringComparison.Ordinal),
            "a drawing or the fallback, never anything else: " + Clip(starved));
        // On a 1 MB thread the parser's 128 levels fit under the probe, so the depth guard speaks first.
        var deep = OnThread(1024, () => Plot.RenderPlot("x: -1..1\n" + Repeat("(", 5000) + "x" + Repeat(")", 5000), new PlotMemo(32)));
        Has(deep, "plot: expression nested too deeply");
    }

    // The largest fence the clamps allow: 24 series, 5000 samples, 2000 × 2000.
    // It renders (every constant is at its ceiling, none over), and it is the
    // container the memo refuses — 24 polylines of 5001 points are far past
    // 128 × 1024 code units.
    [Fact]
    public void TheLargestLegalFenceRendersAndIsNotMemoised()
    {
        var lines = new List<string> { "x: -1..1", "width: 2000", "height: 2000", "samples: 5000" };
        for (var index = 0; index < 24; index++) lines.Add("x*" + index.ToString(Inv));
        var source = string.Join("\n", lines);
        var spec = Plot.ParsePlot(source);
        Assert.Equal(24, spec.Series.Count);
        Assert.Equal((2000, 2000, 5000), (spec.Width, spec.Height, spec.Samples));
        var memo = new PlotMemo(32);
        var html = Plot.RenderPlot(source, memo);
        Lacks(html, "plot:");
        Has(html, "viewBox=\"0 0 2000 2000\"");
        Assert.Equal(24, Regex.Matches(html, "<polyline ", RegexOptions.CultureInvariant).Count);
        Assert.True(html.Length > PlotMemo.LargestMemoisedValue, "length " + html.Length);
        Assert.Equal(new PlotMemo.Statistics(0, 0, 1), memo.Snapshot);
        // One more line is the 25th series, and the error names the limit.
        Has(Plot.RenderPlot(source + "\nx*24"), "plot: too many series (limit 24)");
    }

    private static string Repeat(string s, int count)
    {
        var out_ = new StringBuilder(s.Length * count);
        for (var i = 0; i < count; i++) out_.Append(s);
        return out_.ToString();
    }
}
