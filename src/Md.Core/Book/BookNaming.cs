using System.Globalization;
using System.Text;

namespace Md.Core.Book;

/// <summary>
/// The ordering-prefix grammar behind Rename… / Move Up / Move Down and every
/// display title: a sibling name is <c>prefix + stem + "." + ext</c>, where the prefix
/// is a leading ASCII number plus its separators ("01-", "2. ") and the extension only
/// counts when it is one of the article extensions (a chapter folder "v1.2" keeps its
/// dot in the stem). Port of <c>BookLibrary.splitExtension / splitPrefix /
/// displayName / renamedName</c> (md.macOS BookNavigator.swift).
///
/// The two existing ports disagree on the prefix grammar. Swift accepts the separators
/// <c>- . ) space</c> and needs none ("3rd party" → prefix "3", stem "rd party");
/// Kotlin accepts <c>- . space _</c> and demands at least one ("3rd party" is
/// unnumbered, "3_draft" has prefix "3_"). macOS is the source of truth, so this file
/// follows Swift; BookNamingTests pins both disputed names so the choice is visible.
/// </summary>
public static class BookNaming
{
    /// <summary>File extensions treated as articles — the plain-text types the document side reads.</summary>
    public static readonly IReadOnlySet<string> ArticleExtensions =
        new HashSet<string>(StringComparer.Ordinal) { "md", "markdown", "txt" };

    /// <summary>Swift <c>"-.) "</c> — the characters that may follow the digits of an ordering prefix.</summary>
    private const string PrefixSeparators = "-.) ";

    /// <summary>
    /// Whether a file with this name is an article: an article extension after the
    /// last dot, any case. "draft.md.bak" is not, "SHOUTY.MD" is, ".md" (a dotfile)
    /// is not — the listing never shows dotfiles anyway.
    /// </summary>
    public static bool IsArticleName(string name) => SplitExtension(name).Ext.Length != 0;

    /// <summary>
    /// A sibling name split into its article extension (without the dot, original
    /// case — <c>RenamedName</c> puts it back as written) and the rest. Only an article
    /// extension counts; anything else, or a dot at position 0, leaves the name whole.
    /// </summary>
    public static (string Base, string Ext) SplitExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0) return (name, "");
        var ext = name[(dot + 1)..];
        // ToLowerInvariant mirrors Swift's lowercased(): a full simple case mapping
        // (the Kelvin sign folds to "k"), which OrdinalIgnoreCase's upper-casing would
        // not reproduce. Irrelevant for real names, exact for parity.
        if (!ArticleExtensions.Contains(ext.ToLowerInvariant())) return (name, "");
        return (name[..dot], ext);
    }

    /// <summary>
    /// A base name (extension already removed) split into its ordering prefix — the
    /// leading ASCII digits and the separator run after them — and the display stem.
    /// No digits → no prefix. Nothing left after digits and separators ("2026", "01-")
    /// → no prefix either: an all-number name keeps the number as its stem, there would
    /// be nothing to display otherwise.
    /// </summary>
    public static (string Prefix, string Stem) SplitPrefix(string baseName)
    {
        var digits = LeadingAsciiDigits(baseName);
        var index = digits;
        while (index < baseName.Length && PrefixSeparators.Contains(baseName[index])) index++;
        if (digits == 0 || index >= baseName.Length) return ("", baseName);
        return (baseName[..index], baseName[index..]);
    }

    /// <summary>The name as the writer reads it: prefix and article extension stripped. Pre-fills Rename….</summary>
    public static string DisplayName(string name) => SplitPrefix(SplitExtension(name).Base).Stem;

    /// <summary>
    /// The on-disk name after a rename to the display name <paramref name="stem"/>: the
    /// ordering prefix survives exactly as written (a loose "2. " is not normalised —
    /// only a Move does that) and so does the extension.
    /// </summary>
    public static string RenamedName(string name, string stem)
    {
        var (baseName, ext) = SplitExtension(name);
        return SplitPrefix(baseName).Prefix + stem + (ext.Length == 0 ? "" : "." + ext);
    }

    /// <summary>
    /// Length of the leading run of ASCII '0'…'9'. Swift walks <c>Character</c>s, so a
    /// digit carrying a combining mark ("1\u0301") is one non-ASCII grapheme and ends
    /// the run <em>before</em> that digit; a code-unit scan would count it. Unicode
    /// digits (U+0663 "٣") never count on either side.
    /// </summary>
    internal static int LeadingAsciiDigits(string s)
    {
        var n = 0;
        while (n < s.Length && s[n] >= '0' && s[n] <= '9')
        {
            if (n + 1 < s.Length && IsGraphemeExtender(s, n + 1)) break;
            n++;
        }
        return n;
    }

    /// <summary>GB9/GB9a: a mark or joiner that glues onto the preceding code point.</summary>
    private static bool IsGraphemeExtender(string s, int index)
    {
        if (!Rune.TryGetRuneAt(s, index, out var rune)) return false;
        if (rune.Value is 0x200C or 0x200D) return true;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }
}
