using System.Globalization;
using Md.Core.Text;

namespace Md.App.Logic.Text;

/// <summary>
/// The pure half of the editor's SmartTyping hooks (docs/smart-typing.md §3.3 "Windows", the
/// retype rule of §3.4, §3.5): everything <c>EditorPane</c> works out before it touches the
/// <c>TextBox</c> that needs no state, kept here so it is tested off Windows. The stateful half —
/// the tracked capital and the override of §3.4, the plan between two events, the flags — is
/// <see cref="TypingHooks"/>; the pane itself is a shell that reads <c>_box.Text</c> and the
/// selection, asks the hooks, and applies the answer with the same <c>Select</c> +
/// <c>SelectedText</c> pair the Tab key uses.
///
/// <para>
/// <b>The letter hook.</b> A WinUI <c>TextBox</c> raises <c>BeforeTextChanging</c> with the text
/// it is about to hold; <see cref="InsertionOverSelection"/> is the diff §3.3 asks for ("exactly
/// one replacement of the current selection"), <see cref="IsWordInsertion"/> the reduction of an
/// insertion to the one scalar <c>SmartTyping.Capitalize</c> judges, and <see cref="Plan"/> the
/// judgement of one such insertion, retype rule included — the override is the hooks' say, made
/// before this is asked. What the pane does with a plan is in <c>EditorPane</c>;
/// <see cref="Applies"/> is the check it makes first.
/// </para>
/// <para>
/// Character classes and whitespace here follow the specification's §0.3–§0.4 to the letter, as
/// the pure functions do: WS19 is the nineteen units spelled out in <see cref="IsWS19"/> (never
/// <c>char.IsWhiteSpace</c>), a lowercase letter is general category <c>Ll</c> of the <i>scalar</i>
/// (never <c>char.IsLower</c>, which answers for a surrogate half), and the uppercase mapping is
/// the pure function's own (<see cref="Upper"/>), so the retype rule and the capital it undoes
/// cannot disagree.
/// </para>
/// </summary>
public static class SmartTypingAdapter
{
    // ── §3.3 Windows: the diff ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The text an edit inserts in place of the selection, or null when the edit is anything else.
    /// <paramref name="newText"/> is one insertion over the selection iff it equals
    /// <c>old[0, start) + inserted + old[start + length, …)</c> for a non-empty <c>inserted</c>. A
    /// deletion, an undo, a replacement elsewhere and an unchanged text all answer null — and none
    /// of them is ever capitalized or mistaken for typing at the selection.
    /// </summary>
    public static string? InsertionOverSelection(string oldText, int selectionStart, int selectionLength, string newText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        if (selectionStart < 0 || selectionLength < 0 || selectionStart + selectionLength > oldText.Length) return null;

        var inserted = newText.Length - oldText.Length + selectionLength;
        if (inserted <= 0) return null;

        var selectionEnd = selectionStart + selectionLength;
        if (!newText.AsSpan(0, selectionStart).SequenceEqual(oldText.AsSpan(0, selectionStart))) return null;
        if (!newText.AsSpan(selectionStart + inserted).SequenceEqual(oldText.AsSpan(selectionEnd))) return null;
        return newText.Substring(selectionStart, inserted);
    }

    // ── §3.3 first paragraph: reduction to one scalar ─────────────────────────────────────────

    /// <summary>
    /// §3.3: an insertion is a <i>word insertion</i> iff it contains no line terminator and no WS19
    /// unit except at most one trailing SP, its first scalar is a Lowercase letter (<c>Ll</c>), and
    /// it contains none of <c>://</c>, <c>www.</c>, <c>@</c>, <c>/</c> — §2.1f applied to the
    /// insertion itself, because the pure function sees only the first scalar and cannot tell a
    /// pasted <c>https://a.b</c> or <c>@nettrash</c> from a word.
    /// </summary>
    public static bool IsWordInsertion(string insertion)
    {
        ArgumentNullException.ThrowIfNull(insertion);
        if (insertion.Length == 0) return false;

        for (var i = 0; i < insertion.Length; i++)
        {
            int u = insertion[i];
            if (u == 0x0A || u == 0x0D) return false;
            if (IsWS19(u) && !(u == 0x20 && i == insertion.Length - 1)) return false;
        }

        if (Category(ScalarAt(insertion, 0, out _)) != UnicodeCategory.LowercaseLetter) return false;

        return !insertion.Contains("://", StringComparison.Ordinal)
            && !insertion.Contains("www.", StringComparison.Ordinal)
            && !insertion.Contains('@')
            && !insertion.Contains('/');
    }

    /// <summary>The first scalar of a non-empty string as its own string: one unit, or a surrogate pair, or a lone surrogate.</summary>
    public static string FirstScalar(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) throw new ArgumentException("Empty", nameof(text));
        ScalarAt(text, 0, out var length);
        return text.Substring(0, length);
    }

    // ── §0.7 through the pure function ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>upper(s)</c> exactly as <c>SmartTyping</c> defines it — simple mapping, one scalar, the
    /// Georgian / Greek-iota / micro-sign exclusions and the vector-decided dotless i — or null
    /// when undefined. §0.9 item 5: on an empty document rule A applies unconditionally, so the
    /// pure function over <c>""</c> is the mapping itself, and there is no second copy of §0.7 to
    /// drift from the first.
    /// </summary>
    public static string? Upper(string scalar)
    {
        ArgumentNullException.ThrowIfNull(scalar);
        return SmartTyping.Capitalize(string.Empty, 0, 0, scalar);
    }

    // ── §3.4, second rule ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Independently of the override, a single lowercase letter typed over a one-scalar selection
    /// whose scalar is that letter's <c>upper</c> is always inserted as typed": the select-and-retype
    /// gesture, which needs no state.
    /// </summary>
    public static bool RetypesOwnCapital(string text, int selectionStart, int selectionLength, string typedScalar)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(typedScalar);
        if (selectionLength <= 0 || selectionStart < 0 || selectionStart + selectionLength > text.Length) return false;

        var selected = text.AsSpan(selectionStart, selectionLength);
        ScalarAt(selected, 0, out var length);
        if (length != selected.Length) return false;

        return Upper(typedScalar) is { } upper && selected.SequenceEqual(upper);
    }

    // ── The letter hook, whole ────────────────────────────────────────────────────────────────

    /// <summary>
    /// What <c>BeforeTextChanging</c> decides for a change the hooks have already tracked and not
    /// waved through as typed, given the text the box holds, its selection and the text it is
    /// about to hold. Null means "let the edit stand as it is": it is not an insertion over the
    /// selection, not a word insertion, the retype rule says "as typed", or the pure function
    /// answers null. Pure: the override (§3.4) is <see cref="CapitalTracker"/>'s decision, taken
    /// by <see cref="TypingHooks.BeforeTextChanging"/> before this is called, and a paste is the
    /// hooks' flag — neither is asked here.
    /// </summary>
    public static CapitalizationPlan? Plan(string oldText, int selectionStart, int selectionLength, string newText)
    {
        var inserted = InsertionOverSelection(oldText, selectionStart, selectionLength, newText);
        if (inserted is null || !IsWordInsertion(inserted)) return null;

        var first = FirstScalar(inserted);
        if (RetypesOwnCapital(oldText, selectionStart, selectionLength, first)) return null;

        var capital = SmartTyping.Capitalize(oldText, selectionStart, selectionStart + selectionLength, first);
        if (capital is null) return null;

        return new CapitalizationPlan(selectionStart, inserted, capital, capital + inserted[first.Length..], selectionLength);
    }

    /// <summary>
    /// Whether a plan still fits the box once the edit it was made for has been applied
    /// (<c>TextChanging</c>): the inserted text sits where the selection was, and the selection is
    /// either the collapsed caret after it or — should the control report it a beat late — still
    /// exactly what the insertion replaced. Anything else — the control transformed the input, the
    /// edit was cancelled, another change got in first — and the plan is dropped, so the worst
    /// outcome of a surprise is a missing capital, never a mangled word.
    /// </summary>
    public static bool Applies(CapitalizationPlan plan, string text, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        var end = plan.Position + plan.Inserted.Length;
        if (plan.Position < 0 || end > text.Length) return false;
        if (!text.AsSpan(plan.Position, plan.Inserted.Length).SequenceEqual(plan.Inserted)) return false;
        return (selectionLength == 0 && selectionStart == end)
            || (selectionStart == plan.Position && selectionLength == plan.Replaced);
    }

    // ── Enter ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Enter hook: <c>SmartTyping.Enter</c> over the box's own text and selection, with the
    /// edit's replacement spelled the way the control spells a line break. The function inserts
    /// U+000A (§0.1); the WinUI <c>TextBox</c> reports and stores U+000D (shell-design.md §3.2),
    /// so the LFs are swapped for CRs before the assignment — one unit for one unit, which is why
    /// <c>Caret</c> needs no adjustment. The text slices the edit copies are the box's own and
    /// already CR, so nothing else in a replacement is touched.
    /// </summary>
    public static SmartTyping.EnterEdit? Enter(string boxText, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(boxText);
        if (selectionStart < 0 || selectionLength < 0 || selectionStart + selectionLength > boxText.Length) return null;
        var edit = SmartTyping.Enter(boxText, selectionStart, selectionStart + selectionLength);
        return edit is { } e ? e with { Replacement = e.Replacement.Replace('\n', '\r') } : null;
    }

    // ── §0.1, §0.3, §0.4: the units the rules above read ──────────────────────────────────────

    /// <summary>
    /// WS19 — U+0009, U+0020, U+00A0, U+1680, U+2000–U+200A, U+200B, U+202F, U+205F, U+3000: the
    /// nineteen units of Foundation's <c>.whitespaces</c>, the one meaning of "whitespace" in the
    /// specification, spelled here as <c>SmartTyping</c> spells it and never as <c>char.IsWhiteSpace</c>.
    /// </summary>
    static bool IsWS19(int u) =>
        u == 0x09 || u == 0x20 || u == 0xA0 || u == 0x1680
        || (u >= 0x2000 && u <= 0x200A) || u == 0x200B || u == 0x202F || u == 0x205F || u == 0x3000;

    /// <summary>The scalar starting at unit <paramref name="i"/> and its unit length; a lone surrogate is its own scalar (category <c>Cs</c>).</summary>
    static int ScalarAt(ReadOnlySpan<char> s, int i, out int length)
    {
        int u = s[i];
        if (u >= 0xD800 && u <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
        {
            length = 2;
            return 0x10000 + ((u - 0xD800) << 10) + (s[i + 1] - 0xDC00);
        }
        length = 1;
        return u;
    }

    /// <summary>The general category of one scalar — the <c>int</c> overload, so a lone surrogate is <c>Surrogate</c>, not a letter.</summary>
    static UnicodeCategory Category(int scalar) => CharUnicodeInfo.GetUnicodeCategory(scalar);
}

/// <summary>
/// What the pane applies after the control has inserted the typed text: replace
/// <c>[Position, Position + Inserted.Length)</c> — the insertion, as it now sits in the box — with
/// <see cref="Replacement"/>, the same text with its first scalar as <see cref="Capital"/>, and put
/// the caret after it. That replacement is the second undo unit of §3.5, the native insertion the
/// first; <see cref="Capital"/> at <see cref="Position"/> is what <see cref="CapitalTracker"/> then
/// tracks (§3.4). <see cref="Replaced"/> is the length of the selection the insertion replaced, for
/// <see cref="SmartTypingAdapter.Applies"/>.
/// </summary>
public readonly record struct CapitalizationPlan(int Position, string Inserted, string Capital, string Replacement, int Replaced = 0);
