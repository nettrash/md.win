namespace Md.Core.Markdown;

/// <summary>The eleven block kinds. That is the whole model.</summary>
/// <remarks>
/// There is no image, math, CSV, HTML or link-reference-definition kind: math, CSV/TSV and the
/// diagram languages are decided by the <i>renderer</i> from a code block's language, or by the
/// inline pass. The list is flat except for <see cref="Quote"/>, the only recursive kind.
/// </remarks>
public enum BlockKind
{
    Heading,
    Paragraph,
    List,
    CodeBlock,
    Quote,
    Table,
    ThematicBreak,
    PageBreak,
    Note,
    FrontMatter,
    FootnoteDefinition,
}

/// <summary>Column alignment of a GFM table, as written in the delimiter row. <c>:---</c> and <c>---</c> are both <see cref="Leading"/>.</summary>
public enum ColumnAlignment
{
    Leading,
    Center,
    Trailing,
}

/// <summary>
/// One <c>key: value</c> line of a document's front matter. Order is the order written and
/// duplicate keys are kept — a record of what the author wrote, not a dictionary; consumers take
/// the first occurrence.
/// </summary>
public sealed record MetadataField(string Key, string Value);

/// <summary>
/// A single list row. <see cref="Level"/> is a depth tag (<c>indent / 2</c>), not a tree, so the
/// renderer insets without reconstructing nested lists. <see cref="Ordinal"/> is null for bullets;
/// <see cref="Task"/> is null for a plain item, false for <c>[ ]</c>, true for <c>[x]</c> / <c>[X]</c>.
/// </summary>
public sealed record ListItem(string Text, int Level, int? Ordinal, bool? Task);

/// <summary>One table-of-contents entry. <see cref="Line"/> is 0-based; for a setext heading it is the text line, not the underline. <see cref="Slug"/> is the id the HTML writer gives the same heading.</summary>
public sealed record OutlineEntry(int Level, string Text, string Slug, int Line);

/// <summary>One private author note (<c>&lt;!-- note: … --&gt;</c>). <see cref="Line"/> is the 0-based line the comment opens on.</summary>
public sealed record NoteEntry(string Text, int Line);

/// <summary>
/// A top-level block paired with the 0-based source line it started on — what an editor needs for
/// scroll sync. Kept beside the block rather than inside it so <see cref="MarkdownBlock"/> stays
/// identical to the Swift model every export path depends on. Blocks nested in a quote carry no
/// line: the recursion re-parses stripped text whose lines do not address the outer document.
/// </summary>
public sealed record PlacedBlock(MarkdownBlock Block, int Line);

/// <summary>
/// One parsed block. Blocks carry no id and no source range (the Swift preview keys on index);
/// value equality is structural, list members included, so two parses of one document compare
/// equal — the <c>parse == parseWithLines.map(block)</c> invariant is asserted that way.
/// </summary>
public abstract record MarkdownBlock
{
    private MarkdownBlock() { }

    public abstract BlockKind Kind { get; }

    /// <summary>ATX or setext heading; <see cref="Text"/> is trimmed and has any closing <c>#</c> run stripped.</summary>
    public sealed record Heading(int Level, string Text) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.Heading;
    }

    /// <summary>Raw lines joined by <c>"\n"</c>. Not trimmed: leading and trailing spaces survive.</summary>
    public sealed record Paragraph(string Text) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.Paragraph;
    }

    /// <summary>One flag for the whole run: <c>- a\n1. b</c> is ordered with ordinals <c>[null, 1]</c>.</summary>
    public sealed record List(bool Ordered, IReadOnlyList<ListItem> Items) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.List;

        public bool Equals(List? other) =>
            other is not null && Ordered == other.Ordered && Sequences.Equal(Items, other.Items);

        public override int GetHashCode() => HashCode.Combine(Ordered, Sequences.Hash(Items));
    }

    /// <summary><see cref="Language"/> is the first U+0020-delimited word of the info string, null when empty. Never lowercased here — the renderer folds case at dispatch.</summary>
    public sealed record CodeBlock(string? Language, string Code) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.CodeBlock;
    }

    /// <summary>The only recursive kind.</summary>
    public sealed record Quote(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.Quote;

        public bool Equals(Quote? other) => other is not null && Sequences.Equal(Blocks, other.Blocks);

        public override int GetHashCode() => Sequences.Hash(Blocks);
    }

    /// <summary>Header cells, one alignment per header cell, and body rows padded or truncated to the header width.</summary>
    public sealed record Table(
        IReadOnlyList<string> Header,
        IReadOnlyList<ColumnAlignment> Alignments,
        IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.Table;

        public bool Equals(Table? other) =>
            other is not null
            && Sequences.Equal(Header, other.Header)
            && Sequences.Equal(Alignments, other.Alignments)
            && Sequences.EqualRows(Rows, other.Rows);

        public override int GetHashCode() =>
            HashCode.Combine(Sequences.Hash(Header), Sequences.Hash(Alignments), Sequences.HashRows(Rows));
    }

    public sealed record ThematicBreak : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.ThematicBreak;
    }

    /// <summary>A line holding exactly <c>\newpage</c> or <c>\pagebreak</c>.</summary>
    public sealed record PageBreak : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.PageBreak;
    }

    /// <summary>A <c>&lt;!-- note: … --&gt;</c> author note; never rendered.</summary>
    public sealed record Note(string Text) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.Note;
    }

    /// <summary>Only ever the first block, only at quote depth 0.</summary>
    public sealed record FrontMatter(IReadOnlyList<MetadataField> Fields) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.FrontMatter;

        public bool Equals(FrontMatter? other) => other is not null && Sequences.Equal(Fields, other.Fields);

        public override int GetHashCode() => Sequences.Hash(Fields);
    }

    /// <summary><c>[^id]: text</c>, with wrapped continuation lines absorbed. <see cref="Id"/> is ASCII <c>[A-Za-z0-9_-]</c> only.</summary>
    public sealed record FootnoteDefinition(string Id, string Text) : MarkdownBlock
    {
        public override BlockKind Kind => BlockKind.FootnoteDefinition;
    }
}

/// <summary>Structural equality for the list-bearing records; records compare list members by reference otherwise.</summary>
internal static class Sequences
{
    public static bool Equal<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
        }
        return true;
    }

    public static bool EqualRows(IReadOnlyList<IReadOnlyList<string>> a, IReadOnlyList<IReadOnlyList<string>> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!Equal(a[i], b[i])) return false;
        }
        return true;
    }

    public static int Hash<T>(IReadOnlyList<T> a)
    {
        var hash = new HashCode();
        hash.Add(a.Count);
        foreach (var item in a) hash.Add(item);
        return hash.ToHashCode();
    }

    public static int HashRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var hash = new HashCode();
        hash.Add(rows.Count);
        foreach (var row in rows) hash.Add(Hash(row));
        return hash.ToHashCode();
    }
}
