using Md.App.Logic.Commands;
using Md.App.Logic.Settings;
using Md.App.Logic.Text;

namespace Md.App.Logic.Tests;

/// <summary>
/// <see cref="TypingHooks"/> driven the way <c>EditorPane</c> drives it, over a <see cref="FakeTextBox"/>
/// that raises the control's events in the control's order — with the two things the adapter's
/// tests never see in the loop: the session's echo of every keystroke back into <c>SetText</c>
/// (both windows re-render on every session change) and the control's own Undo / Redo, which raise
/// <c>BeforeTextChanging</c> like typing does. The <see cref="Editor"/> below is the pane's wiring
/// line for line; <see cref="AppSurfaceTests"/> pins the real pane to the same calls in the same
/// places, so what passes here is what the pane does. The eight consequences of smart-typing.md
/// §3.4 are pinned here through the box, and the state machine itself (<see cref="CapitalTracker"/>,
/// <see cref="TextEdit"/>) on its own below them.
/// </summary>
public sealed class TypingHooksTests
{
    /// <summary>
    /// The pane and its window, as modelled: the hooks fed the box's text and selection at each
    /// event, a plan applied through <c>Select</c> + <c>SelectedText</c>, <c>TextChanged</c> reported
    /// as an edit unless it is our own assignment, the session's echo through <c>SetText</c>, and
    /// the flags settled on the next dispatcher turn. Every user action is its own input message,
    /// so the queued turn runs before the next one.
    /// </summary>
    sealed class Editor
    {
        readonly Queue<Action> _dispatcher = new();

        public FakeTextBox Box { get; } = new();
        public TypingHooks Hooks { get; } = new();
        /// <summary>The session's text: what <c>OnEditorTextEdited</c> writes and <c>OnSessionChanged</c> / <c>RenderStage</c> echoes back.</summary>
        public string SessionText { get; private set; } = string.Empty;
        public int Echoes { get; private set; }

        public Editor()
        {
            Box.BeforeTextChanging += newText => Hooks.BeforeTextChanging(Box.Text, Box.SelectionStart, Box.SelectionLength, newText);
            Box.TextChanging += () =>
            {
                if (Hooks.TextChanging(Box.Text, Box.SelectionStart, Box.SelectionLength) is { } plan)
                    Apply(plan.Position, plan.Inserted.Length, plan.Replacement, plan.Position + plan.Replacement.Length);
            };
            Box.TextChanged += () =>
            {
                if (Hooks.IsReplacing) return;
                TextEdited(Box.Text);
            };
            Box.Paste += AnnouncePaste;
        }

        public string Text => Box.Text;
        public CapitalTracker Tracker => Hooks.Tracker;

        // ── the window ────────────────────────────────────────────────────────────────────

        void TextEdited(string text)
        {
            // OnEditorTextEdited's guard, then TextFileSession.Edit → Changed → SetText(_session.Text).
            if (string.Equals(text, SessionText, StringComparison.Ordinal)) return;
            SessionText = text;
            Echoes++;
            SetText(SessionText);
        }

        /// <summary>EditorPane.SetText: the echo is not a replacement; a differing text is assigned with the flag up.</summary>
        public void SetText(string text)
        {
            if (!Hooks.ExternalText(Box.Text, text)) return;
            Hooks.Replacing(() => Box.SetText(text));
        }

        /// <summary>EditorPane.ResetHistory, what RenderStage / OnSessionChanged call on a new UndoGeneration: the hooks first, then the control's history.</summary>
        public void ResetHistory()
        {
            Hooks.Reset();
            Box.ClearUndoRedoHistory();
        }

        // ── the pane ──────────────────────────────────────────────────────────────────────

        void Apply(int location, int length, string replacement, int caret) =>
            Hooks.Applying(() =>
            {
                Box.Select(location, length);
                Box.SelectedText = replacement;
                Box.Select(caret, 0);
            });

        /// <summary>
        /// EditorPane.ReplaceRange — the find bar's Replace / Replace All (shell-design.md §3.5):
        /// the same Select + SelectedText pair, announced to the hooks as an external edit rather
        /// than applied under Applying alone, so the tracking is cleared the way a Revert clears it.
        /// The control's history is left alone: a replace is one undoable step.
        /// </summary>
        public void ReplaceRange(int start, int length, string replacement) =>
            Hooks.ExternalEdit(() =>
            {
                Box.Select(start, length);
                Box.SelectedText = replacement;
                Box.Select(start + replacement.Length, 0);
            });

        void AnnouncePaste()
        {
            Hooks.AnnouncePaste();
            _dispatcher.Enqueue(Hooks.SettlePaste);
        }

        void AnnounceHistory()
        {
            Hooks.AnnounceHistory();
            _dispatcher.Enqueue(Hooks.SettleHistory);
        }

        /// <summary>PreviewKeyDown for Backspace / Delete: announced, settled a turn later; the control deletes.</summary>
        void AnnounceDeletion(DeleteDirection direction)
        {
            Hooks.AnnounceDeletion(direction);
            _dispatcher.Enqueue(Hooks.SettleDeletion);
        }

        /// <summary>One keystroke per scalar; each is its own input message.</summary>
        public void Type(string text)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                Box.Type(rune.ToString());
                Turn();
            }
        }

        public void Backspace(int times = 1)
        {
            for (var i = 0; i < times; i++)
            {
                AnnounceDeletion(DeleteDirection.Backward);
                Box.Backspace();
                Turn();
            }
        }

        public void Delete()
        {
            AnnounceDeletion(DeleteDirection.Forward);
            Box.Delete();
            Turn();
        }

        /// <summary>Ctrl+X and Edit ▸ Cut: the control removes the selection itself; nothing is announced (§3.4: a deletion).</summary>
        public void Cut()
        {
            Box.Cut();
            Turn();
        }

        public void Select(int start, int length) => Box.Select(start, length);

        /// <summary>Ctrl+Z (PreviewKeyDown announces, the control undoes) and Edit ▸ Undo (EditorPane.Undo) are the same two calls.</summary>
        public void Undo()
        {
            AnnounceHistory();
            Box.Undo();
            Turn();
        }

        public void Redo()
        {
            AnnounceHistory();
            Box.Redo();
            Turn();
        }

        /// <summary>Ctrl+V: the control raises Paste, then inserts.</summary>
        public void KeyPaste(string clipboard)
        {
            Box.PasteText(clipboard, raisesPasteEvent: true);
            Turn();
        }

        /// <summary>Edit ▸ Paste through EditorPane.Paste(): announced by the pane, then PasteFromClipboard(), which may or may not raise Paste.</summary>
        public void MenuPaste(string clipboard, bool controlRaisesPaste)
        {
            AnnouncePaste();
            Box.PasteText(clipboard, controlRaisesPaste);
            Turn();
        }

        /// <summary>Enter through the hooks; the keyboard's modifiers are the pane's business and are not modelled.</summary>
        public bool Enter()
        {
            if (Hooks.Enter(Box.Text, Box.SelectionStart, Box.SelectionLength) is not { } e)
            {
                Box.Type("\r");
                Turn();
                return false;
            }
            Apply(e.Location, e.Length, e.Replacement, e.Caret);
            Turn();
            return true;
        }

        /// <summary>The dispatcher turn between two input messages.</summary>
        public void Turn()
        {
            while (_dispatcher.Count > 0) _dispatcher.Dequeue()();
        }
    }

    static void AssertTracked(Editor e, int at, string capital)
    {
        Assert.Equal(at, e.Tracker.CapitalAt);
        Assert.Equal(capital, e.Tracker.Capital);
        Assert.False(e.Tracker.IsArmed);
    }

    static void AssertArmed(Editor e, int at)
    {
        Assert.Equal(at, e.Tracker.ArmedAt);
        Assert.False(e.Tracker.IsTracking);
    }

    static void AssertIdle(Editor e)
    {
        Assert.False(e.Tracker.IsTracking);
        Assert.False(e.Tracker.IsArmed);
    }

    // ── the in-app self-test's scenarios, proved against the model first ─────────────────────

    public static TheoryData<string> SelfTestScenarios()
    {
        var data = new TheoryData<string>();
        foreach (var scenario in Md.App.Services.SelfTestTyping.Scenarios) data.Add(scenario.Name);
        return data;
    }

    /// <summary>
    /// <c>md.exe --selftest</c> types these on the real editor with real keystrokes and asserts the
    /// text below (shell-design.md §11.4). Here the same keys go through the modelled pane, so every
    /// expectation is what the tested state machine does — a failure on Windows is then the real
    /// TextBox disagreeing with the model (event order, undo units, a key the control eats), which
    /// is exactly what the run is for, and never a wrong vector.
    /// </summary>
    [Theory]
    [MemberData(nameof(SelfTestScenarios))]
    public void EverySelfTestScenarioHoldsInTheModel(string name)
    {
        var scenario = Md.App.Services.SelfTestTyping.Scenarios.Single(s => s.Name == name);
        var e = new Editor();
        e.Hooks.Configure(new TypingSettings(scenario.ContinueLists, scenario.CapitalizeSentences));
        e.SetText(scenario.Initial);
        e.ResetHistory();
        e.Select(scenario.Caret, 0);
        foreach (var key in scenario.Keys)
        {
            switch (key.Kind)
            {
                case Md.App.Services.TypingKeyKind.Text: e.Type(key.Text); break;
                case Md.App.Services.TypingKeyKind.Enter: e.Enter(); break;
                case Md.App.Services.TypingKeyKind.Backspace: e.Backspace(); break;
                case Md.App.Services.TypingKeyKind.Undo: e.Undo(); break;
            }
        }
        Assert.Equal(scenario.Expected, e.Text);
    }

    [Fact]
    public void TheSelfTestScenariosHaveUniqueNamesAndCrLineEnds()
    {
        var scenarios = Md.App.Services.SelfTestTyping.Scenarios;
        Assert.Equal(scenarios.Count, scenarios.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count());
        // The control reports CR (§3.6): an LF in a vector could only ever fail on Windows.
        Assert.All(scenarios, s => Assert.DoesNotContain('\n', s.Initial + s.Expected));
    }

    // ── the echo (finding: SetText's echo disarmed the override after every keystroke) ────────

    [Fact]
    public void TheEchoOfAKeystrokeLeavesTheCapitalTracked()
    {
        var e = new Editor();
        e.Type("m");
        Assert.Equal("M", e.Text);
        Assert.Equal(1, e.Echoes);                        // the session echoed the capital back into SetText
        Assert.Equal("M", e.SessionText);
        AssertTracked(e, 0, "M");                         // and the tracking survived it
    }

    [Fact]
    public void TheEchoOfADeletionLeavesTheOverrideArmed()
    {
        var e = new Editor();
        e.Type("m");
        e.Backspace();
        Assert.Equal("", e.Text);
        Assert.Equal("", e.SessionText);                  // echoed, as identical text
        AssertArmed(e, 0);
    }

    [Fact]
    public void ARealExternalReplacementClearsTheTrackingAndTheOverride()
    {
        // Revert, Reload from Disk, an article switch: the text differs, both are gone (§3.4).
        var e = new Editor();
        e.Type("m");
        Assert.True(e.Tracker.IsTracking);
        e.SetText("other text");
        Assert.Equal("other text", e.Text);
        AssertIdle(e);
        Assert.False(e.Box.CanUndo);                      // Text = cleared the history, as the design accepts for these

        e.SetText("");                                    // an armed override goes the same way
        e.Type("m");
        Assert.Equal("M", e.Text);
        e.Backspace();
        AssertArmed(e, 0);
        e.SetText("replaced again");
        AssertIdle(e);
    }

    [Fact]
    public void AnArticleSwitchToIdenticalTextClearsTheTrackingAndTheOverrideToo()
    {
        // A book with two articles whose text is the same — two chapters' fresh "Intro", two the
        // writer emptied — or Revert / Reload of a file the disk agrees with: an external replacement
        // (§3.4) that SetText sees as the echo's identical text. The windows' signal is the session's
        // UndoGeneration, and it goes through the pane's ResetHistory, which tells the hooks before it
        // clears the control's own history; SetText itself stays the echo's gate.
        var e = new Editor();
        e.Type("m");
        e.Backspace();
        AssertArmed(e, 0);
        e.ResetHistory();                                 // RenderStage / OnSessionChanged: a new UndoGeneration
        e.SetText("");                                    // the other article holds the same text: no assignment
        AssertIdle(e);
        Assert.False(e.Box.CanUndo);
        e.Type("m");
        Assert.Equal("M", e.Text);                        // judged afresh in the other article

        e.Type("d");                                      // a tracked capital goes the same way
        AssertTracked(e, 0, "M");
        e.ResetHistory();
        e.SetText("Md");
        AssertIdle(e);
        Assert.Equal("Md", e.Text);
    }

    [Fact]
    public void ResetForgetsTheTrackingAndThePlanAndLeavesTheFlagsAlone()
    {
        // The flags describe the input in flight — a composition, a history step, a paste — which a
        // re-render in the middle of one does not end.
        var hooks = new TypingHooks();
        hooks.BeforeTextChanging("", 0, 0, "m");
        Assert.True(hooks.HasPendingPlan);
        hooks.Tracker.Produced(0, "M");
        hooks.CompositionStarted();
        hooks.AnnounceHistory();
        hooks.AnnouncePaste();
        hooks.Reset();
        Assert.False(hooks.HasPendingPlan);
        Assert.False(hooks.Tracker.IsTracking);
        Assert.False(hooks.Tracker.IsArmed);
        Assert.True(hooks.IsComposing);
        Assert.True(hooks.IsHistory);
        Assert.True(hooks.IsPasting);
    }

    [Fact]
    public void TheEchoIsNotAnEditAndTheAssignmentIsNotOne()
    {
        var e = new Editor();
        e.Type("m");
        var echoes = e.Echoes;
        e.SetText("M");                                   // a second echo of the same text
        Assert.Equal(echoes, e.Echoes);
        e.SetText("Reverted");
        Assert.Equal(echoes, e.Echoes);                   // our own assignment is not reported as an edit
    }

    // ── smart-typing.md §3.4, the eight consequences, through the box ─────────────────────────

    [Fact]
    public void Consequence1_TypeMdBackspaceTwiceAndTypeMdAgain()
    {
        var e = new Editor();
        e.Type("md");
        Assert.Equal("Md", e.Text);
        AssertTracked(e, 0, "M");
        e.Backspace(2);
        Assert.Equal("", e.Text);
        AssertArmed(e, 0);                                // the second Backspace removed the capital
        e.Type("md");
        Assert.Equal("md", e.Text);
        AssertIdle(e);                                    // spent by the m; the d was elsewhere anyway
        Assert.Equal("md", e.SessionText);
    }

    [Fact]
    public void Consequence2_TypeIOSBackspaceThriceAndTypeIOSAgain()
    {
        var e = new Editor();
        e.Type("iOS");
        Assert.Equal("IOS", e.Text);
        AssertTracked(e, 0, "I");                         // the O and the S were typed capitals, after p
        e.Backspace(3);
        AssertArmed(e, 0);
        e.Type("iOS");
        Assert.Equal("iOS", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void Consequence3_FiveKeystrokesAfterTheCapitalThenFiveBackspacesAndTheRetype()
    {
        // The four insertions after the capital disarm nothing, because nothing is armed: the
        // capital is tracked, and only its removal arms.
        var e = new Editor();
        e.Type("md is");
        Assert.Equal("Md is", e.Text);
        AssertTracked(e, 0, "M");
        e.Backspace(4);
        Assert.Equal("M", e.Text);
        AssertTracked(e, 0, "M");                         // deletions after p leave it alone
        e.Backspace();
        AssertArmed(e, 0);
        e.Type("md is");
        Assert.Equal("md is", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void Consequence4_SelectTheWholeWordAndRetypeIt()
    {
        var e = new Editor();
        e.Type("md");
        e.Select(0, 2);
        e.Type("md");
        Assert.Equal("md", e.Text);
        AssertIdle(e);                                    // the m over the selection armed and went in as typed; the d cleared
    }

    [Fact]
    public void Consequence5_ALetterTypedInFrontOfAStandingCapitalIsCapitalizedAndBecomesTheTrackedOne()
    {
        var e = new Editor();
        e.Type("m");
        e.Select(0, 0);
        e.Type("a");
        Assert.Equal("AM", e.Text);                       // the M was not deleted: the insertion at p shifted it
        AssertTracked(e, 0, "A");                         // and the new capital is the tracked one; the old M is forgotten
        e.Backspace();
        Assert.Equal("M", e.Text);
        AssertArmed(e, 0);                                // removing the A arms; the M at 0 is not md's memory any more
    }

    [Fact]
    public void Consequence6_UndoRestoresTheLowercaseLetterArmsTheOverrideAndTypingOnKeepsIt()
    {
        var e = new Editor();
        e.Type("m");
        Assert.Equal("M", e.Text);
        e.Undo();
        Assert.Equal("m", e.Text);                        // Windows restores the lowercase (§3.5)
        Assert.Equal(1, e.Box.SelectionStart);
        AssertArmed(e, 0);                                // the undo is an edit that removed the capital
        e.Type("d");
        Assert.Equal("md", e.Text);                       // an insertion elsewhere than q clears; nothing re-judges the m
        AssertIdle(e);
    }

    [Fact]
    public void Consequence7_ACutBeforeTheCapitalThenBackspaceAndRetype()
    {
        var e = new Editor();
        e.Type("Xxx. ");
        e.Type("m");
        Assert.Equal("Xxx. M", e.Text);
        AssertTracked(e, 5, "M");
        e.Select(0, 5);
        e.Cut();
        Assert.Equal("M", e.Text);
        AssertTracked(e, 0, "M");                         // offset tracking survives the cut
        e.Select(1, 0);                                   // the cut leaves the caret before the M; ⌫ needs it after
        e.Backspace();
        Assert.Equal("", e.Text);
        AssertArmed(e, 0);
        e.Type("m");
        Assert.Equal("m", e.Text);
    }

    [Fact]
    public void Consequence8_DeletingTheLetterAfterTheCapitalArmsNothing()
    {
        var e = new Editor();
        e.Type("m");
        e.Type("d");
        Assert.Equal("Md", e.Text);
        e.Backspace();
        Assert.Equal("M", e.Text);
        AssertTracked(e, 0, "M");                         // the capital still stands
        e.Type("d");
        Assert.Equal("Md", e.Text);
        AssertTracked(e, 0, "M");
    }

    // ── more of §3.4 through the box ──────────────────────────────────────────────────────────

    [Fact]
    public void ADeletionBeforeAnArmedOverrideShiftsIt()
    {
        var e = new Editor();
        e.Type("Xxx. m");
        e.Backspace();
        Assert.Equal("Xxx. ", e.Text);
        AssertArmed(e, 5);
        e.Select(0, 5);
        e.Cut();
        Assert.Equal("", e.Text);
        AssertArmed(e, 0);                                // deletions never clear; one before q shifts it
        e.Type("m");
        Assert.Equal("m", e.Text);
    }

    [Fact]
    public void AnInsertionElsewhereThanQClearsTheOverride()
    {
        var e = new Editor();
        e.Type("Xxx. m");
        e.Backspace();
        AssertArmed(e, 5);
        e.Select(0, 0);
        e.Type("y");
        Assert.Equal("YXxx. ", e.Text);                   // judged like any letter at a line start
        AssertTracked(e, 0, "Y");
        e.Select(6, 0);
        e.Type("m");
        Assert.Equal("YXxx. M", e.Text);                  // nothing was armed at 6 any more
    }

    [Fact]
    public void ANonWordInsertionAtQLeavesTheOverrideArmed()
    {
        // A digit, a newline, a capital typed where the deleted capital was: not the retype, and
        // not "elsewhere" either — the override waits.
        var e = new Editor();
        e.Type("m");
        e.Backspace();
        e.Type("1");
        Assert.Equal("1", e.Text);
        AssertArmed(e, 0);
        e.Select(0, 0);
        e.Type("m");
        Assert.Equal("m1", e.Text);                       // the retype, at last
        AssertIdle(e);
    }

    [Fact]
    public void ACapitalTypedOverTheDeletedCapitalsSlotAndTypedOnFromIsJudgedLikeAnyText()
    {
        var e = new Editor();
        e.Type("m");
        e.Backspace();
        e.Type("M");                                      // the writer types the capital themself
        AssertArmed(e, 0);
        e.Type("d. x");
        Assert.Equal("Md. X", e.Text);                    // the d cleared the override; the x is a sentence start
        AssertTracked(e, 4, "X");
    }

    [Fact]
    public void APasteBeforeTheCapitalShiftsIt()
    {
        var e = new Editor();
        e.Type("m");
        e.Select(0, 0);
        e.KeyPaste("ab");
        Assert.Equal("abM", e.Text);                      // a paste is never capitalized, and sits before p
        AssertTracked(e, 2, "M");
        e.Select(3, 0);
        e.Backspace();
        AssertArmed(e, 2);
        e.Type("m");
        Assert.Equal("abm", e.Text);
    }

    [Fact]
    public void APasteOverTheCapitalThatPutsItBackKeepsItStanding()
    {
        // §3.4: a replacement covering p removes the capital "unless the inserted text puts that
        // same capital scalar back at the same offset".
        var e = new Editor();
        e.Type("m");
        e.Select(0, 1);
        e.KeyPaste("Md");
        Assert.Equal("Md", e.Text);
        AssertTracked(e, 0, "M");
        e.Backspace(2);
        AssertArmed(e, 0);
        e.Type("m");
        Assert.Equal("m", e.Text);
    }

    [Fact]
    public void APasteAtQIsAWordInsertionAtQAndSpendsTheOverride()
    {
        var e = new Editor();
        e.Type("m");
        e.Backspace();
        e.KeyPaste("md");
        Assert.Equal("md", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void ADeletionCoveringTheCapitalAndMoreArmsAtItsStart()
    {
        var e = new Editor();
        e.Type("md is");
        e.Select(0, 3);
        e.Delete();
        Assert.Equal("is", e.Text);
        AssertArmed(e, 0);
        e.Type("m");
        Assert.Equal("mis", e.Text);
    }

    [Fact]
    public void ForwardDeleteOfTheCapitalArmsTheOverrideToo()
    {
        var e = new Editor();
        e.Type("md");
        e.Select(0, 0);
        e.Delete();
        Assert.Equal("d", e.Text);
        AssertArmed(e, 0);
        e.Type("m");
        Assert.Equal("md", e.Text);
    }

    [Fact]
    public void BackspaceOfTheCapitalNextToTheWritersIdenticalLetterArmsTheOverride()
    {
        // `I` (md's) then `I` (the writer's, Shift+I): the two units are the same, so the text alone
        // cannot say which one a Backspace or Delete removed — the smallest diff picks the last of the
        // run. The key says which: PreviewKeyDown announces the direction, and the shrink is anchored
        // at the caret on that side (§3.4: the edit that removed the capital arms at its start).
        var e = new Editor();
        e.Type("i");
        Assert.Equal("I", e.Text);
        e.Type("I");
        Assert.Equal("II", e.Text);
        AssertTracked(e, 0, "I");
        e.Select(1, 0);
        e.Backspace();                                    // [0, 1): md's capital
        Assert.Equal("I", e.Text);
        AssertArmed(e, 0);
        e.Type("i");
        Assert.Equal("iI", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void DeleteOfTheCapitalNextToTheWritersIdenticalLetterArmsTheOverride()
    {
        var e = new Editor();
        e.Type("i");
        e.Type("I");
        Assert.Equal("II", e.Text);
        e.Select(0, 0);
        e.Delete();                                       // [0, 1): md's capital
        Assert.Equal("I", e.Text);
        AssertArmed(e, 0);
        e.Type("i");
        Assert.Equal("iI", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void DeletingTheWritersIdenticalLetterNextToTheCapitalLeavesItTracked()
    {
        // The mirror: the same two keys removing the writer's I, not md's — Delete with the caret
        // between the two, Backspace with the caret after them.
        var e = new Editor();
        e.Type("i");
        e.Type("I");
        e.Select(1, 0);
        e.Delete();                                       // [1, 2): the writer's
        Assert.Equal("I", e.Text);
        AssertTracked(e, 0, "I");

        e.Type("I");
        Assert.Equal("II", e.Text);
        e.Select(2, 0);
        e.Backspace();                                    // [1, 2): the writer's again
        Assert.Equal("I", e.Text);
        AssertTracked(e, 0, "I");
        e.Type("i");                                      // after the standing capital: judged, not a sentence start
        Assert.Equal("Ii", e.Text);
        AssertTracked(e, 0, "I");
    }

    [Fact]
    public void TheOneScalarRetypeOverTheTrackedCapitalGoesInAsTypedAndArmsTheOverride()
    {
        // §3.4's last rule: "inserted as typed — and, since it removed the capital, it arms the
        // override". The retype is not what spends it; the next word insertion at q is.
        var e = new Editor();
        e.Type("md is");
        e.Select(0, 1);
        e.Type("m");
        Assert.Equal("md is", e.Text);
        AssertArmed(e, 0);
        e.Backspace();
        Assert.Equal("d is", e.Text);
        AssertArmed(e, 0);
        e.Type("m");
        Assert.Equal("md is", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void TheOneScalarRetypeOverACapitalMdDidNotWriteIsAsTypedButArmsNothing()
    {
        var e = new Editor();
        e.Type("m");
        e.Backspace();
        e.Type("T");                                      // the writer's own capital, at the deleted one's slot
        e.Type("he");
        Assert.Equal("The", e.Text);
        AssertIdle(e);
        e.Select(0, 1);
        e.Type("t");
        Assert.Equal("the", e.Text);                      // the retype rule needs no state
        AssertIdle(e);                                    // and, having removed no capital of md's, arms nothing
        e.Backspace();
        e.Type("t");
        Assert.Equal("The", e.Text);                      // so a Backspace and a retype are judged afresh
    }

    [Fact]
    public void TheEnterEditIsTrackedLikeAnyOtherEdit()
    {
        // The exit rule on an empty item deletes the marker: a deletion ending at q shifts it.
        var e = new Editor();
        e.Type("- m");
        Assert.Equal("- M", e.Text);
        AssertTracked(e, 2, "M");
        e.Backspace();
        AssertArmed(e, 2);
        Assert.True(e.Enter());
        Assert.Equal("", e.Text);
        AssertArmed(e, 0);
        e.Type("m");
        Assert.Equal("m", e.Text);
    }

    [Fact]
    public void ANewCapitalReplacesTheTrackedOneAndTheOldOneIsForgottenNotArmed()
    {
        var e = new Editor();
        e.Type("m");
        e.Type(". x");
        Assert.Equal("M. X", e.Text);
        AssertTracked(e, 3, "X");
        e.Select(1, 0);
        e.Backspace();                                    // deletes the M — the capital md forgot
        Assert.Equal(". X", e.Text);
        AssertTracked(e, 2, "X");                         // shifted, not armed
        e.Type("m");
        Assert.Equal("M. X", e.Text);                     // judged afresh: a line start
        AssertTracked(e, 0, "M");
    }

    [Fact]
    public void ACompositionIsTrackedThoughNeverJudged()
    {
        var e = new Editor();
        e.Type("m");
        e.Hooks.CompositionStarted();
        e.Type("x");
        Assert.Equal("Mx", e.Text);
        AssertTracked(e, 0, "M");
        e.Backspace(2);
        AssertArmed(e, 0);
        e.Hooks.CompositionEnded();
        e.Type("m");
        Assert.Equal("m", e.Text);
    }

    // ── history (finding: Undo / Redo were re-judged as typing) ───────────────────────────────

    [Fact]
    public void RedoReinsertsTheLetterAsItWasAndKeepsTheRedoStack()
    {
        var e = new Editor();
        e.Type("hello");
        Assert.Equal("Hello", e.Text);
        var units = e.Box.UndoUnits;                      // h, h→H, e, l, l, o
        Assert.Equal(6, units);
        for (var i = 0; i < units; i++) e.Undo();
        Assert.Equal("", e.Text);
        Assert.Equal(units, e.Box.RedoUnits);
        AssertArmed(e, 0);                                // the undo of the capital armed it; the undo of the h collapsed q onto 0

        e.Redo();
        Assert.Equal("h", e.Text);                        // not H: a Redo is not typing
        Assert.Equal(units - 1, e.Box.RedoUnits);         // and nothing new was edited over it
        AssertIdle(e);                                    // a word insertion at q, tracked though not judged: spent
        for (var i = 1; i < units; i++) e.Redo();
        Assert.Equal("Hello", e.Text);
        Assert.False(e.Box.CanRedo);
        AssertIdle(e);                                    // Redo restores the text, not md's memory of the capital
    }

    [Fact]
    public void UndoOfADeletionRestoresTheTextThatWasThere()
    {
        // "md rocks" through the override, the word deleted, then Ctrl+Z: the control re-inserts
        // "md" at 0 with the caret after it, which BeforeTextChanging cannot tell from typing.
        var e = new Editor();
        e.Type("m");
        e.Backspace();
        e.Type("md rocks");
        Assert.Equal("md rocks", e.Text);
        e.Select(0, 2);
        e.Delete();
        Assert.Equal(" rocks", e.Text);
        e.Undo();
        Assert.Equal("md rocks", e.Text);                 // never "Md rocks": text that never existed
    }

    [Fact]
    public void UndoImmediatelyAfterACapitalRestoresTheLowercaseLetterAndTheCaret()
    {
        // §3.5 in the two-unit model: the control's insertion and our replacement are two units, so
        // one Ctrl+Z takes back the capital alone, keeps the caret, and arms the override at p.
        var e = new Editor();
        e.Type("h");
        Assert.Equal("H", e.Text);
        Assert.Equal(2, e.Box.UndoUnits);
        e.Undo();
        Assert.Equal("h", e.Text);
        Assert.Equal(1, e.Box.SelectionStart);
        Assert.Equal(0, e.Box.SelectionLength);
        AssertArmed(e, 0);
        e.Undo();
        Assert.Equal("", e.Text);
        AssertArmed(e, 0);                                // a deletion never clears it
        e.Type("h");
        Assert.Equal("h", e.Text);
    }

    [Fact]
    public void UndoThenBackspaceAndRetypeIsTheGestureToo()
    {
        var e = new Editor();
        e.Type("m");
        e.Undo();
        Assert.Equal("m", e.Text);
        e.Backspace();
        e.Type("m");
        Assert.Equal("m", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void UndoOfTheCapitalArmsTheOverrideWhereverItSitsInTheHistory()
    {
        // "hi": two Ctrl+Z give "h" with the caret kept. The first undo deletes after p; the second
        // is the edit that removed the capital, and arms — nothing typed on had "disarmed" anything,
        // because nothing was armed while the capital stood.
        var e = new Editor();
        e.Type("hi");
        Assert.Equal("Hi", e.Text);
        AssertTracked(e, 0, "H");
        e.Undo();
        Assert.Equal("H", e.Text);
        AssertTracked(e, 0, "H");
        e.Undo();
        Assert.Equal("h", e.Text);
        Assert.Equal(1, e.Box.SelectionStart);
        AssertArmed(e, 0);
        e.Type("i");
        Assert.Equal("hi", e.Text);                       // the h is not re-judged; the i clears
        AssertIdle(e);
    }

    [Fact]
    public void RedoOfTheCapitalPutsTheTextBackAndLeavesTheOverrideArmed()
    {
        // The decision for Redo: it is a non-word insertion at q — the text comes back, the
        // override stays, and the capital is not tracked again. Deleting it and retyping is still
        // the lowercase; typing on clears, as after any capital.
        var e = new Editor();
        e.Type("m");
        e.Undo();
        e.Redo();
        Assert.Equal("M", e.Text);
        Assert.Equal(1, e.Box.SelectionStart);
        AssertArmed(e, 0);
        e.Backspace();
        Assert.Equal("", e.Text);
        AssertArmed(e, 0);
        e.Type("m");
        Assert.Equal("m", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void RedoOfTheCapitalThenTypingOnClearsTheOverride()
    {
        var e = new Editor();
        e.Type("m");
        e.Undo();
        e.Redo();
        e.Type("d");
        Assert.Equal("Md", e.Text);
        AssertIdle(e);
        e.Backspace(2);
        e.Type("m");
        Assert.Equal("M", e.Text);                        // judged afresh: nothing remembered the redone capital
    }

    [Fact]
    public void AHistoryStepWithNothingToDoDoesNotSkipTheNextKeystroke()
    {
        var e = new Editor();
        e.Undo();                                         // nothing to undo: no change, the flag settles on the turn
        Assert.False(e.Hooks.IsHistory);
        e.Type("m");
        Assert.Equal("M", e.Text);
    }

    [Fact]
    public void TheHistoryFlagIsNotConsumedByTheFirstChangeItCovers()
    {
        // One Undo may report more than one change; every one of them is history until the turn.
        var h = new TypingHooks();
        h.AnnounceHistory();
        h.BeforeTextChanging("", 0, 0, "h");
        Assert.False(h.HasPendingPlan);
        h.BeforeTextChanging("h", 1, 0, "he");
        Assert.False(h.HasPendingPlan);
        Assert.True(h.IsHistory);
        h.SettleHistory();
        h.BeforeTextChanging("", 0, 0, "h");
        Assert.True(h.HasPendingPlan);
    }

    [Theory]
    [InlineData(VirtualKeys.Z, true, false, true)]       // Ctrl+Z
    [InlineData(VirtualKeys.Y, true, false, true)]       // Ctrl+Y
    [InlineData(VirtualKeys.Z, false, false, false)]     // z
    [InlineData(VirtualKeys.Y, false, false, false)]     // y
    [InlineData(VirtualKeys.Z, true, true, false)]       // AltGr+Z is ż on the Polish layout, and judged
    [InlineData(VirtualKeys.V, true, false, false)]      // Ctrl+V is a paste, with its own flag
    [InlineData(VirtualKeys.A, true, false, false)]
    public void OnlyCtrlZAndCtrlYWithoutAltAnnounceHistory(int key, bool control, bool alt, bool expected) =>
        Assert.Equal(expected, TypingHooks.IsHistoryChord(key, control, alt));

    // ── paste (finding: Edit ▸ Paste did not arm the paste flag) ──────────────────────────────

    [Fact]
    public void CtrlVIsNeverCapitalized()
    {
        var e = new Editor();
        e.KeyPaste("nettrash");
        Assert.Equal("nettrash", e.Text);
        AssertIdle(e);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EditMenuPasteIsNeverCapitalizedWhetherOrNotPasteFromClipboardRaisesTheEvent(bool controlRaisesPaste)
    {
        var e = new Editor();
        e.MenuPaste("nettrash", controlRaisesPaste);
        Assert.Equal("nettrash", e.Text);
        Assert.False(e.Hooks.IsPasting);                  // consumed, or settled on the turn
    }

    [Fact]
    public void APasteNobodyAnnouncesWouldBeCapitalizedWhichIsWhyTheEditRowGoesThroughThePane()
    {
        var e = new Editor();
        e.Box.PasteText("nettrash", raisesPasteEvent: false);   // PasteFromClipboard() reached directly, no event
        e.Turn();
        Assert.Equal("Nettrash", e.Text);
    }

    [Fact]
    public void AnEmptyClipboardDoesNotSkipTheNextKeystroke()
    {
        var e = new Editor();
        e.MenuPaste("", controlRaisesPaste: false);
        Assert.False(e.Hooks.IsPasting);
        e.Type("m");
        Assert.Equal("M", e.Text);
    }

    // ── the rest of the pane's sequencing ─────────────────────────────────────────────────────

    [Fact]
    public void OurOwnReplacementIsNotJudgedAgain()
    {
        // The capital is applied through the same events; it is tracked once, at 0, and the
        // session sees one edit per keystroke's final text.
        var e = new Editor();
        e.Type("m");
        Assert.Equal("M", e.Text);
        AssertTracked(e, 0, "M");
        Assert.Equal("M", e.SessionText);
    }

    [Fact]
    public void ACompositionSkipsBothFeatures()
    {
        var e = new Editor();
        e.Hooks.CompositionStarted();
        e.Type("m");
        Assert.Equal("m", e.Text);
        e.Type(" ");
        e.Type("- a");
        Assert.False(e.Enter());
        Assert.Equal("m - a\r", e.Text);
        e.Hooks.CompositionEnded();
        e.Type("x");
        Assert.Equal("m - a\rX", e.Text);
    }

    [Fact]
    public void CapitalizeSentencesOffIsNeverJudgedAndDropsTheGesture()
    {
        var e = new Editor();
        e.Type("m");
        Assert.True(e.Tracker.IsTracking);
        e.Hooks.Configure(new TypingSettings(ContinueLists: true, CapitalizeSentences: false));
        AssertIdle(e);
        e.Backspace();
        e.Type("hello");
        Assert.Equal("hello", e.Text);
        AssertIdle(e);
        e.Hooks.Configure(TypingSettings.Defaults);
        e.Type(". x");
        Assert.Equal("hello. X", e.Text);
        AssertTracked(e, 7, "X");
    }

    [Fact]
    public void EnterContinuesTheListAndTheCapitalBeforeItStaysTracked()
    {
        var e = new Editor();
        e.Type("- m");
        Assert.Equal("- M", e.Text);
        AssertTracked(e, 2, "M");
        Assert.True(e.Enter());
        Assert.Equal("- M\r- ", e.Text);                  // the CR spelling of the continuation
        AssertTracked(e, 2, "M");                         // an edit after p leaves it alone
        Assert.Equal(6, e.Box.SelectionStart);
        e.Type("n");
        Assert.Equal("- M\r- N", e.Text);
        AssertTracked(e, 6, "N");
    }

    [Fact]
    public void ContinueListsOffLeavesEnterToTheControl()
    {
        var e = new Editor();
        e.Hooks.Configure(new TypingSettings(ContinueLists: false, CapitalizeSentences: true));
        e.Type("- m");
        Assert.False(e.Enter());
        Assert.Equal("- M\r", e.Text);
        AssertTracked(e, 2, "M");                         // the control's newline is tracked like any edit
    }

    [Fact]
    public void ExternalTextChecksItsArguments()
    {
        var h = new TypingHooks();
        Assert.Throws<ArgumentNullException>(() => h.ExternalText(null!, ""));
        Assert.Throws<ArgumentNullException>(() => h.ExternalText("", null!));
        Assert.False(h.ExternalText("same", "same"));
        Assert.True(h.ExternalText("one", "two"));
    }

    [Fact]
    public void TheDefaultsAreBothOn()
    {
        var h = new TypingHooks();
        Assert.True(h.ContinueLists);
        Assert.True(h.CapitalizeSentences);
        Assert.False(h.IsComposing);
        Assert.False(h.IsPasting);
        Assert.False(h.IsHistory);
        Assert.False(h.IsReplacing);
        Assert.False(h.Tracker.IsTracking);
        Assert.False(h.Tracker.IsArmed);
    }

    [Fact]
    public void ANonBmpLetterIsOneKeystrokeAndOneCapital()
    {
        // Deseret 𐐨 (U+10428) → 𐐀 (U+10400): two units in, two units replaced, tracked at 0.
        var e = new Editor();
        e.Type("\U00010428");
        Assert.Equal("\U00010400", e.Text);
        AssertTracked(e, 0, "\U00010400");
        e.Backspace();
        Assert.Equal("", e.Text);
        AssertArmed(e, 0);
        e.Type("\U00010428");
        Assert.Equal("\U00010428", e.Text);
    }

    // ── §3.5 the find bar's Replace: an external edit, and one undo unit ──────────────────────

    /// <summary>
    /// The replacement is the writer's own text, so it goes in exactly as typed. The first half of
    /// this test proves the position IS one the capital machinery acts on — typed there, `beta`
    /// becomes `Beta` — which is what makes the second half mean anything.
    /// </summary>
    [Fact]
    public void AReplaceIsNotTypingAndIsNeverCapitalized()
    {
        var typed = new Editor();
        typed.SetText("Ready. alpha");
        typed.Select(7, 5);
        typed.Type("beta");
        Assert.Equal("Ready. Beta", typed.Text);            // typing there capitalizes

        var replaced = new Editor();
        replaced.SetText("Ready. alpha");
        replaced.ReplaceRange(7, 5, "beta");
        Assert.Equal("Ready. beta", replaced.Text);         // replacing there does not
        Assert.Equal("Ready. beta", replaced.SessionText);  // and the session saw the edit
    }

    /// <summary>
    /// §3.4: a replace is an external replacement as far as the tracking goes — a Revert, a Reload,
    /// an article switch. The capital md wrote is forgotten rather than followed through moved text,
    /// and no override is armed: the writer did not delete that capital, the find bar did.
    /// </summary>
    [Fact]
    public void AReplaceClearsTheTrackedCapitalAndArmsNothing()
    {
        var e = new Editor();
        e.Type("m");
        AssertTracked(e, 0, "M");

        e.ReplaceRange(0, 1, "q");
        Assert.Equal("q", e.Text);
        AssertIdle(e);
    }

    [Fact]
    public void AReplaceOverAnArmedOverrideClearsThatToo()
    {
        var e = new Editor();
        e.Type("m");
        e.Backspace();
        AssertArmed(e, 0);

        e.ReplaceRange(0, 0, "x");
        Assert.Equal("x", e.Text);
        AssertIdle(e);
    }

    /// <summary>
    /// The whole point of planning Replace All as ONE span: one <c>SelectedText</c> assignment is
    /// one undo unit, so a single Ctrl+Z puts every hit back at once. (The fake box always gives a
    /// programmatic assignment its own unit; shell-design.md §3.6 keeps the real RichEdit on the
    /// Windows checklist.)
    /// </summary>
    [Fact]
    public void ReplaceAllIsOneUndoUnitHoweverManyHitsItCovered()
    {
        const string text = "alpha beta alpha gamma alpha";
        var e = new Editor();
        e.SetText(text);
        Assert.Equal(0, e.Box.UndoUnits);                   // Text = cleared the history

        var plan = Md.App.Logic.Documents.TextSearch.ReplaceAll(text, "alpha", "X");
        Assert.Equal(3, plan.Count);
        e.ReplaceRange(plan.Apply.Start, plan.Apply.Length, plan.Apply.Text);

        Assert.Equal("X beta X gamma X", e.Text);
        Assert.Equal("X beta X gamma X", e.SessionText);
        Assert.Equal(1, e.Box.UndoUnits);                   // one step for all three

        e.Undo();
        Assert.Equal(text, e.Text);
        Assert.Equal(text, e.SessionText);
        Assert.Equal(0, e.Box.UndoUnits);
    }

    /// <summary>
    /// The session's echo of a replace is identical text, like the echo of a keystroke: it must not
    /// be assigned again (which would clear the undo history the replace just earned).
    /// </summary>
    [Fact]
    public void TheEchoOfAReplaceLeavesTheUndoHistoryStanding()
    {
        var e = new Editor();
        e.SetText("alpha");
        e.ReplaceRange(0, 5, "omega");
        Assert.Equal(1, e.Echoes);
        Assert.Equal("omega", e.SessionText);
        Assert.Equal(1, e.Box.UndoUnits);
    }

    /// <summary>Typing after a replace is judged again: the external edit clears the state, it does not switch the feature off.</summary>
    [Fact]
    public void TypingAfterAReplaceIsStillTyping()
    {
        var e = new Editor();
        e.SetText("Ready. alpha");
        e.ReplaceRange(7, 5, "beta");
        e.Select(e.Text.Length, 0);
        e.Type(" Done. gamma");
        Assert.Equal("Ready. beta Done. Gamma", e.Text);
    }

    // ── the state machine, pure (§3.4) ────────────────────────────────────────────────────────

    static CapitalTracker Armed(int at)
    {
        var t = new CapitalTracker();
        t.Produced(at, "M");
        t.Edited(new TextEdit(at, 1, ""), wordInsertion: false);
        Assert.Equal(at, t.ArmedAt);
        return t;
    }

    [Fact]
    public void TheTrackerStartsIdle()
    {
        var t = new CapitalTracker();
        Assert.False(t.IsTracking);
        Assert.False(t.IsArmed);
        Assert.Null(t.CapitalAt);
        Assert.Null(t.Capital);
        Assert.Null(t.ArmedAt);
    }

    [Fact]
    public void ProducedTracksTheCapitalAndForgetsWhateverWasThere()
    {
        var t = new CapitalTracker();
        t.Produced(4, "M");
        Assert.Equal(4, t.CapitalAt);
        Assert.Equal("M", t.Capital);
        Assert.False(t.IsArmed);
        t.Produced(7, "T");                               // the old one is forgotten, not armed
        Assert.Equal(7, t.CapitalAt);
        Assert.Equal("T", t.Capital);
        Assert.False(t.IsArmed);

        var armed = Armed(3);
        armed.Produced(0, "A");
        Assert.False(armed.IsArmed);
        Assert.Equal(0, armed.CapitalAt);
    }

    [Fact]
    public void AnEditWhoseRangeEndsAtOrBeforeTheCapitalShiftsIt()
    {
        var t = new CapitalTracker();
        t.Produced(5, "M");
        Assert.False(t.Edited(new TextEdit(0, 0, "ab"), true));    // an insertion before it
        Assert.Equal(7, t.CapitalAt);
        t.Edited(new TextEdit(0, 5, ""), false);                    // a cut before it
        Assert.Equal(2, t.CapitalAt);
        t.Edited(new TextEdit(2, 0, "x"), true);                    // an insertion AT p is before it (§3.4: "included")
        Assert.Equal(3, t.CapitalAt);
        t.Edited(new TextEdit(0, 3, "y"), true);                    // a replacement ending at p
        Assert.Equal(1, t.CapitalAt);
        Assert.Equal("M", t.Capital);
        Assert.False(t.IsArmed);
    }

    [Fact]
    public void AnEditAfterTheCapitalLeavesItAlone()
    {
        var t = new CapitalTracker();
        t.Produced(2, "M");
        t.Edited(new TextEdit(3, 0, "d"), true);
        t.Edited(new TextEdit(3, 1, ""), false);
        t.Edited(new TextEdit(3, 4, "paste"), false);
        Assert.Equal(2, t.CapitalAt);
        Assert.False(t.IsArmed);
    }

    [Fact]
    public void AnEditCoveringTheCapitalRemovesItAndArmsAtTheEditsStart()
    {
        var t = new CapitalTracker();
        t.Produced(5, "M");
        Assert.False(t.Edited(new TextEdit(5, 1, ""), false));     // Backspace, Delete
        Assert.False(t.IsTracking);
        Assert.Null(t.Capital);
        Assert.Equal(5, t.ArmedAt);

        t.Produced(5, "M");
        t.Edited(new TextEdit(2, 6, ""), false);                    // a wider deletion
        Assert.Equal(2, t.ArmedAt);

        t.Produced(5, "M");
        Assert.False(t.Edited(new TextEdit(5, 1, "1"), false));    // a non-word over it: armed, not the retype
        Assert.Equal(5, t.ArmedAt);

        t.Produced(3, "M");
        Assert.True(t.Edited(new TextEdit(1, 3, "ab"), true));     // a word from before p that swallows it: at q, as typed
        Assert.Equal(1, t.ArmedAt);
    }

    [Fact]
    public void TheUndoOfACapitalIsTheReplacementThatRemovesIt()
    {
        var t = new CapitalTracker();
        t.Produced(0, "M");
        Assert.True(t.Edited(new TextEdit(0, 1, "m"), true));      // M → m at p: removed, armed at p, and as typed
        Assert.False(t.IsTracking);
        Assert.Equal(0, t.ArmedAt);
        Assert.False(t.Edited(new TextEdit(0, 1, "M"), false));    // the Redo: a non-word insertion at q, stays armed
        Assert.Equal(0, t.ArmedAt);
        Assert.False(t.IsTracking);
    }

    [Fact]
    public void AReplacementThatPutsTheCapitalBackAtTheSameOffsetKeepsItTracked()
    {
        var t = new CapitalTracker();
        t.Produced(0, "M");
        t.Edited(new TextEdit(0, 1, "Md"), false);
        Assert.Equal(0, t.CapitalAt);
        Assert.False(t.IsArmed);

        t.Produced(2, "M");
        t.Edited(new TextEdit(0, 3, "xyM"), false);
        Assert.Equal(2, t.CapitalAt);
        t.Edited(new TextEdit(0, 3, "xyMz"), false);
        Assert.Equal(2, t.CapitalAt);

        t.Edited(new TextEdit(0, 3, "xM"), false);                  // the M is at 1 now, not 2: removed
        Assert.False(t.IsTracking);
        Assert.Equal(0, t.ArmedAt);
    }

    [Fact]
    public void ATwoUnitCapitalIsTrackedWhole()
    {
        var t = new CapitalTracker();
        t.Produced(2, "\U00010400");
        t.Edited(new TextEdit(4, 0, "x"), true);                    // after both units
        Assert.Equal(2, t.CapitalAt);
        t.Edited(new TextEdit(2, 2, ""), false);                    // the pair deleted as one, as RichEdit does
        Assert.Equal(2, t.ArmedAt);

        t.Produced(2, "\U00010400");
        t.Edited(new TextEdit(0, 4, "ab\U00010400"), false);        // put back whole at 2
        Assert.Equal(2, t.CapitalAt);

        // An edit inside the pair (no keyboard makes one) destroys the scalar: removed, and q is
        // the edit's start, as the rule says.
        t.Edited(new TextEdit(3, 1, "\uDC28"), false);
        Assert.False(t.IsTracking);
        Assert.Equal(3, t.ArmedAt);
    }

    [Fact]
    public void WhileArmedAWordInsertionAtQIsAsTypedOnceAndSpendsIt()
    {
        var t = Armed(0);
        Assert.True(t.Edited(new TextEdit(0, 0, "m"), true));
        Assert.False(t.IsArmed);
        Assert.False(t.Edited(new TextEdit(0, 0, "m"), true));     // spent: judged by the pure function again
        Assert.True(Armed(4).Edited(new TextEdit(4, 3, "md"), true));   // over a selection that starts at q too
    }

    [Fact]
    public void WhileArmedAnInsertionElsewhereClears()
    {
        var t = Armed(3);
        Assert.False(t.Edited(new TextEdit(0, 0, "x"), true));
        Assert.False(t.IsArmed);
        t = Armed(3);
        t.Edited(new TextEdit(5, 0, " "), false);
        Assert.False(t.IsArmed);
        t = Armed(3);
        t.Edited(new TextEdit(0, 2, "ab"), true);                   // a replacement that starts elsewhere, though it ends at q
        Assert.False(t.IsArmed);
        t = Armed(3);
        t.Edited(new TextEdit(1, 4, "ab"), true);                   // one that covers q from before it
        Assert.False(t.IsArmed);
    }

    [Fact]
    public void WhileArmedANonWordInsertionAtQStaysArmed()
    {
        var t = Armed(3);
        Assert.False(t.Edited(new TextEdit(3, 0, "1"), false));
        Assert.False(t.Edited(new TextEdit(3, 0, "\r"), false));
        Assert.False(t.Edited(new TextEdit(3, 0, "M"), false));
        Assert.False(t.Edited(new TextEdit(3, 2, "paste"), false));
        Assert.Equal(3, t.ArmedAt);
    }

    [Fact]
    public void WhileArmedDeletionsShiftOrCollapseQAndNeverClear()
    {
        var t = Armed(5);
        t.Edited(new TextEdit(0, 2, ""), false);                    // before q: shifted
        Assert.Equal(3, t.ArmedAt);
        t.Edited(new TextEdit(1, 1, ""), false);
        Assert.Equal(2, t.ArmedAt);
        t.Edited(new TextEdit(2, 3, ""), false);                    // starting at q: q stays
        Assert.Equal(2, t.ArmedAt);
        t.Edited(new TextEdit(3, 1, ""), false);                    // after q: nothing
        Assert.Equal(2, t.ArmedAt);
        t.Edited(new TextEdit(0, 4, ""), false);                    // covering q from before: q moves to its start
        Assert.Equal(0, t.ArmedAt);
        Assert.True(t.IsArmed);
    }

    [Fact]
    public void TheRemovingEditThatIsItselfAWordInsertionAtQGoesInAsTypedAndStaysArmed()
    {
        // Select "Md", type "md" (consequence 4); select "M", type "m" (the retype rule): both
        // remove the capital, both go in as typed, and the override they armed stands until a
        // later word insertion at q spends it or an insertion elsewhere clears it.
        var t = new CapitalTracker();
        t.Produced(0, "M");
        Assert.True(t.Edited(new TextEdit(0, 2, "md"), true));
        Assert.Equal(0, t.ArmedAt);
        Assert.False(t.Edited(new TextEdit(2, 0, "x"), true));
        Assert.False(t.IsArmed);

        t.Produced(0, "M");
        Assert.True(t.Edited(new TextEdit(0, 1, "m"), true));
        Assert.Equal(0, t.ArmedAt);
        t.Edited(new TextEdit(0, 1, ""), false);
        Assert.Equal(0, t.ArmedAt);
        Assert.True(t.Edited(new TextEdit(0, 0, "m"), true));
        Assert.False(t.IsArmed);
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        var t = new CapitalTracker();
        t.Produced(3, "M");
        t.Clear();
        Assert.False(t.IsTracking);
        Assert.Null(t.Capital);
        var armed = Armed(3);
        armed.Clear();
        Assert.False(armed.IsArmed);
        Assert.False(armed.Edited(new TextEdit(3, 0, "m"), true));
    }

    [Fact]
    public void ANoOpEditChangesNothing()
    {
        var t = new CapitalTracker();
        t.Produced(3, "M");
        Assert.False(t.Edited(new TextEdit(1, 0, ""), false));
        Assert.Equal(3, t.CapitalAt);
        var armed = Armed(2);
        Assert.False(armed.Edited(new TextEdit(2, 0, ""), true));
        Assert.Equal(2, armed.ArmedAt);
    }

    [Fact]
    public void ProducedRejectsNonsense()
    {
        var t = new CapitalTracker();
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Produced(-1, "M"));
        Assert.Throws<ArgumentException>(() => t.Produced(0, ""));
        Assert.Throws<ArgumentNullException>(() => t.Produced(0, null!));
    }

    // ── the diff every edit is reduced to ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("", 0, 0, "m", 0, 0, "m")]                      // the first letter
    [InlineData("M", 1, 0, "Md", 1, 0, "d")]                     // typing on
    [InlineData("Md", 0, 2, "m", 0, 2, "m")]                     // a letter over a selection
    [InlineData("Md", 0, 0, "aMd", 0, 0, "a")]                   // a letter in front
    [InlineData("Md", 2, 0, "M", 1, 1, "")]                      // Backspace
    [InlineData("M", 1, 0, "", 0, 1, "")]                        // Backspace on the last unit
    [InlineData("Md", 0, 0, "d", 0, 1, "")]                      // Delete
    [InlineData("Xxx. M", 0, 5, "M", 0, 5, "")]                  // Cut
    [InlineData("M", 1, 0, "m", 0, 1, "m")]                      // Undo of a capital
    [InlineData("m", 1, 0, "M", 0, 1, "M")]                      // Redo of one
    [InlineData("aa", 1, 0, "aaa", 1, 0, "a")]                   // inside a run the selection says where
    [InlineData("aa", 2, 0, "aaa", 2, 0, "a")]
    [InlineData("aaa", 1, 0, "aa", 0, 1, "")]                    // a shrink inside a run, no key announced: the unit before the caret (Backspace assumed)
    [InlineData("aaa", 0, 0, "aa", 0, 1, "")]                    // at the start only Delete fits
    [InlineData("aaa", 3, 0, "a", 1, 2, "")]                     // Ctrl+Backspace: the two before the caret
    [InlineData("abc", 1, 0, "ac", 1, 1, "")]                    // only the unit after the caret fits: Delete, whatever was announced
    [InlineData("ab", 5, 0, "axb", 1, 0, "x")]                   // a selection the text cannot hold: the smallest replacement
    [InlineData("ab", -1, 0, "axb", 1, 0, "x")]
    [InlineData("hello world", 0, 11, "bye", 0, 11, "bye")]     // a paste over everything
    [InlineData("abc", 1, 1, "aXYc", 1, 1, "XY")]
    [InlineData("- x\r", 4, 0, "- x\rh", 4, 0, "h")]             // after a CR line end, as the control spells it
    public void BetweenFindsTheOneReplacement(string old, int start, int length, string @new, int editStart, int editLength, string inserted) =>
        Assert.Equal(new TextEdit(editStart, editLength, inserted), TextEdit.Between(old, start, length, @new));

    [Theory]
    [InlineData("II", 1, "I", DeleteDirection.Backward, 0)]      // md's I, before the caret
    [InlineData("II", 1, "I", DeleteDirection.Forward, 1)]       // the writer's, after it
    [InlineData("II", 1, "I", DeleteDirection.Unknown, 0)]       // nobody announced: Backspace assumed
    [InlineData("II", 0, "I", DeleteDirection.Backward, 0)]      // announced Backspace at the start: only Delete fits, and the text is what happened
    [InlineData("II", 2, "I", DeleteDirection.Forward, 1)]       // announced Delete at the end: only Backspace fits
    [InlineData("aaa", 2, "a", DeleteDirection.Backward, 0)]     // Ctrl+Backspace inside a run
    [InlineData("aaa", 1, "a", DeleteDirection.Forward, 1)]      // Ctrl+Delete inside a run
    [InlineData("a𐐀𐐀", 3, "a𐐀", DeleteDirection.Backward, 1)]   // a pair as one, before the caret
    [InlineData("a𐐀𐐀", 1, "a𐐀", DeleteDirection.Forward, 1)]    // and after it
    public void BetweenAnchorsACaretDeletionOnTheSideTheKeyAnnounced(string old, int caret, string @new, DeleteDirection direction, int editStart) =>
        Assert.Equal(new TextEdit(editStart, old.Length - @new.Length, ""), TextEdit.Between(old, caret, 0, @new, direction));

    [Fact]
    public void TheDirectionNeverChangesAnEditThatIsNotACaretShrink()
    {
        // A lingering flag — Backspace over a selection, a key repeat that met a selection, a
        // history step in the same turn — anchors at the selection or falls to the smallest diff as
        // before; only a collapsed caret's shrink reads it.
        Assert.Equal(new TextEdit(0, 5, ""), TextEdit.Between("Xxx. M", 0, 5, "M", DeleteDirection.Backward));
        Assert.Equal(new TextEdit(1, 0, "b"), TextEdit.Between("a", 1, 0, "ab", DeleteDirection.Backward));
        Assert.Equal(new TextEdit(0, 1, "m"), TextEdit.Between("M", 1, 0, "m", DeleteDirection.Forward));
        Assert.Equal(new TextEdit(1, 1, ""), TextEdit.Between("abc", 5, 0, "ac", DeleteDirection.Backward));   // a caret the text cannot hold
    }

    [Theory]
    [InlineData(VirtualKeys.Back, DeleteDirection.Backward)]
    [InlineData(VirtualKeys.Delete, DeleteDirection.Forward)]
    [InlineData(VirtualKeys.Z, DeleteDirection.Unknown)]
    [InlineData(VirtualKeys.Enter, DeleteDirection.Unknown)]
    [InlineData(VirtualKeys.X, DeleteDirection.Unknown)]                 // Ctrl+X is a selection, which the text settles
    public void OnlyBackspaceAndDeleteAnnounceASide(int key, DeleteDirection expected) =>
        Assert.Equal(expected, TypingHooks.DeletionKey(key));

    [Fact]
    public void TheDeletionFlagSettlesATurnLaterAndIsNotConsumed()
    {
        // A Backspace at the start of the text reports no change: the next keystroke is judged as
        // usual, and a Delete key with nothing after the caret likewise.
        var e = new Editor();
        e.Backspace();                                    // nothing to delete
        Assert.Equal(DeleteDirection.Unknown, e.Hooks.Deleting);
        e.Type("m");
        Assert.Equal("M", e.Text);
        e.Delete();
        Assert.Equal("M", e.Text);
        Assert.Equal(DeleteDirection.Unknown, e.Hooks.Deleting);

        var hooks = new TypingHooks();
        hooks.AnnounceDeletion(DeleteDirection.Forward);
        hooks.BeforeTextChanging("II", 1, 0, "I");
        Assert.Equal(DeleteDirection.Forward, hooks.Deleting);           // still up for a second change in the same step
        hooks.SettleDeletion();
        Assert.Equal(DeleteDirection.Unknown, hooks.Deleting);
    }

    [Fact]
    public void BetweenIsNullForIdenticalText()
    {
        Assert.Null(TextEdit.Between("", 0, 0, ""));
        Assert.Null(TextEdit.Between("Md is", 2, 0, "Md is"));
    }

    [Fact]
    public void BetweenNeverSplitsASurrogatePair()
    {
        // The two Deseret letters share a high surrogate: the smallest unit diff would start at 1.
        Assert.Equal(new TextEdit(0, 2, "𐐨"), TextEdit.Between("𐐀", 2, 0, "𐐨"));
        Assert.Equal(new TextEdit(0, 2, "𐐀"), TextEdit.Between("𐐨x", 3, 0, "𐐀x"));
        // Two pairs sharing a low surrogate: the suffix stops before it.
        Assert.Equal(new TextEdit(1, 2, "𝐀"), TextEdit.Between("a𐐀", 3, 0, "a𝐀"));
        // A pair deleted whole is two units, one edit.
        Assert.Equal(new TextEdit(0, 2, ""), TextEdit.Between("𐐀", 2, 0, ""));
    }

    [Fact]
    public void TheEditKnowsItsShape()
    {
        var insertion = new TextEdit(3, 0, "ab");
        Assert.Equal(3, insertion.End);
        Assert.Equal(2, insertion.Delta);
        Assert.True(insertion.IsInsertion);
        Assert.False(insertion.IsDeletion);

        var deletion = new TextEdit(3, 2, "");
        Assert.Equal(5, deletion.End);
        Assert.Equal(-2, deletion.Delta);
        Assert.False(deletion.IsInsertion);
        Assert.True(deletion.IsDeletion);

        var replacement = new TextEdit(3, 2, "xyz");
        Assert.Equal(1, replacement.Delta);
        Assert.True(replacement.IsInsertion);
        Assert.False(replacement.IsDeletion);
    }

    [Fact]
    public void BetweenChecksItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => TextEdit.Between(null!, 0, 0, ""));
        Assert.Throws<ArgumentNullException>(() => TextEdit.Between("", 0, 0, null!));
    }
}
