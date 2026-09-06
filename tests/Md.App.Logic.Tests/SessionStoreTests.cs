using Md.App.Logic.Settings;
using Md.App.Logic.Windows;
using Md.Core.Document;

namespace Md.App.Logic.Tests;

/// <summary><c>LocalFolder\session.json</c> — the codec, the disk round trip and the three rules of §1.6.</summary>
public sealed class SessionStoreTests
{
    const string Folder = @"C:\Users\n\AppData\Local\md";

    static SessionWindow Row(string path, ViewMode mode = ViewMode.Split, bool zen = false, bool zenReading = false) =>
        new(path, mode, zen, zenReading, new WindowRect(120, 80, 900, 640), false);

    [Fact]
    public void TheFileNameIsTheOneSettingsKeysPublishesForPrivacy()
    {
        Assert.Equal("session.json", SessionStore.FileName);
        Assert.Equal(SettingsKeys.SessionFileName, SessionStore.FileName);
        Assert.Equal(@"C:\Users\n\AppData\Local\md\session.json", SessionStore.PathIn(Folder));
    }

    [Fact]
    public void TheDocumentShapeIsTheDesigns()
    {
        var state = new SessionState(
            [new SessionWindow(@"C:\Docs\a.md", ViewMode.Split, false, false, new WindowRect(120, 80, 900, 640), false)],
            new SessionBook(true, new WindowRect(200, 100, 1000, 700), false));

        Assert.Equal(
            """{"v":1,"windows":[{"path":"C:\\Docs\\a.md","mode":"split","zen":false,"zenReading":false,"x":120,"y":80,"w":900,"h":640,"maximized":false}],"book":{"open":true,"x":200,"y":100,"w":1000,"h":700,"maximized":false}}""",
            SessionStore.Encode(state));
    }

    [Theory]
    [InlineData(ViewMode.Edit)]
    [InlineData(ViewMode.Split)]
    [InlineData(ViewMode.Preview)]
    public void EveryWindowFieldSurvivesTheRoundTrip(ViewMode mode)
    {
        var state = new SessionState(
            [new SessionWindow(@"C:\Docs\a.md", mode, true, true, new WindowRect(-40, 7, 1024, 768), true)],
            new SessionBook(false, new WindowRect(1, 2, 3, 4), true));

        Assert.Equal(state, SessionStore.Decode(SessionStore.Encode(state)));
    }

    [Fact]
    public void ASessionWithNoBookWindowOmitsTheBookObject()
    {
        var json = SessionStore.Encode(new SessionState([Row(@"C:\a.md")], null));
        Assert.DoesNotContain("\"book\"", json, StringComparison.Ordinal);
        Assert.Null(SessionStore.Decode(json).Book);
    }

    [Fact]
    public void AnUntitledDocumentIsNeverRecorded()
    {
        // §1.6: only saved documents are listed — an untitled draft is not autosaved (§14 row 8),
        // so a row for one would restore an empty window over text the writer never got back.
        var state = new SessionState([Row(""), Row("   "), Row(@"C:\Docs\a.md")], null);
        var decoded = SessionStore.Decode(SessionStore.Encode(state));

        Assert.Equal(@"C:\Docs\a.md", Assert.Single(decoded.Windows).Path);
    }

    [Fact]
    public void TheEncoderItselfOmitsThePathlessRowRatherThanLeavingItForTheDecoder()
    {
        // The round trip above passes either way: Decode drops a pathless row too, so it cannot
        // tell whether Encode wrote one. §1.6 is a property of the FILE — PRIVACY.md enumerates what
        // session.json holds — so the assertion has to be on the bytes.
        var json = SessionStore.Encode(new SessionState([Row(""), Row("   "), Row(@"C:\Docs\a.md")], null));

        Assert.DoesNotContain("\"path\":\"\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"path\":\"   \"", json, StringComparison.Ordinal);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(json, "\"path\":").Count);
    }

    [Fact]
    public void ASessionWrittenByALaterVersionIsIgnoredWholeRatherThanReadInPart()
    {
        // The forward-compatibility trap: a v2 file whose rows still LOOK like v1 rows must not be
        // half-understood — the fields a later md adds are exactly the ones this build would drop
        // on the next write, silently losing them.
        const string later = """
            {"v":2,"windows":[{"path":"C:\\Docs\\a.md","mode":"edit","zen":true,"x":1,"y":2,"w":900,"h":640,"pinned":true}],"book":{"open":true,"x":0,"y":0,"w":1,"h":1}}
            """;
        Assert.Equal(SessionState.Empty, SessionStore.Decode(later));

        // And the other direction: a v1 file carrying fields this build has never heard of still
        // reads, because the version is the contract and unknown keys are not.
        const string v1WithExtras = """
            {"v":1,"theme":"dark","windows":[{"path":"C:\\Docs\\a.md","mode":"edit","zen":true,"zenReading":false,"x":1,"y":2,"w":900,"h":640,"maximized":false,"pinned":true}]}
            """;
        var row = Assert.Single(SessionStore.Decode(v1WithExtras).Windows);
        Assert.Equal(@"C:\Docs\a.md", row.Path);
        Assert.Equal(ViewMode.Edit, row.Mode);
        Assert.True(row.Zen);
        Assert.Equal(new WindowRect(1, 2, 900, 640), row.Placement);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"v":2,"windows":[{"path":"C:\\a.md"}]}""")]
    [InlineData("""{"v":1,"windows":[{"path":"C:\\a.md"}""")]
    public void AnUnreadableSessionIsAnEmptyOne(string? json) =>
        Assert.Equal(SessionState.Empty, SessionStore.Decode(json));

    [Fact]
    public void MissingFieldsTakeTheirDefaultsRatherThanDroppingTheRow()
    {
        var decoded = SessionStore.Decode("""{"v":1,"windows":[{"path":"C:\\a.md"}]}""");
        var row = Assert.Single(decoded.Windows);

        Assert.Equal(ViewModes.WindowDefault, row.Mode);
        Assert.False(row.Zen);
        Assert.False(row.ZenReading);
        Assert.Equal(new WindowRect(0, 0, 0, 0), row.Placement);
        Assert.False(row.Maximized);
    }

    [Fact]
    public void AnUnknownModeReadsAsTheWindowDefault() =>
        Assert.Equal(ViewModes.WindowDefault, Assert.Single(SessionStore.Decode("""{"v":1,"windows":[{"path":"C:\\a.md","mode":"zen"}]}""").Windows).Mode);

    [Fact]
    public void RestoringSkipsFilesThatAreGone()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\a.md", "# a");
        var state = new SessionState([Row(@"C:\Docs\a.md"), Row(@"C:\Docs\gone.md")], new SessionBook(true, new WindowRect(0, 0, 1000, 700), false));

        var restorable = SessionStore.Restorable(state, fs);

        Assert.Equal(@"C:\Docs\a.md", Assert.Single(restorable.Windows).Path);
        Assert.NotNull(restorable.Book);
    }

    [Fact]
    public void ABookThatWasNotOpenIsNotRestored() =>
        Assert.Null(SessionStore.Restorable(new SessionState([], new SessionBook(false, new WindowRect(0, 0, 1000, 700), false)), new FakeFileSystem()).Book);

    [Fact]
    public void TheDiskRoundTripGoesThroughLocalFolderSessionJson()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(Folder);
        fs.AddFile(@"C:\Docs\a.md", "# a");
        var state = new SessionState([Row(@"C:\Docs\a.md", ViewMode.Preview, zen: true, zenReading: true)], null);

        Assert.True(SessionStore.Save(fs, Folder, state));
        Assert.Equal(state, SessionStore.Load(fs, Folder));
        Assert.Equal(state.Windows, SessionStore.Restorable(SessionStore.Load(fs, Folder), fs).Windows);
    }

    [Fact]
    public void AMissingSessionFileLoadsAsEmpty() =>
        Assert.Equal(SessionState.Empty, SessionStore.Load(new FakeFileSystem(), Folder));

    [Fact]
    public void AFailedWriteIsReportedRatherThanThrownSoACloseIsNeverBlocked()
    {
        var fs = new FakeFileSystem { NextWriteFailure = new UnauthorizedAccessException("read-only") };
        Assert.False(SessionStore.Save(fs, Folder, new SessionState([Row(@"C:\a.md")], null)));
    }
}
