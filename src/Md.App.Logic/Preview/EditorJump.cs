namespace Md.App.Logic.Preview;

/// <summary>
/// The editor half of a Contents or Notes pick (§3.3): put the caret at the start of a line and let
/// the <c>TextBox</c> scroll it into view. Same one-shot shape as <see cref="PreviewNavigation"/> —
/// a fresh id per request, performed once — so picking the same note twice works.
/// </summary>
/// <param name="Id">Fresh per request.</param>
/// <param name="Line">0-based line index into the editor's text.</param>
public sealed record EditorJump(Guid Id, int Line);

/// <summary>
/// "Performed once per id", the rule both jump kinds share, kept out of the code-behind so it is
/// testable. <c>EditorPane</c> claims a jump and then does the WinUI half (focus, select, one
/// dispatcher turn later — a Notes jump may have just switched Preview → Edit and the pane is not
/// laid out yet); a coordinator rebuilt by a Split → Edit → Split round-trip must not replay the
/// last request, which is what <see cref="Claim"/> prevents.
/// </summary>
public sealed class EditorJumpTracker
{
    Guid? _last;

    /// <summary>True the first time an id is seen, false ever after. A null request is never claimed.</summary>
    public bool Claim(EditorJump? jump)
    {
        if (jump is null || jump.Id == _last) return false;
        _last = jump.Id;
        return true;
    }
}
