using PrintableBook.Core.Abstractions;

namespace PrintableBook.Core.Application.Processing;

/// <summary>
/// Geometry returned by outer border-line detection. No image transformation is implied.
/// </summary>
public sealed record BorderLineDetectionResult(
    bool HasBorder,
    BorderLineSideResult Left,
    BorderLineSideResult Right,
    BorderLineSideResult Top,
    BorderLineSideResult Bottom,
    ImageRectangle? BorderBounds)
{
    public static BorderLineDetectionResult NoBorder(
        BorderLineSideResult? left = null,
        BorderLineSideResult? right = null,
        BorderLineSideResult? top = null,
        BorderLineSideResult? bottom = null) =>
        new(false,
            left ?? BorderLineSideResult.Missing(),
            right ?? BorderLineSideResult.Missing(),
            top ?? BorderLineSideResult.Missing(),
            bottom ?? BorderLineSideResult.Missing(),
            null);

    public static BorderLineDetectionResult Detected(
        BorderLineSideResult left,
        BorderLineSideResult right,
        BorderLineSideResult top,
        BorderLineSideResult bottom,
        ImageRectangle borderBounds) =>
        new(true, left, right, top, bottom, borderBounds);
}
