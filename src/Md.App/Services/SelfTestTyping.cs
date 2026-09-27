// The typing scenarios the in-app self-test (shell-design.md §11.4) plays on the REAL editor, as
// data. The same file is compiled into Md.App.Logic.Tests (MD_SELFTEST_VECTORS), which plays every
// scenario through TypingHooksTests' modelled editor — the fake TextBox raising the control's events
// in the control's order — and asserts the same expected text. So an expectation here is never a
// guess: it is what the tested model does, and a self-test failure on Windows is a place where the
// real TextBox disagrees with the model, not a typo in a vector. Pure C#, no WinUI: it has to compile
// on the Mac, where the tests run.
#if SELFTEST || MD_SELFTEST_VECTORS

namespace Md.App.Services;

/// <summary>One key, as the self-test injects it and the model types it.</summary>
internal enum TypingKeyKind
{
    /// <summary>Characters, one keystroke per scalar (<c>KEYEVENTF_UNICODE</c> on Windows).</summary>
    Text,

    /// <summary>Enter with no modifier.</summary>
    Enter,

    /// <summary>Backspace at the caret.</summary>
    Backspace,

    /// <summary>Ctrl+Z.</summary>
    Undo,
}

internal readonly record struct TypingKey(TypingKeyKind Kind, string Text = "")
{
    public static TypingKey Type(string text) => new(TypingKeyKind.Text, text);

    public static readonly TypingKey Enter = new(TypingKeyKind.Enter);

    public static readonly TypingKey Backspace = new(TypingKeyKind.Backspace);

    public static readonly TypingKey Undo = new(TypingKeyKind.Undo);
}

/// <summary>
/// A box holding <see cref="Initial"/> with the caret at <see cref="Caret"/> and a fresh history,
/// the two Edit ▸ Typing switches as given, the keys pressed in order — and the text the box must
/// then hold, spelled as the control reports it (CR line ends).
/// </summary>
internal sealed record TypingScenario(
    string Name,
    string Initial,
    int Caret,
    bool ContinueLists,
    bool CapitalizeSentences,
    IReadOnlyList<TypingKey> Keys,
    string Expected);

internal static class SelfTestTyping
{
    /// <summary>
    /// Enter's continuation, the capital, the override gesture, Ctrl+Z after a capital and each
    /// switch off — the 1.5 behaviour the CHANGELOG promises, in the smallest texts that show it.
    /// </summary>
    public static IReadOnlyList<TypingScenario> Scenarios { get; } =
    [
        // Continue Lists and Tables (capital off, so only Enter is under test).
        new("enter.continuesABullet", "", 0, true, false,
            [TypingKey.Type("- a"), TypingKey.Enter], "- a\r- "),
        new("enter.countsANumberedItemOn", "", 0, true, false,
            [TypingKey.Type("1. a"), TypingKey.Enter], "1. a\r2. "),
        new("enter.onAnEmptyItemEndsTheList", "", 0, true, false,
            [TypingKey.Type("- a"), TypingKey.Enter, TypingKey.Enter], "- a\r"),
        new("enter.switchOffIsAPlainNewline", "", 0, false, false,
            [TypingKey.Type("- a"), TypingKey.Enter], "- a\r"),

        // Capitalize Sentences (lists off, so only the letter is under test).
        new("capital.firstLetterOfTheDocument", "", 0, false, true,
            [TypingKey.Type("hello")], "Hello"),
        new("capital.firstLetterOfALine", "One\r", 4, false, true,
            [TypingKey.Type("two")], "One\rTwo"),
        new("capital.afterASentenceEnds", "One. ", 5, false, true,
            [TypingKey.Type("two")], "One. Two"),
        new("capital.overrideDeleteAndRetypeStaysLowercase", "", 0, false, true,
            [TypingKey.Type("m"), TypingKey.Backspace, TypingKey.Type("md")], "md"),
        new("capital.ctrlZAfterACapitalRestoresTheLowercaseLetter", "", 0, false, true,
            [TypingKey.Type("m"), TypingKey.Undo], "m"),
        // 2026-09-27, the manual checklist: after that Ctrl+Z, a new line's first word went in lowercase.
        new("capital.ctrlZThenANewLineStillCapitalizes", "", 0, false, true,
            [TypingKey.Type("m"), TypingKey.Undo, TypingKey.Enter, TypingKey.Type("hello")], "m\rHello"),
        // The caret after that Ctrl+Z is collapsed after the letter: a d typed on is the second letter, not its replacement.
        new("capital.ctrlZThenTypingOnKeepsTheLetter", "", 0, false, true,
            [TypingKey.Type("m"), TypingKey.Undo, TypingKey.Type("d")], "md"),
        // The box's text holds the capital itself, not only its screen: nothing typed after it refreshes the text.
        new("capital.aCapitalTypedLastIsInTheText", "One. ", 5, false, true,
            [TypingKey.Type("t")], "One. T"),
        new("capital.switchOffLeavesTheLetterAlone", "", 0, false, false,
            [TypingKey.Type("hello")], "hello"),

        // Both on, the way a writer has them.
        new("both.aListItemsFirstLetter", "", 0, true, true,
            [TypingKey.Type("- one"), TypingKey.Enter, TypingKey.Type("two")], "- One\r- Two"),
    ];
}

#endif
