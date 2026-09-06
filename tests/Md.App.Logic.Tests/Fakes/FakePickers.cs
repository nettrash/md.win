namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// <see cref="IPickers"/> with scripted results; every call is recorded with its arguments, so a
/// test can pin the suggested name, the type choices and their order. Unscripted = the user
/// cancelled (empty list / null).
/// </summary>
public sealed class FakePickers : IPickers
{
    public sealed record SaveCall(string SuggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> Choices, string DefaultExtension);

    public List<IReadOnlyList<string>> OpenCalls { get; } = [];
    public List<SaveCall> SaveCalls { get; } = [];
    public List<string?> FolderCalls { get; } = [];

    public Queue<IReadOnlyList<string>> OpenAnswers { get; } = new();
    public Queue<string?> SaveAnswers { get; } = new();
    public Queue<string?> FolderAnswers { get; } = new();

    public Task<IReadOnlyList<string>> OpenFilesAsync(IReadOnlyList<string> extensions)
    {
        OpenCalls.Add(extensions);
        return Task.FromResult(OpenAnswers.Count > 0 ? OpenAnswers.Dequeue() : (IReadOnlyList<string>)[]);
    }

    public Task<string?> SaveFileAsync(string suggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices, string defaultExtension)
    {
        SaveCalls.Add(new SaveCall(suggestedName, choices, defaultExtension));
        return Task.FromResult(SaveAnswers.Count > 0 ? SaveAnswers.Dequeue() : null);
    }

    public Task<string?> PickFolderAsync(string? title)
    {
        FolderCalls.Add(title);
        return Task.FromResult(FolderAnswers.Count > 0 ? FolderAnswers.Dequeue() : null);
    }
}
