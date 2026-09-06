namespace Md.App.Logic.Activation;

/// <summary>
/// The activation kinds md answers, named without WinRT. Mirrors the four
/// <c>Microsoft.Windows.AppLifecycle.ExtendedActivationKind</c> values an app can actually receive
/// (Launch, File, Protocol, StartupTask); everything else the platform may invent arrives as
/// <see cref="Other"/> and is treated as a launch (§1.2's last row).
/// </summary>
public enum ActivationKind
{
    Launch,
    File,
    Protocol,
    StartupTask,
    Other,
}

/// <summary>
/// One item of a file activation: the path, and whether the shell handed us a folder. The folder
/// flag is the App adapter's answer to <c>item is StorageFolder</c> — it cannot be inferred from
/// the name, and a <c>.textbundle</c> is a folder whose name has an extension (§6.5).
/// </summary>
public readonly record struct ActivationItem(string Path, bool IsFolder)
{
    public static ActivationItem File(string path) => new(path, false);

    public static ActivationItem Folder(string path) => new(path, true);
}

/// <summary>
/// The OS-free description of one activation, built by the App adapter from
/// <c>AppInstance.GetActivatedEventArgs()</c> / the <c>AppInstance.Activated</c> payload and
/// consumed by <see cref="ActivationRouter"/> (§1.2). Everything the routing rules need is a field
/// here, so the rules are pure: what kind, which items, the launch arguments, whether this is the
/// process's first activation, and whether there is a session worth restoring.
/// </summary>
/// <param name="Kind">The activation kind; anything but <see cref="ActivationKind.File"/> routes as a launch.</param>
/// <param name="Items">File-activation items, in the order the shell gave them.</param>
/// <param name="Arguments">The raw <c>ILaunchActivatedEventArgs.Arguments</c> string, or null.</param>
/// <param name="IsFirst">
/// True for the activation that started the process, false for one redirected here from a second
/// instance. Only the first may restore a session; a redirect that carries nothing means "the user
/// asked for md again", which is a new untitled window (§1.2).
/// </param>
/// <param name="HasRestorableSession">
/// <c>session.json</c> lists at least one saved document that still exists. Computed by the App
/// from <see cref="Windows.SessionStore"/> before routing, so the router never touches a disk.
/// </param>
public sealed record ActivationDescription(
    ActivationKind Kind,
    IReadOnlyList<ActivationItem> Items,
    string? Arguments,
    bool IsFirst,
    bool HasRestorableSession)
{
    public static ActivationDescription ForLaunch(string? arguments, bool isFirst, bool hasRestorableSession = false) =>
        new(ActivationKind.Launch, [], arguments, isFirst, hasRestorableSession);

    public static ActivationDescription ForFiles(IEnumerable<ActivationItem> items, bool isFirst, bool hasRestorableSession = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(ActivationKind.File, [.. items], null, isFirst, hasRestorableSession);
    }

    /// <summary>A protocol, startup or unknown activation: §1.2's "anything else" row, routed as a launch.</summary>
    public static ActivationDescription ForOther(ActivationKind kind, bool isFirst, bool hasRestorableSession = false) =>
        new(kind, [], null, isFirst, hasRestorableSession);

    /// <summary>
    /// The launch arguments as file paths: shell-quoted tokens, with the executable and any switch
    /// dropped. Two tokens are never documents:
    ///
    /// <list type="bullet">
    /// <item>a leading <c>*.exe</c> — the App SDK's own sample splits <c>Arguments</c> into several
    /// strings and the docs point Windows Forms and WPF apps at <c>Environment.GetCommandLineArgs</c>
    /// for "raw command-line arguments on a plain Launch activation", so an unpackaged run can see
    /// its own image path first. A document is never named <c>.exe</c>, so dropping it costs
    /// nothing and a packaged activation (which has no such token) is unaffected;</item>
    /// <item>a switch — anything starting with <c>-</c> or <c>/</c>, which a Windows path cannot do
    /// — <b>and everything after it</b>. md's only switch is <c>--selftest &lt;outDir&gt;</c>
    /// (§11.4), whose operand is an output directory, not a document; a command line carrying a
    /// switch is a switch invocation, not a document list, so treating the rest as documents would
    /// open the self-test's output folder as a file.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<string> ArgumentPaths
    {
        get
        {
            var tokens = SplitArguments(Arguments);
            var paths = new List<string>(tokens.Count);
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Length == 0) continue;
                if (i == 0 && token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (token[0] is '-' or '/') return [];
                paths.Add(token);
            }
            return paths;
        }
    }

    /// <summary>
    /// <c>CommandLineToArgvW</c>'s rules, which is how Windows itself splits the string a shortcut
    /// or a console hands us: whitespace separates, a run of <c>n</c> backslashes before a quote
    /// collapses to <c>n/2</c> and only an odd run escapes the quote, and <c>""</c> inside a quoted
    /// run is a literal quote. Naive splitting on spaces would break every path with a space in it.
    /// </summary>
    public static IReadOnlyList<string> SplitArguments(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var started = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (c == '\\')
            {
                // Count the run first: only a run immediately before a quote is an escape.
                var run = 0;
                while (i < commandLine.Length && commandLine[i] == '\\') { run++; i++; }
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', run / 2);
                    if (run % 2 == 1) { current.Append('"'); started = true; continue; }   // this quote is literal
                    i--;                                                                   // let the quote branch see it
                }
                else
                {
                    current.Append('\\', run);
                    i--;                                                                   // re-read the non-backslash
                }
                started = true;
                continue;
            }

            if (c == '"')
            {
                if (quoted && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    current.Append('"');            // "" inside a quoted run is one literal quote
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
                started = true;
                continue;
            }

            if (!quoted && (c == ' ' || c == '\t'))
            {
                if (started) tokens.Add(current.ToString());
                current.Clear();
                started = false;
                continue;
            }

            current.Append(c);
            started = true;
        }

        if (started) tokens.Add(current.ToString());
        return [.. tokens.Where(t => t.Length > 0)];
    }
}
