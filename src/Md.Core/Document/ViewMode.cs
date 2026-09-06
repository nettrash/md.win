using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Md.Core.Document;

// Port of md.macOS/md/ViewMode.swift (byte-identical between the iPhone and Mac apps, mirrored by
// md.Android/ui/ViewMode.kt). Which of the three display modes a window offers, which one it shows,
// and which one a *file* opens in. THE ONE INVARIANT: what gets stored is the RAW preference, never
// EffectiveMode's output — a narrow window coerces a remembered Split to Edit for display without
// discarding the preference, and a navigation nudge stores nothing at all.

/// <summary>The editor's three display modes, in switch order (Swift <c>DocumentView.Mode</c>).</summary>
public enum ViewMode
{
    Edit,
    Split,
    Preview,
}

public static class ViewModes
{
    /// <summary>CaseIterable order — also the View-menu order and the Split-offered order.</summary>
    public static readonly IReadOnlyList<ViewMode> All = [ViewMode.Edit, ViewMode.Split, ViewMode.Preview];

    /// <summary>Per-window state key (<c>@SceneStorage("md.viewMode")</c>). Never the per-file memory.</summary>
    public const string WindowStateKey = "md.viewMode";

    /// <summary>The per-window default: <c>Mode(rawValue: stored) ?? .split</c>.</summary>
    public const ViewMode WindowDefault = ViewMode.Split;

    /// <summary>
    /// The Swift <c>rawValue</c>: what <c>md.viewMode</c> / <c>md.bookViewMode</c> hold. Spelled the same as
    /// the memory tokens but deliberately a separate function — the tokens are on disk in three apps
    /// and must not follow an enum rename; the raw value is the Mac's own scene state.
    /// </summary>
    public static string RawValue(this ViewMode mode) => mode switch
    {
        ViewMode.Edit => "edit",
        ViewMode.Split => "split",
        ViewMode.Preview => "preview",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>
    /// <c>Mode(rawValue:)</c>: exact, case-sensitive; anything else (including null) is null so the
    /// caller falls back to <see cref="WindowDefault"/>.
    /// </summary>
    public static ViewMode? FromRawValue(string? raw) => raw switch
    {
        "edit" => ViewMode.Edit,
        "split" => ViewMode.Split,
        "preview" => ViewMode.Preview,
        _ => null,
    };

    /// <summary>Menu title.</summary>
    public static string Label(this ViewMode mode) => mode switch
    {
        ViewMode.Edit => "Edit",
        ViewMode.Split => "Split",
        ViewMode.Preview => "Preview",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>The digit behind ⌘1 / ⌘2 / ⌘3 (Ctrl+1/2/3 on Windows).</summary>
    public static string CommandKey(this ViewMode mode) => mode switch
    {
        ViewMode.Edit => "1",
        ViewMode.Split => "2",
        ViewMode.Preview => "3",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}

/// <summary>The width rule, the open rule and the display rule. Pure functions — no view, no storage, no clock.</summary>
public static class ViewModeRule
{
    private static readonly IReadOnlyList<ViewMode> Narrow = [ViewMode.Edit, ViewMode.Preview];

    /// <summary>All three when there is room for Split (a desktop window always), else Edit and Preview.</summary>
    public static IReadOnlyList<ViewMode> AvailableModes(bool isWide) => isWide ? ViewModes.All : Narrow;

    /// <summary>
    /// The stored preference coerced to one the width supports: Split collapses to Edit when narrow.
    /// Display-only — the caller keeps the raw preference so widening brings Split back.
    /// </summary>
    public static ViewMode EffectiveMode(ViewMode stored, bool isWide)
    {
        if (AvailableModes(isWide).Contains(stored)) return stored;
        return stored == ViewMode.Preview ? ViewMode.Preview : ViewMode.Edit;
    }

    /// <summary>
    /// What the window shows: the navigation nudge when one is in force, else the preference — then
    /// coerced by <see cref="EffectiveMode"/>. The nudge is plain view state and is never stored.
    /// </summary>
    public static ViewMode DisplayedMode(ViewMode preferred, ViewMode? navigation, bool isWide)
        => EffectiveMode(navigation ?? preferred, isWide);

    /// <summary>
    /// The transient mode a jump needs so its destination pane is visible, or null when it already is.
    /// Split shows both panes, so it never nudges. Asked about the DISPLAYED mode, never the raw one:
    /// on a narrow window a raw Split shows as Edit, and the raw mode would answer "no nudge needed".
    /// </summary>
    public static ViewMode? NavigationNudge(ViewMode displayed, ViewMode wants)
        => displayed == wants || displayed == ViewMode.Split ? null : wants;

    /// <summary>
    /// The mode a document opens in: remembered (raw, uncoerced) → Edit for an empty document →
    /// Split where Split is offered → Preview. <paramref name="hasFileIdentity"/> never selects a
    /// branch; it is in the shared signature so every suite can assert it stays that way.
    /// </summary>
    public static ViewMode OpenViewMode(ViewMode? remembered, bool isEmptyDocument, bool hasFileIdentity, bool isWide)
    {
        _ = hasFileIdentity;
        if (remembered is { } known) return known;
        if (isEmptyDocument) return ViewMode.Edit;
        return isWide ? ViewMode.Split : ViewMode.Preview;
    }
}

/// <summary>
/// Where the one <c>md.viewModeMemory</c> string lives. The app backs it with
/// <c>ApplicationData.Current.LocalSettings</c>; tests with <see cref="InMemoryViewModeStore"/>.
/// </summary>
public interface IViewModeStore
{
    string? Load();
    void Save(string value);
}

public sealed class InMemoryViewModeStore : IViewModeStore
{
    public string? Value { get; set; }
    public int Reads { get; private set; }
    public int Writes { get; private set; }

    public string? Load()
    {
        Reads++;
        return Value;
    }

    public void Save(string value)
    {
        Writes++;
        Value = value;
    }
}

/// <summary>
/// Remembers, per file, the raw mode it was last shown in. One string under <see cref="SettingsKey"/>:
/// a <c>v1</c> header line, then one <c>&lt;16 hex&gt; &lt;token&gt;</c> line per file, newest first, no
/// trailing newline, at most 200 entries. Not JSON on purpose (the Android suite cannot run org.json),
/// and the three ports must agree byte for byte.
/// </summary>
public static class ViewModeMemory
{
    public const string SettingsKey = "md.viewModeMemory";
    public const string Header = "v1";
    public const int MaxEntries = 200;

    public readonly record struct Entry(string Identity, ViewMode Mode);

    // MARK: Identity

    /// <summary>
    /// The stable identity of a file: <see cref="Sha256Prefix"/> of <c>"file:" + CanonicalPath(path)</c>.
    /// Apple hashes <c>"file:" + url.resolvingSymlinksInPath().standardizedFileURL.path</c>; Android hashes
    /// <c>"saf:" + authority + ":" + documentId</c>. The prefix is per platform — the store never syncs
    /// between devices — so only the two ASCII vectors in the tests must agree across ports.
    /// A rename or move outside the app changes the path and loses the memory. Accepted.
    /// </summary>
    public static string IdentityFor(string path) => Sha256Prefix("file:" + CanonicalPath(path));

    /// <summary>Platform default: fold case on Windows (NTFS compares names case-insensitively), not elsewhere.</summary>
    public static string CanonicalPath(string path) => CanonicalPath(path, foldCase: OperatingSystem.IsWindows());

    /// <summary>
    /// The bytes that get hashed (after the <c>"file:"</c> prefix):
    /// 1. <c>Path.GetFullPath</c> — absolute, <c>.</c>/<c>..</c> collapsed, separators native (the Swift
    ///    <c>standardizedFileURL</c>), trailing separator dropped;
    /// 2. symlinks and junctions resolved on every component, but ONLY when the whole path names
    ///    something on disk — Foundation's <c>resolvingSymlinksInPath()</c> is a no-op for a missing path,
    ///    and an identity is only ever computed for a document the app has open;
    /// 3. when <paramref name="foldCase"/>: <c>ToUpperInvariant</c>. Windows hands the same file over as
    ///    <c>C:\Docs\A.md</c> and <c>c:\docs\a.md</c> (shell, MRU list, command line, drag-drop), and without
    ///    folding one file would hold two disagreeing entries. Upcasing, not lowercasing, because that is
    ///    how NTFS itself compares names (its $UpCase table), so two spellings fold together exactly when
    ///    the file system treats them as one name. Folding also keeps the identity through a case-only
    ///    rename, which the true-case spelling (<c>GetFinalPathNameByHandle</c>) would not — and needs no
    ///    P/Invoke. A directory with case sensitivity switched on shares one entry between <c>A.md</c> and
    ///    <c>a.md</c>; a view mode, not data, so harmless.
    /// </summary>
    public static string CanonicalPath(string path, bool foldCase)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        full = ResolveLinks(full);
        return foldCase ? full.ToUpperInvariant() : full;
    }

    /// <summary>First 16 hex digits (8 bytes) of SHA-256 over the UTF-8 bytes of <paramref name="value"/>.</summary>
    public static string Sha256Prefix(string value)
    {
        // Encoding.UTF8 writes no BOM; a lone surrogate becomes U+FFFD, which a Swift String cannot hold anyway.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest.AsSpan(0, 8));
    }

    private static string ResolveLinks(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) return fullPath;
            var current = fullPath;
            // SYMLOOP_MAX-sized cap: a link cycle must end in "the path as given", never in a hang.
            for (var hop = 0; hop < 40; hop++)
            {
                var resolved = ResolveFirstLink(current);
                if (resolved is null) return current;
                current = resolved;
            }
            return current;
        }
        catch (IOException)
        {
            return fullPath;
        }
        catch (UnauthorizedAccessException)
        {
            return fullPath;
        }
    }

    /// <summary>
    /// Walks the components root-first; the first one that is a link is replaced by its final target
    /// (on Windows .NET answers that through GetFinalPathNameByHandle, so junctions resolve too) with the
    /// remaining components re-appended. Null when no component is a link. The caller loops, because the
    /// target's own intermediate components may be links as well.
    /// </summary>
    private static string? ResolveFirstLink(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var parts = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var prefix = root;
        for (var i = 0; i < parts.Length; i++)
        {
            prefix = Path.Combine(prefix, parts[i]);
            FileSystemInfo info = Directory.Exists(prefix) ? new DirectoryInfo(prefix) : new FileInfo(prefix);
            if (info.LinkTarget is null) continue;
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null) return null;
            var rebuilt = target.FullName;
            for (var j = i + 1; j < parts.Length; j++) rebuilt = Path.Combine(rebuilt, parts[j]);
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(rebuilt));
        }
        return null;
    }

    // MARK: Tokens

    /// <summary>The stored spelling. Written out by hand — never the enum name — so a rename cannot re-spell the disk.</summary>
    public static string Token(ViewMode mode) => mode switch
    {
        ViewMode.Edit => "edit",
        ViewMode.Split => "split",
        ViewMode.Preview => "preview",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>
    /// The mode a token names, or null. Surrounding Foundation whitespace (Zs + TAB + U+200B, no
    /// newlines — Swift <c>.whitespaces</c>) is forgiven and ASCII case is folded. ASCII-only on purpose:
    /// Swift's <c>lowercased()</c> is a full mapping, so no non-ASCII letter can ever produce one of these
    /// three words there (İ → i̇, ı stays ı); .NET's <c>OrdinalIgnoreCase</c>/<c>ToLowerInvariant</c> would
    /// let <c>EDİT</c> and <c>edıt</c> through.
    /// </summary>
    public static ViewMode? ModeForToken(string token)
    {
        var start = 0;
        var end = token.Length;
        while (start < end && IsFoundationWhitespace(token[start])) start++;
        while (end > start && IsFoundationWhitespace(token[end - 1])) end--;
        var trimmed = token.AsSpan(start, end - start);
        if (AsciiEqualsIgnoreCase(trimmed, "edit")) return ViewMode.Edit;
        if (AsciiEqualsIgnoreCase(trimmed, "split")) return ViewMode.Split;
        if (AsciiEqualsIgnoreCase(trimmed, "preview")) return ViewMode.Preview;
        return null;
    }

    /// <summary>Foundation <c>CharacterSet.whitespaces</c>: U+0009, U+0020, U+00A0, U+1680, U+2000–U+200A, U+200B, U+202F, U+205F, U+3000.</summary>
    private static bool IsFoundationWhitespace(char c) => c switch
    {
        '\t' or ' ' or '\u00A0' or '\u1680' or '\u200B' or '\u202F' or '\u205F' or '\u3000' => true,
        >= '\u2000' and <= '\u200A' => true,
        _ => false,
    };

    private static bool AsciiEqualsIgnoreCase(ReadOnlySpan<char> candidate, string lowercaseWord)
    {
        if (candidate.Length != lowercaseWord.Length) return false;
        for (var i = 0; i < candidate.Length; i++)
        {
            var c = candidate[i];
            if (c is >= 'A' and <= 'Z') c = (char)(c + 32);
            if (c != lowercaseWord[i]) return false;
        }
        return true;
    }

    // MARK: Codec

    /// <summary>
    /// Parse a stored value. A missing or wrong first line means "absent" — the whole value is discarded.
    /// Past that a line that does not parse is skipped, never fatal, and the first entry for an identity
    /// wins. Lines split on LF (empty lines kept), fields on U+0020 (empty fields dropped) — both the way
    /// Swift's <c>split(separator:)</c> does it, over Characters (<see cref="SplitCharacters"/>), so a CRLF
    /// value reads as absent here exactly as on the Mac, wherever the CR sits. Kotlin splits on code
    /// units and <c>trim()</c>s each line, which reads a CRLF value as present; the three ports agree
    /// on every value any of them writes, and the Mac is the source of truth for the rest.
    /// </summary>
    public static IReadOnlyList<Entry> Decode(string? text)
    {
        if (text is null) return [];
        var lines = SplitCharacters(text, '\n', omitEmpty: false);
        if (lines[0] != Header) return [];

        var entries = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Count; i++)
        {
            var fields = SplitCharacters(lines[i], ' ', omitEmpty: true);
            // The mode is parsed BEFORE the identity is marked seen: an unreadable token must not
            // consume that file's one slot and hide a good line for the same file further down.
            if (fields.Count != 2) continue;
            if (ModeForToken(fields[1]) is not { } mode) continue;
            var identity = fields[0];
            if (!IsIdentity(identity) || !seen.Add(identity)) continue;
            entries.Add(new Entry(identity, mode));
        }
        return entries;
    }

    /// <summary>
    /// Swift <c>String.split(separator:)</c>: the separator is a Character, so one fused into a larger
    /// extended grapheme cluster — <c>"\r\n"</c>, or a space wearing a combining mark — is not a
    /// separator. <c>StringInfo</c> walks the same UAX #29 clusters, so this is the Swift call, not a
    /// code-unit approximation of it. Returns <c>[""]</c> for an empty string when empties are kept.
    /// </summary>
    private static List<string> SplitCharacters(string text, char separator, bool omitEmpty)
    {
        var parts = new List<string>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(i));
            if (length == 1 && text[i] == separator)
            {
                if (!omitEmpty || i > start) parts.Add(text[start..i]);
                start = i + 1;
            }
            i += length;
        }
        if (!omitEmpty || start < text.Length) parts.Add(text[start..]);
        return parts;
    }

    /// <summary>Render entries back. No trailing newline, so the codec round-trips exactly; <c>Encode([]) == "v1"</c>.</summary>
    public static string Encode(IEnumerable<Entry> entries)
    {
        var builder = new StringBuilder(Header);
        foreach (var entry in entries)
        {
            builder.Append('\n').Append(entry.Identity).Append(' ').Append(Token(entry.Mode));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Exactly 16 characters, each in <c>0123456789abcdef</c>. Uppercase is rejected — no port writes it,
    /// so accepting it would let one file hold two disagreeing entries. ASCII membership, not
    /// <c>char.IsAsciiHexDigit</c>-plus-Unicode: full-width digits are out. <c>Length</c> is UTF-16 units,
    /// equivalent to Swift's grapheme count here because any non-ASCII fails the hex test anyway.
    /// </summary>
    public static bool IsIdentity(string candidate)
    {
        if (candidate.Length != 16) return false;
        foreach (var c in candidate)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        }
        return true;
    }

    // MARK: The MRU list

    /// <summary><paramref name="entries"/> with <paramref name="identity"/> at the front as <paramref name="mode"/>, the oldest past <see cref="MaxEntries"/> dropped.</summary>
    public static IReadOnlyList<Entry> Touched(IReadOnlyList<Entry> entries, string identity, ViewMode mode)
    {
        var updated = new List<Entry>(entries.Count + 1) { new Entry(identity, mode) };
        foreach (var entry in entries)
        {
            if (entry.Identity != identity) updated.Add(entry);
        }
        if (updated.Count > MaxEntries) updated.RemoveRange(MaxEntries, updated.Count - MaxEntries);
        return updated;
    }

    // MARK: Lookup / store

    /// <summary>Everything remembered, newest first. The one read path, so an absent or unreadable value becomes "nothing" in one place.</summary>
    public static IReadOnlyList<Entry> Entries(IViewModeStore store) => Decode(store.Load());

    public static ViewMode? Lookup(string identity, IViewModeStore store)
    {
        foreach (var entry in Entries(store))
        {
            if (entry.Identity == identity) return entry.Mode;
        }
        return null;
    }

    /// <summary><paramref name="mode"/> must be the RAW preference — never <see cref="ViewModeRule.EffectiveMode"/>'s output.</summary>
    public static void Remember(ViewMode mode, string identity, IViewModeStore store)
        => store.Save(Encode(Touched(Entries(store), identity, mode)));
}

/// <summary>
/// The book exemption. Writer mode opens an article through the same path as any document, so the
/// navigator marks the file just before opening it and the window that lands on it claims the mark and
/// becomes exempt from the per-file memory both ways — sticky for the window's life, Save As included
/// (Android clears its flag on Save As; the Mac is the source of truth). A mark older than
/// <see cref="MarkLifetime"/> is discarded: re-activating the already-open article never fires the
/// claim, and a stale mark would silently exempt the next ordinary open of that file.
/// </summary>
public static class BookArticleOpens
{
    public static readonly TimeSpan MarkLifetime = TimeSpan.FromSeconds(10);

    /// <summary>Injectable clock so the expiry is testable without sleeping.</summary>
    public static Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    private static readonly Dictionary<string, DateTime> Pending = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>Called just before writer mode opens an article.</summary>
    public static void Mark(string path)
    {
        lock (Gate)
        {
            DropExpired();
            Pending[ViewModeMemory.IdentityFor(path)] = Now();
        }
    }

    /// <summary>True when this open came from the book. Consumes the mark, so the next open of the same file is ordinary.</summary>
    public static bool ClaimOpen(string path)
    {
        lock (Gate)
        {
            DropExpired();
            return Pending.Remove(ViewModeMemory.IdentityFor(path));
        }
    }

    public static void Reset()
    {
        lock (Gate) Pending.Clear();
    }

    private static void DropExpired()
    {
        var cutoff = Now() - MarkLifetime;
        foreach (var stale in Pending.Where(p => p.Value <= cutoff).Select(p => p.Key).ToList())
        {
            Pending.Remove(stale);
        }
    }
}
