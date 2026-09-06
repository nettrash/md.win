namespace Md.App.Logic.Documents;

/// <summary>The line break a file uses on disk. The model in memory is always LF (§3.2).</summary>
public enum NewLine
{
    Lf,
    CrLf,
}

/// <summary>
/// The file's line-break convention, remembered at load and re-applied at save (§3.2). The first
/// break in the file wins; a file with none writes LF, as a new document does (the Mac writes
/// <c>\n</c>). A lone CR is read as a break but never written back — the model has no third form,
/// and a classic-Mac file becomes an LF file the first time it is saved (accepted, §3.2).
/// </summary>
public static class LineEndings
{
    public const string Lf = "\n";
    public const string CrLf = "\r\n";

    /// <summary>The sequence a <see cref="NewLine"/> writes.</summary>
    public static string Text(NewLine newLine) => newLine == NewLine.CrLf ? CrLf : Lf;

    /// <summary>
    /// The convention of <paramref name="text"/>: the first break decides, and only CR+LF is CRLF —
    /// a lone CR, a lone LF and a file with no break at all are all LF.
    /// </summary>
    public static NewLine Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') return NewLine.Lf;
            if (text[i] == '\r') return i + 1 < text.Length && text[i + 1] == '\n' ? NewLine.CrLf : NewLine.Lf;
        }
        return NewLine.Lf;
    }

    /// <summary>Any mixture of CRLF / CR / LF to the model's LF. Order matters: CRLF first, or each pair becomes two breaks.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.IndexOf('\r') < 0 ? text : text.Replace(CrLf, Lf).Replace('\r', '\n');
    }

    /// <summary>The bytes' convention applied to LF text on the way out. Input must already be LF (<see cref="Normalize"/>).</summary>
    public static string Apply(string lfText, NewLine newLine)
    {
        ArgumentNullException.ThrowIfNull(lfText);
        return newLine == NewLine.CrLf ? lfText.Replace(Lf, CrLf) : lfText;
    }
}
