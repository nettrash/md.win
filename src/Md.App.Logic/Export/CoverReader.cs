using System.Net.Http;
using Md.Core.Export;

namespace Md.App.Logic.Export;

/// <summary>
/// Reads the image an EPUB's <c>cover:</c> names (1.6). Core classifies the reference and the
/// bytes (<see cref="EpubMetadataReader"/>); this is the I/O: a <c>data:</c> image is already
/// decoded, a relative path is read beside the document with the TextBundle export's containment
/// (<see cref="Documents.AssetReader.Beside"/>), and an <c>https:</c> image is fetched. Anything
/// unreadable — missing, outside the folder, a failed or slow fetch, bytes that are not a PNG,
/// JPEG, GIF or WebP — is null, and the book is written without a cover. Never throws, except for
/// cancellation of the export itself.
/// </summary>
/// <remarks>
/// The fetch needs no manifest capability: md.win is a full-trust package, which reaches the
/// network the way the preview's remote images already do (PRIVACY.md says so).
/// </remarks>
public static class CoverReader
{
    /// <summary>How long a cover fetch may take before the book goes without one.</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How many redirects a cover fetch follows before it gives up.</summary>
    public const int MaxRedirects = 5;

    // Redirects are followed by hand (FollowedRedirect), so a Location that is not https is refused
    // before it is ever requested — the handler's own following would request it first.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>The cover, or null.</summary>
    /// <param name="reference">The plan's <see cref="EpubPlan.Cover"/>.</param>
    /// <param name="beside">Reads a path relative to the document, contained; null when not found.</param>
    /// <param name="fetch">Fetches an https URL; null on any failure. <see cref="FetchAsync"/> in the app.</param>
    public static async Task<EpubCover?> ReadAsync(
        CoverReference? reference,
        Func<string, byte[]?> beside,
        Func<string, CancellationToken, Task<byte[]?>> fetch,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(beside);
        ArgumentNullException.ThrowIfNull(fetch);
        switch (reference)
        {
            case CoverReference.Inline inline:
                return EpubMetadataReader.Recognise(inline.Data);
            case CoverReference.Local local:
                return EpubMetadataReader.Recognise(beside(local.Path));
            case CoverReference.Https https:
                byte[]? data;
                try
                {
                    data = await fetch(https.Url, ct).ConfigureAwait(false);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    data = null;
                }
                return EpubMetadataReader.Recognise(data);
            default:
                return null;
        }
    }

    /// <summary>
    /// GET an https image: <see cref="FetchTimeout"/> in all, at most
    /// <see cref="EpubMetadataReader.MaxCoverBytes"/>, and at most <see cref="MaxRedirects"/>
    /// redirects, each of which must stay https — checked before it is requested. Null on any failure.
    /// </summary>
    public static async Task<byte[]?> FetchAsync(string url, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(FetchTimeout);
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var current) || !IsHttps(current)) return null;
            for (var hop = 0; ; hop++)
            {
                using var response = await Http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400)
                {
                    if (hop >= MaxRedirects) return null;
                    var next = FollowedRedirect(current, response.Headers.Location);
                    if (next is null) return null;
                    current = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode) return null;
                if (response.Content.Headers.ContentLength > EpubMetadataReader.MaxCoverBytes) return null;
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                return await ReadBoundedAsync(stream, EpubMetadataReader.MaxCoverBytes, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Where a redirect from <paramref name="current"/> goes: <paramref name="location"/> resolved
    /// against it when relative, or null when there is none or it is not https — never requested.
    /// </summary>
    public static Uri? FollowedRedirect(Uri current, Uri? location)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (location is null) return null;
        var next = location.IsAbsoluteUri ? location : new Uri(current, location);
        return IsHttps(next) ? next : null;
    }

    static bool IsHttps(Uri uri) => string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    /// <summary>The whole stream, or null once it passes <paramref name="limit"/> bytes.</summary>
    public static async Task<byte[]?> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
