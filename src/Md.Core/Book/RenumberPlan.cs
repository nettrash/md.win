using System.Globalization;

namespace Md.Core.Book;

/// <summary>One rename inside a sibling group: on-disk name before and after.</summary>
public sealed record RenamePair(string From, string To);

/// <summary>One staged step of a two-phase rename: the item goes to <c>Temp</c> first, then to <c>Target</c>.</summary>
public sealed record StagedRename(string Original, string Temp, string Target);

/// <summary>The one file operation a rename plan needs; throws on failure so the executor can roll back.</summary>
public interface IBookFileMover
{
    void Move(string from, string to);
}

/// <summary><see cref="IBookFileMover"/> over System.IO — files and folders alike.</summary>
public sealed class LocalBookFileMover : IBookFileMover
{
    public static LocalBookFileMover Instance { get; } = new();

    public void Move(string from, string to)
    {
        // Directory.Move refuses a case-only rename on some .NET versions; the
        // two-phase plan never asks for one (the item always passes through a
        // temp name), so the rename reaches here as two ordinary moves.
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }
}

/// <summary>
/// Move Up / Move Down, materialised as renames. Port of <c>BookLibrary.renumberPlan</c>
/// and <c>applyRenames</c>: the sibling group's on-disk names in displayed order plus
/// the requested move in; the renames that spell the new order out on disk out. Every
/// sibling ends up with a zero-padded two-digit prefix — unprefixed names gain one, a
/// loose "2. " is normalised, an identical-stem swap trades names — so the order lives
/// in the names and needs no sidecar. Pure planning; the disk work is
/// <see cref="Apply"/>, two-phase so plans whose targets are also sources ("01-a" ↔
/// "02-a") never collide midway.
/// </summary>
public static class RenumberPlan
{
    /// <summary>Hidden staging name for one item; dot-prefixed so the listing skips it if a crash leaves it behind.</summary>
    public static string TempName() => ".md-reorder-" + Guid.NewGuid().ToString("D").ToUpperInvariant();

    /// <summary>
    /// The renames for moving the item at <paramref name="from"/> to <paramref name="to"/>.
    /// Empty when either index is out of range or they are equal. Only names that
    /// actually change are listed, in new-order position.
    /// </summary>
    public static IReadOnlyList<RenamePair> Plan(IReadOnlyList<string> names, int from, int to)
    {
        if (from < 0 || from >= names.Count || to < 0 || to >= names.Count || from == to)
            return Array.Empty<RenamePair>();
        // Remove first, then insert into the shorter list — Swift's
        // reordered.insert(reordered.remove(at:), at:) — so "to" indexes the final order.
        var reordered = new List<string>(names);
        var moving = reordered[from];
        reordered.RemoveAt(from);
        reordered.Insert(to, moving);
        var plan = new List<RenamePair>();
        for (var index = 0; index < reordered.Count; index++)
        {
            var name = reordered[index];
            var renamed = RenumberedName(name, index + 1);
            if (!string.Equals(renamed, name, StringComparison.Ordinal)) plan.Add(new RenamePair(name, renamed));
        }
        return plan;
    }

    /// <summary>
    /// <paramref name="name"/> at 1-based <paramref name="position"/>: prefix replaced
    /// (or added) as "NN-", stem and extension untouched. Swift's <c>%02d</c> — two digits
    /// minimum, never a cap: 100 → "100-".
    /// </summary>
    public static string RenumberedName(string name, int position)
    {
        var (baseName, ext) = BookNaming.SplitExtension(name);
        return position.ToString("D2", CultureInfo.InvariantCulture) + "-"
            + BookNaming.SplitPrefix(baseName).Stem
            + (ext.Length == 0 ? "" : "." + ext);
    }

    /// <summary>
    /// The two-phase steps for <paramref name="plan"/> inside <paramref name="folder"/>,
    /// as full paths. <paramref name="tempName"/> is a seam for deterministic tests.
    /// </summary>
    public static IReadOnlyList<StagedRename> Stage(IReadOnlyList<RenamePair> plan, string folder, Func<string>? tempName = null)
    {
        tempName ??= TempName;
        var staged = new List<StagedRename>(plan.Count);
        foreach (var pair in plan)
        {
            staged.Add(new StagedRename(
                Path.Combine(folder, pair.From),
                Path.Combine(folder, tempName()),
                Path.Combine(folder, pair.To)));
        }
        return staged;
    }

    /// <summary>
    /// Carry the plan out: every item to its temp name, then every temp to its target.
    /// A failure at any step moves whatever was staged back to where it came from
    /// (best effort — a rollback that fails is not retried) and reports the error
    /// text for the "Could not reorder" alert. An empty plan succeeds without touching
    /// the disk.
    /// </summary>
    public static bool Apply(IReadOnlyList<RenamePair> plan, string folder, IBookFileMover mover, out string? failure)
    {
        failure = null;
        if (plan.Count == 0) return true;
        var steps = Stage(plan, folder);
        var staged = new List<StagedRename>(steps.Count);
        try
        {
            foreach (var step in steps)
            {
                mover.Move(step.Original, step.Temp);
                staged.Add(step);
            }
            foreach (var step in staged) mover.Move(step.Temp, step.Target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            foreach (var step in staged)
            {
                try { mover.Move(step.Temp, step.Original); }
                catch (Exception) { /* nothing better to do — the alert names the first failure */ }
            }
            failure = e.Message;
            return false;
        }
    }

    /// <summary>Same, through System.IO.</summary>
    public static bool Apply(IReadOnlyList<RenamePair> plan, string folder, out string? failure) =>
        Apply(plan, folder, LocalBookFileMover.Instance, out failure);
}
