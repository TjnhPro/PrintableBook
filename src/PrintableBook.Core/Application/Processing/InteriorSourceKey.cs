using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public static class InteriorSourceKey
{
    public static string FromBookRoot(DirectoryReference bookDirectory, FileReference source)
    {
        var candidateRelative = Path.GetRelativePath(bookDirectory.Value, source.Value);
        var relative = Path.IsPathRooted(source.Value) ||
            (!candidateRelative.Equals("..", StringComparison.Ordinal) &&
             !candidateRelative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            ? candidateRelative
            : source.Value;
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("The interior source must be contained by its Book directory.", nameof(source));
        }

        return Normalize(relative);
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            throw new ArgumentException("An interior source key must be a non-rooted relative path.", nameof(value));
        }

        var segments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("An interior source key cannot contain traversal segments.", nameof(value));
        }

        return string.Join('/', segments);
    }
}
