using PrintableBook.Core.Domain.Books;

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
    string? SelectedCoverReference = null)
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
}
