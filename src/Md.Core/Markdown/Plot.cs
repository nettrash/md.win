using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Md.Core.Markdown;

// The ```plot fence: source text in, an SVG string out.
//
// This file is pure and synchronous — no engine, no asset, no script, no
// platform API. That is what lets the live preview, the self-contained HTML
// export, print, PDF, EPUB, LaTeX and "export diagram as SVG" all receive a
// finished <svg> in the bytes the renderer already returns. The one piece of
// state is PlotMemo, a memo of a pure function.
//
// It is a transliteration of md.vscode/src/render/plot.ts, which md, md.macOS
// and md.Android are also transliterations of: same function names, same order,
// hand-written scanning instead of regular expressions, plain records instead of
// generics. Byte parity with the other four ports is the contract — the golden
// figure and the Rust-generated oracle pin it.
//
// WHERE IT DELIBERATELY DIVERGES FROM THE SITE'S PLOTTER
// Four behaviours of nettrash.me's math.rs are bugs, fixed here: floor/ceil/
// round draw; comparisons yield 1.0/0.0; `^` is right-associative; everything is
// a double (5/2 = 2.5). And two in the axes: tick labels are rounded rather than
// truncated, and tick positions are computed by index rather than accumulated.
//
// NUMBER FORMATTING IS THE CROSS-PLATFORM HAZARD
// Rust's {:.1e} writes `1.0e3`; .NET's "E1" writes `1.2E+003`; ties differ too
// (1250 is `1.2e3` in Rust, ties to even on the exact value). Every formatter
// at the foot of this file is hand-written from the exact binary expansion.
// Never ToString("E1"), never ToString("F2"), never Math.Round's banker's default.
//
// AND SO IS pow(10, e)
// pow and log10 are not correctly rounded and differ by *engine version* (Node
// 20 is wrong on 68 of the 632 integer exponents, OpenJDK 21 on 64). Decades are
// parsed from decimal literals, which every language specifies to be correctly
// rounded. Do not reintroduce pow(10, e).
//
// UTF-16, LIKE THE KOTLIN AND TYPESCRIPT SIBLINGS
// Every grammar character is ASCII, so indexing code units instead of Swift's
// scalars changes nothing — except the legend gutter, which counts code points
// (Rune), and the `unexpected character` message, which names a whole code point.

/// <summary>Everything that makes a block unrenderable, with the message the reader sees.</summary>
public sealed class PlotException : Exception
{
    public PlotException(string message) : base(message) { }
}

public static class Plot
{
    // MARK: - The entry point

    /// <summary>One ```plot fence, as the block of markup the renderer embeds.</summary>
    /// <remarks>
    /// The container is emitted unconditionally — for a good plot, an empty block
    /// and a broken one alike — because every export path counts <c>div.plot</c>
    /// containers to pair a figure with its source block. A block that cannot be
    /// parsed keeps its source visible under one <c>plot: …</c> line. Memoised on
    /// <paramref name="source"/>; failures are memoised too, on purpose — a
    /// half-typed fence is a parse error on most keystrokes.
    /// </remarks>
    public static string RenderPlot(string source) => RenderPlot(source, memo);

    internal static string RenderPlot(string source, PlotMemo memo)
    {
        var memoised = memo.Value(source);
        if (memoised is not null) return memoised;
        string container;
        try
        {
            container = "<div class=\"plot\">" + PlotSvg(source) + "</div>";
        }
        catch (PlotException error)
        {
            container = "<div class=\"plot\"><pre>" + EscapeHtml("plot: " + error.Message + "\n" + source) + "</pre></div>";
        }
        catch (Exception error)
        {
            // Nothing may escape into the Markdown renderer, which has no other
            // failure mode.
            container = "<div class=\"plot\"><pre>" + EscapeHtml("plot: " + error.Message + "\n" + source) + "</pre></div>";
        }
        memo.Store(container, source);
        return container;
    }

    private static readonly PlotMemo memo = new(32);

    /// <summary>Entries, hits and misses of the shared memo. Nothing in the app branches on these.</summary>
    public static PlotMemo.Statistics MemoStatistics => memo.Snapshot;

    /// <summary>Drop every memoised container. Only the tests need this.</summary>
    public static void ClearMemo() => memo.RemoveAll();

    /// <summary>The <c>&lt;svg&gt;</c> element for a fence, <c>""</c> for an empty block, or a <see cref="PlotException"/>.</summary>
    public static string PlotSvg(string source)
    {
        var spec = ParsePlot(source);
        // A block with nothing in it draws nothing, like an empty Mermaid block. A
        // block with directives but no series is not empty: draw the empty axes
        // those directives describe, so the author's text does not vanish.
        if (spec.Series.Count == 0 && !spec.HasDirectives) return "";
        return Draw(spec);
    }

    // MARK: - The fence

    /// <summary>A parsed ```plot block: the directives, resolved, and the series in order.</summary>
    public sealed record Spec
    {
        public double XMin { get; internal set; } = -10;
        public double XMax { get; internal set; } = 10;
        /// <summary>null when <c>y: auto</c> (the default) — the range is fitted to the samples.</summary>
        public double? YMin { get; internal set; }
        public double? YMax { get; internal set; }
        public string Title { get; internal set; } = "";
        public string XLabel { get; internal set; } = "";
        public string YLabel { get; internal set; } = "";
        public Legend Legend { get; internal set; } = Legend.Auto;
        public bool Grid { get; internal set; } = true;
        public bool Axes { get; internal set; } = true;
        public int Width { get; internal set; } = DefaultWidth;
        public int Height { get; internal set; } = DefaultHeight;
        public int Samples { get; internal set; } = DefaultSamples;
        internal List<Series> SeriesList { get; } = new();
        public IReadOnlyList<Series> Series => SeriesList;
        /// <summary>
        /// Whether the fence carried at least one directive — what separates a
        /// genuinely empty block from one the author wrote into but gave no series.
        /// </summary>
        public bool HasDirectives { get; internal set; }
    }

    public enum Legend { On, Off, Auto }

    public enum SeriesKind { Function, Parametric, Points }

    /// <summary>One curve — three shapes in one flat record, which every port reads without a downcast.</summary>
    /// <param name="Label">The author's own label, or null — then the legend shows <paramref name="Source"/>.</param>
    /// <param name="Source">The source text of the series, verbatim, for the legend and for messages.</param>
    /// <param name="Expression"><c>Function</c>: y = f(x). <c>Parametric</c>: the x half. Unused by <c>Points</c>.</param>
    /// <param name="YExpression"><c>Parametric</c> only: the y half.</param>
    /// <param name="Parameter"><c>Parametric</c> only: the parameter's name (<c>"x"</c> for a function, <c>""</c> for points).</param>
    public sealed record Series(
        SeriesKind Kind,
        string? Label,
        string Source,
        Node? Expression,
        Node? YExpression,
        string Parameter,
        double TMin,
        double TMax,
        IReadOnlyList<Point> Points);

    public readonly record struct Point(double X, double Y);

    /// <summary>The directive keys. Anything else before a colon is a series, not an error.</summary>
    private static readonly string[] Directives =
    {
        "x", "y", "title", "xlabel", "ylabel", "legend", "grid", "axes", "width", "height", "samples",
    };

    private const int DefaultWidth = 600;
    private const int DefaultHeight = 400;
    private const int DefaultSamples = 1000;
    private const int MinWidth = 160;
    private const int MaxWidth = 2000;
    private const int MinHeight = 120;
    private const int MaxHeight = 2000;
    private const int MinSamples = 50;
    private const int MaxSamples = 5000;
    /// <summary>
    /// The most series one fence may draw — the one input an author sets just by
    /// adding lines, and the preview re-renders the document on every keystroke.
    /// </summary>
    private const int MaxSeries = 24;
    /// <summary>
    /// The deepest an expression may nest. Parser and evaluator both recurse, and
    /// a .NET StackOverflowException cannot be caught — it kills the process — so
    /// this guard is load-bearing.
    /// </summary>
    private const int MaxDepth = 128;
    /// <summary>
    /// The most binary/unary nodes one expression may hold. The depth guard cannot
    /// see a long flat chain (`x+x+x+…` parses at constant depth, then blows the
    /// stack in the evaluator).
    /// </summary>
    private const int MaxNodes = 4096;

    /// <summary>
    /// .NET only: fail as a <see cref="PlotException"/> where the platform would
    /// otherwise die. The node budget bounds the evaluator's recursion at 4096
    /// frames, which the Swift, Kotlin and TypeScript stacks absorb, but a 1 MB
    /// Windows thread does not with any margin — measured here, a 4096-node chain
    /// needs more than 768 KB under tier-0 JIT (more than 1 MB on Debug IL). A
    /// StackOverflowException cannot be caught, so the parser and the evaluator
    /// probe the stack first and the budget's own message stands in for the budget
    /// the platform could not honour. This is the one place the C# output may
    /// differ from the siblings — a <c>plot:</c> line where they draw — and only
    /// where the process would otherwise have been a dead one.
    /// </summary>
    private static void EnsureStack(string message)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw new PlotException(message);
    }

    /// <summary>Read a fence into a <see cref="Spec"/>.</summary>
    /// <remarks>
    /// Blank lines are ignored, a line whose first non-space character is <c>#</c>
    /// is a comment, and order is free. A line is a directive when it reads
    /// <c>key: value</c> and the key is known, so <c>f: x</c> still plots.
    /// </remarks>
    public static Spec ParsePlot(string source)
    {
        var spec = new Spec();

        foreach (var raw in SplitLines(source))
        {
            var line = Trim(raw);
            if (line.Length == 0) continue;
            if (line[0] == '#') continue;
            var directive = MatchDirective(line);
            if (directive is not null)
            {
                ApplyDirective(spec, directive.Value.Key, directive.Value.Value);
                spec.HasDirectives = true;
                continue;
            }
            if (spec.SeriesList.Count == MaxSeries)
            {
                throw new PlotException("too many series (limit " + MaxSeries.ToString(CultureInfo.InvariantCulture) + ")");
            }
            spec.SeriesList.Add(ParseSeries(line));
        }

        if (!(spec.XMax > spec.XMin)) throw new PlotException("x range must be increasing");
        if (spec.YMin is double low && spec.YMax is double high && !(high > low))
        {
            throw new PlotException("y range must be increasing");
        }
        return spec;
    }

    /// <summary>
    /// <c>key: value</c>, whatever the key. Hand-scanned (the equivalent of
    /// <c>^\s*([a-z][a-z-]*)\s*:\s*(.*)$</c> with the grammar's own whitespace),
    /// because the same scan has to exist in every port.
    /// </summary>
    private static (string Key, string Value)? MatchKeyed(string line)
    {
        var index = 0;
        while (index < line.Length && IsSpace(line[index])) index += 1;
        var start = index;
        if (index >= line.Length || !IsLowerLetter(line[index])) return null;
        while (index < line.Length && (IsLowerLetter(line[index]) || line[index] == '-')) index += 1;
        var key = Slice(line, start, index);
        while (index < line.Length && IsSpace(line[index])) index += 1;
        if (index >= line.Length || line[index] != ':') return null;
        return (key, Trim(Slice(line, index + 1, line.Length)));
    }

    /// <summary>The same line, when the key is one this renderer knows — what keeps <c>f: x</c> from failing the block.</summary>
    private static (string Key, string Value)? MatchDirective(string line)
    {
        var keyed = MatchKeyed(line);
        if (keyed is null || !Contains(Directives, keyed.Value.Key)) return null;
        return keyed;
    }

    private static void ApplyDirective(Spec spec, string key, string value)
    {
        if (key == "x")
        {
            var range = ParseRange(value, "x");
            spec.XMin = range.Min;
            spec.XMax = range.Max;
            return;
        }
        if (key == "y")
        {
            if (Lowercased(value) == "auto")
            {
                spec.YMin = null;
                spec.YMax = null;
                return;
            }
            var range = ParseRange(value, "y");
            spec.YMin = range.Min;
            spec.YMax = range.Max;
            return;
        }
        if (key == "title") { spec.Title = value; return; }
        if (key == "xlabel") { spec.XLabel = value; return; }
        if (key == "ylabel") { spec.YLabel = value; return; }
        if (key == "legend")
        {
            var word = Lowercased(value);
            if (word == "on") { spec.Legend = Legend.On; return; }
            if (word == "off") { spec.Legend = Legend.Off; return; }
            if (word == "auto") { spec.Legend = Legend.Auto; return; }
            throw new PlotException("legend must be 'on', 'off' or 'auto'");
        }
        if (key == "grid" || key == "axes")
        {
            var word = Lowercased(value);
            if (word != "on" && word != "off") throw new PlotException(key + " must be 'on' or 'off'");
            if (key == "grid") spec.Grid = word == "on"; else spec.Axes = word == "on";
            return;
        }
        if (key == "width")
        {
            spec.Width = ClampInteger(Number(value, "width"), MinWidth, MaxWidth);
            return;
        }
        if (key == "height")
        {
            spec.Height = ClampInteger(Number(value, "height"), MinHeight, MaxHeight);
            return;
        }
        // `samples`, the only key left.
        spec.Samples = ClampInteger(Number(value, "samples"), MinSamples, MaxSamples);
    }

    /// <summary>
    /// <c>A..B</c>. Both ends are constant expressions, so <c>x: -pi..pi</c> and
    /// <c>x: 0..2*pi</c> work; an end that mentions a variable is an error rather
    /// than a silent zero. The first <c>..</c> wins.
    /// </summary>
    private static (double Min, double Max) ParseRange(string value, string key)
    {
        var at = IndexOfPair(value, '.', '.');
        if (at < 0) throw new PlotException(key + " range must be written min..max");
        var min = Constant(Slice(value, 0, at), key);
        var max = Constant(Slice(value, at + 2, value.Length), key);
        if (!(max > min)) throw new PlotException(key + " range must be increasing");
        return (min, max);
    }

    /// <summary>A constant expression: no variable, finite.</summary>
    private static double Constant(string text, string key)
    {
        var trimmed = Trim(text);
        if (trimmed.Length == 0) throw new PlotException(key + " range must be written min..max");
        var value = Evaluate(ParseExpression(trimmed, ""), "", 0);
        if (!IsFiniteNumber(value)) throw new PlotException(key + " range must be finite");
        return value;
    }

    private static double Number(string value, string key)
    {
        var parsed = Evaluate(ParseExpression(Trim(value), ""), "", 0);
        if (!IsFiniteNumber(parsed)) throw new PlotException(key + " must be a number");
        return parsed;
    }

    /// <summary>A pixel count or a sample count: rounded to a whole number, then clamped.</summary>
    private static int ClampInteger(double value, int low, int high)
    {
        var whole = RoundTiesAway(value);
        if (whole < low) return low;
        if (whole > high) return high;
        // `Number` already refused anything non-finite, so the cast is exact here.
        return (int)whole;
    }

    /// <summary>
    /// One series line. The label is everything left of the first top-level
    /// <c>=</c> that is not part of <c>==</c>, <c>&lt;=</c>, <c>&gt;=</c> or
    /// <c>!=</c>; the language has no assignment, so a bare <c>=</c> is always a
    /// label separator.
    /// </summary>
    private static Series ParseSeries(string line)
    {
        string? label = null;
        var body = line;
        var at = LabelSeparator(line);
        if (at >= 0)
        {
            label = Trim(Slice(line, 0, at));
            body = Trim(Slice(line, at + 1, line.Length));
            if (label.Length == 0) label = null;
        }
        else if (!IsPointsLine(line))
        {
            // `f: x` — a `key:` line whose key is no directive of ours: the key is
            // the label and the rest is the series. `points:` is the one colon that
            // means something else, and it is claimed above.
            var keyed = MatchKeyed(line);
            if (keyed is not null && keyed.Value.Value.Length != 0)
            {
                label = keyed.Value.Key;
                body = keyed.Value.Value;
            }
        }
        if (body.Length == 0) throw new PlotException("a series needs an expression");

        var points = ParsePointsSeries(label, body);
        if (points is not null) return points;
        var parametric = ParseParametricSeries(label, body);
        if (parametric is not null) return parametric;

        return new Series(SeriesKind.Function, label, body, ParseExpression(body, "x"), null,
            "x", 0, 0, Array.Empty<Point>());
    }

    /// <summary>The index of the label <c>=</c>, or −1.</summary>
    private static int LabelSeparator(string line)
    {
        var depth = 0;
        for (var index = 0; index < line.Length; index++)
        {
            var c = line[index];
            if (c == '(') depth += 1;
            else if (c == ')') depth -= 1;
            else if (c == '=' && depth == 0)
            {
                // `==` is a comparison, and `<=`, `>=`, `!=` end with one. The scan
                // stops at the first top-level `=` either way — it does not keep looking.
                if (index + 1 < line.Length && line[index + 1] == '=') return -1;
                if (index > 0)
                {
                    var before = line[index - 1];
                    if (before == '=' || before == '<' || before == '>' || before == '!') return -1;
                }
                return index;
            }
        }
        return -1;
    }

    private const string PointsPrefix = "points:";

    /// <summary>Does this line open a points series? Case-insensitive prefix.</summary>
    private static bool IsPointsLine(string line)
    {
        var count = PointsPrefix.Length;
        if (line.Length < count) return false;
        return Lowercased(Slice(line, 0, count)) == PointsPrefix;
    }

    /// <summary><c>points: x,y x,y …</c>, or null when this is not a points series.</summary>
    private static Series? ParsePointsSeries(string? label, string body)
    {
        if (!IsPointsLine(body)) return null;
        var prefix = PointsPrefix.Length;

        var points = new List<Point>();
        foreach (var token in SplitWhitespace(Slice(body, prefix, body.Length)))
        {
            var comma = IndexOf(token, ',');
            if (comma < 0)
            {
                // U+2014 EM DASH, as in every port.
                throw new PlotException("points must be x,y pairs — '" + token + "' is not one");
            }
            points.Add(new Point(Constant(Slice(token, 0, comma), "point"),
                Constant(Slice(token, comma + 1, token.Length), "point")));
        }
        if (points.Count == 0) throw new PlotException("points needs at least one x,y pair");
        return new Series(SeriesKind.Points, label, body, null, null, "", 0, 0, points);
    }

    /// <summary>
    /// <c>(fx(t), fy(t)) for t in A..B</c>, or null when this is not a parametric
    /// series. Deliberately narrow — an opening parenthesis whose match is
    /// followed by the word <c>for</c> — so <c>(x+1)*2</c> stays a function of x.
    /// </summary>
    private static Series? ParseParametricSeries(string? label, string body)
    {
        if (body[0] != '(') return null;
        var close = MatchingParenthesis(body, 0);
        if (close < 0) return null;
        var tail = Trim(Slice(body, close + 1, body.Length));
        if (!StartsWithWord(tail, "for")) return null;

        var inside = Slice(body, 1, close);
        var comma = TopLevelComma(inside);
        if (comma < 0) throw new PlotException("a parametric series needs (x(t), y(t))");

        // `for t in A..B`
        var rest = Trim(Slice(tail, 3, tail.Length));
        var index = 0;
        while (index < rest.Length && IsIdentifierPart(rest[index])) index += 1;
        var parameter = Slice(rest, 0, index);
        if (index == 0 || !IsIdentifierStart(rest[0]))
        {
            throw new PlotException("expected a parameter name after 'for'");
        }
        var afterName = Trim(Slice(rest, index, rest.Length));
        if (!StartsWithWord(afterName, "in")) throw new PlotException("expected 'in' after '" + parameter + "'");
        var range = ParseRange(Trim(Slice(afterName, 2, afterName.Length)), parameter);

        return new Series(SeriesKind.Parametric, label, body,
            ParseExpression(Trim(Slice(inside, 0, comma)), parameter),
            ParseExpression(Trim(Slice(inside, comma + 1, inside.Length)), parameter),
            parameter, range.Min, range.Max, Array.Empty<Point>());
    }

    // MARK: - The expression language: tokens

    private enum TokenKind { Number, Name, Op, Open, Close, Comma, End }

    /// <summary>One flat record: <c>Text</c> is the spelling of a name or operator, <c>Value</c> the value of a number.</summary>
    private readonly record struct Token(TokenKind Kind, string Text, double Value);

    /// <summary>The two-character operators, longest match first — <c>&lt;=</c> before <c>&lt;</c>.</summary>
    private static readonly string[] LongOperators = { "||", "&&", "==", "!=", "<=", ">=" };
    private static readonly string[] ShortOperators = { "+", "-", "*", "/", "%", "^", "<", ">", "!" };

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            // Space and tab only. Any other whitespace — NBSP, a newline — is an
            // unexpected character, as in every port.
            if (IsSpace(c))
            {
                index += 1;
                continue;
            }
            if (IsDigit(c) || (c == '.' && index + 1 < text.Length && IsDigit(text[index + 1])))
            {
                var scanned = ScanNumber(text, index);
                tokens.Add(new Token(TokenKind.Number, Slice(text, index, scanned.End), scanned.Value));
                index = scanned.End;
                continue;
            }
            if (IsIdentifierStart(c))
            {
                var start = index;
                while (index < text.Length && IsIdentifierPart(text[index])) index += 1;
                tokens.Add(new Token(TokenKind.Name, Slice(text, start, index), 0));
                continue;
            }
            if (c == '(') { tokens.Add(new Token(TokenKind.Open, "(", 0)); index += 1; continue; }
            if (c == ')') { tokens.Add(new Token(TokenKind.Close, ")", 0)); index += 1; continue; }
            if (c == ',') { tokens.Add(new Token(TokenKind.Comma, ",", 0)); index += 1; continue; }
            var two = index + 1 < text.Length ? Slice(text, index, index + 2) : "";
            if (two.Length != 0 && Contains(LongOperators, two))
            {
                tokens.Add(new Token(TokenKind.Op, two, 0));
                index += 2;
                continue;
            }
            var one = c.ToString();
            if (Contains(ShortOperators, one))
            {
                tokens.Add(new Token(TokenKind.Op, one, 0));
                index += 1;
                continue;
            }
            // Name the whole code point, as Swift does: the fallback <pre> is UTF-8
            // encoded later, and a lone surrogate half is not encodable.
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                one = Slice(text, index, index + 2);
            }
            throw new PlotException("unexpected character '" + one + "'");
        }
        tokens.Add(new Token(TokenKind.End, "", 0));
        return tokens;
    }

    /// <summary>
    /// A number literal: <c>123</c>, <c>1.5</c>, <c>.5</c>, <c>5.</c>, <c>1e-3</c>,
    /// <c>1.2E+4</c>. No hex, no digit separators, no sign (that is unary minus).
    /// The exponent is taken only if at least one digit follows its optional sign.
    /// </summary>
    private static (int End, double Value) ScanNumber(string text, int start)
    {
        var index = start;
        while (index < text.Length && IsDigit(text[index])) index += 1;
        if (index < text.Length && text[index] == '.')
        {
            index += 1;
            while (index < text.Length && IsDigit(text[index])) index += 1;
        }
        if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
        {
            var lookahead = index + 1;
            if (lookahead < text.Length && (text[lookahead] == '+' || text[lookahead] == '-')) lookahead += 1;
            if (lookahead < text.Length && IsDigit(text[lookahead]))
            {
                index = lookahead;
                while (index < text.Length && IsDigit(text[index])) index += 1;
            }
        }
        var literal = Slice(text, start, index);
        // The platform's decimal→binary conversion is the one place it is more
        // trustworthy than anything written by hand: correctly rounded, and (since
        // .NET Core 3.0) saturating to ±∞ rather than throwing on `1e400`, which is
        // what Swift, JavaScript, Kotlin and Rust all do with that literal.
        if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value))
        {
            throw new PlotException("'" + literal + "' is not a number");
        }
        return (index, value);
    }

    // MARK: - The expression language: the tree

    public enum NodeKind { Number, Variable, Unary, Binary, Call }

    /// <summary>An expression node — one record with the fields each kind uses, so the tree ports without generics.</summary>
    public sealed class Node
    {
        public NodeKind Kind { get; }
        /// <summary><c>Number</c>.</summary>
        public double Value { get; }
        /// <summary><c>Variable</c> (its name), <c>Unary</c>/<c>Binary</c> (the operator), <c>Call</c> (the function).</summary>
        public string Text { get; }
        /// <summary><c>Unary</c> (the operand), <c>Binary</c> (the left side).</summary>
        public Node? Left { get; }
        /// <summary><c>Binary</c>.</summary>
        public Node? Right { get; }
        /// <summary><c>Call</c>.</summary>
        public IReadOnlyList<Node> Arguments { get; }

        internal Node(NodeKind kind, double value, string text, Node? left, Node? right, IReadOnlyList<Node> arguments)
        {
            Kind = kind;
            Value = value;
            Text = text;
            Left = left;
            Right = right;
            Arguments = arguments;
        }
    }

    private static readonly Node[] NoArguments = Array.Empty<Node>();

    private static Node NumberNode(double value) => new(NodeKind.Number, value, "", null, null, NoArguments);
    private static Node VariableNode(string name) => new(NodeKind.Variable, 0, name, null, null, NoArguments);
    private static Node UnaryNode(string op, Node operand) => new(NodeKind.Unary, 0, op, operand, null, NoArguments);
    private static Node BinaryNode(string op, Node left, Node right) => new(NodeKind.Binary, 0, op, left, right, NoArguments);
    private static Node CallNode(string name, List<Node> args) => new(NodeKind.Call, 0, name, null, null, args);

    /// <summary>
    /// The function roster — exactly the site's names and arity, nothing else.
    /// <c>min</c>, <c>max</c>, <c>if</c>, <c>log</c> are deliberately absent: a name
    /// that works in one port and not the others is worse than one that works in none.
    /// </summary>
    private static readonly string[] Functions =
    {
        "sin", "cos", "tan", "asin", "acos", "atan",
        "sinh", "cosh", "tanh", "asinh", "acosh", "atanh",
        "sqrt", "cbrt", "abs", "exp", "exp2", "ln", "log2", "log10",
        "floor", "ceil", "round",
        "atan2", "pow", "hypot",
    };

    /// <summary>The three two-argument functions; everything else takes one.</summary>
    private static readonly string[] BinaryFunctions = { "atan2", "pow", "hypot" };

    private static int Arity(string name) => Contains(BinaryFunctions, name) ? 2 : 1;

    /// <summary>Precedence, loosest to tightest. <c>^</c> is the only right-associative level.</summary>
    private static int Precedence(string op)
    {
        if (op == "||") return 1;
        if (op == "&&") return 2;
        if (op == "==" || op == "!=") return 3;
        if (op == "<" || op == "<=" || op == ">" || op == ">=") return 3;
        if (op == "+" || op == "-") return 4;
        if (op == "*" || op == "/" || op == "%") return 5;
        if (op == "^") return 6;
        return 0;
    }

    private const int PowerPrecedence = 6;

    /// <summary>
    /// Parse <paramref name="text"/> as an expression in which
    /// <paramref name="variable"/> is the only free name (besides <c>pi</c> and
    /// <c>e</c>). Pass <c>""</c> for a constant expression.
    /// </summary>
    public static Node ParseExpression(string text, string variable)
    {
        var parser = new Parser(Tokenize(text), variable);
        var node = parser.Expression(1);
        parser.ExpectEnd();
        return node;
    }

    /// <summary>Precedence climbing, hand-written — the same twenty lines in every port.</summary>
    private sealed class Parser
    {
        private readonly List<Token> tokens;
        private readonly string variable;
        private int index;
        private int depth;
        private int nodes;

        public Parser(List<Token> tokens, string variable)
        {
            this.tokens = tokens;
            this.variable = variable;
        }

        /// <summary>Charge one node against the budget, so a flat chain cannot outrun the depth guard.</summary>
        private void Count()
        {
            nodes += 1;
            if (nodes > MaxNodes) throw new PlotException("expression too large");
        }

        public Node Expression(int minimum)
        {
            depth += 1;
            if (depth > MaxDepth) throw new PlotException("expression nested too deeply");
            EnsureStack("expression nested too deeply");
            try
            {
                return ExpressionInner(minimum);
            }
            finally
            {
                depth -= 1;
            }
        }

        private Node ExpressionInner(int minimum)
        {
            var left = Unary();
            while (true)
            {
                var token = Peek();
                if (token.Kind != TokenKind.Op) break;
                var level = Precedence(token.Text);
                if (level == 0 || level < minimum) break;
                index += 1;
                // `^` is RIGHT-associative — `2^3^2` is 512. evalexpr's 64 is the
                // site's bug, and this is the deliberate divergence.
                var next = token.Text == "^" ? level : level + 1;
                var right = Expression(next);
                Count();
                left = BinaryNode(token.Text, left, right);
            }
            return left;
        }

        private Node Unary()
        {
            var token = Peek();
            if (token.Kind == TokenKind.Op && (token.Text == "-" || token.Text == "+" || token.Text == "!"))
            {
                index += 1;
                // Parsed at the `^` level, which makes unary minus bind *looser*
                // than exponentiation: `-x^2` is −(x²) and `-2^2` is −4.
                var operand = Expression(PowerPrecedence);
                Count();
                return UnaryNode(token.Text, operand);
            }
            return Primary();
        }

        private Node Primary()
        {
            var token = Peek();
            if (token.Kind == TokenKind.Number)
            {
                index += 1;
                return NumberNode(token.Value);
            }
            if (token.Kind == TokenKind.Open)
            {
                index += 1;
                var inner = Expression(1);
                if (Peek().Kind != TokenKind.Close) throw new PlotException("expected ')'");
                index += 1;
                return inner;
            }
            if (token.Kind == TokenKind.Name)
            {
                index += 1;
                return Name(token.Text);
            }
            if (token.Kind == TokenKind.End) throw new PlotException("the expression ends too early");
            if (token.Kind == TokenKind.Close) throw new PlotException("unmatched ')'");
            throw new PlotException("unexpected '" + token.Text + "'");
        }

        private Node Name(string spelling)
        {
            if (Peek().Kind == TokenKind.Open)
            {
                if (!Contains(Functions, spelling)) throw new PlotException("unknown function '" + spelling + "'");
                index += 1;
                var args = new List<Node>();
                if (Peek().Kind != TokenKind.Close)
                {
                    args.Add(Expression(1));
                    while (Peek().Kind == TokenKind.Comma)
                    {
                        index += 1;
                        args.Add(Expression(1));
                    }
                }
                if (Peek().Kind != TokenKind.Close) throw new PlotException("expected ')'");
                index += 1;
                var wanted = Arity(spelling);
                if (args.Count != wanted)
                {
                    throw new PlotException(spelling + " takes " + wanted.ToString(CultureInfo.InvariantCulture)
                        + " argument" + (wanted == 1 ? "" : "s") + ", not " + args.Count.ToString(CultureInfo.InvariantCulture));
                }
                return CallNode(spelling, args);
            }
            if (spelling == "pi" || spelling == "e") return NumberNode(spelling == "pi" ? Math.PI : Math.E);
            if (variable.Length != 0 && spelling == variable) return VariableNode(spelling);
            if (Contains(Functions, spelling))
            {
                // U+2014 EM DASH, U+2026 HORIZONTAL ELLIPSIS — the message every port pins.
                throw new PlotException(spelling + " is a function — write " + spelling + "(…)");
            }
            throw new PlotException("unknown name '" + spelling + "'");
        }

        public void ExpectEnd()
        {
            var token = Peek();
            if (token.Kind == TokenKind.End) return;
            if (token.Kind == TokenKind.Close) throw new PlotException("unmatched ')'");
            throw new PlotException("unexpected '" + token.Text + "'");
        }

        private Token Peek() => tokens[index];
    }

    // MARK: - Evaluation

    /// <summary>Evaluate <paramref name="node"/> with <paramref name="variable"/> bound to <paramref name="value"/>.</summary>
    /// <remarks>
    /// Every value is a double and evaluation never fails: a domain error is NaN or
    /// ±∞, which breaks the curve where it happens rather than failing the block.
    /// Comparisons and the Boolean operators yield 1.0 and 0.0 — the site's eval
    /// closure discards Booleans, so <c>(x &gt; 0) * sqrt(x)</c> draws nothing there.
    /// </remarks>
    public static double Evaluate(Node node, string variable, double value)
    {
        // A left-deep chain `x+x+…` within the 4096-node budget recurses 4096 deep
        // here, which is more than a 1 MB thread reliably holds under tier-0 JIT.
        // See EnsureStack: the one exception to "evaluation never fails", and it
        // fails the way the budget does rather than the way a stack overflow does.
        EnsureStack("expression too large");
        if (node.Kind == NodeKind.Number) return node.Value;
        if (node.Kind == NodeKind.Variable) return node.Text == variable ? value : double.NaN;
        if (node.Kind == NodeKind.Unary)
        {
            var operand = Evaluate(node.Left!, variable, value);
            if (node.Text == "-") return -operand;
            if (node.Text == "+") return operand;
            return operand == 0 ? 1 : 0;
        }
        if (node.Kind == NodeKind.Binary)
        {
            var left = Evaluate(node.Left!, variable, value);
            var right = Evaluate(node.Right!, variable, value);
            return Binary(node.Text, left, right);
        }
        // A call.
        var first = Evaluate(node.Arguments[0], variable, value);
        if (node.Arguments.Count == 2)
        {
            var second = Evaluate(node.Arguments[1], variable, value);
            if (node.Text == "atan2") return Math.Atan2(first, second);
            if (node.Text == "pow") return PowIEEE(first, second);
            return double.Hypot(first, second);
        }
        return Unary(node.Text, first);
    }

    private static double Binary(string op, double left, double right)
    {
        if (op == "+") return left + right;
        if (op == "-") return left - right;
        if (op == "*") return left * right;
        // Real division: `5/2` is 2.5, `1/0` is ∞, `0/0` is NaN.
        if (op == "/") return left / right;
        // The truncated remainder — C's fmod, JavaScript's and Rust's `%`, and
        // C#'s `%` on doubles. Never Math.IEEERemainder, never a floor-mod.
        if (op == "%") return left % right;
        if (op == "^") return PowIEEE(left, right);
        if (op == "==") return left == right ? 1 : 0;
        if (op == "!=") return left != right ? 1 : 0;
        if (op == "<") return left < right ? 1 : 0;
        if (op == "<=") return left <= right ? 1 : 0;
        if (op == ">") return left > right ? 1 : 0;
        if (op == ">=") return left >= right ? 1 : 0;
        if (op == "&&") return left != 0 && right != 0 ? 1 : 0;
        return left != 0 || right != 0 ? 1 : 0;
    }

    /// <summary>
    /// <c>pow</c> as IEEE 754-2019 §9.2 and C99 define it: <c>pow(1, y)</c> is 1
    /// for every y including NaN, and <c>pow(±1, ±∞)</c> is 1.
    /// </summary>
    /// <remarks>
    /// Swift's C <c>pow</c> already answers so; JavaScript and Java return NaN for
    /// all five, so their ports wrap. .NET's <see cref="Math.Pow"/> happens to agree
    /// with C on the current runtime, but the wrapper is unconditional: the five
    /// results are the contract, not a property of whichever CRT is underneath.
    /// </remarks>
    private static double PowIEEE(double b, double exponent)
    {
        if (b == 1) return 1;
        if (b == -1 && (exponent == double.PositiveInfinity || exponent == double.NegativeInfinity)) return 1;
        return Math.Pow(b, exponent);
    }

    private static double Unary(string name, double v)
    {
        if (name == "sin") return Math.Sin(v);
        if (name == "cos") return Math.Cos(v);
        if (name == "tan") return Math.Tan(v);
        if (name == "asin") return Math.Asin(v);
        if (name == "acos") return Math.Acos(v);
        if (name == "atan") return Math.Atan(v);
        if (name == "sinh") return Math.Sinh(v);
        if (name == "cosh") return Math.Cosh(v);
        if (name == "tanh") return Math.Tanh(v);
        if (name == "asinh") return Math.Asinh(v);
        if (name == "acosh") return Math.Acosh(v);
        if (name == "atanh") return Math.Atanh(v);
        if (name == "sqrt") return Math.Sqrt(v);
        if (name == "cbrt") return Math.Cbrt(v);
        if (name == "abs") return Math.Abs(v);
        if (name == "exp") return Math.Exp(v);
        // pow(2, v), raw, in every port — not exp2; base 2 never reaches the five
        // PowIEEE cases.
        if (name == "exp2") return Math.Pow(2, v);
        if (name == "ln") return Math.Log(v);
        if (name == "log2") return Math.Log2(v);
        if (name == "log10") return Math.Log10(v);
        // floor / ceil / round DRAW. On the site they produce nothing (its
        // preprocessor rewrites every roster name to `math::…` while evalexpr binds
        // exactly these three bare). `round` is ties-away-from-zero, as Rust's is —
        // never Math.Round's banker's default.
        if (name == "floor") return Math.Floor(v);
        if (name == "ceil") return Math.Ceiling(v);
        return RoundTiesAway(v);
    }

    // MARK: - Drawing

    /// <summary>The palette, purple first so a one-series plot matches the site's colour.</summary>
    private static readonly string[] Palette =
    {
        "#673AB7", "#E5390F", "#0F9D58", "#F4B400", "#00838F", "#C2185B", "#5D4037", "#455A64",
    };

    /// <summary>The site's margin, and the room each extra asks for beyond it.</summary>
    private const double Margin = 40.0;
    private const double TitleRoom = 24.0;
    private const double AxisLabelRoom = 18.0;
    private const double LegendMinimum = 72.0;
    private const double LegendPadding = 28.0;
    private const double LegendFont = 11.0;
    /// <summary>Never let the extras eat the figure: the plot area keeps at least this.</summary>
    private const double MinPlot = 40.0;

    private readonly record struct Sample(double X, double Y);

    private static string Draw(Spec spec)
    {
        // Sample first: `y: auto` fits the range to what the series produce, so the
        // samples must exist before the geometry does. They are reused for the
        // drawing pass — sampling twice would be one more chance to disagree.
        var sampled = new List<List<Sample>>(spec.Series.Count);
        foreach (var series in spec.Series) sampled.Add(SampleSeries(series, spec));

        var (yMin, yMax) = ResolveY(spec, sampled);

        var labels = new List<string>(spec.Series.Count);
        foreach (var series in spec.Series) labels.Add(series.Label ?? series.Source);
        var showLegend =
            spec.Legend == Legend.On ||
            (spec.Legend == Legend.Auto && (spec.Series.Count >= 2 || HasExplicitLabel(spec.Series)));

        var width = spec.Width;
        var height = spec.Height;

        var legendWidth = 0.0;
        if (showLegend)
        {
            var longest = 0.0;
            foreach (var label in labels) longest = Math.Max(longest, TextWidth(label, LegendFont));
            legendWidth = Math.Max(LegendMinimum, Math.Ceiling(longest) + LegendPadding);
            legendWidth = Math.Min(legendWidth, Math.Max(0, width - 2 * Margin - MinPlot));
            if (legendWidth < LegendMinimum / 2) legendWidth = 0;
        }

        var horizontal = FitMargins(width, Margin + (spec.YLabel.Length == 0 ? 0 : AxisLabelRoom), Margin + legendWidth);
        var vertical = FitMargins(height, Margin + (spec.Title.Length == 0 ? 0 : TitleRoom),
            Margin + (spec.XLabel.Length == 0 ? 0 : AxisLabelRoom));
        var left = horizontal.Low;
        var top = vertical.Low;
        var plotW = width - left - horizontal.High;
        var plotH = height - top - vertical.High;

        var xMin = spec.XMin;
        var xMax = spec.XMax;
        // Exactly this association — divide first, then multiply by the plot size.
        // It is what makes the two-decimal coordinates match the other ports.
        double Sx(double x) => left + ((x - xMin) / (xMax - xMin)) * plotW;
        double Sy(double y) => top + ((yMax - y) / (yMax - yMin)) * plotH;

        var xStep = NiceStep(xMax - xMin);
        var yStep = NiceStep(yMax - yMin);
        var xTicks = Ticks(xMin, xMax, xStep);
        var yTicks = Ticks(yMin, yMax, yStep);

        var w = width.ToString(CultureInfo.InvariantCulture);
        var h = height.ToString(CultureInfo.InvariantCulture);
        var out_ = new StringBuilder(20000);
        out_.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ").Append(w).Append(' ').Append(h).Append('"');
        out_.Append(" width=\"").Append(w).Append("\" height=\"").Append(h).Append("\" role=\"img\">");
        // The accessible name. No `id` anywhere in this SVG: a document may hold
        // two plots and a duplicated id is how one figure wears another's clip
        // path. An <svg role="img"> is named by its own <title>.
        out_.Append("<title>").Append(EscapeHtml(AccessibleName(spec, labels))).Append("</title>");

        // Ink is `currentColor` with opacity throughout — never the site's greys.
        // The renderer is theme-blind in every port, so one SVG has to be right in
        // light, dark, print, an exported page and a saved file. No <style> either:
        // SVG <style> inside an HTML document is document-scoped and leaks.
        if (spec.Grid)
        {
            out_.Append("<g stroke=\"currentColor\" stroke-width=\"0.5\" opacity=\"0.15\">");
            foreach (var tick in xTicks)
            {
                var at = Fixed(Sx(tick), 1);
                out_.Append("<line x1=\"").Append(at).Append("\" y1=\"").Append(Fixed(top, 1))
                    .Append("\" x2=\"").Append(at).Append("\" y2=\"").Append(Fixed(top + plotH, 1)).Append("\"/>");
            }
            foreach (var tick in yTicks)
            {
                var at = Fixed(Sy(tick), 1);
                out_.Append("<line x1=\"").Append(Fixed(left, 1)).Append("\" y1=\"").Append(at)
                    .Append("\" x2=\"").Append(Fixed(left + plotW, 1)).Append("\" y2=\"").Append(at).Append("\"/>");
            }
            out_.Append("</g>");
        }

        if (spec.Axes)
        {
            out_.Append("<g stroke=\"currentColor\" stroke-width=\"1\" opacity=\"0.4\">");
            if (yMin <= 0 && yMax >= 0)
            {
                var at = Fixed(Sy(0), 1);
                out_.Append("<line x1=\"").Append(Fixed(left, 1)).Append("\" y1=\"").Append(at)
                    .Append("\" x2=\"").Append(Fixed(left + plotW, 1)).Append("\" y2=\"").Append(at).Append("\"/>");
            }
            if (xMin <= 0 && xMax >= 0)
            {
                var at = Fixed(Sx(0), 1);
                out_.Append("<line x1=\"").Append(at).Append("\" y1=\"").Append(Fixed(top, 1))
                    .Append("\" x2=\"").Append(at).Append("\" y2=\"").Append(Fixed(top + plotH, 1)).Append("\"/>");
            }
            out_.Append("</g>");

            out_.Append("<g font-size=\"10\" fill=\"currentColor\" opacity=\"0.65\" font-family=\"sans-serif\">");
            foreach (var tick in xTicks)
            {
                out_.Append("<text x=\"").Append(Fixed(Sx(tick), 1)).Append("\" y=\"").Append(Fixed(top + plotH + 15, 1))
                    .Append("\" text-anchor=\"middle\">").Append(EscapeHtml(FormatLabel(tick))).Append("</text>");
            }
            foreach (var tick in yTicks)
            {
                out_.Append("<text x=\"").Append(Fixed(left - 5, 1)).Append("\" y=\"").Append(Fixed(Sy(tick), 1))
                    .Append("\" text-anchor=\"end\" dominant-baseline=\"middle\">").Append(EscapeHtml(FormatLabel(tick))).Append("</text>");
            }
            out_.Append("</g>");

            out_.Append("<rect x=\"").Append(Fixed(left, 1)).Append("\" y=\"").Append(Fixed(top, 1))
                .Append("\" width=\"").Append(Fixed(plotW, 1)).Append("\" height=\"").Append(Fixed(plotH, 1))
                .Append("\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\" opacity=\"0.25\"/>");
        }

        if (spec.Title.Length != 0)
        {
            // Centred on the plot area, not the canvas: the xlabel below is, and a
            // legend gutter would otherwise push the two out of line.
            out_.Append("<text x=\"").Append(Fixed(left + plotW / 2, 1)).Append("\" y=\"").Append(Fixed(top - 14, 1))
                .Append("\" text-anchor=\"middle\" font-size=\"13\" font-weight=\"600\" fill=\"currentColor\" opacity=\"0.85\" ")
                .Append("font-family=\"sans-serif\">").Append(EscapeHtml(spec.Title)).Append("</text>");
        }
        if (spec.XLabel.Length != 0)
        {
            out_.Append("<text x=\"").Append(Fixed(left + plotW / 2, 1)).Append("\" y=\"").Append(Fixed(height - 8, 1))
                .Append("\" text-anchor=\"middle\" font-size=\"11\" fill=\"currentColor\" opacity=\"0.85\" font-family=\"sans-serif\">")
                .Append(EscapeHtml(spec.XLabel)).Append("</text>");
        }
        if (spec.YLabel.Length != 0)
        {
            var x = Fixed(14, 1);
            var y = Fixed(top + plotH / 2, 1);
            out_.Append("<text x=\"").Append(x).Append("\" y=\"").Append(y)
                .Append("\" text-anchor=\"middle\" transform=\"rotate(-90 ").Append(x).Append(' ').Append(y).Append(")\" ")
                .Append("font-size=\"11\" fill=\"currentColor\" opacity=\"0.85\" font-family=\"sans-serif\">")
                .Append(EscapeHtml(spec.YLabel)).Append("</text>");
        }

        for (var index = 0; index < spec.Series.Count; index++)
        {
            Polylines(out_, sampled[index], spec.Series[index], Colour(index), Sx, Sy, xMin, xMax, yMin, yMax);
        }

        if (legendWidth > 0)
        {
            var x = width - horizontal.High + 8;
            out_.Append("<g font-size=\"11\" font-family=\"sans-serif\">");
            for (var index = 0; index < labels.Count; index++)
            {
                var y = top + 12 + index * 16.0;
                out_.Append("<line x1=\"").Append(Fixed(x, 1)).Append("\" y1=\"").Append(Fixed(y - 4, 1))
                    .Append("\" x2=\"").Append(Fixed(x + 14, 1)).Append("\" y2=\"").Append(Fixed(y - 4, 1))
                    .Append("\" stroke=\"").Append(Colour(index)).Append("\" stroke-width=\"2\"/>");
                out_.Append("<text x=\"").Append(Fixed(x + 20, 1)).Append("\" y=\"").Append(Fixed(y, 1))
                    .Append("\" fill=\"currentColor\" opacity=\"0.85\">").Append(EscapeHtml(labels[index])).Append("</text>");
            }
            out_.Append("</g>");
        }

        out_.Append("</svg>");
        return out_.ToString();
    }

    /// <summary>
    /// The polylines of one series. A point is drawable when it is finite and
    /// inside the window (tested on the raw values, inclusive); anything else ends
    /// the run and the next drawable point starts a new one — which is what makes
    /// <c>tan(x)</c> seven branches. A run of one point is still emitted.
    /// </summary>
    private static void Polylines(StringBuilder out_, List<Sample> samples, Series series, string stroke,
        Func<double, double> sx, Func<double, double> sy, double xMin, double xMax, double yMin, double yMax)
    {
        var run = new StringBuilder();
        var started = false;
        var marks = new List<string>();
        foreach (var point in samples)
        {
            var drawable =
                IsFiniteNumber(point.X) &&
                IsFiniteNumber(point.Y) &&
                point.X >= xMin &&
                point.X <= xMax &&
                point.Y >= yMin &&
                point.Y <= yMax;
            if (drawable)
            {
                var px = Fixed(sx(point.X), 2);
                var py = Fixed(sy(point.Y), 2);
                if (started) run.Append(' ');
                run.Append(px).Append(',').Append(py);
                started = true;
                if (series.Kind == SeriesKind.Points)
                {
                    marks.Add("<circle cx=\"" + px + "\" cy=\"" + py + "\" r=\"2.5\" fill=\"" + stroke + "\"/>");
                }
            }
            else if (started)
            {
                out_.Append("<polyline points=\"").Append(run).Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\"/>");
                run.Clear();
                started = false;
            }
        }
        if (run.Length != 0)
        {
            out_.Append("<polyline points=\"").Append(run).Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\"/>");
        }
        foreach (var mark in marks) out_.Append(mark);
    }

    /// <summary>
    /// The samples of one series: <c>x_i = xMin + (i / samples) * (xMax - xMin)</c>
    /// for i in 0…samples inclusive — that expression and not the cheaper
    /// <c>xMin + i * dx</c>, which differs in the last bits for 221 of the 1001
    /// default samples and does change the output.
    /// </summary>
    private static List<Sample> SampleSeries(Series series, Spec spec)
    {
        var out_ = new List<Sample>(spec.Samples + 1);
        if (series.Kind == SeriesKind.Points)
        {
            foreach (var point in series.Points) out_.Add(new Sample(point.X, point.Y));
            return out_;
        }
        if (series.Kind == SeriesKind.Parametric)
        {
            var tSpan = series.TMax - series.TMin;
            for (var i = 0; i <= spec.Samples; i++)
            {
                var t = series.TMin + ((double)i / spec.Samples) * tSpan;
                out_.Add(new Sample(Evaluate(series.Expression!, series.Parameter, t),
                    Evaluate(series.YExpression!, series.Parameter, t)));
            }
            return out_;
        }
        var span = spec.XMax - spec.XMin;
        for (var i = 0; i <= spec.Samples; i++)
        {
            var x = spec.XMin + ((double)i / spec.Samples) * span;
            out_.Add(new Sample(x, Evaluate(series.Expression!, "x", x)));
        }
        return out_;
    }

    /// <summary>
    /// <c>y: auto</c> — fit the finite samples, then pad 5 % on each side. Nothing
    /// finite at all falls back to −1…1; a flat series is padded by 5 % of its
    /// own value, or by 1 when that is zero too.
    /// </summary>
    private static (double Min, double Max) ResolveY(Spec spec, List<List<Sample>> sampled)
    {
        if (spec.YMin is double fixedLow && spec.YMax is double fixedHigh) return (fixedLow, fixedHigh);
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;
        foreach (var samples in sampled)
        {
            foreach (var point in samples)
            {
                if (!IsFiniteNumber(point.Y)) continue;
                if (point.Y < low) low = point.Y;
                if (point.Y > high) high = point.Y;
            }
        }
        if (!IsFiniteNumber(low) || !IsFiniteNumber(high)) return (-1, 1);
        var span = high - low;
        if (span > 0) return Padded(low - span * 0.05, high + span * 0.05, low, high);
        var padding = Math.Abs(high) * 0.05;
        var pad = padding > 0 ? padding : 1;
        return Padded(low - pad, high + pad, low, high);
    }

    /// <summary>
    /// The padded range, or the unpadded one when padding overflowed to infinity
    /// (<c>high - low</c> overflows for a series straddling ~1e308) — otherwise
    /// every <c>sy()</c> is NaN and the curve silently vanishes.
    /// </summary>
    private static (double Min, double Max) Padded(double min, double max, double low, double high)
    {
        if (IsFiniteNumber(min) && IsFiniteNumber(max) && max > min) return Bounded(min, max);
        if (high > low) return Bounded(low, high);
        return Bounded(low - 1, high + 1);
    }

    /// <summary>
    /// A range whose *width* is finite, not merely whose ends are. Only an
    /// overflowing width needs help; [1.69e308, 1.71e308] is left exactly as asked.
    /// </summary>
    private static (double Min, double Max) Bounded(double min, double max)
    {
        if (!(max > min)) return (-1, 1);
        if (IsFiniteNumber(max - min)) return (min, max);
        return (min / 2, max / 2);
    }

    private static string AccessibleName(Spec spec, List<string> labels)
    {
        if (spec.Title.Length != 0) return spec.Title;
        if (labels.Count == 0) return "Plot";
        return "Plot of " + string.Join(", ", labels);
    }

    private static bool HasExplicitLabel(IReadOnlyList<Series> series)
    {
        foreach (var one in series) if (one.Label is not null) return true;
        return false;
    }

    private static string Colour(int index) => Palette[index % Palette.Length];

    /// <summary>
    /// An estimate of a string's width at <paramref name="size"/> pixels, for the
    /// legend gutter only: 0.6 em per code point, no font metrics.
    /// </summary>
    private static double TextWidth(string text, double size) => CountCharacters(text) * size * 0.6;

    /// <summary>
    /// Two margins that leave at least <see cref="MinPlot"/> between them; without
    /// this a 160 × 120 figure with a title, both labels and a legend draws inside out.
    /// </summary>
    private static (double Low, double High) FitMargins(double total, double low, double high)
    {
        if (total - low - high >= MinPlot) return (low, high);
        var room = Math.Max(0, total - MinPlot);
        var sum = low + high;
        if (sum <= 0) return (0, 0);
        var scaled = Math.Floor((room * low) / sum);
        return (scaled, room - scaled);
    }

    // MARK: - Axes

    /// <summary>
    /// A decade — 10 raised to <paramref name="exponent"/> — read from a decimal
    /// literal, never from <c>pow</c>.
    /// </summary>
    /// <remarks>
    /// <c>pow(10, e)</c> is not correctly rounded and differs by runtime version:
    /// for rough = 9.999999999999999e-05 it is one ULP low under Node 20 and
    /// OpenJDK 21, which moved a tick step and turned md.vscode's CI red. Parsing a
    /// decimal literal is specified to be correctly rounded in every port's
    /// language. The three non-finite answers are what <c>pow</c> gave (the oracle
    /// records them), and the guards are also what keeps the <c>int</c> cast
    /// defined — <c>(int)double.NaN</c> is not.
    /// </remarks>
    public static double Decade(double exponent)
    {
        if (double.IsNaN(exponent)) return double.NaN;
        if (exponent >= 309) return double.PositiveInfinity;   // 1e309 overflows a double
        if (exponent <= -324) return 0;                         // 1e-324 underflows to zero
        return double.Parse("1e" + ((int)exponent).ToString(CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The site's tick spacing, ported exactly — except that the decade comes from
    /// <see cref="Decade"/> and the exponent is pinned by exact comparison rather
    /// than trusted from <c>log10</c>, which is not correctly rounded either
    /// (<c>floor(log10(9.999999999999999e-05))</c> is −4 here, one decade high).
    /// </summary>
    public static double NiceStep(double range) => NiceStep(range, Math.Log10);

    /// <summary>
    /// <see cref="NiceStep(double)"/> with an injectable <c>log10</c> — the tests
    /// perturb it by ±1 decade and demand every oracle row unchanged, which is the
    /// family's guard that the pin, not the platform, decides the step.
    /// </summary>
    internal static double NiceStep(double range, Func<double, double> log10)
    {
        var rough = range / 8;
        var exponent = Math.Floor(log10(rough));
        if (Decade(exponent) > rough) exponent -= 1;
        else if (Decade(exponent + 1) <= rough) exponent += 1;
        var magnitude = Decade(exponent);
        var normalised = rough / magnitude;
        var step = 10.0;
        if (normalised <= 1.5) step = 1;
        else if (normalised <= 3) step = 2;
        else if (normalised <= 7) step = 5;
        return step * magnitude;
    }

    /// <summary>The pinned decade of <paramref name="rough"/> alone — Kotlin's <c>decadeOf</c>, for the tests' witness.</summary>
    internal static double DecadeOf(double rough)
    {
        var exponent = Math.Floor(Math.Log10(rough));
        if (Decade(exponent) > rough) exponent -= 1;
        else if (Decade(exponent + 1) <= rough) exponent += 1;
        return Decade(exponent);
    }

    /// <summary>How far past <c>max</c> a tick may land and still be drawn: one part in 10⁹ of a step.</summary>
    private const double TickEpsilon = 1e-9;
    /// <summary>A safety valve. <see cref="NiceStep(double)"/> gives eight to eleven ticks; a thousand is a bug.</summary>
    private const int MaxTicks = 1000;

    /// <summary>
    /// The ticks of one axis, computed by index, never accumulated: <c>gx += step</c>
    /// reaches −5.55e−17 at the origin and prints "-5.6e-17". The epsilon keeps a
    /// last tick that lands one ULP over the maximum.
    /// </summary>
    public static IReadOnlyList<double> Ticks(double min, double max, double step)
    {
        var out_ = new List<double>();
        if (!(step > 0) || !IsFiniteNumber(step)) return out_;
        var first = Math.Ceiling(min / step) * step;
        var limit = max + step * TickEpsilon;
        for (var i = 0; i < MaxTicks; i++)
        {
            var tick = first + i * step;
            if (tick > limit) break;
            out_.Add(tick);
        }
        return out_;
    }

    /// <summary>
    /// A tick's label — the site's <c>format_label</c> with its integer branch
    /// ROUNDED rather than truncated (a tick at −4 arriving as −3.9999999999999996
    /// printed "-3" there).
    /// </summary>
    public static string FormatLabel(double v)
    {
        if (v == 0) return "0";
        if (double.IsNaN(v)) return FormatFixed(v, 2);
        var magnitude = Math.Abs(v);
        if (magnitude >= 1000 || magnitude < 0.01) return FormatExponential(v, 1);
        var rounded = RoundTiesAway(v);
        if (Math.Abs(v - rounded) < 1e-9) return FormatFixed(rounded, 0);
        return FormatFixed(v, 2);
    }

    // MARK: - Number formatting, by hand

    /// <summary>
    /// Significant digits taken from the exact binary value. Twenty-five decides
    /// every rounding this file performs: a double is m/2^k and a decimal tie at
    /// the second digit is k/10^m, and two such numbers that differ do so by at
    /// least ~10⁻¹⁸ relatively — far above the 10⁻²⁵ the last digit resolves. So a
    /// digit string reading "…5000…0" here *is* an exact tie.
    /// </summary>
    private const int SignificantDigitCount = 25;

    /// <summary>
    /// The first 25 significant decimal digits of <paramref name="value"/> (&gt; 0)
    /// and the decimal exponent of the first one.
    /// </summary>
    /// <remarks>
    /// Exact by construction, the way Kotlin's <c>BigDecimal(v)</c> is: the double
    /// is m × 2^k, so for k &lt; 0 its digits are those of m × 5^−k with −k decimal
    /// places. Not <c>ToString("E24")</c> — correct on .NET Core 3.0+, but a
    /// formatter that pads zeros past the 17th digit (Java's, old .NET's) is
    /// exactly the fake-tie bug this file exists to avoid, and the arithmetic is
    /// one multiply. A tie at the 25th digit rounds up, as Kotlin's HALF_UP does
    /// (Swift's printf rounds it to even); the argument above is why no decision
    /// this file makes can see the difference.
    /// </remarks>
    private static (string Digits, int Exponent) SignificantDigits(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var biased = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        int k;
        if (biased == 0)
        {
            k = -1074;                       // subnormal: no implicit bit
        }
        else
        {
            mantissa |= 1L << 52;
            k = biased - 1075;
        }
        // Shed trailing zero bits: it keeps 5^n small for ordinary values.
        while (k < 0 && (mantissa & 1) == 0)
        {
            mantissa >>= 1;
            k += 1;
        }
        string digits;
        int exponent;
        if (k >= 0)
        {
            digits = (new BigInteger(mantissa) << k).ToString(CultureInfo.InvariantCulture);
            exponent = digits.Length - 1;
        }
        else
        {
            var scale = -k;
            digits = (new BigInteger(mantissa) * PowerOfFive(scale)).ToString(CultureInfo.InvariantCulture);
            exponent = digits.Length - 1 - scale;
        }
        if (digits.Length < SignificantDigitCount) return (digits + Zeros(SignificantDigitCount - digits.Length), exponent);
        if (digits.Length == SignificantDigitCount) return (digits, exponent);
        var kept = digits.Substring(0, SignificantDigitCount);
        if (digits[SignificantDigitCount] < '5') return (kept, exponent);
        var carried = Increment(kept);
        if (!carried.Overflow) return (carried.Digits, exponent);
        return ("1" + Zeros(SignificantDigitCount - 1), exponent + 1);
    }

    /// <summary>
    /// Boxed, deliberately: a <see cref="BigInteger"/> is a 16-byte struct (sign +
    /// array reference), and a reader racing a writer on a struct array element can
    /// see one field of each — a sign of 1 with a null array reads as the integer 1
    /// and a coordinate would be formatted from the wrong digits. A reference is
    /// written atomically and .NET publishes a new object's fields with it, so
    /// the box makes the race harmless. Idempotent to compute, so two writers are.
    /// </summary>
    private static readonly object?[] powersOfFive = new object?[1075];

    private static BigInteger PowerOfFive(int n)
    {
        if (powersOfFive[n] is BigInteger cached) return cached;
        var value = BigInteger.Pow(5, n);
        powersOfFive[n] = value;
        return value;
    }

    /// <summary>
    /// Rust's <c>{:.&lt;n&gt;e}</c> — <c>1.0e3</c>, <c>-5.0e-3</c>, <c>4.9e-324</c>: no
    /// <c>+</c> on the exponent, no zero padding, ties to even on the exact value
    /// (1250 is <c>1.2e3</c>, where .NET's "E1" writes <c>1.2E+003</c>).
    /// </summary>
    public static string FormatExponential(double v, int fractionDigits)
    {
        if (double.IsNaN(v)) return "NaN";
        if (v == double.PositiveInfinity) return "inf";
        if (v == double.NegativeInfinity) return "-inf";
        var sign = IsNegative(v) ? "-" : "";
        var magnitude = Math.Abs(v);
        if (magnitude == 0)
        {
            return sign + "0" + (fractionDigits > 0 ? "." + Zeros(fractionDigits) : "") + "e0";
        }
        var scanned = SignificantDigits(magnitude);
        var rounded = RoundDigits(scanned.Digits, fractionDigits + 1);
        var exponent = scanned.Exponent + (rounded.Overflow ? 1 : 0);
        var digits = rounded.Digits;
        var fraction = fractionDigits > 0 ? "." + digits.Substring(1) : "";
        return sign + digits[0] + fraction + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Rust's <c>{:.&lt;n&gt;}</c> — <c>2.50</c>, <c>-0.00</c>, <c>1000.00</c>: ties to
    /// even on the exact value (0.125 is <c>0.12</c>), and the sign survives a
    /// rounded-away zero.
    /// </summary>
    public static string FormatFixed(double v, int fractionDigits)
    {
        if (double.IsNaN(v)) return "NaN";
        if (v == double.PositiveInfinity) return "inf";
        if (v == double.NegativeInfinity) return "-inf";
        var sign = IsNegative(v) ? "-" : "";
        var magnitude = Math.Abs(v);

        var whole = "0";
        var fraction = "";
        if (magnitude != 0)
        {
            var scanned = SignificantDigits(magnitude);
            var wholeLength = scanned.Exponent + 1;
            if (wholeLength <= 0)
            {
                fraction = Zeros(-wholeLength) + scanned.Digits;
            }
            else if (wholeLength >= scanned.Digits.Length)
            {
                whole = scanned.Digits + Zeros(wholeLength - scanned.Digits.Length);
            }
            else
            {
                whole = scanned.Digits.Substring(0, wholeLength);
                fraction = scanned.Digits.Substring(wholeLength);
            }
        }

        if (fraction.Length <= fractionDigits)
        {
            fraction += Zeros(fractionDigits - fraction.Length);
        }
        else
        {
            var kept = fraction.Substring(0, fractionDigits);
            var next = fraction[fractionDigits] - '0';
            var up = next > 5;
            if (next == 5)
            {
                var more = false;
                for (var index = fractionDigits + 1; index < fraction.Length; index++)
                {
                    if (fraction[index] != '0') { more = true; break; }
                }
                if (more)
                {
                    up = true;
                }
                else
                {
                    var previous = fractionDigits > 0 ? fraction[fractionDigits - 1] - '0' : whole[whole.Length - 1] - '0';
                    up = previous % 2 == 1;
                }
            }
            if (!up)
            {
                fraction = kept;
            }
            else
            {
                var carried = Increment(kept);
                if (carried.Overflow)
                {
                    // `.99` + 1 is `1.00`: the fraction goes back to zeros and the
                    // carry lands on the integer part, the one place a digit string
                    // may grow (999 -> 1000).
                    fraction = Zeros(fractionDigits);
                    whole = Increment(whole).Digits;
                }
                else
                {
                    fraction = carried.Digits;
                }
            }
        }

        return sign + whole + (fractionDigits > 0 ? "." + fraction : "");
    }

    /// <summary><see cref="FormatFixed"/>, for the coordinates the emitter writes.</summary>
    private static string Fixed(double v, int fractionDigits) => FormatFixed(v, fractionDigits);

    /// <summary>
    /// Round a digit string to <paramref name="keep"/> digits, ties to even,
    /// reporting whether the carry ran off the front — then the digits are
    /// <c>1</c> followed by zeros and the caller owes the exponent a 1.
    /// </summary>
    private static (string Digits, bool Overflow) RoundDigits(string digits, int keep)
    {
        if (keep >= digits.Length) return (digits + Zeros(keep - digits.Length), false);
        var kept = digits.Substring(0, keep);
        var next = digits[keep] - '0';
        var up = next > 5;
        if (next == 5)
        {
            var more = false;
            for (var index = keep + 1; index < digits.Length; index++)
            {
                if (digits[index] != '0') { more = true; break; }
            }
            up = more || (digits[keep - 1] - '0') % 2 == 1;
        }
        if (!up) return (kept, false);
        var carried = Increment(kept);
        if (!carried.Overflow) return (carried.Digits, false);
        return ("1" + Zeros(keep - 1), true);
    }

    /// <summary><paramref name="digits"/> + 1, keeping the length; <c>Overflow</c> says the carry ran off the front.</summary>
    private static (string Digits, bool Overflow) Increment(string digits)
    {
        var out_ = digits.ToCharArray();
        var index = out_.Length - 1;
        while (index >= 0)
        {
            if (out_[index] == '9')
            {
                out_[index] = '0';
                index -= 1;
            }
            else
            {
                out_[index] = (char)(out_[index] + 1);
                return (new string(out_), false);
            }
        }
        return ("1" + new string(out_), true);
    }

    /// <summary>
    /// Rust's <c>f64::round</c>: halfway cases go away from zero. Not
    /// <c>floor(v + 0.5)</c> (sends 0.49999999999999994 to 1), not banker's.
    /// </summary>
    private static double RoundTiesAway(double v)
    {
        var whole = Math.Truncate(v);
        var fraction = v - whole;
        if (fraction >= 0.5) return whole + 1;
        if (fraction <= -0.5) return whole - 1;
        return whole;
    }

    private static bool IsNegative(double v) => v < 0 || (v == 0 && 1 / v < 0);

    private static string Zeros(int count) => count > 0 ? new string('0', count) : "";

    // MARK: - Markup

    /// <summary>
    /// Four of the five predefined XML entities — <c>&amp;amp;</c> <c>&amp;lt;</c>
    /// <c>&amp;gt;</c> <c>&amp;quot;</c> — and never anything else. Not
    /// <c>&amp;apos;</c>: a bare <c>'</c> is well-formed in text and in the
    /// double-quoted attributes this renderer writes, and the reference escapes
    /// exactly these four, which the byte-parity golden depends on. Never a named
    /// entity like <c>&amp;nbsp;</c>, undefined in XML — the EPUB body is XHTML.
    /// </summary>
    private static string EscapeHtml(string s)
    {
        // The four are BMP and cannot be halves of a surrogate pair, so a
        // per-code-unit pass is exact.
        var out_ = new StringBuilder(s.Length + 16);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': out_.Append("&amp;"); break;
                case '<': out_.Append("&lt;"); break;
                case '>': out_.Append("&gt;"); break;
                case '"': out_.Append("&quot;"); break;
                default: out_.Append(c); break;
            }
        }
        return out_.ToString();
    }

    // MARK: - Small string helpers
    //
    // Written out rather than reached for: the ports have different ideas of what
    // `trim` and `split` mean, and string.Trim() / char.IsWhiteSpace strip far
    // more than the grammar's space and tab.

    private static bool IsSpace(char c) => c == ' ' || c == '\t';

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static bool IsLowerLetter(char c) => c >= 'a' && c <= 'z';

    private static bool IsIdentifierStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsDigit(c);

    private static bool IsFiniteNumber(double v) => !double.IsNaN(v) && v != double.PositiveInfinity && v != double.NegativeInfinity;

    private static string Trim(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsSpace(text[start])) start += 1;
        while (end > start && IsSpace(text[end - 1])) end -= 1;
        return Slice(text, start, end);
    }

    /// <summary>
    /// The locale-independent lowercase, never the current culture's: a Turkish
    /// locale spells <c>AUTO</c> with a dotless ı and the directive stops matching.
    /// Only ASCII words are ever compared with the result.
    /// </summary>
    private static string Lowercased(string text) => text.ToLowerInvariant();

    /// <summary>Lines, on CRLF, LF or CR — the parser's own set, not the wide Unicode one. Always appends the trailing segment.</summary>
    private static List<string> SplitLines(string source)
    {
        var out_ = new List<string>();
        var current = new StringBuilder();
        var index = 0;
        while (index < source.Length)
        {
            var c = source[index];
            if (c == '\n')
            {
                out_.Add(current.ToString());
                current.Clear();
            }
            else if (c == '\r')
            {
                out_.Add(current.ToString());
                current.Clear();
                if (index + 1 < source.Length && source[index + 1] == '\n') index += 1;
            }
            else
            {
                current.Append(c);
            }
            index += 1;
        }
        out_.Add(current.ToString());
        return out_;
    }

    private static List<string> SplitWhitespace(string text)
    {
        var out_ = new List<string>();
        var current = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (IsSpace(c))
            {
                if (current.Length != 0) out_.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length != 0) out_.Add(current.ToString());
        return out_;
    }

    private static bool Contains(string[] list, string value)
    {
        foreach (var one in list) if (string.Equals(one, value, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>The index of the first <paramref name="first"/><paramref name="second"/> pair, or −1.</summary>
    private static int IndexOfPair(string text, char first, char second)
    {
        var index = 0;
        while (index + 1 < text.Length)
        {
            if (text[index] == first && text[index + 1] == second) return index;
            index += 1;
        }
        return -1;
    }

    /// <summary>The index of the first <paramref name="needle"/>, or −1.</summary>
    private static int IndexOf(string text, char needle)
    {
        for (var index = 0; index < text.Length; index++) if (text[index] == needle) return index;
        return -1;
    }

    /// <summary>The index of the <c>)</c> matching the <c>(</c> at <paramref name="open"/>, or −1.</summary>
    private static int MatchingParenthesis(string text, int open)
    {
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            var c = text[index];
            if (c == '(') depth += 1;
            else if (c == ')')
            {
                depth -= 1;
                if (depth == 0) return index;
            }
        }
        return -1;
    }

    /// <summary>The index of the first comma at parenthesis depth 0, or −1.</summary>
    private static int TopLevelComma(string text)
    {
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (c == '(') depth += 1;
            else if (c == ')') depth -= 1;
            else if (c == ',' && depth == 0) return index;
        }
        return -1;
    }

    /// <summary>Does <paramref name="text"/> begin with <paramref name="word"/> as a whole word?</summary>
    private static bool StartsWithWord(string text, string word)
    {
        if (text.Length < word.Length) return false;
        for (var index = 0; index < word.Length; index++) if (text[index] != word[index]) return false;
        if (text.Length == word.Length) return true;
        return !IsIdentifierPart(text[word.Length]);
    }

    /// <summary>
    /// The characters of a label, for the legend gutter — code points, as the
    /// TypeScript reference counts them (<c>for (const _ of text)</c>) and Swift's
    /// scalars do. <c>string.Length</c> would double-count an emoji and shift the
    /// gutter, changing the figure's bytes. A lone surrogate counts one, as in Java and JS.
    /// </summary>
    private static int CountCharacters(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes()) count += 1;
        return count;
    }

    private static string Slice(string s, int from, int to) => to > from ? s.Substring(from, to - from) : "";
}
