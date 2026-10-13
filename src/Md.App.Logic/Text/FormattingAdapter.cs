using Md.App.Logic.Commands;
using Md.Core.Text;

namespace Md.App.Logic.Text;

/// <summary>
/// The editor's half of the formatting commands (docs/formatting.md), kept off Windows so it is
/// tested: which <see cref="FormatOp"/> a command runs, the edit it makes of the box's own text, and
/// whether a keystroke wraps the selection.
/// </summary>
/// <remarks>
/// The WinUI <c>TextBox</c> stores a line break as U+000D (shell-design.md §3.2) and
/// <see cref="MarkdownFormatting"/> reads U+000A, so the box's text is handed over with CR swapped
/// for LF and the replacement handed back with LF swapped for CR — one unit for one, so every offset
/// holds, exactly as <see cref="SmartTypingAdapter.Enter"/> does it. <c>EditorPane</c> applies the
/// answer with one <c>Select</c> + <c>SelectedText</c> pair: one undo unit.
/// </remarks>
public static class FormattingAdapter
{
    /// <summary>The <see cref="FormatOp"/> a Format command runs; null for any other command.</summary>
    public static FormatOp? OpFor(CommandId id) => id switch
    {
        CommandId.Bold => FormatOp.Bold,
        CommandId.Italic => FormatOp.Italic,
        CommandId.InlineCode => FormatOp.Code,
        CommandId.Strikethrough => FormatOp.Strikethrough,
        CommandId.Link => FormatOp.Link,
        CommandId.Heading1 => FormatOp.Heading1,
        CommandId.Heading2 => FormatOp.Heading2,
        CommandId.Heading3 => FormatOp.Heading3,
        CommandId.Heading4 => FormatOp.Heading4,
        CommandId.Heading5 => FormatOp.Heading5,
        CommandId.Heading6 => FormatOp.Heading6,
        CommandId.BulletList => FormatOp.BulletList,
        CommandId.TaskList => FormatOp.TaskList,
        CommandId.Quote => FormatOp.Quote,
        CommandId.Indent => FormatOp.Indent,
        CommandId.Outdent => FormatOp.Outdent,
        _ => null,
    };

    /// <summary>Every Format command, in table order — what the windows register.</summary>
    public static IReadOnlyList<CommandId> Commands { get; } =
        [.. CommandTable.All.Where(spec => OpFor(spec.Id) is not null).Select(spec => spec.Id)];

    /// <summary>
    /// The edit <paramref name="op"/> makes of the box's text and selection, in the box's own
    /// spelling (CR line breaks), or null for none.
    /// </summary>
    public static FormatEdit? Edit(string boxText, int selectionStart, int selectionLength, FormatOp op)
    {
        ArgumentNullException.ThrowIfNull(boxText);
        if (!Fits(boxText, selectionStart, selectionLength)) return null;
        var edit = MarkdownFormatting.Format(boxText.Replace('\r', '\n'), selectionStart, selectionStart + selectionLength, op);
        return edit is { } e ? e with { Text = e.Text.Replace('\n', '\r') } : null;
    }

    /// <summary>
    /// <c>BeforeTextChanging</c>'s question: is this change one of <c>* _ ` $</c> typed over a
    /// non-empty selection? Then the wrap it becomes (box spelling, over the original selection —
    /// <see cref="AfterInsertion"/> moves it onto the inserted character), else null. Exactly one unit replacing the whole selection — a paste of "*" is the caller's to
    /// exclude, as are a composition and history.
    /// </summary>
    public static FormatEdit? Wrap(string boxText, int selectionStart, int selectionLength, string newText)
    {
        ArgumentNullException.ThrowIfNull(boxText);
        ArgumentNullException.ThrowIfNull(newText);
        if (selectionLength <= 0 || !Fits(boxText, selectionStart, selectionLength)) return null;
        var inserted = SmartTypingAdapter.InsertionOverSelection(boxText, selectionStart, selectionLength, newText);
        // Asked on every keystroke over a selection: anything but the four is answered before the
        // box's text is copied.
        if (inserted is not ("*" or "_" or "`" or "$")) return null;
        var edit = MarkdownFormatting.WrapTyped(boxText.Replace('\r', '\n'), selectionStart, selectionStart + selectionLength, inserted);
        return edit is { } e ? e with { Text = e.Text.Replace('\n', '\r') } : null;
    }

    /// <summary>
    /// The wrap as the box will need it once the control has inserted the typed character itself:
    /// the same replacement and selection, over <c>[Start, Start + 1)</c> — the one character that
    /// now stands where the selection was. The control's insertion is the first undo unit, this the
    /// second, as with SmartTyping's capital (§3.5): the keystroke is never cancelled, so it can
    /// never be lost.
    /// </summary>
    public static FormatEdit AfterInsertion(FormatEdit wrap) => wrap with { End = wrap.Start + 1 };

    /// <summary>
    /// Whether <see cref="AfterInsertion"/>'s edit still fits the box: the typed character is at
    /// <c>Start</c> and the caret is collapsed just after it. Anything else — the control transformed
    /// the input, another change got in first — and the plan is dropped, leaving the character typed.
    /// </summary>
    public static bool StillApplies(FormatEdit pending, string boxText, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(boxText);
        return pending.End == pending.Start + 1
            && pending.Start >= 0 && pending.End <= boxText.Length
            && boxText[pending.Start] == pending.Text[0]
            && selectionLength == 0 && selectionStart == pending.End;
    }

    static bool Fits(string text, int start, int length) => start >= 0 && length >= 0 && start + length <= text.Length;
}

/// <summary>
/// The AltGr guard for the heading chords (docs/formatting.md §5). On many European layouts AltGr
/// is Ctrl+Alt, so AltGr+2 — which types ² on German, @ on Polish — arrives as Ctrl+Alt+2, the
/// Heading 2 accelerator. A heading chord must not swallow a character the layout types.
/// </summary>
/// <remarks>
/// The rule: when the layout produces no character for Ctrl+Alt+that key, the chord is always a
/// heading. When it does, AltGr — the right Alt key, with the left one up — is typing, and the
/// accelerator lets the key through; the left Alt held with Ctrl is a deliberate chord and formats.
/// The two layout facts are Win32's (<c>ToUnicodeEx</c>, <c>GetKeyState</c>) and asked in
/// <c>Md.App</c>; the decision is here so it is tested.
/// </remarks>
public static class HeadingChordGuard
{
    /// <summary>Whether a Ctrl+Alt chord is one of the six headings, the only ones the guard looks at.</summary>
    public static bool IsHeading(CommandId id) =>
        id is CommandId.Heading1 or CommandId.Heading2 or CommandId.Heading3
            or CommandId.Heading4 or CommandId.Heading5 or CommandId.Heading6;

    /// <summary>True: run the heading. False: let the key through — AltGr is typing a character.</summary>
    public static bool Formats(bool layoutTypesCharacter, bool rightAltDown, bool leftAltDown) =>
        !layoutTypesCharacter || leftAltDown || !rightAltDown;
}
