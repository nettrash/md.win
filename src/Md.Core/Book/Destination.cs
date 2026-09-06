namespace Md.Core.Book;

/// <summary>
/// Where a selected path ends up after a plan renames siblings inside a folder — the
/// item itself, or the chapter an article lives in. Port of <c>BookLibrary.destination</c>,
/// the pure half of the workspace's selection remapping: only the <em>first</em> path
/// component under the folder is remapped; paths outside the folder and names the plan
/// does not touch come back exactly as given (not even standardized), as Swift returns
/// the original URL object.
/// </summary>
public static class Destination
{
    public static string Of(string path, string folder, IReadOnlyList<RenamePair> plan)
    {
        var folderPath = BookPaths.Standardize(folder);
        var fullPath = BookPaths.Standardize(path);
        if (!BookPaths.IsInside(fullPath, folderPath)) return path;
        var start = folderPath.EndsWith(Path.DirectorySeparatorChar) ? folderPath.Length : folderPath.Length + 1;
        // Swift split(separator: "/") drops empty parts; GetFullPath has already
        // collapsed doubled separators, so RemoveEmptyEntries is belt and braces.
        var components = fullPath[start..]
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0) return path;
        var renamed = plan.FirstOrDefault(pair => string.Equals(pair.From, components[0], BookPaths.Comparison))?.To;
        if (renamed is null) return path;
        components[0] = renamed;
        var result = folderPath;
        foreach (var component in components) result = Path.Combine(result, component);
        return result;
    }
}
