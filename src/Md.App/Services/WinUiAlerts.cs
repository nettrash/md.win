using Md.App.Logic;
using Md.App.Logic.Seams;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Md.App.Services;

/// <summary>
/// <see cref="IAlerts"/> as <c>ContentDialog</c>s (§7.9). Three facts shape it: a desktop dialog
/// needs an explicit <c>XamlRoot</c> or it throws; only one dialog may be open per window at a time,
/// so every call queues behind the last; and the buttons are the dialog's own — Primary, Secondary
/// and Close — never content, so the keyboard and the narrator treat them as buttons.
///
/// Defaults follow the platform's safety rule rather than convenience: Delete and Replace default to
/// Cancel, Save defaults to Save. Every string comes from <see cref="Strings"/>.
/// </summary>
internal sealed class WinUiAlerts : IAlerts
{
    readonly Window window;
    readonly SemaphoreSlim gate = new(1, 1);

    public WinUiAlerts(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        this.window = window;
    }

    public async Task WarnAsync(string title, string message)
    {
        var dialog = Dialog(title, message.Length == 0 ? null : message);
        dialog.CloseButtonText = Strings.Buttons.OK;
        dialog.DefaultButton = ContentDialogButton.Close;
        await ShowAsync(dialog);
    }

    public async Task<string?> PromptNameAsync(string title, string message, string initial, string acceptLabel)
    {
        var box = new TextBox { Text = initial };
        // Selecting on Loaded, not in the initialiser: the control re-applies its own default
        // selection when it enters the tree, so a selection set before that is gone by the time the
        // dialog is on screen — and the whole point is that typing replaces the old name.
        box.Loaded += (_, _) => box.SelectAll();
        var panel = new StackPanel { Spacing = 12 };
        if (message.Length > 0) panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);

        var dialog = Dialog(title, panel);
        dialog.PrimaryButtonText = acceptLabel;
        dialog.CloseButtonText = Strings.Buttons.Cancel;
        dialog.DefaultButton = ContentDialogButton.Primary;
        // Enter in the TextBox must commit the dialog, not insert a newline; the box is single-line
        // already, so the dialog's own default-button handling is all that is needed here.
        var result = await ShowAsync(dialog);
        return result == ContentDialogResult.Primary ? box.Text : null;
    }

    public async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        var dialog = Dialog(title, message);
        dialog.PrimaryButtonText = Strings.Buttons.Delete;
        dialog.CloseButtonText = Strings.Buttons.Cancel;
        dialog.DefaultButton = ContentDialogButton.Close;
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task<CloseChoice> AskSaveChangesAsync(string title)
    {
        var dialog = Dialog(title, null);
        dialog.PrimaryButtonText = Strings.Buttons.Save;
        dialog.SecondaryButtonText = Strings.Buttons.DontSave;
        dialog.CloseButtonText = Strings.Buttons.Cancel;
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await ShowAsync(dialog) switch
        {
            ContentDialogResult.Primary => CloseChoice.Save,
            ContentDialogResult.Secondary => CloseChoice.DontSave,
            _ => CloseChoice.Cancel,
        };
    }

    public async Task<bool> ConfirmReplaceAsync(string message)
    {
        var dialog = Dialog(message, null);
        dialog.PrimaryButtonText = Strings.Buttons.Replace;
        dialog.CloseButtonText = Strings.Buttons.Cancel;
        dialog.DefaultButton = ContentDialogButton.Close;
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    ContentDialog Dialog(string title, object? content) => new()
    {
        // Required in a desktop app: without it the dialog has no tree to attach to and throws.
        XamlRoot = window.Content?.XamlRoot,
        Title = title,
        Content = content,
    };

    async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await gate.WaitAsync();
        try
        {
            // The window may have closed while this call waited its turn; a dialog with no tree
            // would throw, and there is nobody left to answer it anyway.
            if (dialog.XamlRoot is null) return ContentDialogResult.None;
            return await dialog.ShowAsync();
        }
        finally
        {
            gate.Release();
        }
    }
}
