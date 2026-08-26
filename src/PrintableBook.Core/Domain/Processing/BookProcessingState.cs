using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Domain.Processing;

/// <summary>
/// Persistable state of a book project. Step names remain opaque to keep processing rules configurable.
/// </summary>
public sealed record BookProcessingState(
    BookId BookId,
    BookProcessingStatus Status,
    string? CurrentStep,
    string? LastCompletedStep,
    string? FailedStep,
    ProcessingFailure? Failure,
    DateTimeOffset UpdatedAt,
    bool MayResume,
    string? ConfigurationFingerprint = null,
    IReadOnlyList<string>? PublishedArtifactReferences = null,
    string? SelectedCoverReference = null,
    IReadOnlyDictionary<string, FrameMode>? InteriorFrameOverrides = null,
    bool HasBackground = false,
    IReadOnlyList<string>? InactiveInteriorSourceKeys = null,
    bool HasIntro = false,
    IReadOnlyList<string>? SelectedIntroTemplateKeys = null)
{
    public static BookProcessingState NotStarted(BookId bookId) => new(
        bookId,
        BookProcessingStatus.NotStarted,
        null,
        null,
        null,
        null,
        DateTimeOffset.MinValue,
        false,
        null,
        []);

    public BookProcessingState Start(DateTimeOffset timestamp, string? configurationFingerprint = null) => this with
    {
        Status = BookProcessingStatus.Running,
        CurrentStep = null,
        FailedStep = null,
        Failure = null,
        UpdatedAt = timestamp,
        MayResume = false,
        ConfigurationFingerprint = configurationFingerprint
    };

    public BookProcessingState BeginStep(string step, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            throw new ArgumentException("A processing step is required.", nameof(step));
        }

        return this with
        {
            Status = BookProcessingStatus.Running,
            CurrentStep = step,
            UpdatedAt = timestamp,
            MayResume = false
        };
    }

    public BookProcessingState CompleteStep(string step, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            throw new ArgumentException("A processing step is required.", nameof(step));
        }

        return this with
        {
            Status = BookProcessingStatus.Running,
            CurrentStep = null,
            LastCompletedStep = step,
            UpdatedAt = timestamp
        };
    }

    public BookProcessingState Fail(string step, ProcessingFailure failure, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            throw new ArgumentException("A processing step is required.", nameof(step));
        }

        ArgumentNullException.ThrowIfNull(failure);
        return this with
        {
            Status = BookProcessingStatus.Failed,
            CurrentStep = step,
            FailedStep = step,
            Failure = failure,
            UpdatedAt = timestamp,
            MayResume = true
        };
    }

    public BookProcessingState Cancel(DateTimeOffset timestamp) => this with
    {
        Status = BookProcessingStatus.Cancelled,
        UpdatedAt = timestamp,
        MayResume = true
    };

    public BookProcessingState Interrupt(DateTimeOffset timestamp) => this with
    {
        Status = BookProcessingStatus.Interrupted,
        UpdatedAt = timestamp,
        MayResume = true
    };

    public BookProcessingState Complete(DateTimeOffset timestamp) => this with
    {
        Status = BookProcessingStatus.Completed,
        CurrentStep = null,
        UpdatedAt = timestamp,
        MayResume = false
    };

    public BookProcessingState RecordPublishedArtifacts(IEnumerable<string> artifactReferences)
    {
        ArgumentNullException.ThrowIfNull(artifactReferences);
        return this with { PublishedArtifactReferences = artifactReferences.ToArray() };
    }

    public BookProcessingState SelectCover(string coverReference)
    {
        if (string.IsNullOrWhiteSpace(coverReference))
        {
            throw new ArgumentException("A cover reference is required.", nameof(coverReference));
        }

        return this with { SelectedCoverReference = coverReference };
    }

    public FrameMode GetInteriorFrameMode(string sourceKey)
    {
        ValidateSourceKey(sourceKey);
        return InteriorFrameOverrides is not null && InteriorFrameOverrides.TryGetValue(sourceKey, out var mode) ? mode : FrameMode.Auto;
    }

    public BookProcessingState SetInteriorFrameMode(string sourceKey, FrameMode mode)
    {
        ValidateSourceKey(sourceKey);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported frame mode.");
        var overrides = InteriorFrameOverrides is null
            ? new Dictionary<string, FrameMode>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, FrameMode>(InteriorFrameOverrides, StringComparer.OrdinalIgnoreCase);
        if (mode == FrameMode.Auto) overrides.Remove(sourceKey); else overrides[sourceKey] = mode;
        return this with { InteriorFrameOverrides = overrides.Count == 0 ? null : overrides };
    }

    public BookProcessingState SetHasBackground(bool enabled) => this with { HasBackground = enabled };

    public BookProcessingState SetHasIntro(bool enabled) => this with { HasIntro = enabled };

    public BookProcessingState SetIntroTemplateKeys(IEnumerable<string> templateKeys)
    {
        ArgumentNullException.ThrowIfNull(templateKeys);
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var templateKey in templateKeys)
        {
            var normalized = IntroTemplateSourceKey.Normalize(templateKey);
            if (seen.Add(normalized)) keys.Add(normalized);
        }

        return this with { SelectedIntroTemplateKeys = keys.Count == 0 ? [] : keys };
    }

    public bool IsInteriorActive(string sourceKey)
    {
        ValidateSourceKey(sourceKey);
        return InactiveInteriorSourceKeys is null || !InactiveInteriorSourceKeys.Contains(sourceKey, StringComparer.OrdinalIgnoreCase);
    }

    public BookProcessingState SetInteriorActive(string sourceKey, bool isActive)
    {
        ValidateSourceKey(sourceKey);
        var inactive = InactiveInteriorSourceKeys is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(InactiveInteriorSourceKeys.Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.OrdinalIgnoreCase);
        if (isActive) inactive.Remove(sourceKey); else inactive.Add(sourceKey);
        return this with { InactiveInteriorSourceKeys = inactive.Count == 0 ? null : inactive.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    private static void ValidateSourceKey(string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey)) throw new ArgumentException("An interior source key is required.", nameof(sourceKey));
    }
}
