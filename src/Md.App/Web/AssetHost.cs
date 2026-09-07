// The preview's origin (shell-final.md §4.2). One virtual host carries both the page and the
// engines, which is the whole trick: the generated HTML keeps its relative rich/… URLs, md-init.js's
// import('./plantuml.js') resolves against its own module URL, and katex.min.css's url(fonts/…)
// resolves against the stylesheet — so the bytes Md.Core writes are the bytes the four ports share,
// with no <base href> and no string rewriting.
using System.Text;
using Md.App.Logic.Preview;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;

namespace Md.App.Web;

internal static class AssetHost
{
    /// <summary>
    /// The mapped folder: the install's <c>web\</c>, never the install root. Md.App.csproj packages
    /// the engines as <c>web\rich\…</c> for exactly this reason — the page can fetch <c>rich/</c> and
    /// nothing else, so md.dll, Examples\ and every assembly stay unreachable. AppContext.BaseDirectory
    /// is right packaged and unpackaged alike (Package.Current throws unpackaged).
    /// </summary>
    public static string WebRoot { get; } = Path.Combine(AppContext.BaseDirectory, AssetOrigin.WebFolderName);

    static bool _servedIndex;

    /// <summary>Every URL on the origin: the document, the engines, the fonts KaTeX's CSS asks for.</summary>
    const string OriginFilter = "https://" + AssetOrigin.Host + "/*";

    /// <summary>
    /// Serve the whole <c>md.assets</c> origin: <c>index.html</c> from memory, everything else from
    /// <see cref="WebRoot"/> on disk.
    /// <paramref name="html"/> is read on every request, so <c>Reload()</c> picks up the new document
    /// — which is precisely what the coordinator's 350 ms debounce relies on.
    /// </summary>
    public static void Attach(CoreWebView2 core, Func<string> html)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(html);

        // ONE filter over the whole origin, and no SetVirtualHostNameToFolderMapping.
        //
        // The mapping was the original design, with index.html served from memory on the same host
        // through WebResourceRequested — resting on Microsoft's how-to, which says the event is
        // still raised "when a requested resource does not exist in the folder that is virtually
        // hosted". On Windows 11 it is not, at least for the top-level document: the first run of
        // this app ever made logged `preview navigation FAILED Unknown` and `ConnectionAborted`
        // with no request reaching the handler, so the mapping took the navigation, found no
        // <install>\web\index.html on disk, and failed it. (The behaviour is undocumented in the
        // reference and the subject of MicrosoftEdge/WebView2Feedback #2103 and #4201.)
        //
        // Serving every byte ourselves is simpler than it sounds and loses nothing: the document
        // comes from memory, rich/ comes off disk through Asset() — the path that was already
        // written as the MIME contingency — and both arrive on the SAME origin, which is the only
        // thing the design actually needs. What was a fallback is now the mechanism, so AssetMime
        // is load-bearing: without a mapping Chromium has no file extension to infer a type from.
        core.AddWebResourceRequestedFilter(
            OriginFilter,
            CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.Document);

        core.WebResourceRequested += (sender, e) =>
        {
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;

            if (IsIndex(uri))
            {
                var bytes = Encoding.UTF8.GetBytes(html());
                // The one interaction no documentation states outright and no test on a Mac can
                // reach: a virtual host mapping is consulted BEFORE WebResourceRequested, and the
                // event is raised only because <install>\web\index.html does not exist on disk. If
                // that ever changes, this line stops appearing and the preview goes blank — so the
                // first time it happens is worth recording.
                if (!_servedIndex)
                {
                    _servedIndex = true;
                    App.Diagnostics.Write($"preview index served from memory, {bytes.Length} bytes");
                }

                e.Response = Response(sender, bytes, "text/html; charset=utf-8", noStore: true);
                return;
            }

            e.Response = Asset(sender, uri);
        };
    }

    /// <summary>
    /// One line, once, about the folder everything the page fetches comes from. If <c>web\rich</c>
    /// is not beside md.exe the preview still renders text and silently loses every formula and
    /// diagram, which is a long way to chase from the symptom.
    /// </summary>
    public static void LogRoot()
    {
        try
        {
            var rich = Path.Combine(WebRoot, AssetOrigin.RichFolderName);
            var engines = Directory.Exists(rich) ? Directory.GetFiles(rich).Length : -1;
            App.Diagnostics.Write($"preview assets: {WebRoot} exists={Directory.Exists(WebRoot)} rich={engines} files");
        }
        catch (Exception e)
        {
            App.Diagnostics.Write($"preview assets: could not be read: {e.Message}");
        }
    }

    static bool IsIndex(Uri uri) =>
        uri.GetLeftPart(UriPartial.Query).Equals(AssetOrigin.IndexUrl, StringComparison.Ordinal);

    /// <summary>A file under <see cref="WebRoot"/>, or a 404 for anything outside it.</summary>
    static CoreWebView2WebResourceResponse Asset(CoreWebView2 core, Uri uri)
    {
        string file;
        byte[] bytes;
        try
        {
            var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            file = Path.GetFullPath(Path.Combine(WebRoot, relative));

            // Containment, as the Mac's scheme handler does it: a path that leaves the root is a 404,
            // never a read.
            var contained = file.StartsWith(WebRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!contained || !File.Exists(file)) return NotFound(core);

            bytes = File.ReadAllBytes(file);
        }
        catch (Exception)
        {
            // WebResourceRequested is synchronous and raised on the UI thread, so an exception here
            // is an unhandled one. A percent-encoded NUL, an over-long path and an unreadable file
            // are all just "not found" as far as the page is concerned.
            return NotFound(core);
        }

        return Response(core, bytes, AssetMime.For(file), noStore: false);
    }

    static CoreWebView2WebResourceResponse NotFound(CoreWebView2 core) =>
        core.Environment.CreateWebResourceResponse(Stream([]), 404, "Not Found", "");

    static CoreWebView2WebResourceResponse Response(CoreWebView2 core, byte[] bytes, string contentType, bool noStore)
    {
        // no-store on the document only: it changes on every keystroke and Chromium must never serve
        // the copy it has. The engines never change while the process lives.
        var headers = noStore
            ? "Content-Type: " + contentType + "\r\nCache-Control: no-store"
            : "Content-Type: " + contentType;

        return core.Environment.CreateWebResourceResponse(Stream(bytes), 200, "OK", headers);
    }

    static IRandomAccessStream Stream(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            // WebResourceRequested is synchronous, so this cannot be awaited; an in-memory stream
            // stores synchronously, which is why the response is built from one and not from a file.
            writer.StoreAsync().GetResults();
            writer.DetachStream();
        }

        stream.Seek(0);
        return stream;
    }
}
