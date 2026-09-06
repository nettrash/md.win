using Md.App.Logic;

namespace Md.App.Logic.Tests;

/// <summary>
/// What Help ▸ About md and Help ▸ md Help / Privacy Policy put in front of a user (§2.8). The
/// dialog itself is WinUI and lives in <c>Md.App.Controls.AboutDialog</c>; everything it says comes
/// from here, so the wording is pinned where it can be tested.
/// </summary>
public class CommandsAboutTests
{
    [Fact]
    public void AboutListsTheFiveBundledEnginesWithTheirVersions() =>
        Assert.Equal<string>(
        [
            "KaTeX 0.17.0 + mhchem",
            "Mermaid 11.16.0",
            "Graphviz 14.1.1 via Viz.js 3.24.0",
            "PlantUML 1.2026.4beta4",
            "highlight.js 11.11.1",
        ], Strings.Help.Engines);

    [Fact]
    public void AboutNeverClaimsThereAreNoThirdPartyDependencies()
    {
        // The family's standing rule (facts/windows-facts.md): md bundles five open source engines
        // and says so; claiming otherwise in a Store-facing surface is a factual error.
        var about = string.Join('\n', [Strings.AppName, Strings.Help.EnginesHeading, Strings.Help.Copyright, .. Strings.Help.Engines]);
        Assert.DoesNotContain("third-party", about, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no dependencies", about, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("engines", Strings.Help.EnginesHeading, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HelpPointsAtTheTwoStoreLinkedPages()
    {
        // The privacy URL is the one Partner Center gets — Store policy 10.5.1 makes it mandatory
        // for a runFullTrust desktop app, and the same page must be reachable from Help.
        Assert.Equal("https://nettrash.me/msstore/md/support.html", Strings.Help.SupportUrl);
        Assert.Equal("https://nettrash.me/msstore/md/privacy.html", Strings.Help.PrivacyUrl);
    }

    [Fact]
    public void TheCopyrightIsTheFamilysLine() => Assert.Equal("© 2026 nettrash. MIT licensed.", Strings.Help.Copyright);
}
