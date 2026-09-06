using Md.Core.Markdown;

namespace Md.Core.Tests;

/// <summary>
/// The CSV / TSV table builder shared by the HTML and LaTeX writers. What counts as a number
/// decides column alignment, and it must mean the same thing on every platform — which is why
/// nothing here goes through a standard-library parser.
/// </summary>
public class DelimitedTableTests
{
    private static void AssertRows(string[][] expected, IReadOnlyList<IReadOnlyList<string>> actual) =>
        Assert.Equal<IEnumerable<string>>(expected, actual);

    [Fact]
    public void DecimalNumberGrammarIsExplicitNotDoubleInit()
    {
        // Swift's Double takes hex and any casing of inf/nan, Java's parseDouble takes neither,
        // .NET's double.TryParse takes thousands separators and culture decimals.
        foreach (var number in new[] { "0", "-1", "+2", "3.5", ".5", "5.", "1e9", "-2.5E-3", "007" })
        {
            Assert.True(DelimitedTable.IsDecimalNumber(number), number);
        }
        foreach (var other in new[]
        {
            "0x10", "inf", "Inf", "INF", "nan", "NaN", "1,000", "1 000", "", "-", ".", "e5", "1e", "1e+",
            "12abc", "1.2.3", "٣", "½", " 1", "1 ", "+", "1e5.0", "1E-", "Infinity", "1_000",
        })
        {
            Assert.False(DelimitedTable.IsDecimalNumber(other), other);
        }
    }

    [Fact]
    public void DelimitedParsingFollowsRFC4180()
    {
        AssertRows([
            ["Name", "Note"],
            ["Doe, Jane", "She said \"hi\""],   // quoted separator, doubled quote
            ["Ann", ""],                          // an empty trailing field
        ], DelimitedTable.ParseDelimited("Name,Note\n\"Doe, Jane\",\"She said \"\"hi\"\"\"\nAnn,\n", ','));

        // A quoted field may hold a line break…
        AssertRows([["a", "one\ntwo"]], DelimitedTable.ParseDelimited("a,\"one\ntwo\"\n", ','));
        // …and a quote that is not at the start of a field is just a character.
        AssertRows([["5\" pipe", "x"]], DelimitedTable.ParseDelimited("5\" pipe,x", ','));
        // After a quoted run closes, what follows is appended to the same field.
        AssertRows([["ab", "c"]], DelimitedTable.ParseDelimited("\"a\"b,c", ','));
        // A last row with no trailing newline is still a row.
        AssertRows([["a", "b"]], DelimitedTable.ParseDelimited("a,b", ','));
        Assert.Empty(DelimitedTable.ParseDelimited("", ','));
        // An empty quoted field with nothing else flushes no row: the field is empty and the row is.
        Assert.Empty(DelimitedTable.ParseDelimited("\"\"", ','));
        AssertRows([["", ""]], DelimitedTable.ParseDelimited(",", ','));
        // An unterminated quoted field runs to the end and is still flushed.
        AssertRows([["a", "b\nc"]], DelimitedTable.ParseDelimited("a,\"b\nc", ','));
    }

    [Fact]
    public void DelimitedParsingHandlesWindowsLineEndings()
    {
        // Spreadsheet exports are exactly where CRLF comes from.
        AssertRows([["a", "b"], ["c", "d"]], DelimitedTable.ParseDelimited("a,b\r\nc,d\r\n", ','));
        AssertRows([["a", "b"], ["c", "d"]], DelimitedTable.ParseDelimited("a,b\rc,d", ','));
    }

    [Fact]
    public void DelimitedParsingUsesTheGivenSeparator()
    {
        AssertRows([["City", "People"], ["Oslo", "709037"]], DelimitedTable.ParseDelimited("City\tPeople\nOslo\t709037\n", '\t'));
        // A comma is content in a TSV.
        AssertRows([["a,b", "c"]], DelimitedTable.ParseDelimited("a,b\tc", '\t'));
    }

    [Fact]
    public void DelimitedParsingWalksCodeUnits()
    {
        // The Swift walks graphemes here, so a separator carrying a combining mark did not split
        // there; Kotlin and TypeScript walk units and do, and so does this port. Recorded.
        AssertRows([["a", "\u0301b"]], DelimitedTable.ParseDelimited("a,\u0301b", ','));
        // A surrogate pair is never split by a BMP separator.
        AssertRows([["\U0001F600", "x"]], DelimitedTable.ParseDelimited("\U0001F600,x", ','));
    }

    [Fact]
    public void DelimitedTableSharesOneBuilder()
    {
        var table = DelimitedTable.From("City,People\nOslo,709037", ',');
        Assert.NotNull(table);
        Assert.Equal(["City", "People"], table.Header);
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Trailing], table.Alignments);
        AssertRows([["Oslo", "709037"]], table.Rows);
        Assert.Null(DelimitedTable.From("", ','));
        // Record equality is structural, so the same data compares equal.
        Assert.Equal(DelimitedTable.From("a,b\n1,2", ','), DelimitedTable.From("a,b\r\n1,2\r\n", ','));
    }

    [Fact]
    public void NumericColumnsAreRightAlignedFromTheBodyOnly()
    {
        // The header never votes: a numeric header over prose stays leading and a prose header
        // over numbers goes trailing.
        Assert.Equal([ColumnAlignment.Trailing, ColumnAlignment.Trailing], DelimitedTable.From("1,2\n3,4", ',')!.Alignments);
        Assert.Equal([ColumnAlignment.Leading], DelimitedTable.From("1\nx", ',')!.Alignments);
        // A column with any non-numeric cell stays left-aligned.
        Assert.Equal([ColumnAlignment.Leading], DelimitedTable.From("A\n1\nn/a", ',')!.Alignments);
        // Empty cells are ignored, not disqualifying.
        Assert.Equal([ColumnAlignment.Trailing], DelimitedTable.From("A\n1\n\n2", ',')!.Alignments);
        // A column with no filled cell at all is leading, header only included.
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Leading], DelimitedTable.From("A,B", ',')!.Alignments);
        Assert.Equal([ColumnAlignment.Leading], DelimitedTable.From("A\n\n", ',')!.Alignments);
        // Hex stays left, on every platform alike.
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Leading], DelimitedTable.From("Item,Value\na,0x10", ',')!.Alignments);
    }

    [Fact]
    public void AlignmentCellIsTrimmedWithAsciiSpaceAndTabOnly()
    {
        // An invisible U+200B is not padding: Foundation's `.whitespaces` strips it and a
        // Zs-based trim does not, so the alignment scan trims ASCII only and every platform agrees
        // the cell is not a number. The opposite decision from the parser's blank-line rule.
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Leading], DelimitedTable.From("Item,Value\na,\u200B1\u200B", ',')!.Alignments);
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Trailing], DelimitedTable.From("Item,Value\na, 1 ", ',')!.Alignments);
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Trailing], DelimitedTable.From("Item,Value\na,\t1\t", ',')!.Alignments);
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Leading], DelimitedTable.From("Item,Value\na,\u00A01", ',')!.Alignments);
        // The cells themselves are handed on untrimmed.
        AssertRows([["a", " 1 "]], DelimitedTable.From("Item,Value\na, 1 ", ',')!.Rows);
    }

    [Fact]
    public void AlignmentsMayOutnumberTheHeader()
    {
        // A wider body row votes for columns the header has not got; the renderer pads.
        var table = DelimitedTable.From("A\n1,2", ',');
        Assert.NotNull(table);
        Assert.Equal(["A"], table.Header);
        Assert.Equal([ColumnAlignment.Trailing, ColumnAlignment.Trailing], table.Alignments);
        AssertRows([["1", "2"]], table.Rows);
    }

    [Fact]
    public void TsvUsesTabs()
    {
        var table = DelimitedTable.From("City\tPeople\nOslo\t709037", '\t');
        Assert.NotNull(table);
        Assert.Equal(["City", "People"], table.Header);
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Trailing], table.Alignments);
    }

    [Fact]
    public void EmptyCsvIsNoTable()
    {
        // The renderer falls back to a bare code block so nothing the author wrote disappears.
        Assert.Null(DelimitedTable.From("", ','));
        Assert.Null(DelimitedTable.From("\"\"", ','));
        // A single newline is one row of one empty field — a table with an empty header cell.
        var blank = DelimitedTable.From("\n", ',');
        Assert.NotNull(blank);
        Assert.Equal([""], blank.Header);
        Assert.Empty(blank.Rows);
    }
}
