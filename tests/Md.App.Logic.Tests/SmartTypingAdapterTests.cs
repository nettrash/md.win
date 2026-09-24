using Md.App.Logic.Settings;
using Md.App.Logic.Text;
using Md.Core.Text;

namespace Md.App.Logic.Tests;

/// <summary>
/// The pure half of the editor's typing hooks (docs/smart-typing.md §3.3 "Windows", the retype
/// rule of §3.4, §3.5): the <c>BeforeTextChanging</c> diff, the reduction of an insertion to one
/// scalar, the retype rule, the plan the pane applies and the Enter edit's CR spelling. The
/// stateful half — the tracked capital and the override of §3.4 — is <c>TypingHooksTests</c>'.
/// <c>EditorPane</c> is a shell around both; what only a real <c>TextBox</c> can settle (that
/// <c>KeyDown</c> pre-empts the control's Enter, that a <c>SelectedText</c> assignment is one undo
/// unit) is on the Windows checklist, not here.
/// </summary>
public class SmartTypingAdapterTests
{
    // ── The diff ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", 0, 0, "h", "h")]                          // the first letter of a document
    [InlineData("ab", 1, 0, "axb", "x")]                      // a letter between two
    [InlineData("ab", 2, 0, "ab ", " ")]                      // a space at the end
    [InlineData("ab", 0, 2, "hello", "hello")]                // a word over a whole selection
    [InlineData("abc", 1, 1, "aXYc", "XY")]                   // two units over one
    [InlineData("aa", 1, 0, "aaa", "a")]                      // ambiguous by prefix/suffix, unambiguous by the selection
    [InlineData("- x\r", 4, 0, "- x\rh", "h")]                // after a CR line end, as the control spells it
    [InlineData("ab", 1, 0, "a\U0001F600b", "\U0001F600")]    // a surrogate pair is one insertion of two units
    public void TheInsertionOverTheSelectionIsFound(string old, int start, int length, string @new, string expected) =>
        Assert.Equal(expected, SmartTypingAdapter.InsertionOverSelection(old, start, length, @new));

    [Theory]
    [InlineData("ab", 1, 0, "ab")]         // unchanged
    [InlineData("ab", 1, 0, "a")]          // a deletion
    [InlineData("ab", 0, 1, "b")]          // a selection deleted
    [InlineData("Hello", 1, 0, "hello")]   // an undo of a capital: same length, changed before the caret
    [InlineData("ab", 1, 0, "xab")]        // an insertion, but not at the selection
    [InlineData("ab", 1, 0, "abx")]        // an insertion after the selection, not at it
    [InlineData("ab", 3, 0, "abc")]        // a selection the text cannot hold
    [InlineData("ab", -1, 0, "xab")]
    [InlineData("ab", 1, -1, "axb")]
    public void AnythingElseIsNotAnInsertion(string old, int start, int length, string @new) =>
        Assert.Null(SmartTypingAdapter.InsertionOverSelection(old, start, length, @new));

    [Fact]
    public void ALetterOverASelectionIsAnInsertion() =>
        Assert.Equal("x", SmartTypingAdapter.InsertionOverSelection("ab", 0, 1, "xb"));

    // ── §3.3: word insertions ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("h")]
    [InlineData("hello")]
    [InlineData("hello ")]                 // one trailing SP — a predicted word with its space
    [InlineData("écrire")]
    [InlineData("привет")]
    [InlineData("\U00010428")]             // DESERET SMALL LETTER LONG I — Ll, off the BMP
    [InlineData("don't")]
    [InlineData("re-enter")]
    [InlineData("ǆ")]                      // U+01C6, Ll
    public void AWordInsertionStartsWithALowercaseLetterAndHoldsNoSpace(string insertion) =>
        Assert.True(SmartTypingAdapter.IsWordInsertion(insertion));

    [Theory]
    [InlineData("")]
    [InlineData("H")]                      // uppercase
    [InlineData("1")]                      // a digit
    [InlineData(" ")]                      // SP alone: its first scalar is not a letter
    [InlineData(" h")]                     // a leading space
    [InlineData("hello  ")]                // two trailing spaces
    [InlineData("hello world")]            // a phrase (dictation, paste)
    [InlineData("hello\tx")]
    [InlineData("hello\u00A0x")]           // NBSP is WS19
    [InlineData("hello\u3000")]            // ideographic space, even trailing: only SP may trail
    [InlineData("hello\u200B")]            // zero-width space is WS19
    [InlineData("hello\n")]                // a line terminator
    [InlineData("hello\r")]
    [InlineData("h\r\n")]
    [InlineData("https://a.b")]            // the four §2.1f markers, on the insertion itself
    [InlineData("www.nettrash.me")]
    [InlineData("nettrash@nettrash.me")]
    [InlineData("~/Documents/x")]
    [InlineData("a/b")]
    [InlineData("\u0301x")]                // a mark first
    [InlineData("ª")]                      // U+00AA is Lo since Unicode 6.1, not Ll
    [InlineData("\u2028h")]                // LINE SEPARATOR is neither a terminator nor WS19 — but its first scalar is not a letter
    public void AnythingElseIsNotAWordInsertion(string insertion) =>
        Assert.False(SmartTypingAdapter.IsWordInsertion(insertion));

    [Fact]
    public void EveryWS19UnitInsideAWordDisqualifiesIt()
    {
        int[] ws19 =
        [
            0x0009, 0x0020, 0x00A0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006,
            0x2007, 0x2008, 0x2009, 0x200A, 0x200B, 0x202F, 0x205F, 0x3000,
        ];
        Assert.Equal(19, ws19.Length);
        foreach (var u in ws19)
        {
            var word = "he" + (char)u + "llo";
            Assert.False(SmartTypingAdapter.IsWordInsertion(word), $"U+{u:X4} inside a word");
            Assert.False(SmartTypingAdapter.IsWordInsertion("hello" + (char)u + (char)u), $"two trailing U+{u:X4}");
        }
    }

    [Fact]
    public void UnitsThatAreNotWS19AreOrdinaryContent()
    {
        // §0.1 / §0.3: U+2028, U+2029, U+0085, U+000B and U+000C are neither terminators nor whitespace.
        foreach (var u in new[] { 0x2028, 0x2029, 0x0085, 0x000B, 0x000C, 0xFEFF })
            Assert.True(SmartTypingAdapter.IsWordInsertion("he" + (char)u + "llo"), $"U+{u:X4} inside a word");
    }

    [Theory]
    [InlineData("h", "h")]
    [InlineData("hello", "h")]
    [InlineData("\U00010428x", "\U00010428")]
    public void TheFirstScalarIsOneOrTwoUnits(string text, string expected) =>
        Assert.Equal(expected, SmartTypingAdapter.FirstScalar(text));

    [Fact]
    public void LoneSurrogatesAreTheirOwnScalarAndBelongToNoClass()
    {
        // §0.1: a lone surrogate is a scalar of category Cs. A Fact, not theory rows: xUnit
        // serialises theory data through UTF-8, and a lone surrogate comes back as U+FFFD.
        Assert.Equal("\uD83D", SmartTypingAdapter.FirstScalar("\uD83Dx"));
        Assert.Equal("\uDE00", SmartTypingAdapter.FirstScalar("\uDE00h"));
        Assert.False(SmartTypingAdapter.IsWordInsertion("\uD83D"));
        Assert.False(SmartTypingAdapter.IsWordInsertion("\uDE00h"));
        Assert.Null(SmartTypingAdapter.Upper("\uD83D"));
        Assert.Null(SmartTypingAdapter.Upper("\uDE00"));
    }

    [Fact]
    public void TheFirstScalarOfNothingIsAnError() => Assert.Throws<ArgumentException>(() => SmartTypingAdapter.FirstScalar(""));

    // ── §0.7 through the pure function ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a", "A")]
    [InlineData("é", "É")]
    [InlineData("я", "Я")]
    [InlineData("ǆ", "Ǆ")]                // uppercase, not titlecase ǅ
    [InlineData("\u0131", "I")]            // dotless i: the vector-decided mapping, not .NET's identity
    [InlineData("\U00010428", "\U00010400")]
    public void UpperIsThePureFunctionsMapping(string scalar, string expected) =>
        Assert.Equal(expected, SmartTypingAdapter.Upper(scalar));

    [Theory]
    [InlineData("ß")]                      // full mapping is SS, simple is itself
    [InlineData("µ")]                      // MICRO SIGN, excluded
    [InlineData("ა")]                      // Georgian, excluded
    [InlineData("A")]                      // not lowercase
    [InlineData("1")]
    public void UpperIsUndefinedWhereTheSpecificationSaysSo(string scalar) => Assert.Null(SmartTypingAdapter.Upper(scalar));

    // ── §3.4: the retype rule ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ALowercaseLetterOverItsOwnCapitalGoesInAsTyped()
    {
        Assert.True(SmartTypingAdapter.RetypesOwnCapital("Md is", 0, 1, "m"));
        Assert.True(SmartTypingAdapter.RetypesOwnCapital("x É", 2, 1, "é"));
        Assert.True(SmartTypingAdapter.RetypesOwnCapital("\U00010400", 0, 2, "\U00010428"));   // a two-unit selection, one scalar
    }

    [Fact]
    public void TheRetypeRuleNeedsExactlyOneScalarThatIsTheUpper()
    {
        Assert.False(SmartTypingAdapter.RetypesOwnCapital("Md is", 0, 0, "m"));      // no selection
        Assert.False(SmartTypingAdapter.RetypesOwnCapital("Md is", 0, 2, "m"));      // two scalars selected
        Assert.False(SmartTypingAdapter.RetypesOwnCapital("Md is", 1, 1, "m"));      // the selected scalar is not M
        Assert.False(SmartTypingAdapter.RetypesOwnCapital("Nd is", 0, 1, "m"));      // another capital
        Assert.False(SmartTypingAdapter.RetypesOwnCapital("Md is", 0, 1, "M"));      // not a lowercase letter
        Assert.False(SmartTypingAdapter.RetypesOwnCapital("\U00010400", 1, 1, "\U00010428"));   // half a pair
        Assert.False(SmartTypingAdapter.RetypesOwnCapital("Md", 1, 5, "m"));         // out of range
    }

    // ── The plan: the letter hook, whole ──────────────────────────────────────────────────────

    [Fact]
    public void TheFirstLetterOfADocumentIsCapitalized() =>
        Assert.Equal(new CapitalizationPlan(0, "h", "H", "H"), SmartTypingAdapter.Plan("", 0, 0, "h"));

    [Fact]
    public void APredictedWordIsCapitalizedOnItsFirstScalarOnly() =>
        Assert.Equal(new CapitalizationPlan(6, "then ", "T", "Then "), SmartTypingAdapter.Plan("Done. ", 6, 0, "Done. then "));

    [Fact]
    public void ThePlanRemembersWhatTheInsertionReplaced() =>
        Assert.Equal(new CapitalizationPlan(0, "h", "H", "H", Replaced: 2), SmartTypingAdapter.Plan("Xy", 0, 2, "h"));

    [Fact]
    public void ATwoUnitFirstScalarIsReplacedWhole() =>
        Assert.Equal(new CapitalizationPlan(0, "\U00010428x", "\U00010400", "\U00010400x"), SmartTypingAdapter.Plan("", 0, 0, "\U00010428x"));

    [Fact]
    public void TheBoxOwnCRLineEndsAreReadAsLineEnds()
    {
        // The control spells a line break U+000D; §0.1 accepts a lone CR, so nothing is converted on the way in.
        Assert.Equal(new CapitalizationPlan(6, "h", "H", "H"), SmartTypingAdapter.Plan("Notes\r", 6, 0, "Notes\rh"));
        Assert.Equal(new CapitalizationPlan(8, "h", "H", "H"), SmartTypingAdapter.Plan("Notes\r- ", 8, 0, "Notes\r- h"));
        Assert.Null(SmartTypingAdapter.Plan("```\rx\r", 6, 0, "```\rx\rh"));   // inside a fence
    }

    [Fact]
    public void MidWordAndMidSentenceLettersAreLeftAlone()
    {
        Assert.Null(SmartTypingAdapter.Plan("H", 1, 0, "He"));
        Assert.Null(SmartTypingAdapter.Plan("Hello ", 6, 0, "Hello w"));
    }

    [Fact]
    public void EverythingThatIsNotAWordInsertionIsLeftAlone()
    {
        Assert.Null(SmartTypingAdapter.Plan("", 0, 0, "hello world"));      // a phrase
        Assert.Null(SmartTypingAdapter.Plan("", 0, 0, "https://a.b"));      // a link
        Assert.Null(SmartTypingAdapter.Plan("", 0, 0, "H"));                // already a capital
        Assert.Null(SmartTypingAdapter.Plan("", 0, 0, " "));                // a space
        Assert.Null(SmartTypingAdapter.Plan("ab", 1, 0, "a"));              // a deletion
        Assert.Null(SmartTypingAdapter.Plan("Hello", 1, 0, "hello"));       // an undo
        Assert.Null(SmartTypingAdapter.Plan("", 0, 0, ""));                 // nothing
    }

    [Fact]
    public void SelectAndRetypeIsAsTypedByThePlanAlone()
    {
        // §3.4's last rule needs no state: M selected, m typed → null, whoever wrote the M.
        Assert.Null(SmartTypingAdapter.Plan("Md is", 0, 1, "md is"));
        // The same keystroke over two scalars is judged: text′ is "" and rule A applies.
        Assert.Equal("M", SmartTypingAdapter.Plan("Md is", 0, 2, "m is")!.Value.Capital);
    }

    [Fact]
    public void ThePlanKnowsNothingOfPasteOrTheOverride()
    {
        // Both are the hooks' decisions, made before Plan is asked: a word at a line start is a
        // plan here whatever brought it, and TypingHooksTests pins that a paste and an armed
        // override never reach this.
        Assert.NotNull(SmartTypingAdapter.Plan("", 0, 0, "hello"));
        Assert.NotNull(SmartTypingAdapter.Plan("", 0, 0, "m"));
    }

    // ── Applies: the check before the replacement ─────────────────────────────────────────────

    [Fact]
    public void ThePlanAppliesWhenTheInsertionLandedAndTheCaretFollowsIt()
    {
        var plan = new CapitalizationPlan(6, "then ", "T", "Then ");
        Assert.True(SmartTypingAdapter.Applies(plan, "Done. then ", 11, 0));
        Assert.False(SmartTypingAdapter.Applies(plan, "Done. then ", 10, 0));   // caret elsewhere
        Assert.False(SmartTypingAdapter.Applies(plan, "Done. then ", 6, 5));    // a selection that is not the replaced one
        // The control reporting the selection a beat late: still the one the insertion replaced.
        Assert.True(SmartTypingAdapter.Applies(plan, "Done. then ", 6, 0));
        var overSelection = new CapitalizationPlan(0, "m", "M", "M", Replaced: 2);
        Assert.True(SmartTypingAdapter.Applies(overSelection, "m is", 1, 0));
        Assert.True(SmartTypingAdapter.Applies(overSelection, "m is", 0, 2));
        Assert.False(SmartTypingAdapter.Applies(overSelection, "m is", 0, 1));
        Assert.False(SmartTypingAdapter.Applies(overSelection, "m is", 0, 0));
        Assert.False(SmartTypingAdapter.Applies(plan, "Done. them ", 11, 0));   // the control changed it
        Assert.False(SmartTypingAdapter.Applies(plan, "Done. ", 6, 0));         // cancelled
        Assert.False(SmartTypingAdapter.Applies(new CapitalizationPlan(-1, "x", "X", "X"), "x", 1, 0));
    }

    // ── Enter ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnterContinuesAListWithTheControlsOwnLineBreak()
    {
        var edit = SmartTypingAdapter.Enter("- item", 6, 0);
        Assert.NotNull(edit);
        Assert.Equal(new SmartTyping.EnterEdit(6, 0, "\r- ", 9), edit);
        Assert.DoesNotContain('\n', edit!.Value.Replacement);
    }

    [Fact]
    public void EnterEditsAreThePureFunctionsWithLFSwappedForCR()
    {
        // Same location, length and caret: the swap is one unit for one unit.
        foreach (var (text, start, length) in new[] { ("- item", 6, 0), ("1. one\r2. two", 13, 0), ("> quote", 7, 0), ("| a | b |\r| - | - |\r| 1 | 2 |", 29, 0), ("- ", 2, 0), ("- a\r- b", 3, 0) })
        {
            var pure = SmartTyping.Enter(text, start, start + length);
            var boxed = SmartTypingAdapter.Enter(text, start, length);
            Assert.Equal(pure is null, boxed is null);
            if (pure is null) continue;
            Assert.Equal(pure.Value.Location, boxed!.Value.Location);
            Assert.Equal(pure.Value.Length, boxed.Value.Length);
            Assert.Equal(pure.Value.Caret, boxed.Value.Caret);
            Assert.Equal(pure.Value.Replacement.Replace('\n', '\r'), boxed.Value.Replacement);
        }
    }

    [Fact]
    public void EnterAnswersNullWhereThePureFunctionDoesAndForABadSelection()
    {
        Assert.Null(SmartTypingAdapter.Enter("plain prose", 11, 0));
        Assert.Null(SmartTypingAdapter.Enter("", 0, 0));
        Assert.Null(SmartTypingAdapter.Enter("- item", 7, 0));
        Assert.Null(SmartTypingAdapter.Enter("- item", -1, 0));
        Assert.Null(SmartTypingAdapter.Enter("- item", 2, 10));
    }

    [Fact]
    public void EnterOverASelectionDeletesItInTheSameEdit()
    {
        // §0.9 item 4: "te" selected inside "- item" — the continue rule inserts at the caret and the
        // edit's range is the selection, so one undoable step both deletes and continues.
        Assert.Equal(new SmartTyping.EnterEdit(3, 2, "\r- ", 6), SmartTypingAdapter.Enter("- item", 3, 2));
        // The whole content selected: text′ is an empty item, so the exit rule clears the line (§1.3).
        Assert.Equal(new SmartTyping.EnterEdit(0, 6, "", 0), SmartTypingAdapter.Enter("- item", 2, 4));
    }

    // ── The settings ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BothTypingSettingsDefaultToOn()
    {
        var store = new InMemorySettingsStore();
        Assert.Equal(TypingSettings.Defaults, TypingSettings.Read(store));
        Assert.True(TypingSettings.Defaults.ContinueLists);
        Assert.True(TypingSettings.Defaults.CapitalizeSentences);
    }

    [Fact]
    public void TheSettingsReadTheirKeysAndNothingElse()
    {
        var store = new InMemorySettingsStore();
        store.SetBool(SettingsKeys.ContinueLists, false);
        Assert.Equal(new TypingSettings(false, true), TypingSettings.Read(store));
        store.SetBool(SettingsKeys.CapitalizeSentences, false);
        Assert.Equal(new TypingSettings(false, false), TypingSettings.Read(store));
        store.SetString(SettingsKeys.ContinueLists, "true");                   // the wrong type reads as absent
        Assert.Equal(new TypingSettings(true, false), TypingSettings.Read(store));
    }

    [Fact]
    public void ToggleFlipsTheStoredValueFromItsDefaultAndRaisesChanged()
    {
        var store = new InMemorySettingsStore();
        var changes = new List<string>();
        store.Changed += changes.Add;

        TypingSettings.Toggle(store, SettingsKeys.CapitalizeSentences);      // absent ⇒ true ⇒ off
        Assert.False(store.GetBool(SettingsKeys.CapitalizeSentences, true));
        TypingSettings.Toggle(store, SettingsKeys.CapitalizeSentences);
        Assert.True(store.GetBool(SettingsKeys.CapitalizeSentences, false));
        TypingSettings.Toggle(store, SettingsKeys.ContinueLists);
        Assert.False(store.GetBool(SettingsKeys.ContinueLists, true));

        Assert.Equal(new[] { SettingsKeys.CapitalizeSentences, SettingsKeys.CapitalizeSentences, SettingsKeys.ContinueLists }, changes);
        Assert.Throws<ArgumentOutOfRangeException>(() => TypingSettings.Toggle(store, SettingsKeys.PdfPageSize));
    }

    [Fact]
    public void OnlyTheTwoKeysAreTypingKeys()
    {
        Assert.True(TypingSettings.IsKey(SettingsKeys.ContinueLists));
        Assert.True(TypingSettings.IsKey(SettingsKeys.CapitalizeSentences));
        foreach (var key in SettingsKeys.All.Where(k => k != SettingsKeys.ContinueLists && k != SettingsKeys.CapitalizeSentences))
            Assert.False(TypingSettings.IsKey(key));
    }
}
