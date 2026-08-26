using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Creates the portable, brand-relative identity persisted for IntroTemplate artwork.
/// </summary>
public static class IntroTemplateSourceKey
{
    public static string FromTemplateRoot(DirectoryReference templateDirectory, FileReference source)
    {
        ArgumentNullException.ThrowIfNull(templateDirectory);
        ArgumentNullException.ThrowIfNull(source);

        var relative = Path.IsPathRooted(source.Value)
            ? Path.GetRelativePath(templateDirectory.Value, source.Value)
            : source.Value;
        return Normalize(relative);
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            throw new ArgumentException("An IntroTemplate source key must be a non-rooted relative path.", nameof(value));
        }

        var segments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("An IntroTemplate source key cannot contain traversal segments.", nameof(value));
        }

        return string.Join('/', segments);
    }
}
