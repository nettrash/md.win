using Md.App.Logic.Seams;
using Md.Core.Book;
using Md.Core.Markdown;

namespace Md.App.Logic.View;

/// <summary>
/// Keeps <see cref="DerivedText"/> current without putting a parse on the typing path
/// (shell-design.md §5.5; macOS §6.10). The Mac's <c>.task(id: document.text)</c>: the very first
/// fill is immediate, every later one waits <b>250 ms</b> and is cancelled outright by the next
/// keystroke, and the computation itself runs off the UI thread.
/// </summary>
/// <remarks>
/// Nothing here is async: the delay is an <see cref="IScheduler"/> timer and the hop off and back
/// on to the UI thread is a pair of delegates, so a test drives the whole thing with a fake clock
/// and an inline runner and never awaits. A result that lands after a newer edit started is
/// dropped by its generation number rather than by cancelling the work — the parse is cheap and a
/// half-parsed document is never published.
/// </remarks>
public sealed class DerivedTextScheduler
{
    /// <summary>The trailing debounce; the first computation does not wait.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    /// <summary>How the computation leaves the UI thread. Default <see cref="Task.Run(Action)"/>; tests pass <c>work =&gt; work()</c>.</summary>
    public delegate void RunOffThread(Action work);

    readonly IScheduler _scheduler;
    readonly IUiThread _ui;
    readonly IWordCounter _words;
    readonly Func<string, IReadOnlyList<DiagramRef>> _diagrams;
    readonly RunOffThread _offThread;

    IDisposable? _pending;
    long _generation;
    bool _hasComputed;

    /// <param name="diagrams">
    /// The Export ▸ Diagram as SVG rows. Defaults to "none" because Core's <c>DiagramSvg</c> is a
    /// Wave-C module: when it lands this becomes <c>DiagramSvg.Diagrams</c> at the one call site
    /// that builds the scheduler.
    /// </param>
    /// <param name="offThread">Null = <see cref="Task.Run(Action)"/>.</param>
    public DerivedTextScheduler(
        IScheduler scheduler,
        IUiThread ui,
        IWordCounter words,
        Func<string, IReadOnlyList<DiagramRef>>? diagrams = null,
        RunOffThread? offThread = null)
    {
        _scheduler = scheduler;
        _ui = ui;
        _words = words;
        _diagrams = diagrams ?? (_ => []);
        _offThread = offThread ?? (work => Task.Run(work));
    }

    /// <summary>The last published value; <see cref="DerivedText.Empty"/> until the first one lands.</summary>
    public DerivedText Current { get; private set; } = DerivedText.Empty;

    public event Action<DerivedText>? Changed;

    /// <summary>
    /// The document text changed (and, with the same text, "the window appeared"). Cancels a pending
    /// computation and starts the next one — immediately the first time, after <see cref="Delay"/>
    /// afterwards.
    /// </summary>
    public void TextChanged(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _pending?.Dispose();
        _pending = null;
        if (!_hasComputed)
        {
            // The flag flips when the first computation STARTS, not when it lands: a burst of
            // keystrokes before the first result must not all skip the debounce.
            _hasComputed = true;
            Start(text);
            return;
        }
        _pending = _scheduler.After(Delay, () =>
        {
            _pending = null;
            Start(text);
        });
    }

    /// <summary>Drop a pending computation and ignore any in flight (the window is closing).</summary>
    public void Cancel()
    {
        _pending?.Dispose();
        _pending = null;
        _generation++;
    }

    /// <summary>The computation itself, for the callers that need it synchronously (the self-test, an export).</summary>
    public DerivedText Compute(string text) => new(
        _words.Count(text),
        WritingStats.Characters(text),
        MarkdownParser.Outline(text),
        MarkdownParser.Notes(text),
        _diagrams(text));

    void Start(string text)
    {
        var generation = ++_generation;
        _offThread(() =>
        {
            var derived = Compute(text);
            _ui.Post(() =>
            {
                if (generation != _generation) return;
                Current = derived;
                Changed?.Invoke(derived);
            });
        });
    }
}
