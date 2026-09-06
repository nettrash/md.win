using Md.App.Logic.Text;

namespace Md.App.Logic.Tests;

/// <summary>shell-final.md §2.3: strip <c>^[0-9]+-</c> only if something remains, ASCII digits only.</summary>
public class ExampleNameTests
{
    [Theory]
    [InlineData("01-Welcome.md", "Welcome")]
    [InlineData("09-Writer Tools.md", "Writer Tools")]
    [InlineData("007-Bond.markdown", "Bond")]
    [InlineData("1-a.md", "a")]
    [InlineData("01-02-Twice.md", "02-Twice")]          // the prefix is removed once
    [InlineData("01-", "01-")]                          // nothing would remain: the stem stays
    [InlineData("01-.md", "01-")]
    [InlineData("Welcome.md", "Welcome")]               // no prefix
    [InlineData("01Welcome.md", "01Welcome")]           // digits but no dash
    [InlineData("01 Welcome.md", "01 Welcome")]         // a space is not a dash
    [InlineData("-Welcome.md", "-Welcome")]             // a dash but no digits
    [InlineData("\u0661\u0662-Arabic.md", "\u0661\u0662-Arabic")]   // Arabic-Indic digits: \d would strip these, [0-9] must not
    [InlineData("\u0967-Devanagari.md", "\u0967-Devanagari")]
    [InlineData("\uFF10\uFF11-Fullwidth.md", "\uFF10\uFF11-Fullwidth")]
    [InlineData("Title.With.Dots.md", "Title.With.Dots")]
    [InlineData("", "")]
    public void DisplayName_strips_only_an_ascii_ordering_prefix(string fileName, string expected) =>
        Assert.Equal(expected, ExampleName.DisplayName(fileName));

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\md\Examples\03-Tables.md", "Tables")]
    [InlineData("/opt/md/Examples/03-Tables.md", "Tables")]
    [InlineData(@"Examples\Example Book\01-Preface.md", "Preface")]
    [InlineData("Examples/Example Book/02-Getting Started/01-The Editor.md", "The Editor")]
    public void DisplayName_takes_the_last_segment_of_a_path_with_either_separator(string path, string expected) =>
        Assert.Equal(expected, ExampleName.DisplayName(path));

    [Fact]
    public void DisplayName_rejects_null() => Assert.Throws<ArgumentNullException>(() => ExampleName.DisplayName(null!));

    [Fact]
    public void The_nine_bundled_examples_produce_the_expected_rows()
    {
        // TopDirectoryOnly, as ExampleLibrary enumerates: "Example Book\" is not a row.
        var titles = Directory.EnumerateFiles(RepoFiles.At("src", "Md.App", "Examples"), "*.md", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileName(f))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => ExampleName.DisplayName(n))
            .ToList();
        Assert.Equal(new[] { "Welcome", "Formatting", "Tables", "Code", "Images", "Math", "Diagrams", "Plots", "Writer Tools" }, titles);
    }
}
