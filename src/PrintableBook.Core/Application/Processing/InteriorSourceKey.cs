using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

public static class InteriorSourceKey
{
    public static string FromBookRoot(DirectoryReference bookDirectory, FileReference source)
    {
        var relative = Path.IsPathRooted(source.Value)
            ? Path.GetRelativePath(bookDirectory.Value, source.Value)
            : source.Value;
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("The interior source must be contained by its Book directory.", nameof(source));
        }

        return relative.Replace('\\', '/');
    }
}
