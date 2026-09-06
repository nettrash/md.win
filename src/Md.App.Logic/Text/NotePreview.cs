using System.Globalization;
using Md.Core.Text;

namespace Md.App.Logic.Text;

/// <summary>
/// The one-line label a private author note gets in the Go ▸ Notes menu and in the Book window's
/// Notes list (shell-design.md §5.6; macOS §6.9). Port of the Swift <c>notePreview(_:)</c>, which
/// neither Android nor md.vscode has — so this is the only implementation to compare against, and
/// every step of it is the Swift's.
/// </summary>
public static class NotePreview
{
    /// <summary>Swift's <c>collapsed.count > 50</c> then <c>prefix(50)</c> — grapheme clusters, not UTF-16 units.</summary>
    public const int MaxTextElements = 50;

    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var collapsed = Collapse(FirstNonEmptyLine(text));
        if (collapsed.Length == 0) return Strings.EmptyNote;

        // StringInfo walks the same UAX #29 clusters Swift's String.count does, so an emoji ZWJ
        // sequence or a base-plus-mark pair counts once here and once there.
        var info = new StringInfo(collapsed);
        if (info.LengthInTextElements <= MaxTextElements) return collapsed;
        var head = info.SubstringByTextElements(0, MaxTextElements);
        // trimmingCharacters(in: .whitespaces) — Foundation's set (no line terminators), which is
        // Md.Core's Whitespace.TrimWS; leading whitespace is already gone from the collapse.
        return Whitespace.TrimWS(head) + Strings.Ellipsis;
    }

    /// <summary>
    /// Swift's <c>split(whereSeparator: \.isNewline).first</c>: empty subsequences are omitted, so
    /// leading blank lines are skipped and the answer is the first line with anything on it.
    /// "" when the note is blank.
    /// </summary>
    static string FirstNonEmptyLine(string text)
    {
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var length = ElementLength(text, i);
            if (IsNewlineElement(text, i, length))
            {
                if (i > start) return text[start..i];
                start = i + length;
            }
            i += length;
        }
        return start < text.Length ? text[start..] : string.Empty;
    }

    /// <summary>
    /// Swift's <c>split(whereSeparator: \.isWhitespace).joined(separator: " ")</c>: every run of
    /// whitespace becomes one U+0020 and the ends lose theirs. Unicode White_Space
    /// (<c>char.IsWhiteSpace</c>), which is what <c>Character.isWhitespace</c> tests — wider than
    /// Foundation's <c>.whitespaces</c>, and the newlines it also covers cannot occur here.
    /// </summary>
    static string Collapse(string line)
    {
        var parts = new List<string>();
        var start = 0;
        var i = 0;
        while (i < line.Length)
        {
            var length = ElementLength(line, i);
            if (length == 1 && char.IsWhiteSpace(line[i]))
            {
                if (i > start) parts.Add(line[start..i]);
                start = i + 1;
            }
            i += length;
        }
        if (start < line.Length) parts.Add(line[start..]);
        return string.Join(" ", parts);
    }

    // Split over text elements, never code units: a separator fused into a larger cluster — CRLF,
    // or a space wearing a combining mark — is not a separator in Swift, and Md.Core's own
    // ViewModeMemory codec splits the same way for the same reason.
    static int ElementLength(string text, int index) => StringInfo.GetNextTextElementLength(text.AsSpan(index));

    // Character.isNewline: the seven Foundation terminators, plus CRLF as the single cluster it is.
    static bool IsNewlineElement(string text, int index, int length) => length switch
    {
        1 => Whitespace.IsNewline(text[index]),
        2 => text[index] == '\r' && text[index + 1] == '\n',
        _ => false,
    };
}
