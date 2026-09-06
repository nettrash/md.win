namespace Md.App.Logic.Seams;

/// <summary>
/// The footer's word count (§5.5). Logic: <c>IcuWordCounter</c> (in-box icu.dll, matches
/// macOS/Android for CJK) with <c>SimpleWordCounter</c> as the fallback — both in
/// Md.App.Logic.Text, WP4; tests: <c>FakeWordCounter</c>.
/// FROZEN — shell-final.md §13.2. Lives in Seams (not Text/) so WP4 implements, never redefines, it.
/// </summary>
public interface IWordCounter
{
    int Count(string text);
}
