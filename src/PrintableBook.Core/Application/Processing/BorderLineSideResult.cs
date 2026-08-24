namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Result for a single side. Position is a source-image coordinate when found.
/// </summary>
public sealed record BorderLineSideResult(bool Found, int? Position)
{
    public static BorderLineSideResult Missing() => new(false, null);

    public static BorderLineSideResult Detected(int position) => new(true, position);
}
