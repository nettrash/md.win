using System.Globalization;
using Md.Core.Export;

namespace Md.App.Logic.Export;

/// <summary>
/// The name each save picker is opened with, and the picker's own type rows (§7, §6.1). Every stem
/// goes through <see cref="ExportFileNames"/>: the family's split-join sanitiser plus this port's
/// Windows layer (control characters, trailing dots and spaces, the reserved device names), so a
/// title a Mac would happily save cannot open a picker Windows then refuses.
/// </summary>
public static class ExportNames
{
    public const string PdfExtension = ".pdf";
    public const string HtmlExtension = ".html";
    public const string EpubExtension = ".epub";
    public const string LaTeXExtension = ".tex";
    public const string SvgExtension = ".svg";
    public const string MarkdownExtension = ".md";

    /// <summary>One labelled type row — every export picker offers exactly one.</summary>
    public static IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> Choices(string label, string extension) =>
        [(label, (IReadOnlyList<string>)[extension])];

    public static IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> PdfChoices { get; } = Choices(Strings.Exports.Pdf, PdfExtension);
    public static IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> HtmlChoices { get; } = Choices(Strings.Exports.Html, HtmlExtension);
    public static IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> EpubChoices { get; } = Choices(Strings.Exports.Epub, EpubExtension);
    public static IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> LaTeXChoices { get; } = Choices(Strings.Exports.LaTeX, LaTeXExtension);
    public static IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> SvgChoices { get; } = Choices(Strings.Exports.Svg, SvgExtension);

    public static string Pdf(string title) => ExportFileNames.Sanitized(title, PdfExtension);
    public static string Html(string title) => ExportFileNames.Sanitized(title, HtmlExtension);
    public static string Epub(string title) => ExportFileNames.Sanitized(title, EpubExtension);
    public static string LaTeX(string title) => ExportFileNames.Sanitized(title, LaTeXExtension);

    /// <summary>The temp copy Share ▸ Source… makes of an unsaved document.</summary>
    public static string Source(string title) => ExportFileNames.Sanitized(title, MarkdownExtension);

    /// <summary>
    /// One diagram: <c>{sanitised title}-{ordinal + 1}.svg</c>, the ordinal made 1-based for the
    /// reader, as the Mac writes it. The family stem comes first, the suffix next, and the Windows
    /// layer last — in that order a title of <c>CON</c> becomes the perfectly ordinary
    /// <c>CON-1.svg</c>, where sanitising before appending would have produced <c>CON--1.svg</c>.
    /// </summary>
    public static string DiagramSvg(string title, int ordinal) =>
        ExportFileNames.Sanitized(
            ExportFileNames.PortableStem(title) + "-" + (ordinal + 1).ToString(CultureInfo.InvariantCulture),
            SvgExtension);

    /// <summary>The TextBundle is a folder, so this is a folder name, not a file name (§7.7).</summary>
    public static string TextBundleFolder(string title) =>
        ExportFileNames.Sanitized(title, TextBundle.BundleExtension);
}
