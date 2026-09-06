namespace Md.Core.Markdown;

/// <summary>
/// A bounded, thread-safe memo of finished ```plot containers, keyed on the exact
/// fence text that produced them.
/// </summary>
/// <remarks>
/// The live preview rebuilds the whole HTML document on every text change, and a
/// plot is the only rich block whose real cost is paid synchronously in that
/// render (every other engine runs asynchronously in the page). Measured on the
/// Mac port: 4.87 ms for the default figure, 187 ms for four eight-series plots.
///
/// Memoising is sound because <see cref="Plot.RenderPlot(string)"/> is a pure
/// function of one string: no clock, no locale, no theme flag (the ink is
/// <c>currentColor</c>, the series colours are baked). A memo of a pure function
/// returns exactly what recomputing would.
///
/// The lock is load-bearing: the export paths render off the UI thread while the
/// preview renders on it, and two threads inside an unsynchronised
/// <see cref="Dictionary{TKey, TValue}"/> corrupt it rather than read stale.
/// Every critical section is one dictionary probe; the render itself happens
/// outside the lock (two threads racing the same miss both render; the second
/// store simply wins with the same bytes).
///
/// 32 entries, least recently used, the same bound md.vscode's preview uses for
/// its diagram cache.
/// </remarks>
public sealed class PlotMemo
{
    /// <summary>Entries, hits and misses. Read by the tests; nothing else.</summary>
    public readonly record struct Statistics(int Entries, int Hits, int Misses);

    /// <summary>
    /// The largest container worth remembering, in UTF-16 code units — eight times
    /// the default figure.
    /// </summary>
    /// <remarks>
    /// The entry ceiling bounds the count, not the bytes: one fence may legally
    /// reach ~1.8 MB (24 series × <c>samples: 5000</c> at 2000×2000), and 32 of
    /// those would pin ~57 MB. Such a figure is also the one a memo helps least.
    /// Kotlin and TypeScript bound in UTF-16 units; Swift in UTF-8 bytes. The
    /// output is the same either way (a container that large is never memoised
    /// by any port), and the UTF-16 siblings are the ones C# strings resemble.
    /// </remarks>
    public const int LargestMemoisedValue = 128 * 1024;

    private readonly int limit;
    private readonly Lock gate = new();
    private readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);
    /// <summary>
    /// Keys, oldest first. At 32 entries a linear move costs less than a linked
    /// list's bookkeeping, and it is the same two lines in every port.
    /// </summary>
    private readonly List<string> order = new();
    private int hits;
    private int misses;

    public PlotMemo(int limit)
    {
        this.limit = limit;
    }

    /// <summary>The memoised container for <paramref name="key"/>, moved to the young end, or null.</summary>
    public string? Value(string key)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(key, out var value))
            {
                misses += 1;
                return null;
            }
            var index = IndexOf(key);
            if (index >= 0)
            {
                order.RemoveAt(index);
                order.Add(key);
            }
            hits += 1;
            return value;
        }
    }

    /// <summary>Memoise <paramref name="value"/>, evicting the least recently used key past the limit.</summary>
    public void Store(string value, string key)
    {
        // Before the lock, deliberately: the size test needs no shared state.
        if (value.Length > LargestMemoisedValue) return;
        lock (gate)
        {
            var existed = entries.ContainsKey(key);
            entries[key] = value;
            if (existed)
            {
                var index = IndexOf(key);
                if (index >= 0) order.RemoveAt(index);
            }
            order.Add(key);
            while (order.Count > limit)
            {
                entries.Remove(order[0]);
                order.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// The current <see cref="Statistics"/>. Named <c>Snapshot</c> because C#
    /// lets no member share its name with the nested type it returns (Swift's
    /// <c>memo.statistics</c> can); <see cref="Plot.MemoStatistics"/> keeps the
    /// family's name on the public surface.
    /// </summary>
    public Statistics Snapshot
    {
        get
        {
            lock (gate)
            {
                return new Statistics(entries.Count, hits, misses);
            }
        }
    }

    public void RemoveAll()
    {
        lock (gate)
        {
            entries.Clear();
            order.Clear();
            hits = 0;
            misses = 0;
        }
    }

    private int IndexOf(string key)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (string.Equals(order[index], key, StringComparison.Ordinal)) return index;
        }
        return -1;
    }
}
