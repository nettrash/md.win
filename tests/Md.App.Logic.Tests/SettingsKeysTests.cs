using System.Reflection;
using Md.App.Logic.Settings;

namespace Md.App.Logic.Tests;

/// <summary>Pins shell-final.md §9: the eight LocalSettings keys, their defaults, and the other names md stores state under.</summary>
public class SettingsKeysTests
{
    [Fact]
    public void The_eight_keys_are_pinned_in_section_9_order()
    {
        Assert.Equal(
            new[]
            {
                "md.viewModeMemory", "md.pdfPageSize", "md.bookBookmark", "md.bookOpensInSeparateWindows",
                "md.bookLastArticle", "md.bookViewMode", "md.win.windowSize.document", "md.win.windowSize.book",
            },
            SettingsKeys.All);
    }

    [Fact]
    public void Each_constant_spells_its_key()
    {
        Assert.Equal("md.viewModeMemory", SettingsKeys.ViewModeMemory);
        Assert.Equal("md.pdfPageSize", SettingsKeys.PdfPageSize);
        Assert.Equal("md.bookBookmark", SettingsKeys.BookBookmark);
        Assert.Equal("md.bookOpensInSeparateWindows", SettingsKeys.BookOpensInSeparateWindows);
        Assert.Equal("md.bookLastArticle", SettingsKeys.BookLastArticle);
        Assert.Equal("md.bookViewMode", SettingsKeys.BookViewMode);
        Assert.Equal("md.win.windowSize.document", SettingsKeys.DocumentWindowSize);
        Assert.Equal("md.win.windowSize.book", SettingsKeys.BookWindowSize);
    }

    [Fact]
    public void Keys_are_distinct_namespaced_and_free_of_whitespace()
    {
        Assert.Equal(SettingsKeys.All.Count, SettingsKeys.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(SettingsKeys.All, k => Assert.StartsWith("md.", k, StringComparison.Ordinal));
        Assert.All(SettingsKeys.All, k => Assert.DoesNotContain(k, char.IsWhiteSpace));
    }

    [Fact]
    public void Only_the_two_window_sizes_are_Windows_only()
    {
        var win = SettingsKeys.All.Where(k => k.StartsWith("md.win.", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { SettingsKeys.DocumentWindowSize, SettingsKeys.BookWindowSize }, win);
    }

    [Fact]
    public void Every_key_constant_is_listed_in_All()
    {
        // A key added as a constant but forgotten in All would escape PRIVACY's "exactly these" list.
        var constants = typeof(SettingsKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.StartsWith("md.", StringComparison.Ordinal) && v != SettingsKeys.BookAccessToken)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(constants, SettingsKeys.All.OrderBy(v => v, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Defaults_are_the_design_values()
    {
        Assert.Equal("a4", SettingsKeys.PdfPageSizeDefault);
        Assert.Equal("split", SettingsKeys.BookViewModeDefault);
        Assert.Equal("900x640", SettingsKeys.DocumentWindowSizeDefault);
        Assert.Equal("1000x700", SettingsKeys.BookWindowSizeDefault);
    }

    [Fact]
    public void PRIVACY_enumerates_every_key_and_the_session_file()
    {
        // §9: "PRIVACY.md must enumerate exactly" the eight keys, session.json, the md.book token,
        // the MRU list and the WebView2 folder. The keys and the file are pinned here; a new key
        // lands in PRIVACY.md before this test goes green again.
        var privacy = File.ReadAllText(RepoFiles.At("PRIVACY.md"));
        foreach (var key in SettingsKeys.All) Assert.Contains(key, privacy);
        Assert.Contains(SettingsKeys.SessionFileName, privacy);
    }

    [Fact]
    public void The_other_names_md_stores_state_under_are_pinned_and_are_not_settings_keys()
    {
        Assert.Equal("md.book", SettingsKeys.BookAccessToken);
        Assert.Equal("session.json", SettingsKeys.SessionFileName);
        Assert.Equal("WebView2", SettingsKeys.WebView2UserDataFolderName);
        Assert.DoesNotContain(SettingsKeys.BookAccessToken, SettingsKeys.All);
        Assert.DoesNotContain(SettingsKeys.SessionFileName, SettingsKeys.All);
        Assert.DoesNotContain(SettingsKeys.WebView2UserDataFolderName, SettingsKeys.All);
    }
}

/// <summary>The Logic-side store; <c>LocalSettingsStore</c> (Md.App) mirrors these semantics line for line.</summary>
public class InMemorySettingsStoreTests
{
    [Fact]
    public void Absent_reads_as_null_or_the_fallback()
    {
        var s = new InMemorySettingsStore();
        Assert.Null(s.GetString(SettingsKeys.PdfPageSize));
        Assert.False(s.GetBool(SettingsKeys.BookOpensInSeparateWindows, false));
        Assert.True(s.GetBool(SettingsKeys.BookOpensInSeparateWindows, true));
        Assert.Empty(s.Keys);
    }

    [Fact]
    public void Strings_and_bools_round_trip()
    {
        var s = new InMemorySettingsStore();
        s.SetString(SettingsKeys.PdfPageSize, "letter");
        s.SetBool(SettingsKeys.BookOpensInSeparateWindows, true);
        Assert.Equal("letter", s.GetString(SettingsKeys.PdfPageSize));
        Assert.True(s.GetBool(SettingsKeys.BookOpensInSeparateWindows, false));
        Assert.Equal(2, s.Keys.Count);
    }

    [Fact]
    public void A_value_of_the_other_type_reads_as_absent()
    {
        var s = new InMemorySettingsStore();
        s.SetString("k", "true");
        s.SetBool("b", true);
        Assert.False(s.GetBool("k", false));
        Assert.True(s.GetBool("k", true));
        Assert.Null(s.GetString("b"));
    }

    [Fact]
    public void Changed_fires_only_when_the_stored_value_changes()
    {
        var s = new InMemorySettingsStore();
        var changes = new List<string>();
        s.Changed += changes.Add;

        s.SetString("k", "a");
        s.SetString("k", "a");          // same value: silent, so a menu re-writing what it observed cannot loop
        s.SetString("k", "b");
        s.SetBool("x", false);
        s.SetBool("x", false);
        s.Remove("k");
        s.Remove("k");                  // already gone: silent
        s.Remove("never");

        Assert.Equal(new[] { "k", "k", "x", "k" }, changes);
        Assert.Null(s.GetString("k"));
        Assert.Equal(new[] { "x" }, s.Keys);
    }

    [Fact]
    public void Replacing_a_bool_with_a_string_under_one_key_is_a_change()
    {
        var s = new InMemorySettingsStore();
        var changes = 0;
        s.Changed += _ => changes++;
        s.SetBool("k", true);
        s.SetString("k", "True");
        Assert.Equal(2, changes);
        Assert.Equal("True", s.GetString("k"));
        Assert.False(s.GetBool("k", false));
    }

    [Fact]
    public void SetString_rejects_null() => Assert.Throws<ArgumentNullException>(() => new InMemorySettingsStore().SetString("k", null!));
}
