using Md.Core.Book;

namespace Md.Core.Tests;

/// <summary>
/// Move Up / Move Down planning (mdTests: the five testRenumberPlan* tests) plus the
/// Kotlin planMove / renumberedBookName vectors, and the two-phase executor against a
/// fake mover and against the disk.
/// </summary>
public class RenumberPlanTests
{
    private static (string[] From, string[] To) Split(IReadOnlyList<RenamePair> plan) =>
        (plan.Select(p => p.From).ToArray(), plan.Select(p => p.To).ToArray());

    [Fact]
    public void RenumberPlanMaterializesAMoveDown()
    {
        // a moves below b; c is already right, so only two renames.
        var (from, to) = Split(RenumberPlan.Plan(new[] { "01-a.md", "02-b.md", "03-c.md" }, 0, 1));
        Assert.Equal(new[] { "02-b.md", "01-a.md" }, from);
        Assert.Equal(new[] { "01-b.md", "02-a.md" }, to);
    }

    [Fact]
    public void RenumberPlanPrefixesUnprefixedSiblings()
    {
        // Unprefixed and loosely-prefixed names all end up with the canonical
        // zero-padded prefix; display names survive.
        var (from, to) = Split(RenumberPlan.Plan(new[] { "1. intro.md", "notes.md", "02-end.md" }, 2, 1));
        Assert.Equal(new[] { "1. intro.md", "notes.md" }, from);
        Assert.Equal(new[] { "01-intro.md", "03-notes.md" }, to);
    }

    [Fact]
    public void RenumberPlanHandlesChapterFolders()
    {
        // Chapters have no extension, and a dot in a folder name is part of the name.
        var (from, to) = Split(RenumberPlan.Plan(new[] { "Drafts", "01-Final" }, 1, 0));
        Assert.Equal(new[] { "Drafts" }, from);
        Assert.Equal(new[] { "02-Drafts" }, to);
    }

    [Fact]
    public void RenumberPlanSwapsIdenticalStems()
    {
        // Trading places between same-stem names yields renames that pass through
        // each other — the executor stages via temporary names so this never collides.
        var (from, to) = Split(RenumberPlan.Plan(new[] { "01-a.md", "02-a.md" }, 0, 1));
        Assert.Equal(new[] { "02-a.md", "01-a.md" }, from);
        Assert.Equal(new[] { "01-a.md", "02-a.md" }, to);
    }

    [Fact]
    public void RenumberPlanRejectsImpossibleMoves()
    {
        Assert.Empty(RenumberPlan.Plan(new[] { "01-a.md" }, 0, -1));
        Assert.Empty(RenumberPlan.Plan(new[] { "01-a.md" }, 0, 1));
        Assert.Empty(RenumberPlan.Plan(new[] { "01-a.md" }, 0, 0));
        Assert.Empty(RenumberPlan.Plan(Array.Empty<string>(), 0, 0));
        // Kotlin's variants of the same rule.
        Assert.Empty(RenumberPlan.Plan(new[] { "01-a.md", "02-b.md" }, 1, 1));
        Assert.Empty(RenumberPlan.Plan(new[] { "01-a.md", "02-b.md" }, 0, -1));
        Assert.Empty(RenumberPlan.Plan(new[] { "01-a.md", "02-b.md" }, 1, 2));
        Assert.Empty(RenumberPlan.Plan(Array.Empty<string>(), 0, 1));
    }

    [Fact]
    public void KotlinPlanMoveVectorsAgree()
    {
        Assert.Equal(new[] { new RenamePair("03-c.md", "02-c.md"), new RenamePair("02-b.md", "03-b.md") },
            RenumberPlan.Plan(new[] { "01-a.md", "02-b.md", "03-c.md" }, 2, 1));
        Assert.Equal(new[] { new RenamePair("beta.md", "01-beta.md"), new RenamePair("alpha.md", "02-alpha.md") },
            RenumberPlan.Plan(new[] { "alpha.md", "beta.md" }, 1, 0));
        Assert.Equal(new[] { new RenamePair("02-b.md", "01-b.md"), new RenamePair("1. a.md", "02-a.md") },
            RenumberPlan.Plan(new[] { "1. a.md", "02-b.md" }, 0, 1));
        Assert.Equal(new[] { new RenamePair("v1.2", "01-v1.2"), new RenamePair("01-Basics", "02-Basics") },
            RenumberPlan.Plan(new[] { "01-Basics", "v1.2" }, 1, 0));
    }

    [Fact]
    public void RenumberedNameReplacesOrAddsThePrefix()
    {
        Assert.Equal("03-intro.md", RenumberPlan.RenumberedName("intro.md", 3));
        Assert.Equal("02-deploy.md", RenumberPlan.RenumberedName("10-deploy.md", 2));
        Assert.Equal("01-setup", RenumberPlan.RenumberedName("2. setup", 1));
        // Three digits still work; the padding is a minimum, not a cap.
        Assert.Equal("100-late.md", RenumberPlan.RenumberedName("late.md", 100));
        Assert.Equal("01-v1.2", RenumberPlan.RenumberedName("v1.2", 1));
    }

    [Fact]
    public void StagingUsesHiddenTemporaryNamesInsideTheFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Book");
        var plan = RenumberPlan.Plan(new[] { "01-a.md", "02-a.md" }, 0, 1);
        var staged = RenumberPlan.Stage(plan, folder);
        Assert.Equal(2, staged.Count);
        Assert.Equal(Path.Combine(folder, "02-a.md"), staged[0].Original);
        Assert.Equal(Path.Combine(folder, "01-a.md"), staged[0].Target);
        Assert.All(staged, s =>
        {
            Assert.Equal(folder, Path.GetDirectoryName(s.Temp));
            Assert.StartsWith(".md-reorder-", Path.GetFileName(s.Temp), StringComparison.Ordinal);
        });
        Assert.NotEqual(staged[0].Temp, staged[1].Temp);
        var counter = 0;
        var deterministic = RenumberPlan.Stage(plan, folder, () => ".md-reorder-" + (++counter));
        Assert.Equal(Path.Combine(folder, ".md-reorder-1"), deterministic[0].Temp);
    }

    /// <summary>A folder as a dictionary name → content; Move fails the way a file system does.</summary>
    private sealed class MemoryMover : IBookFileMover
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public string? FailWhenMovingTo { get; set; }
        public List<(string From, string To)> Log { get; } = new();

        public void Move(string from, string to)
        {
            Log.Add((from, to));
            if (to == FailWhenMovingTo) throw new IOException("simulated: " + Path.GetFileName(to));
            if (!Files.TryGetValue(from, out var content)) throw new FileNotFoundException(from);
            if (Files.ContainsKey(to)) throw new IOException("exists: " + Path.GetFileName(to));
            Files.Remove(from);
            Files[to] = content;
        }
    }

    [Fact]
    public void ApplySwapsThroughTemporaryNamesWithoutColliding()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Book");
        var mover = new MemoryMover();
        mover.Files[Path.Combine(folder, "01-a.md")] = "first";
        mover.Files[Path.Combine(folder, "02-a.md")] = "second";
        var plan = RenumberPlan.Plan(new[] { "01-a.md", "02-a.md" }, 0, 1);

        Assert.True(RenumberPlan.Apply(plan, folder, mover, out var failure));
        Assert.Null(failure);
        Assert.Equal(2, mover.Files.Count);
        Assert.Equal("second", mover.Files[Path.Combine(folder, "01-a.md")]);
        Assert.Equal("first", mover.Files[Path.Combine(folder, "02-a.md")]);
        // Two phases: every source parked first, then every temp delivered.
        Assert.Equal(4, mover.Log.Count);
        Assert.All(mover.Log.Take(2), m => Assert.StartsWith(".md-reorder-", Path.GetFileName(m.To), StringComparison.Ordinal));
        Assert.All(mover.Log.Skip(2), m => Assert.StartsWith(".md-reorder-", Path.GetFileName(m.From), StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyRollsBackUndeliveredItemsWhenAMoveFails()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Book");
        var mover = new MemoryMover();
        mover.Files[Path.Combine(folder, "01-a.md")] = "a";
        mover.Files[Path.Combine(folder, "02-b.md")] = "b";
        mover.Files[Path.Combine(folder, "03-c.md")] = "c";
        // Plan: 02-b.md -> 01-b.md (delivered first), then 01-a.md -> 02-a.md (refused).
        mover.FailWhenMovingTo = Path.Combine(folder, "02-a.md");
        var plan = RenumberPlan.Plan(new[] { "01-a.md", "02-b.md", "03-c.md" }, 0, 1);

        Assert.False(RenumberPlan.Apply(plan, folder, mover, out var failure));
        Assert.Contains("02-a.md", failure, StringComparison.Ordinal);
        // Swift's rollback is "every temp back to its original", best effort: the
        // item that failed and the ones not yet delivered return to their old names,
        // while "02-b.md" — already delivered as "01-b.md" before the failure — stays
        // where phase two put it. Nothing is ever left hidden under a temporary name.
        Assert.Equal(new[] { "01-a.md", "01-b.md", "03-c.md" },
            mover.Files.Keys.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal("a", mover.Files[Path.Combine(folder, "01-a.md")]);
        Assert.Equal("b", mover.Files[Path.Combine(folder, "01-b.md")]);
        Assert.DoesNotContain(mover.Files.Keys, k => Path.GetFileName(k).StartsWith(".md-reorder-", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyOfAnEmptyPlanTouchesNothing()
    {
        var mover = new MemoryMover();
        Assert.True(RenumberPlan.Apply(Array.Empty<RenamePair>(), Path.GetTempPath(), mover, out var failure));
        Assert.Null(failure);
        Assert.Empty(mover.Log);
    }

    [Fact]
    public void ApplyOnDiskSwapsFilesAndRenamesFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "md-reorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "01-a.md"), "first");
            File.WriteAllText(Path.Combine(root, "02-a.md"), "second");
            Directory.CreateDirectory(Path.Combine(root, "Drafts"));
            Directory.CreateDirectory(Path.Combine(root, "01-Final"));

            Assert.True(RenumberPlan.Apply(RenumberPlan.Plan(new[] { "01-a.md", "02-a.md" }, 0, 1), root, out var failure));
            Assert.Null(failure);
            Assert.Equal("second", File.ReadAllText(Path.Combine(root, "01-a.md")));
            Assert.Equal("first", File.ReadAllText(Path.Combine(root, "02-a.md")));

            Assert.True(RenumberPlan.Apply(RenumberPlan.Plan(new[] { "Drafts", "01-Final" }, 1, 0), root, out failure));
            Assert.True(Directory.Exists(Path.Combine(root, "02-Drafts")));
            Assert.True(Directory.Exists(Path.Combine(root, "01-Final")));
            Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(root), p => Path.GetFileName(p).StartsWith(".md-reorder-", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
