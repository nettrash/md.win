using System.Globalization;

namespace Md.App.Logic.Preview;

/// <summary>
/// Every script the host injects or evaluates (Appendix B). Nothing in <c>rich/</c> or
/// <c>md-init.js</c> is edited — those bytes are shared with three other ports — so anything
/// host-specific lives here and is registered with
/// <c>AddScriptToExecuteOnDocumentCreatedAsync</c> (registered scripts are not part of the DOM, so
/// an export never carries them) or evaluated with <c>ExecuteScriptAsync</c>.
///
/// Both injected scripts run at <em>document start</em> (the Mac's <c>WKUserScript</c> ran at
/// document end), so neither may touch the DOM at injection time; both only register listeners and
/// define functions.
///
/// Every number interpolated into a script goes through <see cref="CultureInfo.InvariantCulture"/> —
/// a German locale would otherwise emit <c>0,5</c> and the page would see one argument as two.
/// </summary>
public static class Scripts
{
    /// <summary>
    /// The Mac's scroll-sync user script, byte for byte, with the single documented substitution:
    /// <c>window.webkit?.messageHandlers?.mdScroll?.postMessage(</c> →
    /// <c>window.chrome.webview.postMessage(</c>. Everything else — the 300 ms echo window, the
    /// rAF throttle, the passive listener, the three <c>window.__md*</c> entry points — is the Mac's.
    /// The host receives <c>{fraction, echo}</c> in <c>WebMessageReceived</c>; see
    /// <see cref="JsonScript.ScrollMessage"/>.
    /// </summary>
    public static readonly string ScrollSync = Lf(
        """
        (function () {
            let lastProgrammatic = 0;
            function maxScroll() {
                return Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
            }
            window.__mdMarkProgrammatic = function () { lastProgrammatic = Date.now(); };
            window.__mdSyncScrollTo = function (fraction) {
                lastProgrammatic = Date.now();
                window.scrollTo(0, Math.max(0, Math.min(1, fraction)) * maxScroll());
            };
            window.__mdScrollTo = function (y) {
                lastProgrammatic = Date.now();
                window.scrollTo(0, y);
            };
            let pending = false;
            window.addEventListener('scroll', function () {
                if (pending) { return; }
                pending = true;
                requestAnimationFrame(function () {
                    pending = false;
                    const max = maxScroll();
                    window.chrome.webview.postMessage({
                        fraction: max > 0 ? window.scrollY / max : 0,
                        echo: (Date.now() - lastProgrammatic) < 300
                    });
                });
            }, { passive: true });
        })();
        """);

    /// <summary>
    /// The substring the Mac script has where <see cref="ScrollSync"/> posts to WebView2 — the one
    /// half of the substitution the pinning test performs.
    /// </summary>
    public const string MacPostMessageCall = "window.webkit?.messageHandlers?.mdScroll?.postMessage(";

    /// <summary>The Windows half of the same substitution.</summary>
    public const string WebView2PostMessageCall = "window.chrome.webview.postMessage(";

    /// <summary>
    /// What WebKit gave for free and Chromium does not: a clicked <c>javascript:</c> href runs
    /// in-page with no navigation, so <c>NavigationStarting</c> never fires and
    /// <see cref="LinkPolicy"/> never gets a say. Capture phase on <c>document</c>, <c>click</c> and
    /// <c>auxclick</c> (a middle click is not a <c>click</c>), letting through only same-document
    /// fragments and http(s) — which <c>NavigationStarting</c> then decides. The href is compared
    /// lower-cased, so <c>JavaScript:</c> is caught too.
    /// </summary>
    public static readonly string LinkGuard = Lf(
        """
        (function () {
          function guard(e) {
            var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
            if (!a) return;
            var href = (a.getAttribute('href') || '').trim().toLowerCase();
            if (href.charAt(0) === '#') return;                                          // same-document
            if (href.indexOf('http://') === 0 || href.indexOf('https://') === 0) return;  // NavigationStarting decides
            e.preventDefault(); e.stopImmediatePropagation();                            // javascript:, data:, file:, mailto:, relative
          }
          document.addEventListener('click', guard, true);
          document.addEventListener('auxclick', guard, true);
        })();
        """);

    /// <summary>
    /// The render-complete probe (§4.8). Exports poll it; the live preview never waits. The JSON
    /// result is the three characters <c>"1"</c> — see <see cref="RenderCompleteResult"/>.
    /// </summary>
    public const string RenderComplete = "document.documentElement.getAttribute('data-md-render-complete')";

    /// <summary>What <see cref="RenderComplete"/> returns once <c>md-init.js</c> has raised the flag: JSON, quotes included.</summary>
    public const string RenderCompleteResult = "\"1\"";

    /// <summary>Read before a reload so the reader keeps their place (§4.5). Absolute pixels, as the Mac saves them.</summary>
    public const string ScrollY = "window.scrollY";

    /// <summary>The grow-to-content measurement the EPUB snapshotter takes.</summary>
    public const string ScrollHeight = "document.documentElement.scrollHeight";

    /// <summary>Restore the pre-reload position through the injected helper; optional chaining makes a page without it a no-op.</summary>
    public static string ScrollTo(double y) =>
        "window.__mdScrollTo?.(" + y.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>Editor → preview: a fraction of the scrollable range, clamped in the page.</summary>
    public static string SyncScrollTo(double fraction) =>
        "window.__mdSyncScrollTo(" + fraction.ToString("R", CultureInfo.InvariantCulture) + ")";

    /// <summary>
    /// Jump to a heading (§4.5). The slug is interpolated raw, exactly as the Mac does: the parser
    /// guarantees slugs are letters, digits, <c>-</c> and <c>_</c> only. Marked programmatic first so
    /// the proportional echo does not drag a Split editor that is jumping by its own exact line.
    /// </summary>
    public static string JumpToSlug(string slug) =>
        "window.__mdMarkProgrammatic?.(); document.getElementById('" + slug + "')?.scrollIntoView(true)";

    /// <summary>
    /// Raw string literals carry the source file's line endings. <c>.gitattributes</c> pins this
    /// checkout to LF, but a script whose bytes are pinned against another port must not depend on
    /// that holding on someone's machine.
    /// </summary>
    static string Lf(string script) => script.Replace("\r\n", "\n", StringComparison.Ordinal);
}
