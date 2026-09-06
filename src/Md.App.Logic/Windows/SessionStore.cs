using System.Text;
using System.Text.Json;
using Md.App.Logic.Documents;
using Md.App.Logic.Seams;
using Md.App.Logic.Settings;
using Md.Core.Document;

namespace Md.App.Logic.Windows;

/// <summary>One restored document window: which file, how it was showing it, and where it was (§1.6).</summary>
/// <param name="Path">The saved document. Untitled drafts are never listed — there is no Windows equivalent of the Mac's Autosave Information (§14 row 8).</param>
/// <param name="Mode">The window's RAW mode preference, not what a narrow window displayed.</param>
/// <param name="Zen">Zen was on.</param>
/// <param name="ZenReading">Zen was showing the preview rather than the editor.</param>
/// <param name="Placement">The client frame in effective pixels; clamped into the current work area on restore.</param>
/// <param name="Maximized">The window was maximised — the frame is what it would restore to.</param>
public sealed record SessionWindow(string Path, ViewMode Mode, bool Zen, bool ZenReading, WindowRect Placement, bool Maximized);

/// <summary>The Book window's half of the session: whether it was open and where (§1.6).</summary>
public sealed record SessionBook(bool Open, WindowRect Placement, bool Maximized);

/// <summary>
/// What <c>session.json</c> holds. Equality is structural over <see cref="Windows"/> — a record
/// would compare the list by reference, and "the file I wrote reads back as what I wrote" is the
/// property the codec is for.
/// </summary>
public sealed record SessionState(IReadOnlyList<SessionWindow> Windows, SessionBook? Book)
{
    public static readonly SessionState Empty = new([], null);

    public bool Equals(SessionState? other) =>
        other is not null && Equals(Book, other.Book) && Windows.SequenceEqual(other.Windows);

    public override int GetHashCode() => HashCode.Combine(Windows.Count, Book);
}

/// <summary>
/// <c>LocalFolder\session.json</c> (shell-design.md §1.6) — where the Mac's
/// <c>@SceneStorage("md.viewMode" / "md.zen" / "md.zenReading")</c> and its AppKit-restored window
/// frames live on Windows. Written on every window close and on Exit, read on a plain first launch.
///
/// Three rules, each a test: only <em>saved</em> documents are listed (an untitled draft is not
/// autosaved, so restoring it would restore nothing); a file that has since gone is skipped
/// silently; and per-window state never goes to <c>LocalSettings</c>, whose values are 8 KB each and
/// app-wide (§9).
/// </summary>
public static class SessionStore
{
    /// <summary>The document's <c>"v"</c>. A file with any other version reads as empty rather than half-understood.</summary>
    public const int Version = 1;

    /// <summary><c>session.json</c> — the same constant <see cref="SettingsKeys.SessionFileName"/> names for PRIVACY.md.</summary>
    public const string FileName = SettingsKeys.SessionFileName;

    /// <summary>The full path inside a LocalFolder.</summary>
    public static string PathIn(string folder) => FileNames.Combine(folder, FileName);

    // ---- codec ----

    /// <summary>
    /// The design's shape byte for byte. A window with no path is dropped here rather than at the
    /// call site: "never record an untitled document" is a property of the file, so the one place
    /// that writes the file enforces it.
    /// </summary>
    public static string Encode(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);

            writer.WriteStartArray("windows");
            foreach (var window in state.Windows)
            {
                if (string.IsNullOrWhiteSpace(window.Path)) continue;
                writer.WriteStartObject();
                writer.WriteString("path", window.Path);
                writer.WriteString("mode", window.Mode.RawValue());
                writer.WriteBoolean("zen", window.Zen);
                writer.WriteBoolean("zenReading", window.ZenReading);
                WriteFrame(writer, window.Placement, window.Maximized);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            if (state.Book is { } book)
            {
                writer.WriteStartObject("book");
                writer.WriteBoolean("open", book.Open);
                WriteFrame(writer, book.Placement, book.Maximized);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The inverse. Anything unreadable — absent, truncated, a different <c>"v"</c>, a row without a
    /// path — is <see cref="SessionState.Empty"/>: a corrupt session file must cost the user their
    /// window layout, never their launch.
    /// </summary>
    public static SessionState Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return SessionState.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return SessionState.Empty;
            if (!root.TryGetProperty("v", out var version) || !version.TryGetInt32(out var v) || v != Version) return SessionState.Empty;

            var windows = new List<SessionWindow>();
            if (root.TryGetProperty("windows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    var path = String(row, "path");
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    windows.Add(new SessionWindow(
                        path,
                        ViewModes.FromRawValue(String(row, "mode")) ?? ViewModes.WindowDefault,
                        Bool(row, "zen"),
                        Bool(row, "zenReading"),
                        Frame(row),
                        Bool(row, "maximized")));
                }
            }

            SessionBook? book = null;
            if (root.TryGetProperty("book", out var bookRow) && bookRow.ValueKind == JsonValueKind.Object)
                book = new SessionBook(Bool(bookRow, "open"), Frame(bookRow), Bool(bookRow, "maximized"));

            return new SessionState(windows, book);
        }
        catch (JsonException)
        {
            return SessionState.Empty;
        }
    }

    // ---- disk ----

    /// <summary>Read the session from a LocalFolder. Missing or unreadable ⇒ <see cref="SessionState.Empty"/>.</summary>
    public static SessionState Load(IFileSystem fileSystem, string folder)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        var path = PathIn(folder);
        try
        {
            if (!fileSystem.FileExists(path)) return SessionState.Empty;
            return Decode(Encoding.UTF8.GetString(fileSystem.ReadAllBytes(path)));
        }
        catch (Exception e) when (IsIoFailure(e))
        {
            return SessionState.Empty;
        }
    }

    /// <summary>
    /// Write it. False when the write failed — a session that cannot be recorded is a lost layout,
    /// never a blocked close, so the caller ignores it.
    /// </summary>
    public static bool Save(IFileSystem fileSystem, string folder, SessionState state)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        try
        {
            fileSystem.WriteAllBytesInPlace(PathIn(folder), Encoding.UTF8.GetBytes(Encode(state)));
            return true;
        }
        catch (Exception e) when (IsIoFailure(e))
        {
            return false;
        }
    }

    /// <summary>
    /// The rows whose files are still there (§1.6 "missing files are skipped silently"), and the
    /// book only when it was open. This is what a launch restores and what
    /// <c>ActivationDescription.HasRestorableSession</c> is computed from — an empty result means the
    /// plain launch opens one untitled window instead.
    /// </summary>
    public static SessionState Restorable(SessionState state, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fileSystem);
        var windows = new List<SessionWindow>(state.Windows.Count);
        foreach (var window in state.Windows)
        {
            if (string.IsNullOrWhiteSpace(window.Path)) continue;
            if (!fileSystem.FileExists(window.Path)) continue;
            windows.Add(window);
        }
        return new SessionState(windows, state.Book is { Open: true } book ? book : null);
    }

    // ---- helpers ----

    static void WriteFrame(Utf8JsonWriter writer, WindowRect frame, bool maximized)
    {
        writer.WriteNumber("x", frame.X);
        writer.WriteNumber("y", frame.Y);
        writer.WriteNumber("w", frame.Width);
        writer.WriteNumber("h", frame.Height);
        writer.WriteBoolean("maximized", maximized);
    }

    static WindowRect Frame(JsonElement row) => new(Int(row, "x"), Int(row, "y"), Int(row, "w"), Int(row, "h"));

    static string? String(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static bool Bool(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    static int Int(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    static bool IsIoFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
}
