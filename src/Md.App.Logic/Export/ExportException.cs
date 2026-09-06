namespace Md.App.Logic.Export;

/// <summary>
/// An export that cannot finish, carrying the message §7.9 puts in the alert body verbatim. The
/// pipeline shows <c>title = "Could not export …"</c> and <c>message = exception.Message</c>, so the
/// string handed to this constructor is user-visible text and lives in <see cref="Strings.Exports"/>,
/// never spelled at the throw site.
/// </summary>
public class ExportException : Exception
{
    public ExportException(string message) : base(message) { }

    public ExportException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// <c>PrintToPdfAsync</c> returned false, or produced nothing (§7.3). Named separately because it is
/// the one export failure that is the browser's own verdict rather than ours — the macOS
/// <c>PDFPaginationError</c>.
/// </summary>
public sealed class PdfPaginationException : ExportException
{
    public PdfPaginationException() : base(Strings.Exports.PdfPagesNotProduced) { }

    public PdfPaginationException(string message) : base(message) { }
}
