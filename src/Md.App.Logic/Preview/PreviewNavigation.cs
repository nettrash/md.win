namespace Md.App.Logic.Preview;

/// <summary>
/// One request to scroll the preview to a heading (§4.5, §5.6). The id is fresh on every request and
/// the coordinator performs each id exactly once, which is what lets two consecutive picks of the
/// <em>same</em> heading both work — the slug is not the identity, the request is.
/// </summary>
/// <param name="Id">Fresh per request; <c>Guid.NewGuid()</c> at the call site.</param>
/// <param name="Slug">The heading's slug, as <c>MarkdownParser.Slug</c> emits it: letters, digits, <c>-</c>, <c>_</c>.</param>
public sealed record PreviewNavigation(Guid Id, string Slug);
