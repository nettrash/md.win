namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// Recording <see cref="IPreviewSurface"/>: every HTML assignment, navigation, reload and script
/// is kept; <see cref="IsShown"/> is settable (collapsed pane); script results come from
/// <see cref="EvalHandler"/>, else <see cref="EvalResults"/>, else the JSON <c>null</c>;
/// <see cref="CompleteNavigation"/> raises the navigation event.
/// </summary>
public sealed class FakePreviewSurface : IPreviewSurface
{
    string _html = "";

    public string Html
    {
        get => _html;
        set { _html = value; HtmlHistory.Add(value); }
    }

    public bool IsShown { get; set; } = true;

    public List<string> HtmlHistory { get; } = [];
    public List<string> Navigations { get; } = [];
    public int ReloadCount { get; private set; }
    public List<string> Evals { get; } = [];

    public Queue<string> EvalResults { get; } = new();
    public Func<string, string>? EvalHandler { get; set; }

    public event Action<bool> NavigationCompleted = delegate { };

    public void Navigate(string url) => Navigations.Add(url);

    public void Reload() => ReloadCount++;

    public Task<string> EvalAsync(string script)
    {
        Evals.Add(script);
        var result = EvalHandler?.Invoke(script) ?? (EvalResults.Count > 0 ? EvalResults.Dequeue() : "null");
        return Task.FromResult(result);
    }

    public void CompleteNavigation(bool isSuccess = true) => NavigationCompleted(isSuccess);
}
