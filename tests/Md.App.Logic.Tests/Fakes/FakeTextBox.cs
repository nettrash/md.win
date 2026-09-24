namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// A WinUI <c>TextBox</c> as the typing hooks see it: text and selection, the three change events
/// in the control's order (<c>BeforeTextChanging</c> with the text about to be held, then the
/// change, then <c>TextChanging</c> and <c>TextChanged</c>), an undo stack of one unit per change
/// that a <c>Text =</c> assignment clears, Undo / Redo that replay a unit through the same events
/// without recording a new one, Cut as the deletion it is, and the <c>Paste</c> event Ctrl+V raises. Every edit that is not a
/// history replay clears the redo stack, as the control's does — which is what a capital applied
/// to a Redo destroys. The one thing this cannot settle is whether a real RichEdit gives a
/// <c>SelectedText</c> assignment made inside <c>TextChanging</c> its own undo unit (shell-design.md
/// §3.6 keeps that on the Windows checklist); here it always does.
/// </summary>
public sealed class FakeTextBox
{
    readonly record struct Unit(string Before, int Start, int Length, string After, int Caret);

    readonly Stack<Unit> _undo = new();
    readonly Stack<Unit> _redo = new();

    public string Text { get; private set; } = string.Empty;
    public int SelectionStart { get; private set; }
    public int SelectionLength { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoUnits => _undo.Count;
    public int RedoUnits => _redo.Count;

    /// <summary>The text the box is about to hold; the box itself still reports the old text and selection.</summary>
    public event Action<string>? BeforeTextChanging;
    /// <summary>Synchronous, once the box holds the new text and reports the caret after the change.</summary>
    public event Action? TextChanging;
    public event Action? TextChanged;
    /// <summary>What Ctrl+V and the context menu raise before the clipboard text goes in.</summary>
    public event Action? Paste;

    public void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, Text.Length);
        SelectionStart = start;
        SelectionLength = Math.Clamp(length, 0, Text.Length - start);
    }

    /// <summary>The user types: the control replaces the selection and puts the caret after the insertion — one undo unit.</summary>
    public void Type(string text) => Edit(SelectionStart, SelectionLength, text);

    /// <summary>Deletes the selection, or the one scalar before the caret — a surrogate pair as one, as RichEdit does.</summary>
    public void Backspace()
    {
        if (SelectionLength > 0) Edit(SelectionStart, SelectionLength, string.Empty);
        else if (SelectionStart > 0)
        {
            var length = SelectionStart >= 2 && char.IsLowSurrogate(Text[SelectionStart - 1]) && char.IsHighSurrogate(Text[SelectionStart - 2]) ? 2 : 1;
            Edit(SelectionStart - length, length, string.Empty);
        }
    }

    /// <summary>Ctrl+X and Edit ▸ Cut: the selection removed as one edit, through the same events a deletion raises. The clipboard is not modelled.</summary>
    public void Cut()
    {
        if (SelectionLength > 0) Edit(SelectionStart, SelectionLength, string.Empty);
    }

    /// <summary>Deletes the selection, or the one scalar after the caret.</summary>
    public void Delete()
    {
        if (SelectionLength > 0) Edit(SelectionStart, SelectionLength, string.Empty);
        else if (SelectionStart < Text.Length)
        {
            var length = SelectionStart + 1 < Text.Length && char.IsHighSurrogate(Text[SelectionStart]) && char.IsLowSurrogate(Text[SelectionStart + 1]) ? 2 : 1;
            Edit(SelectionStart, length, string.Empty);
        }
    }

    /// <summary>The pane's replacement path: an edit like any other — its own undo unit, and the redo stack is gone.</summary>
    public string SelectedText
    {
        get => Text.Substring(SelectionStart, SelectionLength);
        set => Edit(SelectionStart, SelectionLength, value);
    }

    /// <summary>
    /// The clipboard going in. Ctrl+V and the context menu raise <see cref="Paste"/> first; whether
    /// <c>PasteFromClipboard()</c> does is undocumented, so the caller says which control it is
    /// modelling. An empty clipboard changes nothing.
    /// </summary>
    public void PasteText(string clipboard, bool raisesPasteEvent)
    {
        if (raisesPasteEvent) Paste?.Invoke();
        if (clipboard.Length > 0) Type(clipboard);
    }

    /// <summary>Restores the unit's text with the caret where the replaced range ended — after restored text, not selecting it.</summary>
    public void Undo()
    {
        if (_undo.Count == 0) return;
        var unit = _undo.Pop();
        _redo.Push(unit);
        Change(unit.Before, unit.Start + unit.Length, 0);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var unit = _redo.Pop();
        _undo.Push(unit);
        Change(unit.After, unit.Caret, 0);
    }

    public void ClearUndoRedoHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary><c>Text = value</c>: the whole text replaced, the undo history cleared, the selection clamped.</summary>
    public void SetText(string value)
    {
        ClearUndoRedoHistory();
        var start = Math.Min(SelectionStart, value.Length);
        Change(value, start, Math.Min(SelectionLength, value.Length - start));
    }

    void Edit(int start, int length, string replacement)
    {
        var after = string.Concat(Text.AsSpan(0, start), replacement, Text.AsSpan(start + length));
        var caret = start + replacement.Length;
        _undo.Push(new Unit(Text, start, length, after, caret));
        _redo.Clear();
        Change(after, caret, 0);
    }

    void Change(string newText, int start, int length)
    {
        BeforeTextChanging?.Invoke(newText);
        Text = newText;
        SelectionStart = start;
        SelectionLength = length;
        TextChanging?.Invoke();
        TextChanged?.Invoke();
    }
}
