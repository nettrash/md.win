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

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = FetchTimeout,
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
    /// GET an https image: at most <see cref="FetchTimeout"/> and
    /// <see cref="EpubMetadataReader.MaxCoverBytes"/>, and a redirect must not leave https for
    /// plain http. Null on any failure.
    /// </summary>
    public static async Task<byte[]?> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.RequestMessage?.RequestUri is not { } final
                || !string.Equals(final.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                return null;
            }
            if (response.Content.Headers.ContentLength > EpubMetadataReader.MaxCoverBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await ReadBoundedAsync(stream, EpubMetadataReader.MaxCoverBytes, ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

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
