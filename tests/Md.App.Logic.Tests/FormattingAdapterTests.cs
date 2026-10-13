using Md.App.Logic.Commands;
using Md.App.Logic.Settings;
using Md.App.Logic.Tests.Fakes;
using Md.App.Logic.Text;
using Md.Core.Text;

namespace Md.App.Logic.Tests;

/// <summary>
/// The editor's half of the formatting commands (1.6, #3; docs/formatting.md): the command → op map,
/// the TextBox's CR spelling on both sides of <see cref="MarkdownFormatting"/>, the typed-wrap plan
/// and its two-phase application, the AltGr decision for the heading chords, the setting, and when
/// the Format rows are live. The rules themselves are pinned by the shared vectors in Md.Core.Tests.
/// </summary>
public class FormattingAdapterTests
{
    // ── Commands ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryFormatCommandHasItsOpAndNothingElseHasOne()
    {
        Assert.Equal(16, FormattingAdapter.Commands.Count);
        Assert.Equal(FormatOp.Code, FormattingAdapter.OpFor(CommandId.InlineCode));
        Assert.Equal(FormatOp.Heading6, FormattingAdapter.OpFor(CommandId.Heading6));
        Assert.Equal(FormatOp.Outdent, FormattingAdapter.OpFor(CommandId.Outdent));
        // HeadingCycle is the phone bar's; no desktop command runs it.
        Assert.DoesNotContain(FormatOp.HeadingCycle, FormattingAdapter.Commands.Select(id => FormattingAdapter.OpFor(id)!.Value));
        foreach (var id in Enum.GetValues<CommandId>().Except(FormattingAdapter.Commands))
            Assert.Null(FormattingAdapter.OpFor(id));
    }

    // ── The box's CR line breaks ────────────────────────────────────────────────────────────

    [Fact]
    public void ALineCommandReadsAndWritesCr()
    {
        // "a\rb" selected whole: two lines to the TextBox, two lines to the rules.
        var edit = FormattingAdapter.Edit("a\rb", 0, 3, FormatOp.BulletList)!.Value;
        Assert.Equal(new FormatEdit(0, 3, "- a\r- b", 2, 7), edit);
        Assert.Equal("- a\r- b", edit.ApplyTo("a\rb"));
        Assert.DoesNotContain('\n', edit.Text);
    }

    [Fact]
    public void AnInlineCommandOverACrKeepsItsOffsets()
    {
        // The trimmed wrap stops before the line break, CR or LF alike.
        Assert.Equal(new FormatEdit(1, 5, "**word**", 3, 7), FormattingAdapter.Edit("aword\rb", 1, 4, FormatOp.Bold));   // "word"
        Assert.Equal(new FormatEdit(1, 5, "**word**", 3, 7), FormattingAdapter.Edit("aword\rb", 1, 5, FormatOp.Bold));   // "word\r"
    }

    [Fact]
    public void ASelectionOutsideTheBoxIsNoEdit()
    {
        Assert.Null(FormattingAdapter.Edit("abc", 2, 5, FormatOp.Bold));
        Assert.Null(FormattingAdapter.Edit("abc", -1, 0, FormatOp.Bold));
    }

    [Fact]
    public void OutdentWithNothingToRemoveIsNoEdit() => Assert.Null(FormattingAdapter.Edit("text", 2, 0, FormatOp.Outdent));

    // ── The typed wrap ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("*")]
    [InlineData("_")]
    [InlineData("`")]
    [InlineData("$")]
    public void OneOfTheFourTypedOverASelectionPlansAWrap(string typed)
    {
        // "say hello now", "hello" selected, the control about to hold "say * now".
        var wrap = FormattingAdapter.Wrap("say hello now", 4, 5, "say " + typed + " now");
        Assert.Equal(new FormatEdit(4, 9, typed + "hello" + typed, 5, 10), wrap);
    }

    [Theory]
    [InlineData("say hello now", 4, 0, "say *hello now")]     // a caret: ordinary typing
    [InlineData("say hello now", 4, 5, "say ~ now")]          // another character
    [InlineData("say hello now", 4, 5, "say ** now")]         // two units: a paste, not a keystroke
    [InlineData("say hello now", 4, 5, "say  now")]           // a deletion
    [InlineData("say hello now", 4, 5, "*say hello now")]     // a change somewhere else
    public void AnythingElseIsNotAWrap(string text, int start, int length, string newText) =>
        Assert.Null(FormattingAdapter.Wrap(text, start, length, newText));

    [Fact]
    public void AWrapAcrossLinesKeepsTheBoxsCr()
    {
        var wrap = FormattingAdapter.Wrap("a\rb", 0, 3, "`")!.Value;
        Assert.Equal("`a\rb`", wrap.Text);
    }

    [Fact]
    public void AfterTheControlInsertedTheCharacterTheWrapReplacesJustThatCharacter()
    {
        var wrap = FormattingAdapter.Wrap("say hello now", 4, 5, "say * now")!.Value;
        var pending = FormattingAdapter.AfterInsertion(wrap);
        Assert.Equal(new FormatEdit(4, 5, "*hello*", 5, 10), pending);

        // The box now holds the typed star, caret after it: the plan fits, and lands as the spec says.
        Assert.True(FormattingAdapter.StillApplies(pending, "say * now", 5, 0));
        Assert.Equal("say *hello* now", pending.ApplyTo("say * now"));

        // Anything else drops it, leaving the star as typed.
        Assert.False(FormattingAdapter.StillApplies(pending, "say * now", 4, 0));     // caret elsewhere
        Assert.False(FormattingAdapter.StillApplies(pending, "say * now", 5, 1));     // a selection
        Assert.False(FormattingAdapter.StillApplies(pending, "say _ now", 5, 0));     // another character
        Assert.False(FormattingAdapter.StillApplies(pending, "say", 5, 0));           // shorter text
    }

    // ── The AltGr guard ─────────────────────────────────────────────────────────────────────

    [Theory]
    // The layout types nothing for Ctrl+Alt+digit (US, UK): always a heading.
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    // It types a character (German ², Polish @): AltGr is typing; left Ctrl+Alt is the chord.
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    public void AHeadingChordLetsAltGrType(bool layoutTypesCharacter, bool rightAlt, bool leftAlt, bool formats) =>
        Assert.Equal(formats, HeadingChordGuard.Formats(layoutTypesCharacter, rightAlt, leftAlt));

    [Fact]
    public void TheGuardLooksOnlyAtTheHeadings()
    {
        Assert.Equal(
            [CommandId.Heading1, CommandId.Heading2, CommandId.Heading3, CommandId.Heading4, CommandId.Heading5, CommandId.Heading6],
            Enum.GetValues<CommandId>().Where(HeadingChordGuard.IsHeading));
        // Every chord the guard can let through is a Ctrl+Alt one — the AltGr shape.
        Assert.All(Enum.GetValues<CommandId>().Where(HeadingChordGuard.IsHeading),
            id => Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Alt, CommandTable.For(id).Chord!.Value.Modifiers));
    }

    // ── The setting ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WrapSelectionIsOnUntilItIsTurnedOff()
    {
        var store = new FakeSettingsStore();
        Assert.True(WrapSelectionSetting.Read(store));
        WrapSelectionSetting.Toggle(store);
        Assert.False(WrapSelectionSetting.Read(store));
        Assert.False(store.GetBool(SettingsKeys.WrapSelection, true));
        WrapSelectionSetting.Toggle(store);
        Assert.True(WrapSelectionSetting.Read(store));
        Assert.True(WrapSelectionSetting.IsKey("md.wrapSelection"));
        Assert.False(WrapSelectionSetting.IsKey(SettingsKeys.ContinueLists));
        // Not one of SmartTyping's pair: the typing hooks never see it.
        Assert.False(TypingSettings.IsKey(SettingsKeys.WrapSelection));
    }

    // ── When the rows are live ──────────────────────────────────────────────────────────────

    static ShellSnapshot Editing => ShellSnapshot.Empty with { HasDocument = true, EditorVisible = true };

    [Fact]
    public void FormatRowsNeedAnEditorWithADocumentOnScreen()
    {
        foreach (var id in FormattingAdapter.Commands)
        {
            Assert.True(CommandEnablement.IsEnabled(id, Editing), id.ToString());
            Assert.False(CommandEnablement.IsEnabled(id, Editing with { EditorVisible = false }), id.ToString());   // Preview, Zen reading
            Assert.False(CommandEnablement.IsEnabled(id, ShellSnapshot.Empty with { EditorVisible = true }), id.ToString());
            // The Book window: only while an article is open in it.
            Assert.True(CommandEnablement.IsEnabled(id, Editing with { IsBookWindow = true, IsEditingArticle = true }), id.ToString());
            Assert.False(CommandEnablement.IsEnabled(id, Editing with { IsBookWindow = true, IsEditingArticle = false }), id.ToString());
        }
    }

    [Fact]
    public void TheWrapToggleIsAlwaysLiveAndTickedFromTheSnapshot()
    {
        Assert.True(CommandEnablement.IsEnabled(CommandId.WrapSelection, ShellSnapshot.Empty));
        Assert.True(CommandEnablement.IsChecked(CommandId.WrapSelection, ShellSnapshot.Empty));
        Assert.False(CommandEnablement.IsChecked(CommandId.WrapSelection, ShellSnapshot.Empty with { WrapSelection = false }));
    }
}
