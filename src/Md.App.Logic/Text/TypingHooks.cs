using Md.App.Logic.Commands;
using Md.App.Logic.Settings;
using Md.Core.Text;

namespace Md.App.Logic.Text;

/// <summary>
/// The per-editor state of the two SmartTyping hooks (shell-design.md §3.6): the half of the
/// editor's typing that is a state machine rather than a pure function — the tracked capital and
/// the override of smart-typing.md §3.4 (<see cref="CapitalTracker"/>), the plan carried from
/// <c>BeforeTextChanging</c> to <c>TextChanging</c>, the two settings, and the five flags that
/// decide whether a change is judged at all. Kept out of <c>EditorPane</c> so a fake TextBox can
/// drive it in the control's own event order, with the session's echo of every keystroke and the
/// control's Undo / Redo in the loop: that is where the first cut of the pane lost the override —
/// its <c>SetText</c> cleared the gesture on the echo of identical text, and its
/// <c>BeforeTextChanging</c> judged a Redo as typing — and the adapter's tests, which see only the
/// pure functions, could not tell. The pane owns one of these, feeds it <c>_box.Text</c> and the
/// selection at each event, and applies what it answers; nothing here touches a control.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tracking sees every edit; judging sees some.</b> <see cref="BeforeTextChanging"/> turns each
/// change the control reports into one <see cref="TextEdit"/> and hands it to the tracker first —
/// typing, Backspace, Delete, Cut, a paste, a composition update, and the control's own Undo and
/// Redo, which bypass the judging (§3.5) but change the text like anything else. Only then does it
/// decide whether the change is judged for a capital. The two edits this class makes itself — the
/// capital swap and the Enter edit — are applied under <see cref="Applying"/>, so the events they
/// raise are skipped, and are reported to the tracker by <see cref="TextChanging"/> and
/// <see cref="Enter"/> instead. <c>SetText</c>'s echo of identical text raises no event at all; a
/// text that differs is assigned under <see cref="Replacing"/> and <see cref="ExternalText"/> has
/// already cleared the tracker (§3.4: an external replacement clears both). An external replacement
/// whose text equals the box's — another article holding the same text, a reload the disk agrees
/// with — is one <c>SetText</c> cannot see, and the windows report it through <see cref="Reset"/>
/// on the session's <c>UndoGeneration</c>, beside the control's <c>ClearUndoRedoHistory()</c>.
/// </para>
/// <para>
/// <b>Which unit a key removed.</b> A shrink at a collapsed caret is the units before it (Backspace,
/// Ctrl+Backspace) or after it (Delete, Ctrl+Delete), and inside a run of identical units the text
/// alone cannot tell which — <c>II</c> → <c>I</c> is either. <see cref="AnnounceDeletion"/> (the key
/// seen in <c>PreviewKeyDown</c>, settled a turn later like the history flag) hands
/// <see cref="TextEdit.Between"/> the side, so that removing md's <c>I</c> next to the writer's arms
/// the override and removing the writer's leaves the capital tracked.
/// </para>
/// <para>
/// <b>Flags.</b> A composition (<see cref="CompositionStarted"/> … <see cref="CompositionEnded"/>)
/// skips both features. A paste (<see cref="AnnouncePaste"/>: the control's <c>Paste</c> event for
/// Ctrl+V and the context menu, and <c>EditorPane.Paste()</c> for the Edit row, which does not rely
/// on <c>PasteFromClipboard()</c> raising the event) makes the change it produces never capitalized;
/// it is consumed by that change and settled a turn later in case the clipboard had nothing to give.
/// A history operation (<see cref="AnnounceHistory"/>: Ctrl+Z / Ctrl+Y seen in <c>PreviewKeyDown</c>,
/// or <c>EditorPane.Undo()</c> / <c>Redo()</c> for the Edit rows) is not typing: the change it
/// produces is not judged — a Redo that re-inserts a letter is not capitalized into a new edit that
/// would wipe the redo stack — but it is tracked, which is how Undo of a capital arms the override
/// (§3.4: the undo is an edit that removed the capital). It is settled a turn later, never consumed,
/// because one history step may report more than one change.
/// </para>
/// </remarks>
public sealed class TypingHooks
{
    readonly CapitalTracker _tracker = new();
    CapitalizationPlan? _pending;
    // Written from whichever thread the settings store raises Changed on, read on the input thread.
    volatile bool _continueLists = SettingsKeys.ContinueListsDefault;
    volatile bool _capitalizeSentences = SettingsKeys.CapitalizeSentencesDefault;
    bool _composing;
    bool _pasting;
    bool _history;
    bool _applying;
    bool _replacing;
    DeleteDirection _deleting;

    /// <summary>The §3.4 state — the tracked capital, or the armed override — for inspection; the hooks drive it.</summary>
    public CapitalTracker Tracker => _tracker;

    public bool ContinueLists => _continueLists;
    public bool CapitalizeSentences => _capitalizeSentences;
    public bool IsComposing => _composing;
    public bool IsPasting => _pasting;
    public bool IsHistory => _history;
    /// <summary>The side of the caret the announced Backspace / Delete removes, or Unknown when none is announced.</summary>
    public DeleteDirection Deleting => _deleting;
    /// <summary><c>SetText</c>'s own assignment is in progress: its <c>TextChanged</c> is not an edit.</summary>
    public bool IsReplacing => _replacing;
    /// <summary>A plan is waiting for <c>TextChanging</c>.</summary>
    public bool HasPendingPlan => _pending is not null;

    /// <summary>
    /// What the last <see cref="BeforeTextChanging"/> / <see cref="TextChanging"/> pair decided, in
    /// words, for the self-test's report: a capital that did not appear on a real keystroke is
    /// otherwise indistinguishable from one the rules declined. Never read by the app.
    /// </summary>
    public string LastDecision { get; private set; } = "";

    // ── settings (smart-typing.md §3.1) ───────────────────────────────────────────────────────

    /// <summary>Both bools, re-read on every store change for their keys. A function whose setting is off is never called, and its gesture has nothing to undo.</summary>
    public void Configure(TypingSettings typing)
    {
        _continueLists = typing.ContinueLists;
        _capitalizeSentences = typing.CapitalizeSentences;
        if (!typing.CapitalizeSentences) _tracker.Clear();
    }

    // ── flags ─────────────────────────────────────────────────────────────────────────────────

    public void CompositionStarted() => _composing = true;

    public void CompositionEnded() => _composing = false;

    /// <summary>A paste is about to land (§3.3): the change it produces is never capitalized.</summary>
    public void AnnouncePaste() => _pasting = true;

    /// <summary>One dispatcher turn after the announcement: a paste that produced no change must not skip the next keystroke.</summary>
    public void SettlePaste() => _pasting = false;

    /// <summary>Undo or Redo is about to run: the change it produces is tracked but not judged (§3.5).</summary>
    public void AnnounceHistory() => _history = true;

    /// <summary>One dispatcher turn after the announcement: a history step with nothing to do must not skip the next keystroke.</summary>
    public void SettleHistory() => _history = false;

    /// <summary>
    /// Backspace or Delete is about to run (seen in <c>PreviewKeyDown</c>, like the history chords):
    /// the shrink the control reports is anchored at the caret on that side, which is the only way to
    /// tell which of two identical units it removed. Never handled — the control deletes as it always
    /// did; Unknown announces nothing.
    /// </summary>
    public void AnnounceDeletion(DeleteDirection direction) => _deleting = direction;

    /// <summary>One dispatcher turn after the announcement, never consumed: a key with nothing to delete reports no change, and a word deletion is still one.</summary>
    public void SettleDeletion() => _deleting = DeleteDirection.Unknown;

    /// <summary>
    /// The side a key removes from, by virtual key: Backspace (<c>VK_BACK</c>) the units before the
    /// caret, Delete (<c>VK_DELETE</c>) the units after it, with any modifier (Ctrl+Backspace is a word
    /// backward; Shift+Delete is Cut, a selection the text settles by itself). Anything else Unknown.
    /// </summary>
    public static DeleteDirection DeletionKey(int virtualKey) => virtualKey switch
    {
        VirtualKeys.Back => DeleteDirection.Backward,
        VirtualKeys.Delete => DeleteDirection.Forward,
        _ => DeleteDirection.Unknown,
    };

    /// <summary>
    /// Whether a key chord is the control's own Undo / Redo — Ctrl+Z and Ctrl+Y, with or without
    /// Shift (Ctrl+Shift+Z is Redo on many keyboards) — and so announces a history operation from
    /// <c>PreviewKeyDown</c>. Alt excludes it: AltGr reports as Ctrl+Alt, and AltGr+Z is a letter
    /// (<c>ż</c> on the Polish programmer's layout) that must be judged like any other.
    /// </summary>
    public static bool IsHistoryChord(int virtualKey, bool control, bool alt) =>
        control && !alt && virtualKey is VirtualKeys.Z or VirtualKeys.Y;

    /// <summary>
    /// Our own replacement — the Enter edit, the capital — applied with the flag up so the events
    /// it raises are neither judged nor tracked again (its caller has already reported it).
    /// </summary>
    public void Applying(Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _applying = true;
        try
        {
            apply();
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// An edit md makes to the text on the writer's behalf that is not typing: the find bar's
    /// Replace and Replace All (shell-design.md §3.5). The text going in is the writer's own, typed
    /// into the replacement box, so it must land exactly as typed — never judged for a capital, and
    /// never tracked as though the writer had typed it there. The tracking is therefore cleared
    /// first, the way a Revert or a Reload clears it (§3.4: an external replacement clears both the
    /// tracked capital and the override), and the replacement itself runs under
    /// <see cref="Applying"/>, whose events are skipped. What is <i>not</i> cleared is the control's
    /// undo history: a replace is one undoable step like any other edit, which is the difference
    /// between this and <see cref="Reset"/> beside <c>ClearUndoRedoHistory()</c>.
    /// </summary>
    public void ExternalEdit(Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        Reset();
        Applying(apply);
    }

    /// <summary><c>SetText</c>'s assignment, with the flag up so its events are neither judged nor reported as an edit.</summary>
    public void Replacing(Action replace)
    {
        ArgumentNullException.ThrowIfNull(replace);
        _replacing = true;
        try
        {
            replace();
        }
        finally
        {
            _replacing = false;
        }
    }

    // ── SetText ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>SetText(incoming)</c> over a box holding <paramref name="current"/>: true when the text
    /// differs and is to be assigned — an external replacement, which clears the tracked capital,
    /// the override and any pending plan (§3.4) — and false for the echo of identical text, which
    /// changes nothing and leaves the state as it is. Both strings are compared as given; the pane
    /// passes its LF text and the session's, as it always did.
    /// </summary>
    public bool ExternalText(string current, string incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        if (string.Equals(current, incoming, StringComparison.Ordinal)) return false;
        Reset();
        return true;
    }

    /// <summary>
    /// An external replacement the text cannot show: the windows' signal for one is the session's
    /// <c>UndoGeneration</c> — a fresh document, Revert, Reload from Disk, an article switch — and
    /// they report it here (<c>EditorPane.ResetHistory()</c>, beside the control's
    /// <c>ClearUndoRedoHistory()</c>) because the new text may equal the old: another article
    /// holding the same text, a reload of a file the disk agrees with. <see cref="ExternalText"/>
    /// sees only the echo then. Clears the tracked capital, the override and any pending plan
    /// (§3.4); the flags, which describe the input in flight, are left alone.
    /// </summary>
    public void Reset()
    {
        _tracker.Clear();
        _pending = null;
    }

    // ── the hooks ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enter, from <c>KeyDown</c> with no modifier (the pane checks the keyboard): the continuation
    /// edit to apply as one replacement, or null when not ours — the setting off, a composition in
    /// progress, or the function declining. The edit is reported to the tracker on the way, since
    /// the pane applies it under <see cref="Applying"/> and the events are skipped: it may sit
    /// before the capital (shifting it), remove it (the exit rule clearing an item that holds it),
    /// or be an insertion elsewhere than an armed <c>q</c> (§3.4).
    /// </summary>
    public SmartTyping.EnterEdit? Enter(string text, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!_continueLists || _composing) return null;
        var edit = SmartTypingAdapter.Enter(text, selectionStart, selectionLength);
        if (edit is not { } e) return null;
        _tracker.Edited(new TextEdit(e.Location, e.Length, e.Replacement), wordInsertion: false);
        return e;
    }

    /// <summary>
    /// <c>BeforeTextChanging</c>: the box's text and selection as they stand, and the text the box
    /// is about to hold. The change is reduced to one <see cref="TextEdit"/> and tracked (§3.4)
    /// whatever it is, unless it is our own replacement or an external assignment, which their
    /// callers report. Then the plan <see cref="TextChanging"/> applies is decided, or none: a
    /// composition and a history operation are never judged; a paste is judged with the paste flag
    /// (consumed here); the setting off ends it; an insertion the tracker says goes in as typed
    /// ends it; the rest is <see cref="SmartTypingAdapter.Plan"/>.
    /// </summary>
    public void BeforeTextChanging(string text, int selectionStart, int selectionLength, string newText)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(newText);
        _pending = null;
        if (_applying || _replacing)
        {
            LastDecision = "skipped: own change";
            return;
        }

        // The paste flag is consumed by the first judged change after the announcement, as before
        // the tracker existed; a change inside a composition or a history step leaves it alone.
        var judged = !_composing && !_history;
        var pasted = false;
        if (judged)
        {
            pasted = _pasting;
            _pasting = false;
        }

        if (TextEdit.Between(text, selectionStart, selectionLength, newText, _deleting) is not { } edit)
        {
            LastDecision = "no edit";
            return;
        }
        var word = edit.IsInsertion && SmartTypingAdapter.IsWordInsertion(edit.Inserted);
        var asTyped = _tracker.Edited(edit, word);

        // The same gate as one condition, spelled out so the decision can be named.
        if (!judged)
        {
            LastDecision = _composing ? "skipped: composition in progress" : "skipped: history operation";
            return;
        }
        if (!_capitalizeSentences)
        {
            LastDecision = "off";
            return;
        }
        if (asTyped)
        {
            LastDecision = "as typed: the override";
            return;
        }
        if (!word)
        {
            LastDecision = "not a word insertion";
            return;
        }
        if (pasted)
        {
            LastDecision = "pasted";
            return;
        }
        _pending = SmartTypingAdapter.Plan(text, selectionStart, selectionLength, newText);
        LastDecision = _pending is { } planned ? $"plan: {planned.Capital} at {planned.Position}" : "no capital: the function declined";
    }

    /// <summary>
    /// <c>TextChanging</c>: the box's text and selection now that it holds the change. The plan to
    /// apply — replace <c>[Position, Position + Inserted.Length)</c> with <c>Replacement</c> and put
    /// the caret after it — once <see cref="SmartTypingAdapter.Applies"/> confirms the insertion
    /// landed where it was decided; the capital it produces becomes the tracked one (§3.4, §3.5).
    /// Null otherwise. The plan is consumed either way.
    /// </summary>
    public CapitalizationPlan? TextChanging(string text, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_pending is not { } plan) return null;
        _pending = null;
        if (_applying || _replacing)
        {
            LastDecision = "dropped: own change";
            return null;
        }
        if (!SmartTypingAdapter.Applies(plan, text, selectionStart, selectionLength))
        {
            LastDecision = $"dropped: the insertion did not land as planned (selection {selectionStart}+{selectionLength}, text length {text.Length})";
            return null;
        }
        _tracker.Produced(plan.Position, plan.Capital);
        LastDecision = $"applied: {plan.Capital} at {plan.Position}";
        return plan;
    }
}

/// <summary>
/// One change to the box's text as a single replacement: <c>[Start, Start + Length)</c> of the old
/// text became <see cref="Inserted"/>. A deletion has an empty <see cref="Inserted"/>; an insertion
/// has <see cref="Length"/> 0; anything the control does — typing over a selection, Backspace,
/// Delete, Cut, a paste, a composition update, Undo, Redo — is one of these. Offsets are UTF-16
/// units into the old text (§0.1).
/// </summary>
public readonly record struct TextEdit(int Start, int Length, string Inserted)
{
    /// <summary>The end of the replaced range in the old text (exclusive).</summary>
    public int End => Start + Length;

    /// <summary>What the edit does to the length of the text, and so to every offset after it.</summary>
    public int Delta => Inserted.Length - Length;

    /// <summary>Something was put in — §3.4's "insertion", whether or not something was also taken out.</summary>
    public bool IsInsertion => Inserted.Length > 0;

    /// <summary>Something was taken out and nothing put in — §3.4's "deletion".</summary>
    public bool IsDeletion => Inserted.Length == 0 && Length > 0;

    /// <summary>
    /// The one replacement that turns <paramref name="oldText"/> into <paramref name="newText"/>,
    /// or null when they are equal. Anchored at the selection when the new text is the old with
    /// exactly the selection replaced — typing, a paste, Delete and Cut over a selection, a
    /// letter over a selection — which settles the ambiguity of an edit inside a run of identical
    /// units (<c>aa</c> → <c>aaa</c>) the way the control made it. A shrink at a collapsed caret
    /// — Backspace, Delete, and their Ctrl word forms — is anchored at the caret on the side
    /// <paramref name="direction"/> names, the units before it or after it, which settles the same
    /// ambiguity for a deletion (<c>II</c> → <c>I</c> is either unit); when both sides fit and no
    /// key was announced, Backspace is assumed, and when the announced side does not fit the text,
    /// the other is taken, since the text is what happened. Otherwise (Undo, Redo, a change the
    /// control made somewhere other than the selection) the smallest single replacement: the
    /// common prefix and suffix are kept, never splitting a surrogate pair.
    /// </summary>
    public static TextEdit? Between(string oldText, int selectionStart, int selectionLength, string newText, DeleteDirection direction = DeleteDirection.Unknown)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        if (string.Equals(oldText, newText, StringComparison.Ordinal)) return null;

        if (selectionStart >= 0 && selectionLength >= 0 && selectionStart + selectionLength <= oldText.Length)
        {
            var insertedLength = newText.Length - oldText.Length + selectionLength;
            if (insertedLength >= 0
                && newText.AsSpan(0, selectionStart).SequenceEqual(oldText.AsSpan(0, selectionStart))
                && newText.AsSpan(selectionStart + insertedLength).SequenceEqual(oldText.AsSpan(selectionStart + selectionLength)))
            {
                return new TextEdit(selectionStart, selectionLength, newText.Substring(selectionStart, insertedLength));
            }

            var removed = oldText.Length - newText.Length;
            if (selectionLength == 0 && removed > 0)
            {
                var before = new TextEdit(selectionStart - removed, removed, string.Empty);
                var after = new TextEdit(selectionStart, removed, string.Empty);
                var fitsBefore = before.Start >= 0 && IsRemoval(oldText, before, newText);
                var fitsAfter = after.End <= oldText.Length && IsRemoval(oldText, after, newText);
                if (direction == DeleteDirection.Forward)
                {
                    if (fitsAfter) return after;
                    if (fitsBefore) return before;
                }
                else
                {
                    if (fitsBefore) return before;
                    if (fitsAfter) return after;
                }
            }
        }

        var shorter = Math.Min(oldText.Length, newText.Length);
        var prefix = 0;
        while (prefix < shorter && oldText[prefix] == newText[prefix]) prefix++;
        // A high surrogate shared by both texts whose low half differs: the pair is the edit's.
        if (prefix > 0 && prefix < oldText.Length && char.IsHighSurrogate(oldText[prefix - 1]) && char.IsLowSurrogate(oldText[prefix])) prefix--;

        var suffix = 0;
        while (suffix < shorter - prefix && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix]) suffix++;
        // A low surrogate shared by both texts whose high half differs: likewise.
        if (suffix > 0 && oldText.Length - suffix > prefix && char.IsLowSurrogate(oldText[oldText.Length - suffix]) && char.IsHighSurrogate(oldText[oldText.Length - suffix - 1])) suffix--;

        return new TextEdit(prefix, oldText.Length - prefix - suffix, newText.Substring(prefix, newText.Length - prefix - suffix));
    }

    /// <summary><paramref name="newText"/> is <paramref name="oldText"/> with exactly <paramref name="edit"/>'s range taken out.</summary>
    static bool IsRemoval(string oldText, TextEdit edit, string newText) =>
        newText.AsSpan(0, edit.Start).SequenceEqual(oldText.AsSpan(0, edit.Start))
        && newText.AsSpan(edit.Start).SequenceEqual(oldText.AsSpan(edit.End));
}

/// <summary>
/// The side of a collapsed caret a deletion key removes from: <see cref="Backward"/> is Backspace
/// (the units before the caret), <see cref="Forward"/> is Delete (the units after it), and
/// <see cref="Unknown"/> is no key announced — a change the text has to settle by itself.
/// </summary>
public enum DeleteDirection
{
    Unknown,
    Backward,
    Forward,
}

/// <summary>
/// The state of docs/smart-typing.md §3.4, as the hooks drive it. There is one of three: the
/// <b>tracked capital</b> — <c>capital = p</c>, the unit offset of the last capital md produced,
/// kept right through every later edit until the edit that removes that scalar; the <b>armed
/// override</b> at <c>q</c>, set by that removal, under which a word insertion starting exactly at
/// <c>q</c> goes in as typed; or nothing. Every edit to the text passes through
/// <see cref="Edited"/>, in the order the control makes them, before it is applied.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tracking (§3.4, first list).</b> An edit whose range ends at or before <c>p</c> — an insertion
/// at <c>p</c> included — shifts <c>p</c> by the edit's length delta. An edit that starts after the
/// capital's last unit leaves it alone. An edit that covers the capital removes it, unless the
/// inserted text puts that same scalar back at the same offset (a paste of <c>Md</c> over the
/// selected <c>M</c>): then <c>capital = nil</c> and the override is armed at the edit's start.
/// Producing a new capital (<see cref="Produced"/>) replaces the tracked one: the old one is
/// forgotten, not armed.
/// </para>
/// <para>
/// <b>Armed (§3.4, second list).</b> A word insertion that starts exactly at <c>q</c> goes in as
/// typed and clears the override; any insertion that starts elsewhere clears it; an insertion at
/// <c>q</c> that is not a word (a digit, a newline, a capital) leaves it armed. Deletions never
/// clear it: one before <c>q</c> shifts <c>q</c>, one covering <c>q</c> moves <c>q</c> to its start.
/// The edit that removed the capital may itself be a word insertion at <c>q</c> — the whole word
/// <c>Md</c> selected and <c>md</c> typed, or the one-scalar retype of §3.4's last rule — and then
/// it goes in as typed too, and the override <i>stays</i> armed (the rule says the retype arms it);
/// a later word insertion at <c>q</c> spends it.
/// </para>
/// <para>
/// <b>Undo and Redo</b> are edits like any other here (the hooks bypass judging for them, not
/// tracking): the undo of a capital is the replacement <c>M</c> → <c>m</c> at <c>p</c>, which
/// removes the capital and arms the override there, so the restored lowercase letter can be
/// deleted and retyped, or typed on from — an insertion elsewhere clears. A redo of the capital is
/// a non-word insertion at <c>q</c>: it puts the text back and leaves the override armed; it does
/// not make the capital tracked again, because a redo restores text, not md's memory of what it
/// wrote — the next capital produced by typing is.
/// </para>
/// </remarks>
public sealed class CapitalTracker
{
    /// <summary><c>p</c>: the offset of the tracked capital, or null while none is tracked.</summary>
    public int? CapitalAt { get; private set; }

    /// <summary>The tracked capital itself (one scalar, one or two units), or null while none is tracked.</summary>
    public string? Capital { get; private set; }

    /// <summary><c>q</c>: where the override is armed, or null while it is not.</summary>
    public int? ArmedAt { get; private set; }

    public bool IsTracking => CapitalAt is not null;

    public bool IsArmed => ArmedAt is not null;

    /// <summary>md produced <paramref name="capital"/> at <paramref name="position"/>: it is the tracked one now, and whatever was tracked or armed before is forgotten.</summary>
    public void Produced(int position, string capital)
    {
        ArgumentNullException.ThrowIfNull(capital);
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        if (capital.Length == 0) throw new ArgumentException("Empty", nameof(capital));
        CapitalAt = position;
        Capital = capital;
        ArmedAt = null;
    }

    /// <summary>An external replacement of the whole text, or the setting turned off: nothing to remember.</summary>
    public void Clear()
    {
        CapitalAt = null;
        Capital = null;
        ArmedAt = null;
    }

    /// <summary>
    /// One edit, before it is applied, with <paramref name="wordInsertion"/> saying whether its
    /// inserted text is a word insertion (§3.3). Updates the state as the remarks describe and
    /// answers true when the insertion is to go in <b>as typed</b> — a word insertion starting at
    /// the armed <c>q</c> — which the hooks turn into "no plan".
    /// </summary>
    public bool Edited(TextEdit edit, bool wordInsertion)
    {
        ArgumentNullException.ThrowIfNull(edit.Inserted);
        if (edit.Length == 0 && !edit.IsInsertion) return false;   // nothing changed

        var armedByThisEdit = false;
        if (CapitalAt is { } p)
        {
            var capital = Capital!;
            if (edit.End <= p)
            {
                CapitalAt = p + edit.Delta;
            }
            else if (edit.Start >= p + capital.Length || PutsBack(edit, p, capital))
            {
                // After the capital, or the same scalar put back where it was: still standing.
            }
            else
            {
                CapitalAt = null;
                Capital = null;
                ArmedAt = edit.Start;
                armedByThisEdit = true;
            }
        }

        if (ArmedAt is not { } q) return false;

        if (!edit.IsInsertion)
        {
            if (edit.End <= q) ArmedAt = q + edit.Delta;
            else if (edit.Start < q) ArmedAt = edit.Start;
            return false;
        }

        if (edit.Start != q)
        {
            ArmedAt = null;
            return false;
        }
        if (!wordInsertion) return false;
        if (!armedByThisEdit) ArmedAt = null;
        return true;
    }

    /// <summary>The edit covers <paramref name="p"/> but its inserted text holds <paramref name="capital"/> at that same offset.</summary>
    static bool PutsBack(TextEdit edit, int p, string capital)
    {
        if (edit.Start > p) return false;
        var at = p - edit.Start;
        return edit.Inserted.Length >= at + capital.Length
            && edit.Inserted.AsSpan(at, capital.Length).SequenceEqual(capital);
    }
}
