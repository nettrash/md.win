namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// <see cref="IAlerts"/> with scripted answers: enqueue what the user will click; every call is
/// recorded. An unscripted question gets the safe answer — the one that changes nothing
/// (cancel / don't delete / don't replace) — so a forgotten script cannot delete or overwrite
/// anything in a test by accident; assert on the recordings to catch it.
/// </summary>
public sealed class FakeAlerts : IAlerts
{
    public sealed record Warning(string Title, string Message);
    public sealed record NamePrompt(string Title, string Message, string Initial, string AcceptLabel);
    public sealed record Confirmation(string Title, string Message);

    public List<Warning> Warnings { get; } = [];
    public List<NamePrompt> NamePrompts { get; } = [];
    public List<Confirmation> DeleteConfirmations { get; } = [];
    public List<string> SaveChangesQuestions { get; } = [];
    public List<string> ReplaceConfirmations { get; } = [];

    public Queue<string?> NameAnswers { get; } = new();
    public Queue<bool> DeleteAnswers { get; } = new();
    public Queue<CloseChoice> SaveChangesAnswers { get; } = new();
    public Queue<bool> ReplaceAnswers { get; } = new();

    public Task WarnAsync(string title, string message)
    {
        Warnings.Add(new Warning(title, message));
        return Task.CompletedTask;
    }

    public Task<string?> PromptNameAsync(string title, string message, string initial, string acceptLabel)
    {
        NamePrompts.Add(new NamePrompt(title, message, initial, acceptLabel));
        return Task.FromResult(NameAnswers.Count > 0 ? NameAnswers.Dequeue() : null);
    }

    public Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        DeleteConfirmations.Add(new Confirmation(title, message));
        return Task.FromResult(DeleteAnswers.Count > 0 && DeleteAnswers.Dequeue());
    }

    public Task<CloseChoice> AskSaveChangesAsync(string title)
    {
        SaveChangesQuestions.Add(title);
        return Task.FromResult(SaveChangesAnswers.Count > 0 ? SaveChangesAnswers.Dequeue() : CloseChoice.Cancel);
    }

    public Task<bool> ConfirmReplaceAsync(string message)
    {
        ReplaceConfirmations.Add(message);
        return Task.FromResult(ReplaceAnswers.Count > 0 && ReplaceAnswers.Dequeue());
    }
}
