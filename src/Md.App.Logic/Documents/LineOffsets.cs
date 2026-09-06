namespace Md.App.Logic.Documents;

/// <summary>
/// Caret arithmetic for the Contents / Notes jumps (§3.3). The Swift routine verbatim, already in
/// UTF-16 units — which is what <c>TextBox.SelectionStart</c> indexes — and run over the string the
/// control reports (with its <c>\r</c>s), because lines are 1:1 between that form and the model's.
/// </summary>
public static class LineOffsets
{
    /// <summary>
    /// The UTF-16 offset of the first character of <paramref name="line"/> (0-based). <c>\n</c>,
    /// <c>\r</c> and <c>\r\n</c> each count once; U+2028 / U+2029 deliberately do not, because the
    /// parser that produced the line number does not count them either. A line past the end gives
    /// the start of the last existing line, never an out-of-range index.
    /// </summary>
    public static int OffsetOfLine(int line, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var offset = 0;
        var remaining = line;
        var i = 0;
        while (remaining > 0 && i < text.Length)
        {
            var ch = text[i];
            i++;
            if (ch == '\n')
            {
                remaining--;
                offset = i;
            }
            else if (ch == '\r')
            {
                if (i < text.Length && text[i] == '\n') i++;
                remaining--;
                offset = i;
            }
        }
        return offset;
    }
}
