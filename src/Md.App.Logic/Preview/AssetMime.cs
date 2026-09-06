namespace Md.App.Logic.Preview;

/// <summary>
/// The Mac scheme handler's MIME table, to the row (§4.2, rich.md §4.1). Chromium infers the type of
/// a folder-mapped file from its extension and consults the registry for extensions its built-in
/// table lacks, so the mapping normally needs no help — this table is the written-and-off
/// contingency: if a clean Windows install serves <c>.woff2</c> or <c>.js</c> as something the page
/// rejects (an ES module fetched as <c>application/octet-stream</c> is refused outright, which would
/// take PlantUML's dynamic <c>import('./plantuml.js')</c> with it), <c>AssetHost</c> widens its
/// filter to <c>rich/*</c> and serves the bytes itself with these types.
///
/// Deliberately the Mac's nine rows and no more: today's <c>rich/</c> holds only <c>.css</c>,
/// <c>.js</c>, <c>.ttf</c>, <c>.woff</c> and <c>.woff2</c>, and a row this table does not have (a
/// <c>.png</c>, a <c>.wasm</c>) falls to <c>application/octet-stream</c> exactly as it does on macOS
/// and iOS. Add a row here and in the Mac handler together, or the ports drift.
/// </summary>
public static class AssetMime
{
    /// <summary>What an extension this table does not know is served as.</summary>
    public const string Fallback = "application/octet-stream";

    /// <summary>
    /// The type for <paramref name="pathOrExtension"/> — a file name, a path, or a bare extension
    /// with or without its dot. Case-insensitive, invariant: a Turkish locale must not turn
    /// <c>.JS</c> into something else.
    /// </summary>
    public static string For(string? pathOrExtension) => Extension(pathOrExtension) switch
    {
        "js" or "mjs" => "text/javascript",
        "css" => "text/css",
        "html" => "text/html",
        "json" => "application/json",
        "svg" => "image/svg+xml",
        "woff2" => "font/woff2",
        "woff" => "font/woff",
        "ttf" => "font/ttf",
        _ => Fallback,
    };

    /// <summary>The last dot-separated segment, lower-cased; empty when there is no extension.</summary>
    static string Extension(string? pathOrExtension)
    {
        if (string.IsNullOrEmpty(pathOrExtension)) return "";

        var name = pathOrExtension;
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0) name = name[(slash + 1)..];

        var dot = name.LastIndexOf('.');
        var extension = dot >= 0 ? name[(dot + 1)..] : name;
        return extension.ToLowerInvariant();
    }
}
