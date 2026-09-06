namespace Md.App.Logic.Documents;

/// <summary>
/// The adapter between the WinUI <c>TextBox</c> and the session (§3.2). <c>TextBox.Text</c> reports
/// every line break as <c>\r</c>, whatever was assigned, so the model is normalised on the way in
/// and left alone on the way out — correct whether or not a given WinUI build converts.
/// </summary>
public static class EditorText
{
    /// <summary>What the control reports, as the model wants it: LF only.</summary>
    public static string FromTextBox(string boxText) => LineEndings.Normalize(boxText);

    /// <summary>
    /// What to assign to the control. The identity: the control normalises on assignment, and a
    /// round trip through <see cref="FromTextBox"/> gives the model back unchanged. Named so a
    /// reader of the pane sees both directions and neither is "just the string".
    /// </summary>
    public static string ToTextBox(string modelText)
    {
        ArgumentNullException.ThrowIfNull(modelText);
        return modelText;
    }

    /// <summary>
    /// The selection to restore after an external replace (Revert, Reload from Disk, an example
    /// load), clamped to the new text the Mac's way: the caret first, then what is left of the
    /// length. Never throws for a caret past the end of a shorter document.
    /// </summary>
    public static (int Start, int Length) ClampSelection(int start, int length, int textLength)
    {
        if (textLength < 0) throw new ArgumentOutOfRangeException(nameof(textLength));
        var s = Math.Clamp(start, 0, textLength);
        var l = Math.Clamp(length, 0, textLength - s);
        return (s, l);
    }
}
