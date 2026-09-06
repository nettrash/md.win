using System.Text;
using Md.App.Logic.Export;
using Md.App.Logic.Preview;
using Md.App.Logic.Settings;

namespace Md.App.Logic.Tests;

// The fakes in Fakes/ are what every work package's tests drive their logic through, so their
// behaviour is pinned here: a fake that quietly changed its ordering or stamp semantics would make
// another package's green tests mean nothing.

public class FakeSchedulerTests
{
    [Fact]
    public void Timers_fire_earliest_first_and_registration_order_breaks_ties()
    {
        var s = new FakeScheduler();
        var log = new List<string>();
        s.After(TimeSpan.FromMilliseconds(300), () => log.Add("c"));
        s.After(TimeSpan.FromMilliseconds(100), () => log.Add("a1"));
        s.After(TimeSpan.FromMilliseconds(100), () => log.Add("a2"));
        s.After(TimeSpan.FromMilliseconds(200), () => log.Add("b"));

        s.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(new[] { "a1", "a2", "b" }, log);
        Assert.Equal(1, s.PendingTimers);

        s.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(new[] { "a1", "a2", "b", "c" }, log);
        Assert.Equal(0, s.PendingTimers);
    }

    [Fact]
    public void The_clock_reads_the_due_time_inside_a_callback_and_the_target_afterwards()
    {
        var s = new FakeScheduler();
        var start = s.Now;
        DateTimeOffset? seen = null;
        s.After(TimeSpan.FromMilliseconds(100), () => seen = s.Now);

        s.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(start.AddMilliseconds(100), seen);
        Assert.Equal(start.AddSeconds(1), s.Now);
    }

    [Fact]
    public void A_timer_scheduled_by_a_callback_fires_in_the_same_advance_when_it_falls_due()
    {
        var s = new FakeScheduler();
        var log = new List<string>();
        s.After(TimeSpan.FromMilliseconds(100), () =>
        {
            log.Add("first");
            s.After(TimeSpan.FromMilliseconds(50), () => log.Add("chained"));   // due at 150: inside the window
            s.After(TimeSpan.FromMilliseconds(500), () => log.Add("later"));    // due at 600: outside
        });

        s.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(new[] { "first", "chained" }, log);
        Assert.Equal(1, s.PendingTimers);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(400) }, s.PendingDelays);
    }

    [Fact]
    public void Disposing_the_handle_cancels_the_timer()
    {
        var s = new FakeScheduler();
        var fired = false;
        var handle = s.After(TimeSpan.FromMilliseconds(100), () => fired = true);
        Assert.Equal(1, s.PendingTimers);

        handle.Dispose();
        Assert.Equal(0, s.PendingTimers);
        s.Advance(TimeSpan.FromSeconds(1));
        Assert.False(fired);
    }

    [Fact]
    public void Cancelling_from_inside_a_callback_stops_a_later_timer()
    {
        var s = new FakeScheduler();
        var fired = false;
        IDisposable? second = null;
        s.After(TimeSpan.FromMilliseconds(100), () => second!.Dispose());
        second = s.After(TimeSpan.FromMilliseconds(200), () => fired = true);

        s.Advance(TimeSpan.FromSeconds(1));
        Assert.False(fired);
        Assert.Equal(0, s.PendingTimers);
    }

    [Fact]
    public void Restarting_a_debounce_shows_in_PendingDelays()
    {
        var s = new FakeScheduler();
        var handle = s.After(TimeSpan.FromMilliseconds(350), () => { });
        s.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(150) }, s.PendingDelays);

        handle.Dispose();
        s.After(TimeSpan.FromMilliseconds(350), () => { });
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(350) }, s.PendingDelays);
    }

    [Fact]
    public void A_negative_delay_is_due_now_and_RunDue_fires_it_without_moving_the_clock()
    {
        var s = new FakeScheduler();
        var fired = false;
        var start = s.Now;
        s.After(TimeSpan.FromMilliseconds(-5), () => fired = true);
        Assert.False(fired);

        s.RunDue();
        Assert.True(fired);
        Assert.Equal(start, s.Now);
    }

    [Fact]
    public void Posted_actions_run_only_on_RunPosted_fifo_including_reposts()
    {
        var s = new FakeScheduler();
        var log = new List<string>();
        s.Post(() => { log.Add("a"); s.Post(() => log.Add("c")); });
        s.Post(() => log.Add("b"));

        s.Advance(TimeSpan.FromSeconds(1));         // time does not drain posts
        Assert.Empty(log);
        Assert.Equal(2, s.PendingPosts);

        Assert.Equal(3, s.RunPosted());
        Assert.Equal(new[] { "a", "b", "c" }, log);
        Assert.Equal(0, s.PendingPosts);
    }

    [Fact]
    public void A_self_reposting_action_is_reported_not_spun()
    {
        var s = new FakeScheduler();
        void Loop() => s.Post(Loop);
        s.Post(Loop);
        Assert.Throws<InvalidOperationException>(() => s.RunPosted());
    }

    [Fact]
    public void Advancing_backwards_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeScheduler().Advance(TimeSpan.FromMilliseconds(-1)));

    [Fact]
    public void Null_actions_are_rejected()
    {
        var s = new FakeScheduler();
        Assert.Throws<ArgumentNullException>(() => s.After(TimeSpan.Zero, null!));
        Assert.Throws<ArgumentNullException>(() => s.Post(null!));
    }

    [Fact]
    public void A_shared_clock_moves_with_the_scheduler_and_the_scheduler_sees_the_clock()
    {
        var clock = new FakeClock();
        var s = new FakeScheduler(clock);
        Assert.Equal(FakeClock.Epoch, s.Now);

        s.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(FakeClock.Epoch.AddMinutes(1), clock.Now);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(FakeClock.Epoch.AddMinutes(2), s.Now);
    }
}

public class FakeFileSystemTests
{
    const string A = @"C:\Docs\a.md";

    [Fact]
    public void Stamp_is_null_for_a_missing_file_and_mtime_plus_length_for_an_existing_one()
    {
        var fs = new FakeFileSystem();
        Assert.Null(fs.Stamp(A));
        Assert.False(fs.FileExists(A));

        fs.AddFile(A, "hello");
        var stamp = fs.Stamp(A);
        Assert.NotNull(stamp);
        Assert.Equal(5, stamp!.Value.Length);
        Assert.Equal(FakeClock.Epoch.UtcDateTime, stamp.Value.LastWriteUtc);
        Assert.True(fs.FileExists(A));
        Assert.True(fs.DirectoryExists(@"C:\Docs"));
        Assert.Equal("hello", Encoding.UTF8.GetString(fs.ReadAllBytes(A)));
    }

    [Fact]
    public void Every_write_gets_a_strictly_later_stamp_even_when_the_clock_stands_still()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(A, "one");
        var s1 = fs.Stamp(A)!.Value;

        fs.WriteAllBytesInPlace(A, "one"u8);         // same bytes, same length, same clock time
        var s2 = fs.Stamp(A)!.Value;
        Assert.True(s2.LastWriteUtc > s1.LastWriteUtc);
        Assert.Equal(s1.Length, s2.Length);
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void Our_write_is_logged_and_an_external_write_is_not_but_both_move_the_stamp()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(A, "one");
        var s0 = fs.Stamp(A)!.Value;

        fs.WriteAllBytesInPlace(A, "two!"u8);
        var s1 = fs.Stamp(A)!.Value;
        fs.WriteExternally(A, "three");
        var s2 = fs.Stamp(A)!.Value;

        Assert.Equal(new[] { A }, fs.Writes);
        Assert.NotEqual(s0, s1);
        Assert.NotEqual(s1, s2);
        Assert.True(s2.LastWriteUtc > s1.LastWriteUtc);
        Assert.Equal(4, s1.Length);
        Assert.Equal(5, s2.Length);
        Assert.Equal("three", fs.Text(A));
    }

    [Fact]
    public void Touch_changes_the_mtime_and_keeps_the_bytes()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(A, "same");
        var before = fs.Stamp(A)!.Value;

        fs.Touch(A);
        var after = fs.Stamp(A)!.Value;
        Assert.True(after.LastWriteUtc > before.LastWriteUtc);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal("same", fs.Text(A));
        Assert.Empty(fs.Writes);
    }

    [Fact]
    public void The_clock_drives_the_stamps()
    {
        var clock = new FakeClock();
        var fs = new FakeFileSystem(clock);
        fs.AddFile(A, "x");
        Assert.Same(clock, fs.Clock);

        clock.Advance(TimeSpan.FromHours(1));
        fs.WriteAllBytesInPlace(A, "y"u8);
        Assert.Equal(FakeClock.Epoch.AddHours(1).UtcDateTime, fs.Stamp(A)!.Value.LastWriteUtc);
    }

    [Fact]
    public void An_explicit_mtime_is_kept()
    {
        var fs = new FakeFileSystem();
        var when = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        fs.AddFile(A, "x"u8.ToArray(), when);
        Assert.Equal(new FileStamp(when, 1), fs.Stamp(A));
    }

    [Fact]
    public void Move_keeps_the_stamp_and_refuses_to_overwrite()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(A, "x");
        fs.AddFile(@"C:\Docs\b.md", "y");
        var stamp = fs.Stamp(A);

        fs.Move(A, @"C:\Docs\c.md");
        Assert.False(fs.FileExists(A));
        Assert.Equal(stamp, fs.Stamp(@"C:\Docs\c.md"));
        Assert.Throws<IOException>(() => fs.Move(@"C:\Docs\c.md", @"C:\Docs\b.md"));
        Assert.Throws<FileNotFoundException>(() => fs.Move(@"C:\Docs\nope.md", @"C:\Docs\d.md"));
        Assert.Throws<DirectoryNotFoundException>(() => fs.Move(@"C:\Docs\c.md", @"C:\Elsewhere\c.md"));
    }

    [Fact]
    public void Move_renames_a_folder_with_everything_under_it()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Book\01 Intro\a.md", "x");
        fs.AddFile(@"C:\Book\01 Intro\Deeper\b.md", "y");

        fs.Move(@"C:\Book\01 Intro", @"C:\Book\01 Start");
        Assert.False(fs.DirectoryExists(@"C:\Book\01 Intro"));
        Assert.True(fs.DirectoryExists(@"C:\Book\01 Start"));
        Assert.True(fs.DirectoryExists(@"C:\Book\01 Start\Deeper"));
        Assert.Equal("x", fs.Text(@"C:\Book\01 Start\a.md"));
        Assert.Equal("y", fs.Text(@"C:\Book\01 Start\Deeper\b.md"));
    }

    [Fact]
    public void Paths_are_case_insensitive_and_accept_either_separator()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\Notes\a.md", "x");
        Assert.True(fs.FileExists("c:/docs/notes/A.MD"));
        Assert.True(fs.DirectoryExists(@"C:\DOCS\notes"));
        Assert.True(fs.DirectoryExists(@"C:\Docs\Notes\"));
        Assert.True(fs.DirectoryExists("C:/"));
        Assert.Equal(fs.Stamp(@"C:\Docs\Notes\a.md"), fs.Stamp("c:/docs/notes/A.MD"));
    }

    [Fact]
    public void A_read_only_file_refuses_the_in_place_write_and_keeps_its_bytes()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(A, "keep");
        fs.SetReadOnly(A);

        Assert.Throws<UnauthorizedAccessException>(() => fs.WriteAllBytesInPlace(A, "new"u8));
        Assert.Equal("keep", fs.Text(A));
        Assert.Equal(new[] { A }, fs.Writes);     // the attempt is logged: the InfoBar path can be asserted on

        fs.SetReadOnly(A, false);
        fs.WriteAllBytesInPlace(A, "new"u8);
        Assert.Equal("new", fs.Text(A));
    }

    [Fact]
    public void NextWriteFailure_is_thrown_once()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(A, "x");
        fs.NextWriteFailure = new IOException("disk full");

        var ex = Assert.Throws<IOException>(() => fs.WriteAllBytesInPlace(A, "y"u8));
        Assert.Equal("disk full", ex.Message);
        Assert.Equal("x", fs.Text(A));
        Assert.Null(fs.NextWriteFailure);

        fs.WriteAllBytesInPlace(A, "y"u8);
        Assert.Equal("y", fs.Text(A));
    }

    [Fact]
    public void Writing_into_a_missing_folder_fails_like_System_IO_and_into_an_existing_one_creates_the_file()
    {
        var fs = new FakeFileSystem();
        Assert.Throws<DirectoryNotFoundException>(() => fs.WriteAllBytesInPlace(@"C:\Nope\a.md", "x"u8));

        fs.AddDirectory(@"C:\Docs");
        fs.WriteAllBytesInPlace(@"C:\Docs\new.md", "x"u8);
        Assert.True(fs.FileExists(@"C:\Docs\new.md"));
        Assert.NotNull(fs.Stamp(@"C:\Docs\new.md"));
    }

    [Fact]
    public void Reading_a_missing_file_throws()
    {
        var fs = new FakeFileSystem();
        Assert.Throws<FileNotFoundException>(() => fs.ReadAllBytes(A));
        Assert.Null(fs.Bytes(A));
        Assert.Null(fs.Text(A));
    }

    [Fact]
    public void Delete_removes_files_and_whole_folders_and_ignores_a_missing_file()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Book\01 Intro\a.md", "x");
        fs.AddFile(@"C:\Book\02 Body\b.md", "y");

        fs.Delete(@"C:\Book\nothing.md");            // File.Delete of a missing file is a no-op
        fs.Delete(@"C:\Book\01 Intro");
        Assert.False(fs.DirectoryExists(@"C:\Book\01 Intro"));
        Assert.False(fs.FileExists(@"C:\Book\01 Intro\a.md"));
        Assert.True(fs.FileExists(@"C:\Book\02 Body\b.md"));

        fs.Delete(@"C:\Book\02 Body\b.md");
        Assert.False(fs.FileExists(@"C:\Book\02 Body\b.md"));
        Assert.True(fs.DirectoryExists(@"C:\Book\02 Body"));
    }

    [Fact]
    public void EnumerateEntries_lists_direct_children_sorted_case_insensitively_and_throws_for_a_missing_folder()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Book\02 Body\b.md", "y");
        fs.AddFile(@"C:\Book\01 Intro\a.md", "x");
        fs.AddFile(@"C:\Book\README.md", "r");
        fs.AddFile(@"C:\Book\about.md", "r");

        Assert.Equal(new[] { "C:/Book/01 Intro", "C:/Book/02 Body", "C:/Book/about.md", "C:/Book/README.md" }, fs.EnumerateEntries(@"C:\Book"));
        Assert.Throws<DirectoryNotFoundException>(() => fs.EnumerateEntries(@"C:\Nope").ToList());
    }

    [Theory]
    [InlineData(@"C:\Docs\a.md", "C:/Docs/a.md")]
    [InlineData("C:/Docs/", "C:/Docs")]
    [InlineData(@"C:\", "C:/")]
    [InlineData("/tmp/a.md", "/tmp/a.md")]
    [InlineData("/", "/")]
    [InlineData(@"\\server\share\a.md", "//server/share/a.md")]
    public void Keys_use_forward_slashes_without_a_trailing_one(string path, string key) => Assert.Equal(key, FakeFileSystem.Key(path));
}

public class FakePreviewSurfaceTests
{
    [Fact]
    public async Task Records_html_navigations_reloads_and_scripts_in_order()
    {
        var s = new FakePreviewSurface();
        s.Html = "<p>1</p>";
        s.Html = "<p>2</p>";
        s.Navigate("https://md.assets/index.html");
        s.Reload();
        s.Reload();
        await s.EvalAsync("window.scrollY");

        Assert.Equal(new[] { "<p>1</p>", "<p>2</p>" }, s.HtmlHistory);
        Assert.Equal("<p>2</p>", s.Html);
        Assert.Equal(new[] { "https://md.assets/index.html" }, s.Navigations);
        Assert.Equal(2, s.ReloadCount);
        Assert.Equal(new[] { "window.scrollY" }, s.Evals);
    }

    [Fact]
    public async Task Eval_results_come_from_the_handler_then_the_queue_then_json_null()
    {
        var s = new FakePreviewSurface();
        Assert.Equal("null", await s.EvalAsync("a"));

        s.EvalResults.Enqueue("\"1\"");
        s.EvalResults.Enqueue("120.5");
        Assert.Equal("\"1\"", await s.EvalAsync("b"));
        Assert.Equal("120.5", await s.EvalAsync("c"));
        Assert.Equal("null", await s.EvalAsync("d"));

        s.EvalHandler = script => script == "x" ? "42" : "0";
        s.EvalResults.Enqueue("ignored while a handler is set");
        Assert.Equal("42", await s.EvalAsync("x"));
        Assert.Equal("0", await s.EvalAsync("y"));
        Assert.Equal(new[] { "a", "b", "c", "d", "x", "y" }, s.Evals);
    }

    [Fact]
    public void Navigation_completion_is_observable_and_IsShown_is_settable()
    {
        var s = new FakePreviewSurface();
        var results = new List<bool>();
        s.NavigationCompleted += results.Add;
        Assert.True(s.IsShown);

        s.IsShown = false;
        s.CompleteNavigation();
        s.CompleteNavigation(false);
        Assert.Equal(new[] { true, false }, results);
        Assert.False(s.IsShown);
    }
}

public class FakeRenderSurfaceTests
{
    [Fact]
    public async Task Records_every_call_in_order_and_disposal_is_observable()
    {
        var s = new FakeRenderSurface { Kind = RenderKind.Paper };
        await s.LoadAsync("<html/>", CancellationToken.None);
        await s.EvalAsync("document.title");
        var png = await s.CaptureRegionPngAsync(new RectD(1, 2, 300, 40), 2);
        await s.SetHeightAsync(842.5);
        var pdf = await s.PdfAsync(new PrintGeometry(8.27, 11.69));

        Assert.Equal(RenderKind.Paper, s.Kind);
        Assert.Equal(new[] { "<html/>" }, s.LoadedHtml);
        Assert.Equal(new[] { "document.title" }, s.Evals);
        Assert.Equal(new[] { (new RectD(1, 2, 300, 40), 2.0) }, s.Captures);
        Assert.Equal(new[] { 842.5 }, s.Heights);
        Assert.Equal(new[] { new PrintGeometry(8.27, 11.69) }, s.PdfRequests);
        Assert.Equal(0.5, s.PdfRequests[0].MarginIn);          // the design's margin decision is the record's default
        Assert.Equal(s.PngBytes, png);
        Assert.NotSame(s.PngBytes, png);                       // a copy: a caller mutating its bytes cannot change the script
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf), StringComparison.Ordinal);

        Assert.False(s.IsDisposed);
        await s.DisposeAsync();
        Assert.True(s.IsDisposed);
    }

    [Fact]
    public async Task Scripted_results_and_failures_surface_as_the_pipeline_would_see_them()
    {
        var s = new FakeRenderSurface { LoadFailure = new TimeoutException("render"), PdfFailure = new IOException("pdf") };
        await Assert.ThrowsAsync<TimeoutException>(() => s.LoadAsync("x", CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => s.PdfAsync(new PrintGeometry(8.5, 11)));
        Assert.Equal(new[] { "x" }, s.LoadedHtml);            // a failed load was still attempted (and recorded)
        Assert.Single(s.PdfRequests);

        s.EvalResults.Enqueue("\"1\"");
        Assert.Equal("\"1\"", await s.EvalAsync("a"));
        Assert.Equal("null", await s.EvalAsync("b"));
        s.EvalHandler = _ => "7";
        Assert.Equal("7", await s.EvalAsync("c"));

        s.PngBytes = [1, 2, 3];
        Assert.Equal(new byte[] { 1, 2, 3 }, await s.CaptureRegionPngAsync(new RectD(0, 0, 1, 1), 1));
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_load_before_it_is_recorded()
    {
        var s = new FakeRenderSurface();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.LoadAsync("y", cts.Token));
        Assert.Empty(s.LoadedHtml);
    }

    [Fact]
    public async Task The_factory_records_kinds_and_hands_out_fresh_surfaces_unless_told_otherwise()
    {
        var f = new FakeRenderSurfaceFactory();
        var a = await f.CreateAsync(RenderKind.Export);
        var b = await f.CreateAsync(RenderKind.Paper);

        Assert.Equal(new[] { RenderKind.Export, RenderKind.Paper }, f.Kinds);
        Assert.Equal(2, f.Created.Count);
        Assert.NotSame(a, b);
        Assert.Same(a, f.Created[0]);
        Assert.Equal(RenderKind.Export, f.Created[0].Kind);
        Assert.Equal(RenderKind.Paper, f.Created[1].Kind);

        var shared = new FakeRenderSurface { Kind = RenderKind.Screen };
        f.Create = _ => shared;
        Assert.Same(shared, await f.CreateAsync(RenderKind.Screen));
        Assert.Equal(3, f.Created.Count);
    }
}

public class FakeAlertsTests
{
    [Fact]
    public async Task Unscripted_questions_get_the_answer_that_changes_nothing_and_every_call_is_recorded()
    {
        var a = new FakeAlerts();
        await a.WarnAsync("Could not rename", "msg");
        Assert.Null(await a.PromptNameAsync("Rename", "Only the name changes", "old", "Rename"));
        Assert.False(await a.ConfirmDeleteAsync("Delete “x”?", "The article file will be deleted."));
        Assert.Equal(CloseChoice.Cancel, await a.AskSaveChangesAsync("Untitled"));
        Assert.False(await a.ConfirmReplaceAsync("A folder named “x” already exists."));

        Assert.Equal(new[] { new FakeAlerts.Warning("Could not rename", "msg") }, a.Warnings);
        Assert.Equal(new[] { new FakeAlerts.NamePrompt("Rename", "Only the name changes", "old", "Rename") }, a.NamePrompts);
        Assert.Equal(new[] { new FakeAlerts.Confirmation("Delete “x”?", "The article file will be deleted.") }, a.DeleteConfirmations);
        Assert.Equal(new[] { "Untitled" }, a.SaveChangesQuestions);
        Assert.Equal(new[] { "A folder named “x” already exists." }, a.ReplaceConfirmations);
    }

    [Fact]
    public async Task Scripted_answers_are_consumed_in_order_then_the_safe_answer_returns()
    {
        var a = new FakeAlerts();
        a.NameAnswers.Enqueue("New");
        a.NameAnswers.Enqueue(null);
        a.DeleteAnswers.Enqueue(true);
        a.SaveChangesAnswers.Enqueue(CloseChoice.Save);
        a.SaveChangesAnswers.Enqueue(CloseChoice.DontSave);
        a.ReplaceAnswers.Enqueue(true);

        Assert.Equal("New", await a.PromptNameAsync("t", "m", "i", "ok"));
        Assert.Null(await a.PromptNameAsync("t", "m", "i", "ok"));
        Assert.Null(await a.PromptNameAsync("t", "m", "i", "ok"));
        Assert.True(await a.ConfirmDeleteAsync("t", "m"));
        Assert.False(await a.ConfirmDeleteAsync("t", "m"));
        Assert.Equal(CloseChoice.Save, await a.AskSaveChangesAsync("x"));
        Assert.Equal(CloseChoice.DontSave, await a.AskSaveChangesAsync("x"));
        Assert.Equal(CloseChoice.Cancel, await a.AskSaveChangesAsync("x"));
        Assert.True(await a.ConfirmReplaceAsync("m"));
        Assert.False(await a.ConfirmReplaceAsync("m"));
        Assert.Equal(3, a.NamePrompts.Count);
    }
}

public class FakePickersTests
{
    [Fact]
    public async Task Unscripted_pickers_are_cancelled_and_calls_are_recorded_with_their_arguments()
    {
        var p = new FakePickers();
        var choices = new List<(string Label, IReadOnlyList<string> Extensions)> { ("Markdown Document", new[] { ".md", ".markdown" }), ("Plain Text", new[] { ".txt" }) };

        Assert.Empty(await p.OpenFilesAsync(new[] { ".md", ".txt" }));
        Assert.Null(await p.SaveFileAsync("Untitled", choices, ".md"));
        Assert.Null(await p.PickFolderAsync("Choose where to keep the TextBundle"));
        Assert.Null(await p.PickFolderAsync(null));

        Assert.Equal(new[] { ".md", ".txt" }, Assert.Single(p.OpenCalls));
        var save = Assert.Single(p.SaveCalls);
        Assert.Equal("Untitled", save.SuggestedName);
        Assert.Equal(".md", save.DefaultExtension);
        Assert.Equal(new[] { "Markdown Document", "Plain Text" }, save.Choices.Select(c => c.Label));
        Assert.Equal(new string?[] { "Choose where to keep the TextBundle", null }, p.FolderCalls);
    }

    [Fact]
    public async Task Scripted_answers_are_consumed_in_order()
    {
        var p = new FakePickers();
        p.OpenAnswers.Enqueue(new[] { @"C:\Docs\a.md", @"C:\Docs\b.md" });
        p.SaveAnswers.Enqueue(@"C:\Docs\out.pdf");
        p.FolderAnswers.Enqueue(@"C:\Docs");

        Assert.Equal(new[] { @"C:\Docs\a.md", @"C:\Docs\b.md" }, await p.OpenFilesAsync(new[] { ".md" }));
        Assert.Empty(await p.OpenFilesAsync(new[] { ".md" }));
        Assert.Equal(@"C:\Docs\out.pdf", await p.SaveFileAsync("a", [], ".pdf"));
        Assert.Null(await p.SaveFileAsync("a", [], ".pdf"));
        Assert.Equal(@"C:\Docs", await p.PickFolderAsync(null));
        Assert.Null(await p.PickFolderAsync(null));
    }
}

public class FakeFileWatcherTests
{
    [Fact]
    public void Events_are_delivered_only_while_watching_and_default_to_the_watched_path()
    {
        var w = new FakeFileWatcher();
        var changed = new List<string>();
        var renamed = new List<(string, string)>();
        var deleted = new List<string>();
        w.Changed += changed.Add;
        w.Renamed += (o, n) => renamed.Add((o, n));
        w.Deleted += deleted.Add;

        Assert.False(w.IsWatching);
        Assert.False(w.RaiseChanged("x"));                 // not watching: dropped

        w.Watch(@"C:\Docs\a.md");
        Assert.True(w.IsWatching);
        Assert.Equal(@"C:\Docs\a.md", w.WatchedPath);
        Assert.True(w.RaiseChanged());
        Assert.True(w.RaiseRenamed(@"C:\Docs\b.md"));
        Assert.True(w.RaiseDeleted());

        w.Stop();
        Assert.False(w.RaiseChanged());
        w.Watch(@"C:\Docs\c.md");                          // re-target
        Assert.True(w.RaiseChanged());

        Assert.Equal(new[] { @"C:\Docs\a.md", @"C:\Docs\c.md" }, changed);
        Assert.Equal(new[] { (@"C:\Docs\a.md", @"C:\Docs\b.md") }, renamed);
        Assert.Equal(new[] { @"C:\Docs\a.md" }, deleted);
        Assert.Equal(new[] { @"C:\Docs\a.md", @"C:\Docs\c.md" }, w.WatchHistory);
    }

    [Fact]
    public void Dispose_stops_delivery_and_refuses_a_new_target()
    {
        var w = new FakeFileWatcher();
        var raised = 0;
        w.Changed += _ => raised++;
        w.Watch(@"C:\Docs\a.md");

        w.Dispose();
        Assert.True(w.IsDisposed);
        Assert.False(w.IsWatching);
        Assert.False(w.RaiseChanged());
        Assert.Throws<ObjectDisposedException>(() => w.Watch(@"C:\Docs\b.md"));
        Assert.Equal(0, raised);
    }
}

public class FakeDocumentRegistryTests
{
    [Fact]
    public void Owning_is_case_insensitive_one_path_per_window_and_every_mutation_raises_Changed()
    {
        var r = new FakeDocumentRegistry();
        var w1 = Guid.NewGuid();
        var w2 = Guid.NewGuid();
        var changes = 0;
        r.Changed += () => changes++;

        r.AddWindow(w1, "Untitled");
        r.Register(w1, @"C:\Docs\a.md");
        r.Register(w2, @"C:\Docs\b.md");
        Assert.Equal(w1, r.Owning(@"c:\docs\A.MD"));
        Assert.Equal(w2, r.Owning(@"C:\Docs\b.md"));
        Assert.Null(r.Owning(@"C:\Docs\c.md"));

        r.Register(w1, @"C:\Docs\renamed.md");             // Save As / Rename re-keys the window
        Assert.Null(r.Owning(@"C:\Docs\a.md"));
        Assert.Equal(w1, r.Owning(@"C:\Docs\renamed.md"));
        Assert.Equal(@"C:\Docs\renamed.md", r.PathOf(w1));
        Assert.Equal(new[] { (w1, "Untitled"), (w2, "b") }, r.Windows);   // Register keeps an existing title, defaults a new one to the stem

        r.SetTitle(w1, "renamed — Edited");
        r.Unregister(w2);
        Assert.Equal(new[] { (w1, "renamed — Edited") }, r.Windows);
        Assert.Null(r.PathOf(w2));
        Assert.Null(r.Owning(@"C:\Docs\b.md"));

        Assert.Equal(6, changes);
        Assert.Equal(6, r.ChangedCount);
        Assert.Throws<KeyNotFoundException>(() => r.SetTitle(w2, "x"));
    }
}

public class FakeUiThreadTests
{
    [Fact]
    public void Runs_inline_by_default_or_queues_for_RunPosted()
    {
        var ui = new FakeUiThread();
        var log = new List<int>();
        ui.Post(() => log.Add(1));
        Assert.Equal(new[] { 1 }, log);
        Assert.True(ui.IsCurrent);

        ui.RunInline = false;
        ui.IsCurrent = false;
        ui.Post(() => log.Add(2));
        ui.Post(() => log.Add(3));
        Assert.Equal(new[] { 1 }, log);
        Assert.Equal(2, ui.PendingPosts);
        Assert.Equal(2, ui.RunPosted());
        Assert.Equal(new[] { 1, 2, 3 }, log);
        Assert.Equal(3, ui.PostCount);
        Assert.Throws<ArgumentNullException>(() => ui.Post(null!));
    }
}

public class FakeSettingsStoreTests
{
    [Fact]
    public void Logs_writes_and_removals_and_forwards_Changed()
    {
        var s = new FakeSettingsStore();
        var changed = new List<string>();
        s.Changed += changed.Add;

        s.SetString(SettingsKeys.PdfPageSize, "letter");
        s.SetString(SettingsKeys.PdfPageSize, "letter");       // logged, but not a change
        s.SetBool(SettingsKeys.BookOpensInSeparateWindows, true);
        s.Remove(SettingsKeys.PdfPageSize);

        Assert.Equal(new (string, object)[] { (SettingsKeys.PdfPageSize, "letter"), (SettingsKeys.PdfPageSize, "letter"), (SettingsKeys.BookOpensInSeparateWindows, true) }, s.Writes);
        Assert.Equal(new[] { SettingsKeys.PdfPageSize }, s.Removals);
        Assert.Equal(new[] { SettingsKeys.PdfPageSize, SettingsKeys.BookOpensInSeparateWindows, SettingsKeys.PdfPageSize }, changed);
        Assert.Null(s.GetString(SettingsKeys.PdfPageSize));
        Assert.True(s.GetBool(SettingsKeys.BookOpensInSeparateWindows, false));
        Assert.Equal(new[] { SettingsKeys.BookOpensInSeparateWindows }, s.Keys);
        Assert.DoesNotContain(s.Writes, w => w.Key == SettingsKeys.ViewModeMemory);   // the "Zen never stores" shape of assertion
    }
}

public class FakeShareTests
{
    [Fact]
    public async Task Records_shares_and_fails_on_demand()
    {
        var share = new FakeShare();
        await share.ShareFileAsync(@"C:\Temp\a.pdf", "a");
        Assert.Equal(new[] { (@"C:\Temp\a.pdf", "a") }, share.Shared);

        share.Failure = new InvalidOperationException("no share target");
        await Assert.ThrowsAsync<InvalidOperationException>(() => share.ShareFileAsync(@"C:\Temp\b.pdf", "b"));
        Assert.Single(share.Shared);
    }
}

public class FakeWordCounterTests
{
    [Fact]
    public void Counts_runs_with_a_letter_or_digit_unless_overridden_and_records_calls()
    {
        var c = new FakeWordCounter();
        Assert.Equal(4, c.Count("Hello, world \u2014 2 words"));    // the lone em dash is not a word
        Assert.Equal(0, c.Count("   "));
        Assert.Equal(0, c.Count(""));

        c.Override = _ => 42;
        Assert.Equal(42, c.Count("anything"));
        Assert.Equal(new[] { "Hello, world \u2014 2 words", "   ", "", "anything" }, c.Calls);
    }
}

public class FakeFileIdentityTests
{
    [Fact]
    public void Canonical_normalises_separators_and_follows_aliases_case_insensitively()
    {
        var id = new FakeFileIdentity();
        Assert.Equal("C:/Docs/a.md", id.Canonical(@"C:\Docs\a.md"));
        Assert.Equal("C:/Docs/a.md", id.Canonical("  C:/Docs/a.md "));

        id.Alias(@"D:\Junction\a.md", @"C:\Docs\a.md");
        Assert.Equal("C:/Docs/a.md", id.Canonical(@"d:\junction\A.MD"));
        Assert.Equal("C:/Docs/a.md", id.Canonical("D:/Junction/a.md"));
        Assert.Equal(4, id.Calls.Count);
    }
}

public class FakeClockTests
{
    [Fact]
    public void Starts_at_the_epoch_and_moves_only_when_told()
    {
        var clock = new FakeClock();
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), clock.Now);
        clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(FakeClock.Epoch.AddMilliseconds(150), clock.Now);
        clock.Now = FakeClock.Epoch;
        Assert.Equal(FakeClock.Epoch, clock.Now);
    }
}
