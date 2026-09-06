using Md.Core.Text;

namespace Md.Core.Markdown;

/// <summary>
/// The table a <c>```csv</c> / <c>```tsv</c> block describes — header row, inferred alignments,
/// body rows — shared by the HTML and LaTeX writers so one spreadsheet cannot be right-aligned
/// in the PDF and left-aligned in the <c>.tex</c>.
/// </summary>
/// <remarks>
/// The parser knows nothing about CSV: the block reaches the renderer as
/// <c>CodeBlock("csv", …)</c>, the renderer dispatches on the lowercased language (<c>csv</c> →
/// <c>','</c>, <c>tsv</c> → <c>'\t'</c>) and falls back to a bare <c>&lt;pre&gt;&lt;code&gt;</c>
/// when <see cref="From"/> returns null, so nothing the author wrote disappears.
/// </remarks>
public sealed record DelimitedTable(
    IReadOnlyList<string> Header,
    IReadOnlyList<ColumnAlignment> Alignments,
    IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>
    /// Build the table, or null when <paramref name="code"/> parses to no rows. Alignment is
    /// inferred from the <b>body only</b> — the header never votes — and a column whose every
    /// filled cell is a number is <see cref="ColumnAlignment.Trailing"/>. Empty cells are ignored,
    /// not disqualifying; a column with no filled cell is leading. The alignment list may be
    /// longer than the header when a body row is wider.
    /// </summary>
    public static DelimitedTable? From(string code, char separator)
    {
        var rows = ParseDelimited(code, separator);
        if (rows.Count == 0 || rows[0].Count == 0) return null;
        var header = rows[0];
        var body = new List<IReadOnlyList<string>>(rows.Count - 1);
        for (var i = 1; i < rows.Count; i++) body.Add(rows[i]);

        var columns = header.Count;
        foreach (var row in body) columns = Math.Max(columns, row.Count);

        var alignments = new List<ColumnAlignment>(columns);
        for (var column = 0; column < columns; column++)
        {
            var filled = 0;
            var numeric = true;
            foreach (var row in body)
            {
                if (column >= row.Count) continue;
                // SPTAB on purpose: Foundation's `.whitespaces` holds U+200B and Java's Zs trim does
                // not, so `U+200B 1 U+200B` must not be a number on any platform. The opposite call
                // from the parser's blank-line rule; do not unify the two.
                var cell = Whitespace.TrimSpaceTab(row[column]);
                if (cell.Length == 0) continue;
                filled++;
                if (!IsDecimalNumber(cell)) numeric = false;
            }
            alignments.Add(filled > 0 && numeric ? ColumnAlignment.Trailing : ColumnAlignment.Leading);
        }
        return new DelimitedTable(header, alignments, body);
    }

    /// <summary>
    /// <c>[+-]? digits* ( '.' digits* )? ( [eE] [+-]? digits+ )?</c> with at least one digit before
    /// the exponent, ASCII digits only, the whole string consumed.
    /// </summary>
    /// <remarks>
    /// Never <c>double.TryParse</c>: it takes <c>Infinity</c>, <c>NaN</c>, thousands separators and
    /// culture decimals, as Swift's <c>Double(_:)</c> takes hex and Java's takes <c>f</c>/<c>d</c>
    /// suffixes — three standard libraries aligning the same spreadsheet three ways. Never
    /// <c>char.IsDigit</c> either: it is Unicode <c>Nd</c> and accepts <c>٣</c>.
    /// </remarks>
    public static bool IsDecimalNumber(string cell)
    {
        var i = 0;
        int TakeDigits()
        {
            var count = 0;
            while (i < cell.Length && cell[i] >= '0' && cell[i] <= '9')
            {
                count++;
                i++;
            }
            return count;
        }

        if (i < cell.Length && (cell[i] == '+' || cell[i] == '-')) i++;
        var digits = TakeDigits();
        if (i < cell.Length && cell[i] == '.')
        {
            i++;
            digits += TakeDigits();
        }
        if (digits == 0) return false;

        if (i < cell.Length && (cell[i] == 'e' || cell[i] == 'E'))
        {
            i++;
            if (i < cell.Length && (cell[i] == '+' || cell[i] == '-')) i++;
            if (TakeDigits() == 0) return false;
        }
        return i == cell.Length;
    }

    /// <summary>
    /// RFC 4180: a field may be quoted, a quoted field may hold the separator and line breaks, a
    /// doubled quote inside one is a literal quote. A quote only <i>opens</i> a quoted field while
    /// the field is still empty, so <c>5" pipe</c> is literal. A last row without a trailing
    /// newline is still a row; a trailing empty field is kept.
    /// </summary>
    /// <remarks>
    /// Line endings are normalised first (CRLF, then lone CR) rather than matched — spreadsheet
    /// exports are where CRLF comes from. Walks UTF-16 units like Kotlin and TypeScript: a
    /// separator carrying a combining mark <i>does</i> split here, where the Swift's grapheme walk
    /// did not. Recorded divergence; the UTF-16 behaviour is the contract for this port.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<string>> ParseDelimited(string text, char separator)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        var i = 0;

        while (i < normalized.Length)
        {
            var c = normalized[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < normalized.Length && normalized[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
                i++;
                continue;
            }
            if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else
            {
                field.Append(c);
            }
            i++;
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    public bool Equals(DelimitedTable? other) =>
        other is not null
        && Sequences.Equal(Header, other.Header)
        && Sequences.Equal(Alignments, other.Alignments)
        && Sequences.EqualRows(Rows, other.Rows);

    public override int GetHashCode() =>
        HashCode.Combine(Sequences.Hash(Header), Sequences.Hash(Alignments), Sequences.HashRows(Rows));
}
